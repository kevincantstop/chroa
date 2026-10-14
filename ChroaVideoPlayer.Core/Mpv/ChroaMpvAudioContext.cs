using System;
using System.Diagnostics;
using System.Threading.Tasks;
using HanumanInstitute.LibMpv;

namespace ChroaVideoPlayer.Core.Mpv;

public readonly record struct PlaybackVolume(double Volume, bool Muted);

public sealed partial class ChroaMpvContext
{
    private double _volume = 100;
    private bool _muted;

    private bool _hasAudio = true;

    public event EventHandler<PlaybackVolume>? VolumeChanged;

    public event EventHandler<bool>? AudioTrackChanged;

    public void SetVolume(double volume)
    {
        var context = _context;
        if (context is null || !double.IsFinite(volume) || volume < 0) return;

        SafeRun(nameof(SetVolume), () =>
        {
            _volume = volume;

            if (_muted && volume > 0)
            {
                _muted = false;
                context.Mute.Set(false);
            }

            PublishVolume();
            context.Volume.Set(volume);
        });
    }

    public void ToggleMute()
    {
        var context = _context;
        if (context is null) return;

        SafeRun("ToggleMute", () => {
            _muted = !(context.Mute.Get() ?? false);
            context.Mute.Set(_muted);
            PublishVolume();
        });
    }

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

    private void TryPublishVolume()
    {
        var context = _context;
        if (context is null) return;

        try
        {
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


    private void OnAudioReconfig(object? sender, EventArgs e) =>
        SafeRun(nameof(OnAudioReconfig), () => SetHasAudio(true));

    private async Task RefreshAudioTrackAsync()
    {
        for (var i = 0; i < 10; i++)
        {
            if (TryReadHasAudio(out var hasAudio))
            {
                SetHasAudio(hasAudio);
                return;
            }

            await Task.Delay(100);
        }
    }

    private bool TryReadHasAudio(out bool hasAudio)
    {
        hasAudio = true;

        var context = _context;
        if (context is null) return false;

        try
        {
            hasAudio = context.AudioId.Get() is not null;
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"aid not ready yet ({ex.GetType().Name})");
            return false;
        }
    }

    private void SetHasAudio(bool hasAudio)
    {
        if (_hasAudio == hasAudio) return;

        _hasAudio = hasAudio;
        AudioTrackChanged?.Invoke(this, hasAudio);
    }
}
