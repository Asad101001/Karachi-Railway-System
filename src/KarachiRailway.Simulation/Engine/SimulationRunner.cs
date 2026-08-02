using KarachiRailway.Simulation.Models;

namespace KarachiRailway.Simulation.Engine;

/// <summary>
/// Discrete-event simulator for Karachi Railway single-server queue models (M/M/1, M/G/1, G/G/1).
/// Distribution choices are controlled by <see cref="SimulationParameters.ModelType"/>,
/// then each passenger is routed through the decision-flow engine.
/// </summary>
public class SimulationRunner
{
    private readonly SimulationParameters _params;
    private readonly Random _rng;
    private volatile bool _paused;
    private volatile bool _cancelled;

    /// <summary>
    /// Raised after each passenger is processed (useful for real-time UI updates).
    /// The argument is the current <see cref="Passenger"/>.
    /// </summary>
    public event EventHandler<Passenger>? PassengerProcessed;

    // QueueDepthChanged is available for future UI integration (live queue-length display).

    public SimulationRunner(SimulationParameters parameters, Random? random = null)
    {
        _params = parameters?.Clone()
            ?? throw new ArgumentNullException(nameof(parameters));
        _rng = random ?? new Random();
    }

    // ── Control ───────────────────────────────────────────────────────────────

    public void Pause()  => _paused = true;
    public void Resume() => _paused = false;
    public void Cancel() => _cancelled = true;

    // ── Run ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Executes the full batch simulation.
    /// </summary>
    /// <param name="progressCallback">
    /// Optional callback invoked after each passenger is processed.
    /// Receives (passenger, simulationTimeNow).
    /// </param>
    /// <param name="cancellationToken">
    /// External cancellation support (e.g. from UI Stop button).
    /// </param>
    public SimulationResult Run(
        Action<Passenger, double>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        _paused    = false;
        _cancelled = false;

        var flowEngine = new PassengerFlowEngine(_params, _rng);
        var passengers = new List<Passenger>();

        double simTime      = 0.0;
        double serviceEndAt = 0.0;  // When the single server becomes free
        int    passengerIdCounter = 1;

        double durationMinutes = _params.SimulationDurationMinutes;

        // Generate arrivals until simulation time runs out
        while (simTime < durationMinutes)
        {
            if (_cancelled || cancellationToken.IsCancellationRequested)
                break;

            // Respect pause (tight-loop poll – caller should use async Task to avoid blocking)
            while (_paused && !_cancelled && !cancellationToken.IsCancellationRequested)
                Thread.Sleep(50);

            if (_cancelled || cancellationToken.IsCancellationRequested)
                break;

            // Next arrival (model-dependent inter-arrival)
            double interArrival = SampleInterArrival();
            simTime += interArrival;

            if (simTime > durationMinutes)
                break;

            // Build passenger
            var passenger = new Passenger
            {
                Id          = passengerIdCounter++,
                ArrivalTime = simTime,
            };

            // Queue / server assignment
            double startService = Math.Max(simTime, serviceEndAt);
            passenger.ServiceStartTime = startService;

            // Service duration (model-dependent service-time)
            double serviceDuration = SampleServiceTime();
            serviceEndAt = startService + serviceDuration;
            passenger.ExitTime = serviceEndAt;

            // Run the passenger through the decision-flow
            flowEngine.ProcessPassenger(passenger);

            passengers.Add(passenger);

            PassengerProcessed?.Invoke(this, passenger);
            progressCallback?.Invoke(passenger, simTime);
        }

        return BuildResult(passengers, durationMinutes);
    }

    /// <summary>
    /// Async wrapper so the UI can run without blocking the dispatch thread.
    /// </summary>
    public Task<SimulationResult> RunAsync(
        Action<Passenger, double>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(
            () => Run(progressCallback, cancellationToken),
            cancellationToken);
    }

    // ── Playback ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs the full simulation and produces a chronologically ordered list of
    /// <see cref="PlaybackEvent"/> objects – one per passenger step – suitable for
    /// time-based animated playback in the UI.
    /// </summary>
    public (SimulationResult Result, IReadOnlyList<PlaybackEvent> Events) RunForPlayback(
        CancellationToken cancellationToken = default)
    {
        var allEvents = new List<PlaybackEvent>();

        var result = Run(
            progressCallback: (passenger, _) =>
            {
                var steps = passenger.StepTrace;
                if (steps.Count == 0) return;

                double start    = passenger.ArrivalTime;
                double duration = passenger.ExitTime - passenger.ServiceStartTime;
                // Spread steps evenly over the passenger's service duration.
                double interval = steps.Count > 1 ? duration / (steps.Count - 1) : 0.0;

                for (int i = 0; i < steps.Count; i++)
                    allEvents.Add(new PlaybackEvent(passenger.Id, steps[i], start + i * interval));
            },
            cancellationToken: cancellationToken);

        allEvents.Sort((a, b) => a.SimTime.CompareTo(b.SimTime));
        return (result, allEvents);
    }

