namespace KarachiRailway.Simulation.Engine;

/// <summary>
/// Computes steady-state analytical formulas for an M/M/1 queue.
/// </summary>
public static class MM1Calculator
{
    /// <summary>
    /// Computes all steady-state M/M/1 metrics given arrival rate λ and service rate μ.
    /// </summary>
    /// <param name="lambda">Arrival rate λ (passengers per minute). Must be &gt; 0.</param>
    /// <param name="mu">Service rate μ (passengers per minute). Must be &gt; 0.</param>
    /// <returns>Named tuple with all standard KPIs.</returns>
    /// <exception cref="ArgumentException">Thrown when rates are non-positive.</exception>
    public static (double Rho, double Lq, double L, double Wq, double W)
        Compute(double lambda, double mu)
    {
        if (lambda <= 0) throw new ArgumentException("Arrival rate λ must be positive.", nameof(lambda));
        if (mu <= 0)    throw new ArgumentException("Service rate μ must be positive.", nameof(mu));

        double rho = lambda / mu;

        if (rho >= 1.0)
        {
            // Unstable system – return NaN for all queue metrics
            return (rho, double.NaN, double.NaN, double.NaN, double.NaN);
        }

        // Average queue length (excluding any in service)
        double lq = (rho * rho) / (1.0 - rho);

        // Average number in system
        double l = rho / (1.0 - rho);

        // Average wait time in queue
        double wq = lq / lambda;

        // Average time in system
        double w = l / lambda;

        return (rho, lq, l, wq, w);
    }

    /// <summary>
    /// Returns <c>true</c> when the M/M/1 queue is stable (ρ &lt; 1).
    /// </summary>
    public static bool IsStable(double lambda, double mu) =>
        mu > 0 && (lambda / mu) < 1.0;
}
