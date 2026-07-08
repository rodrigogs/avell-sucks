using System;
using System.Windows.Threading;

namespace GamingCenter.UI.Controls;

/// <summary>
/// Coalesces a burst of rapid edits (slider drag, curve drag) into a single
/// trailing action. Every <see cref="Trigger"/> resets the timer; the action
/// runs once, <paramref name="delayMs"/> after the last trigger. Used so live
/// controls actuate hardware on settle, not on every intermediate value.
/// </summary>
public sealed class Debouncer
{
    private readonly DispatcherTimer _timer;
    private Action? _pending;

    public Debouncer(int delayMs = 450)
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delayMs) };
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            var action = _pending;
            _pending = null;
            action?.Invoke();
        };
    }

    /// <summary>Schedule <paramref name="action"/>, replacing any not-yet-fired one.</summary>
    public void Trigger(Action action)
    {
        _pending = action;
        _timer.Stop();
        _timer.Start();
    }

    /// <summary>Cancel a pending action without running it.</summary>
    public void Cancel()
    {
        _timer.Stop();
        _pending = null;
    }
}
