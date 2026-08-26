using System.Windows;
using System.Windows.Threading;

namespace SoulExe.ViewModels;

/// <summary>Throttled UI publish of streaming assistant text onto the WPF dispatcher.</summary>
public sealed class StreamingPreviewPublisher
{
    private readonly Action<string> _publishOnUi;
    private readonly Dispatcher? _dispatcher;
    private long _lastPreviewAt;
    private int _active = 1;

    public StreamingPreviewPublisher(Action<string> publishOnUi, int minIntervalMs = 160)
    {
        _publishOnUi = publishOnUi;
        _dispatcher = Application.Current?.Dispatcher;
        MinIntervalMs = minIntervalMs;
    }

    public int MinIntervalMs { get; }

    public void TryPublish(string preview)
    {
        if (Volatile.Read(ref _active) == 0) return;
        var now = Environment.TickCount64;
        if (now - _lastPreviewAt < MinIntervalMs) return;
        _lastPreviewAt = now;
        if (_dispatcher is null || _dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished)
        {
            _publishOnUi(preview);
            return;
        }
        _ = _dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (Volatile.Read(ref _active) == 0) return;
            _publishOnUi(preview);
        }));
    }

    public void Stop() => Interlocked.Exchange(ref _active, 0);
}