    /// <summary>
    /// Async wrapper for <see cref="RunForPlayback"/>.
    /// </summary>
    public Task<(SimulationResult Result, IReadOnlyList<PlaybackEvent> Events)> RunForPlaybackAsync(
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => RunForPlayback(cancellationToken), cancellationToken);
    }

    // ── Result builder ────────────────────────────────────────────────────────

    private SimulationResult BuildResult(List<Passenger> passengers, double duration)
    {
        var (rho, lq, l, wq, w) = QueueMetricsCalculator.Compute(
            _params.ModelType,
            _params.ArrivalRate,
            _params.ServiceRate,
            _params.ServiceCv,
            _params.ArrivalCv);

        int completed = passengers.Count(p => p.Completed);
        int left      = passengers.Count(p => p.LeftSystem);

        double simAvgWait = passengers.Count > 0
            ? passengers.Average(p => p.WaitTime)
            : 0;
        double simAvgSystem = passengers.Count > 0
            ? passengers.Average(p => p.SystemTime)
            : 0;

        return new SimulationResult
        {
            ModelType               = _params.ModelType,
            Utilization             = rho,
            AvgQueueWaitTime        = wq,
            AvgSystemTime           = w,
            AvgQueueLength          = lq,
            AvgNumberInSystem       = l,
            TotalArrived            = passengers.Count,
            TotalCompleted          = completed,
            TotalLeft               = left,
            SimulationDurationMinutes = duration,
            SimAvgWaitTime          = simAvgWait,
            SimAvgSystemTime        = simAvgSystem,
            Passengers              = passengers,
        };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Samples from Exponential(rate) distribution via inverse CDF.</summary>
    private double SampleExponential(double rate)
    {
        double u;
        do { u = _rng.NextDouble(); } while (u == 0.0);
        return -Math.Log(u) / rate;
    }

    private double SampleInterArrival()
    {
        return _params.ModelType switch
        {
            QueueModelType.MM1 => SampleExponential(_params.ArrivalRate),
            QueueModelType.MG1 => SampleExponential(_params.ArrivalRate),
            QueueModelType.GG1 => SampleGammaByMeanAndCv(1.0 / _params.ArrivalRate, _params.ArrivalCv),
            _ => SampleExponential(_params.ArrivalRate),
        };
    }

    private double SampleServiceTime()
    {
        return _params.ModelType switch
        {
            QueueModelType.MM1 => SampleExponential(_params.ServiceRate),
            QueueModelType.MG1 => SampleGammaByMeanAndCv(1.0 / _params.ServiceRate, _params.ServiceCv),
            QueueModelType.GG1 => SampleGammaByMeanAndCv(1.0 / _params.ServiceRate, _params.ServiceCv),
            _ => SampleExponential(_params.ServiceRate),
        };
    }

    private double SampleGammaByMeanAndCv(double mean, double cv)
    {
        if (mean <= 0) throw new ArgumentOutOfRangeException(nameof(mean));
        if (cv <= 0) throw new ArgumentOutOfRangeException(nameof(cv));

        if (Math.Abs(cv - 1.0) < 1e-9)
            return SampleExponential(1.0 / mean);

        double shape = 1.0 / (cv * cv);
        double scale = mean / shape;
        return SampleGamma(shape) * scale;
    }

    // Marsaglia and Tsang method.
    private double SampleGamma(double shape)
    {
        if (shape <= 0)
            throw new ArgumentOutOfRangeException(nameof(shape));

        if (shape < 1.0)
        {
            double u = _rng.NextDouble();
            return SampleGamma(shape + 1.0) * Math.Pow(u, 1.0 / shape);
        }

        double d = shape - 1.0 / 3.0;
        double c = 1.0 / Math.Sqrt(9.0 * d);

        while (true)
        {
            double x = SampleStandardNormal();
            double v = 1.0 + c * x;
            if (v <= 0) continue;

            v = v * v * v;
            double u = _rng.NextDouble();

            if (u < 1.0 - 0.0331 * x * x * x * x)
                return d * v;

            if (Math.Log(u) < 0.5 * x * x + d * (1.0 - v + Math.Log(v)))
                return d * v;
        }
    }

    private double SampleStandardNormal()
    {
        double u1;
        do { u1 = _rng.NextDouble(); } while (u1 == 0.0);
        double u2 = _rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }
}
