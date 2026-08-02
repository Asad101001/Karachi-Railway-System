using System.ComponentModel.DataAnnotations;

namespace KarachiRailway.Simulation.Models;

/// <summary>
/// All configurable parameters for one simulation run.
/// Includes validation annotations that the UI layer can surface.
/// </summary>
public class SimulationParameters
{
    /// <summary>Queueing model used by the simulator.</summary>
    public QueueModelType ModelType { get; set; } = QueueModelType.MM1;

    // ── M/M/1 Rates ─────────────────────────────────────────────────────────

    /// <summary>Passenger arrival rate λ (passengers per minute).</summary>
    [Range(0.01, double.MaxValue, ErrorMessage = "Arrival rate (λ) must be greater than 0.")]
    public double ArrivalRate { get; set; } = 8.0;

    /// <summary>Service rate μ (passengers per minute).</summary>
    [Range(0.01, double.MaxValue, ErrorMessage = "Service rate (μ) must be greater than 0.")]
    public double ServiceRate { get; set; } = 10.0;

    /// <summary>
    /// Service-time coefficient of variation (C_s).
    /// For M/M/1 this is ignored (fixed to exponential, C_s = 1).
    /// For M/G/1 and G/G/1 this controls service variability.
    /// </summary>
    [Range(0.01, 10.0, ErrorMessage = "Service CV must be > 0.")]
    public double ServiceCv { get; set; } = 1.5;

    /// <summary>
    /// Inter-arrival-time coefficient of variation (C_a).
    /// Used only for G/G/1.
    /// </summary>
    [Range(0.01, 10.0, ErrorMessage = "Arrival CV must be > 0.")]
    public double ArrivalCv { get; set; } = 0.8;

    // ── Simulation Duration ──────────────────────────────────────────────────

    /// <summary>Total simulated time in minutes.</summary>
    [Range(1, int.MaxValue, ErrorMessage = "Simulation duration must be at least 1 minute.")]
    public int SimulationDurationMinutes { get; set; } = 120;

    // ── Flow Probabilities ───────────────────────────────────────────────────

    /// <summary>Probability that a passenger already has/needs a ticket (direct to counter).</summary>
    [Range(0.0, 1.0, ErrorMessage = "Probabilities must be between 0 and 1.")]
    public double TicketRequiredProbability { get; set; } = 0.65;

    /// <summary>Probability that a passenger at the inquiry desk decides to buy a ticket.</summary>
    [Range(0.0, 1.0, ErrorMessage = "Probabilities must be between 0 and 1.")]
    public double BuyTicketProbability { get; set; } = 0.80;

    /// <summary>
    /// Probability that a passenger at the payment stage uses a card
    /// (as opposed to cash).
    /// </summary>
    [Range(0.0, 1.0, ErrorMessage = "Probabilities must be between 0 and 1.")]
    public double CardUsageProbability { get; set; } = 0.45;

    /// <summary>Probability that a passenger's card is valid.</summary>
    [Range(0.0, 1.0, ErrorMessage = "Probabilities must be between 0 and 1.")]
    public double CardValidProbability { get; set; } = 0.95;

    /// <summary>Probability that a passenger's bank account is valid.</summary>
    [Range(0.0, 1.0, ErrorMessage = "Probabilities must be between 0 and 1.")]
    public double AccountValidProbability { get; set; } = 0.97;

    /// <summary>
    /// Probability that a passenger has sufficient funds (applies to both
    /// cash-sufficient-funds and card-funds-available checks).
    /// </summary>
    [Range(0.0, 1.0, ErrorMessage = "Probabilities must be between 0 and 1.")]
    public double SufficientFundsProbability { get; set; } = 0.90;

    // ── Derived / Computed ───────────────────────────────────────────────────

    /// <summary>Server utilisation ρ = λ / μ.</summary>
    public double Utilization => ServiceRate > 0 ? ArrivalRate / ServiceRate : double.NaN;

    /// <summary>True when the system is stable (ρ &lt; 1).</summary>
    public bool IsStable => Utilization < 1.0;

    /// <summary>Returns a deep copy with the same parameter values.</summary>
    public SimulationParameters Clone() => new()
    {
        ModelType = ModelType,
        ArrivalRate = ArrivalRate,
        ServiceRate = ServiceRate,
        ServiceCv = ServiceCv,
        ArrivalCv = ArrivalCv,
        SimulationDurationMinutes = SimulationDurationMinutes,
        TicketRequiredProbability = TicketRequiredProbability,
        BuyTicketProbability = BuyTicketProbability,
        CardUsageProbability = CardUsageProbability,
        CardValidProbability = CardValidProbability,
        AccountValidProbability = AccountValidProbability,
        SufficientFundsProbability = SufficientFundsProbability,
    };
}
