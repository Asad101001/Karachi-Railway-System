using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace KarachiRailway.Desktop.ViewModels;

/// <summary>
/// Represents one customer row in the M/M/1 modelling spreadsheet table.
/// Matches the column layout shown in the reference simulation images.
/// </summary>
public sealed class ModellingRowViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    // ── Identity ─────────────────────────────────────────────────────────────
    public int    SNo        { get; init; }  // Serial number (customer #)

    // ── Cumulative Probability Distribution ──────────────────────────────────
    public double CumProb       { get; init; }  // Cumulative probability
    public double CumProbLookup { get; init; }  // Lookup value (previous row's cum prob)

    // ── Inter-Arrival ─────────────────────────────────────────────────────────
    public int    MinsBetweenArrivals { get; init; }  // Integer minutes between arrivals

    // ── Random Number & Derived Values ───────────────────────────────────────
    public double RandomInterArrival { get; init; }  // Random U[0,1] for inter-arrival
    public int    PoissonArrival     { get; init; }  // Drawn from Poisson / lookup table

    // ── Service ───────────────────────────────────────────────────────────────
    public double RandomService  { get; init; }  // Random U[0,1] for service time
    public double ExpService     { get; init; }  // Exponential service time (minutes)

    // ── Event Timeline ────────────────────────────────────────────────────────
    public double ArrivalTime    { get; init; }  // Cumulative arrival time
    public double StartTime      { get; init; }  // Service start time
    public double EndTime        { get; init; }  // Service end time

    // ── Performance Measures ─────────────────────────────────────────────────
    public double TurnaroundTime { get; init; }  // End - Arrival
    public double WaitTime       { get; init; }  // Start - Arrival (queue wait)
    public double ResponseTime   { get; init; }  // End - Start (= service time)

    // ── Computed background colors for zebra striping ─────────────────────────
    public string RowBackground => SNo % 2 == 0 ? "#F8FAF8" : "#FFFFFF";
}
