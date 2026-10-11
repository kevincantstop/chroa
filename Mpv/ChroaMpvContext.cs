using System;
using System.Diagnostics;
using System.Threading.Tasks;
using HanumanInstitute.LibMpv;

namespace ChroaVideoPlayer.Mpv;

/// <summary>一次播放进度上报：当前位置与总时长（秒）。</summary>
public readonly record struct PlaybackProgress(double Position, double Duration);

/// <summary>一次音量上报：音量（mpv 的 volume 单位）与是否静音。</summary>
public readonly record struct PlaybackVolume(double Volume, bool Muted);

/// <summary>
/// 所有与 mpv 的交互都收敛在这里：接住 MpvView 推来的 <see cref="MpvContext"/>、
/// 订阅事件、把 mpv 语义翻译成应用语义（播放、宽高比…）。
/// ViewModel 只持有它并管理状态，不直接接触任何 mpv 类型。
/// </summary>
public sealed class ChroaMpvContext
{
    private MpvContext? _context;
    private string? _pendingFile;
    private bool _aspectReported;

    /// <summary>
    /// dwidth/dheight 是否已经反映"当前这个视频"。
    /// start-file 之后、video-reconfig 之前，这两个属性很可能还是**上一个视频**的值，
    /// 那时读出来的宽高比是旧的 —— 会导致新视频的比例永远不被上报。
    /// </summary>
    private bool _sizeTrustworthy;

    /// <summary>
    /// 进度上报的最小间隔（毫秒）。mpv 的 time-pos 是逐帧变的，
    /// 原样推给 UI 只会让进度条抖（架构文档 §6.4）。
    /// </summary>
    private const int ProgressPublishIntervalMs = 200;

    private readonly Stopwatch _progressClock = Stopwatch.StartNew();
    private double _position;
    private double _duration;

    /// <summary>当前音量与静音状态。初值取 mpv 的默认音量 100，Attach 后会立刻读一次真实值。</summary>
    private double _volume = 100;
    private bool _muted;

    /// <summary>
    /// 由 MpvView 通过 OneWayToSource 绑定写入（见 MainWindow.axaml 的 MpvContext="{Binding Mpv.ViewContext}"）。
    /// 注意：切换 Renderer 时 MpvView 会销毁旧 context 并推来一个新的，所以这里会被赋值多次 ——
    /// 不要把 context 引用缓存到别处，setter 内部会自动完成事件订阅/解绑。
    /// </summary>
    public MpvContext? ViewContext
    {
        get => _context;
        set
        {
            if (ReferenceEquals(_context, value)) return;

            Detach(_context);
            _context = value;
            Attach(value);
        }
    }

    /// <summary>
    /// 探测到视频宽高比（显示宽 / 显示高）时触发。
    /// 可能在 mpv 自己的线程上触发，订阅方若要改 UI 需要自己切回 UI 线程。
    /// </summary>
    public event EventHandler<double>? VideoAspectDetected;

    /// <summary>
    /// 播放/暂停状态变化。来自 mpv 的 pause 属性推送（属性观察，不是轮询 —— 架构文档 §6.4 的要求）。
    /// 可能在 mpv 自己的线程上触发。
    /// </summary>
    public event EventHandler<bool>? PauseChanged;

    /// <summary>
    /// mpv 成功加载了一个文件（file-loaded）时触发。空状态 UI 靠它切换到"已加载"布局。
    /// 可能在 mpv 自己的线程上触发，订阅方若要改 UI 需要自己切回 UI 线程。
    /// </summary>
    public event EventHandler? MediaOpened;

    /// <summary>
    /// 播放进度（位置 / 总时长，秒）。来自 mpv 的 time-pos 与 duration 属性推送，
    /// 已按 ProgressPublishIntervalMs 节流；seek / 暂停 / 换文件时会强制补发一次。
    /// 可能在 mpv 自己的线程上触发。
    /// </summary>
    public event EventHandler<PlaybackProgress>? ProgressChanged;

