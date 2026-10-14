using System;
using System.Diagnostics;
using HanumanInstitute.LibMpv;

namespace ChroaVideoPlayer.Core.Mpv;

public sealed partial class ChroaMpvContext
{
    private double _speed = 1;

    public event EventHandler<double>? SpeedChanged;

    public void SetSpeed(double speed)
    {
        var context = _context;
        if (context is null || !double.IsFinite(speed) || speed <= 0) return;

        SafeRun(nameof(SetSpeed), () =>
        {
            _speed = speed;
            PublishSpeed();

            context.Speed.Set(speed);
        });
    }

    private void OnSpeedPropertyChanged(object? sender, MpvValueChangedEventArgs<double, double> e) =>
        SafeRun(nameof(OnSpeedPropertyChanged), () =>
        {
            if (e.NewValue is { } v && double.IsFinite(v) && v > 0) _speed = v;
            PublishSpeed();
        });

    private void PublishSpeed() => SpeedChanged?.Invoke(this, _speed);

    private void TryPublishSpeed()
    {
        var context = _context;
        if (context is null) return;

        try
        {
            _speed = context.Speed.Get() ?? _speed;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"speed not ready yet ({ex.GetType().Name})");
            return;
        }

        PublishSpeed();
    }
}
