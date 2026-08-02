namespace KarachiRailway.Desktop.ViewModels;

/// <summary>
/// Summary record for one completed simulation run, shown in the Reports tab.
/// </summary>
public sealed class SimulationRunSummary
{
    public int    RunNumber  { get; init; }
    public string ModelName  { get; init; } = string.Empty;
    public double Lambda     { get; init; }   // Arrival rate λ
    public double Mu         { get; init; }   // Service rate μ
    public double Rho        { get; init; }   // Utilization ρ
    public double Lq         { get; init; }   // Avg queue length
    public double Wq         { get; init; }   // Avg queue wait (min)
    public double W          { get; init; }   // Avg system time (min)
    public double L          { get; init; }   // Avg number in system
    public int    TotalServed   { get; init; }
    public int    TotalLeft     { get; init; }
    public int    TotalArrived  { get; init; }
    public double Throughput    { get; init; }
    public double CompletionPct { get; init; }
    public double SimDuration   { get; init; }
    public string Timestamp     { get; init; } = string.Empty;

    // ── Formatted display helpers ─────────────────────────────────────────────
    public string RhoFormatted        => $"{Rho:F4}";
    public string LqFormatted         => $"{Lq:F3}";
    public string WqFormatted         => $"{Wq:F3}";
    public string WFormatted          => $"{W:F3}";
    public string LFormatted          => $"{L:F3}";
    public string ThroughputFormatted => $"{Throughput:F3}";
    public string CompletionFormatted => $"{CompletionPct:F1}%";

    // Color-coded utilization badge
    public string RhoColor => Rho switch
    {
        < 0.5  => "#22C55E",   // green – low load
        < 0.8  => "#EAB308",   // gold – medium load
        < 1.0  => "#F97316",   // orange – high load
        _      => "#EF4444",   // red – overloaded
    };
}
