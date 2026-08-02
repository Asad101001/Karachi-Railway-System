using System.Windows.Threading;
using KarachiRailway.Simulation.Models;

namespace KarachiRailway.Desktop.Playback;

/// <summary>
/// Drives time-based animation of simulation events on the WPF UI thread.
/// On every timer tick the playback clock advances by elapsed real time scaled by SpeedMultiplier
/// (at 1x, 1 simulated minute advances per 1 real second), and all due events are fired.
/// </summary>
public sealed class PlaybackController
{
    private const int TickMs = 100;

    private readonly DispatcherTimer _timer;

    private IReadOnlyList<PlaybackEvent> _events = Array.Empty<PlaybackEvent>();
    private int    _nextIndex;
    private double _playbackTime;   // simulated minutes elapsed so far
    private double _speedMultiplier = 1.0;
    private DateTime _lastTickUtc;

    // ── Public events ────────────────────────────────────────────────────────

    /// <summary>Raised on the UI thread for each event that becomes due.</summary>
    public event Action<PlaybackEvent>? EventApplied;

    /// <summary>Raised on the UI thread when all events have been dispatched.</summary>
    public event Action? PlaybackCompleted;

    // ── Public state ─────────────────────────────────────────────────────────

    public bool   IsPlaying    => _timer.IsEnabled;
    public double PlaybackTime => _playbackTime;
    public int    EventsTotal  => _events.Count;
    public int    EventsDone   => _nextIndex;

    public double SpeedMultiplier
    {
        get => _speedMultiplier;
        set => _speedMultiplier = Math.Max(0.05, value);
    }

    // ── Constructor ──────────────────────────────────────────────────────────

    public PlaybackController()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(TickMs),
        };
        _timer.Tick += OnTick;
    }

    // ── Control ───────────────────────────────────────────────────────────────

    /// <summary>Load a new event list and reset the playback clock to zero.</summary>
    public void Load(IReadOnlyList<PlaybackEvent> events)
    {
        _timer.Stop();
        _events       = events;
        _nextIndex    = 0;
        _playbackTime = 0;
    }

    public void Start()
    {
        if (_events.Count == 0) return;
        _lastTickUtc = DateTime.UtcNow;
        _timer.Start();
    }

    public void Pause() => _timer.Stop();

    public void Resume()
    {
        _lastTickUtc = DateTime.UtcNow;
        _timer.Start();
    }

    public void Reset()
    {
        _timer.Stop();
        _nextIndex    = 0;
        _playbackTime = 0;
    }

    /// <summary>
    /// Advances playback by exactly one event (if available) while remaining paused.
    /// Returns true when an event was applied, false when there are no remaining events.
    /// </summary>
    public bool StepForward()
    {
        _timer.Stop();

        if (_nextIndex >= _events.Count)
            return false;

        var evt = _events[_nextIndex];
        _playbackTime = Math.Max(_playbackTime, evt.SimTime);
        EventApplied?.Invoke(evt);
        _nextIndex++;

        if (_nextIndex >= _events.Count && _events.Count > 0)
            PlaybackCompleted?.Invoke();

        return true;
    }

    // ── Timer tick ───────────────────────────────────────────────────────────

    private void OnTick(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        double elapsedSeconds = (now - _lastTickUtc).TotalSeconds;
        _lastTickUtc = now;

        if (elapsedSeconds <= 0) return;

        // Advance simulated time in minutes (at 1x speed, 1 real second = 0.6 simulated minutes)
        _playbackTime += elapsedSeconds * _speedMultiplier * 0.6;

        while (_nextIndex < _events.Count &&
               _events[_nextIndex].SimTime <= _playbackTime)
        {
            EventApplied?.Invoke(_events[_nextIndex]);
            _nextIndex++;
        }

        if (_nextIndex >= _events.Count && _events.Count > 0)
        {
            _timer.Stop();
            PlaybackCompleted?.Invoke();
        }
    }
}