    /// <summary>
    /// 音量 / 静音变化。来自 mpv 的 volume 与 mute 属性推送
    /// （架构文档 §290：音量只走 mpv 的 volume 属性，应用侧不做任何增益）。
    /// 可能在 mpv 自己的线程上触发。
    /// </summary>
    public event EventHandler<PlaybackVolume>? VolumeChanged;

    /// <summary>
    /// 播放指定文件。若 mpv 上下文还没就绪（视图尚未创建），会记住这个文件，
    /// 等上下文推过来之后自动播放。
    /// </summary>
    public void Play(string path)
    {
        var context = _context;
        if (context is null)
        {
            _pendingFile = path;
            return;
        }

        _ = PlayAsync(context, path);
    }

    /// <summary>切换播放 / 暂停。</summary>
    public void TogglePause()
    {
        var context = _context;
        if (context is null) return;

        // 每次都读 mpv 的真实状态再取反，不在应用侧自己维护一份，
        // 免得和 mpv 实际状态脱节（新文件加载时 mpv 会把 pause 重置）。
        context.Pause.Set(!(context.Pause.Get() ?? false));
    }

    /// <summary>跳到指定位置（秒）。绝对定位。</summary>
    public void SeekTo(double seconds)
    {
        var context = _context;
        if (context is null || !double.IsFinite(seconds) || seconds < 0) return;

        try
        {
            // 乐观上报：先把本地位置改成目标值并立刻推一次。
            // 否则进度条要等 mpv 回报新位置才动，中途还可能收到"seek 之前的旧时间"而弹回去。
            _position = seconds;
            PublishProgress(force: true);

            // 命令用异步形式，不要阻塞 UI 线程（架构文档 §6.4）
            _ = context.Seek(seconds, SeekOption.Absolute).InvokeAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Seek failed: {ex}");
        }
    }

