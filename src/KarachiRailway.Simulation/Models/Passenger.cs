namespace KarachiRailway.Simulation.Models;

/// <summary>
/// Tracks a single passenger's journey through the system.
/// </summary>
public class Passenger
{
    public int Id { get; init; }

    /// <summary>Formatted ID for UI presentation (e.g., C001).</summary>
    public string CustomerId => $"CUS-{Id:D4}";

    /// <summary>Simulation-time (minutes) when the passenger arrived.</summary>
    public double ArrivalTime { get; init; }

    /// <summary>Simulation-time (minutes) when service (ticket-counter or equiv.) started.</summary>
    public double ServiceStartTime { get; set; }

    /// <summary>Simulation-time (minutes) when the passenger exited the system (either completed or left).</summary>
    public double ExitTime { get; set; }

    /// <summary>Ordered list of steps this passenger went through.</summary>
    public List<PassengerStep> StepTrace { get; } = new();

    /// <summary>Whether the passenger successfully boarded and departed.</summary>
    public bool Completed { get; set; }

    /// <summary>Whether the passenger left the system without boarding.</summary>
    public bool LeftSystem { get; set; }

    /// <summary>Time spent waiting in queue before service started.</summary>
    public double WaitTime => ServiceStartTime - ArrivalTime;

    /// <summary>Total time spent in the system.</summary>
    public double SystemTime => ExitTime - ArrivalTime;

    public override string ToString() =>
        $"Passenger #{Id} | Arrived: {ArrivalTime:F2} | " +
        $"Wait: {WaitTime:F2} | System: {SystemTime:F2} | " +
        $"{(Completed ? "✔ Completed" : "✘ Left System")}";
}
