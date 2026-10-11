using System;
using System.Diagnostics;
using System.Threading.Tasks;
using HanumanInstitute.LibMpv;

namespace ChroaVideoPlayer.Mpv;

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
        context.StartFile += OnStartFile;
        context.FileLoaded += OnFileLoaded;
        context.VideoReconfig += OnVideoReconfig;
        context.Pause.Changed += OnPausePropertyChanged;

        // 上下文就绪之前调用过 Play 的话，在这里补播
        var pending = _pendingFile;
        if (pending is not null)
        {
            _pendingFile = null;
            _ = PlayAsync(context, pending);
        }
    }

    private void Detach(MpvContext? context)
    {
        if (context is null) return;

        context.StartFile -= OnStartFile;
        context.FileLoaded -= OnFileLoaded;
        context.VideoReconfig -= OnVideoReconfig;
        context.Pause.Changed -= OnPausePropertyChanged;
    }

    // ==== 视频宽高比探测 ====

    private void OnStartFile(object? sender, MpvStartFileEventArgs e) => _aspectReported = false;

    private void OnFileLoaded(object? sender, EventArgs e) => TryReportAspect();

    private void OnVideoReconfig(object? sender, EventArgs e) => TryReportAspect();

    private void OnPausePropertyChanged(object? sender, MpvValueChangedEventArgs<bool, bool> e) =>
        PauseChanged?.Invoke(this, e.NewValue ?? false);

    private void TryReportAspect()
    {
        // 每个文件只报一次：IMAX 这类播放中会切画幅的片源，
        // 若跟着每次 video-reconfig 调窗口，窗口会一直跳。
        if (_aspectReported) return;

        var context = _context;
        if (context is null) return;

        // dwidth/dheight = 应用了 aspect scaling 之后的显示尺寸。
        // width/height 是解码尺寸，变形片源会算错。
        var width = context.DisplayWidth.Get() ?? 0;
        var height = context.DisplayHeight.Get() ?? 0;
        if (width <= 0 || height <= 0) return;      // 还没就绪，等下一个事件

        _aspectReported = true;
        VideoAspectDetected?.Invoke(this, (double)width / height);
    }
}