    /// <summary>设置音量。范围由 UI 决定（ViewModel 里是 0~100），mpv 自己会按 volume-max 截断。</summary>
    public void SetVolume(double volume)
    {
        var context = _context;
        if (context is null || !double.IsFinite(volume) || volume < 0) return;

        try
        {
            _volume = volume;

            // 静音状态下拖音量条：顺手解除静音 —— 否则"拖了没声音"，看着像坏了
            if (_muted && volume > 0)
            {
                _muted = false;
                context.Mute.Set(false);
            }

            PublishVolume();
            context.Volume.Set(volume);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"SetVolume failed: {ex}");
        }
    }

    /// <summary>切换静音。</summary>
    public void ToggleMute()
    {
        var context = _context;
        if (context is null) return;

        try
        {
            // 和 TogglePause 一样：读 mpv 的真实状态再取反，不在应用侧维护第二份真相
            _muted = !(context.Mute.Get() ?? false);
            context.Mute.Set(_muted);
            PublishVolume();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ToggleMute failed: {ex}");
        }
    }

    private static async Task PlayAsync(MpvContext context, string path)
    {
        try
        {
            // OpenGlView 在 OnOpenGlInit 里会先调用 StopRendering()，而它会发 mpv 的 stop 命令。
            // 所以在渲染上下文建立之前 loadfile 会被这一步清掉 —— 必须等它就绪。
            var waited = 0;
            while (!context.IsCustomRendering() && waited < 5000)
            {
                await Task.Delay(50);
                waited += 50;
            }

            if (!context.IsCustomRendering())
            {
                Debug.WriteLine("OpenGL 渲染上下文 5 秒内没建立，画面出不来（检查 ANGLE / RenderingMode）");
            }

            await context.LoadFile(path).InvokeAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"LoadFile failed: {ex}");
        }
    }

    private void Attach(MpvContext? context)
    {
        if (context is null) return;

        _aspectReported = false;
        _sizeTrustworthy = false;
        context.StartFile += OnStartFile;
        context.FileLoaded += OnFileLoaded;
        context.VideoReconfig += OnVideoReconfig;
        context.Pause.Changed += OnPausePropertyChanged;
        context.TimePos.Changed += OnTimePosChanged;
        context.Duration.Changed += OnDurationChanged;
        context.SeekRaised += OnSeekRaised;
        context.Volume.Changed += OnVolumePropertyChanged;
        context.Mute.Changed += OnMutePropertyChanged;

        // 上下文就绪之前调用过 Play 的话，在这里补播
        var pending = _pendingFile;
        if (pending is not null)
        {
            _pendingFile = null;
            _ = PlayAsync(context, pending);
        }

        // 属性观察只在"变化"时推送，初始值不会自己来 —— 订阅之后主动读一次
        TryPublishVolume();
    }

    private void Detach(MpvContext? context)
    {
        if (context is null) return;

        context.StartFile -= OnStartFile;
        context.FileLoaded -= OnFileLoaded;
        context.VideoReconfig -= OnVideoReconfig;
        context.Pause.Changed -= OnPausePropertyChanged;
        context.TimePos.Changed -= OnTimePosChanged;
        context.Duration.Changed -= OnDurationChanged;
        context.SeekRaised -= OnSeekRaised;
        context.Volume.Changed -= OnVolumePropertyChanged;
        context.Mute.Changed -= OnMutePropertyChanged;
    }

    // ==== 视频宽高比探测 ====

    private void OnStartFile(object? sender, MpvStartFileEventArgs e) =>
        SafeRun(nameof(OnStartFile), () =>
        {
            // 新文件开始：重置。此刻 dwidth/dheight 还是上一个视频的值，
            // 所以要等 video-reconfig 之后才允许读取（见 _sizeTrustworthy）。
            _aspectReported = false;
            _sizeTrustworthy = false;

            // 进度也重置：新文件从头开始。duration 稍后会由 mpv 推来。
            _position = 0;
            _duration = 0;
            PublishProgress(force: true);
        });

    private void OnVideoReconfig(object? sender, EventArgs e) =>
        SafeRun(nameof(OnVideoReconfig), () =>
        {
            // 视频输出已按新尺寸重新配置 —— 此刻 dwidth/dheight 才可信
            _sizeTrustworthy = true;
            TryReportAspect();
        });

    private void OnFileLoaded(object? sender, EventArgs e) =>
        SafeRun(nameof(OnFileLoaded), () =>
        {
            // 音频输出此时才就绪，顺手把音量/静音再确认一次
            // （Attach 时的读取可能落在"属性还不可读"的窗口里）
            TryPublishVolume();

            // 先通知"有东西加载了"：这是 UI 切布局的信号，不该被下面的比例重试拖延。
            MediaOpened?.Invoke(this, EventArgs.Empty);

            // file-loaded 可能早于 video-reconfig，此时读到的是上一个视频的尺寸。
            // 所以尺寸不可信时只安排重试，等 video-reconfig 到了（或超时兜底）再读。
            if (_sizeTrustworthy) TryReportAspect();
            else _ = RetryAspectAsync();
        });

    private void OnPausePropertyChanged(object? sender, MpvValueChangedEventArgs<bool, bool> e) =>
        SafeRun(nameof(OnPausePropertyChanged), () =>
        {
            PauseChanged?.Invoke(this, e.NewValue ?? false);

            // 暂停瞬间补报一次：节流可能把最后一个位置吞掉了，停下来时应当停在准确的位置上。
            PublishProgress(force: true);
        });

    private void TryReportAspect()
    {
        // 每个文件只报一次：IMAX 这类播放中会切画幅的片源，
        // 若跟着每次 video-reconfig 调窗口，窗口会一直跳。
        if (_aspectReported) return;

        // 尺寸还不可信就读，会拿到上一个视频的比例
        if (!_sizeTrustworthy) return;

        var context = _context;
        if (context is null) return;

        int width, height;
        try
        {
            // dwidth/dheight = 应用了 aspect scaling 之后的显示尺寸。
            // width/height 是解码尺寸，变形片源会算错。
            //
            // 但在 vo=libmpv 的渲染路径下，这两个属性可能还不可用，而 LibMpv 对不可用的
            // 属性会抛 MpvException。这个异常绝不能放出去 —— 它会顺着事件循环线程冒出去，
            // 把整个事件循环永久搞死（详见 SafeRun 的说明）。
            width = context.DisplayWidth.Get() ?? 0;
            height = context.DisplayHeight.Get() ?? 0;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"aspect not ready yet ({ex.GetType().Name})");
            _ = RetryAspectAsync();
            return;
        }

        if (width <= 0 || height <= 0) return;      // 还没就绪，等下一个事件

        _aspectReported = true;
        VideoAspectDetected?.Invoke(this, (double)width / height);
    }

    /// <summary>属性还没就绪时，短时间内多试几次。</summary>
    private async Task RetryAspectAsync()
    {
        for (var i = 0; i < 20 && !_aspectReported; i++)
        {
            await Task.Delay(100);

            // 兜底：1 秒后即使没等到 video-reconfig 也允许读一次 ——
            // 那时新视频肯定已经配置完成，尺寸是可信的。
            if (i == 10) _sizeTrustworthy = true;

            SafeRun(nameof(RetryAspectAsync), TryReportAspect);
        }
    }

    // ==== 播放进度（time-pos / duration）====

    private void OnTimePosChanged(object? sender, MpvValueChangedEventArgs<double, double> e) =>
        SafeRun(nameof(OnTimePosChanged), () =>
        {
            if (e.NewValue is { } v && double.IsFinite(v) && v >= 0) _position = v;
            PublishProgress(force: false);
        });

    private void OnDurationChanged(object? sender, MpvValueChangedEventArgs<double, double> e) =>
        SafeRun(nameof(OnDurationChanged), () =>
        {
            if (e.NewValue is { } v && double.IsFinite(v) && v >= 0) _duration = v;

            // 时长极少变，一变就要立刻反映到进度条量程上，不能等节流窗口。
            PublishProgress(force: true);
        });

    private void OnSeekRaised(object? sender, EventArgs e) =>
        SafeRun(nameof(OnSeekRaised), () => PublishProgress(force: true));

    /// <summary>把当前进度推给订阅方。force=false 时按 ProgressPublishIntervalMs 节流。</summary>
    private void PublishProgress(bool force)
    {
        if (!force && _progressClock.ElapsedMilliseconds < ProgressPublishIntervalMs) return;

        _progressClock.Restart();
        ProgressChanged?.Invoke(this, new PlaybackProgress(_position, _duration));
    }

    // ==== 音量 / 静音（volume / mute）====

    private void OnVolumePropertyChanged(object? sender, MpvValueChangedEventArgs<double, double> e) =>
        SafeRun(nameof(OnVolumePropertyChanged), () =>
        {
            if (e.NewValue is { } v && double.IsFinite(v)) _volume = v;
            PublishVolume();
        });

    private void OnMutePropertyChanged(object? sender, MpvValueChangedEventArgs<bool, bool> e) =>
        SafeRun(nameof(OnMutePropertyChanged), () =>
        {
            _muted = e.NewValue ?? false;
            PublishVolume();
        });

    private void PublishVolume() => VolumeChanged?.Invoke(this, new PlaybackVolume(_volume, _muted));

    /// <summary>读一次 mpv 当前的音量 / 静音并上报。属性还没就绪时只记一条调试输出。</summary>
    private void TryPublishVolume()
    {
        var context = _context;
        if (context is null) return;

        try
        {
            // 上下文刚建立、或音频输出还没打开时这两个属性可能不可读（同 DisplayWidth 的情况）
            _volume = context.Volume.Get() ?? _volume;
            _muted = context.Mute.Get() ?? _muted;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"volume/mute not ready yet ({ex.GetType().Name})");
            return;
        }

        PublishVolume();
    }

    /// <summary>
    /// 事件处理器绝不能抛异常。
    /// LibMpv 的事件循环是跑在 Task 上的（MpvSimpleEventLoop.ProcessEvents），处理器里的异常会
    /// 顺着它冒出去把这个 Task 永久 fault —— 之后再也收不到任何 mpv 事件。而该 Task 从未被观察，
    /// 所以既不崩溃也不报错：画面照播、同步调用（Get/Set/LoadFile）照常，只有"事件驱动"的功能
    /// 集体哑掉，极难定位。
    /// </summary>
    private static void SafeRun(string name, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"mpv handler {name} threw: {ex}");
        }
    }
}