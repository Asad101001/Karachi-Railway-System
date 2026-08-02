using KarachiRailway.Simulation.Models;

namespace KarachiRailway.Simulation.Engine;

/// <summary>
/// Computes analytical/approximate queue metrics for supported single-server models.
/// </summary>
public static class QueueMetricsCalculator
{
    public static (double Rho, double Lq, double L, double Wq, double W) Compute(
        QueueModelType modelType,
        double lambda,
        double mu,
        double serviceCv,
        double arrivalCv)
    {
        if (lambda <= 0) throw new ArgumentException("Arrival rate λ must be positive.", nameof(lambda));
        if (mu <= 0) throw new ArgumentException("Service rate μ must be positive.", nameof(mu));

        double rho = lambda / mu;
        if (rho >= 1.0)
            return (rho, double.NaN, double.NaN, double.NaN, double.NaN);

        double meanService = 1.0 / mu;

        return modelType switch
        {
            QueueModelType.MM1 => MM1Calculator.Compute(lambda, mu),
            QueueModelType.MG1 => ComputeMG1(lambda, meanService, serviceCv, rho),
            QueueModelType.GG1 => ComputeGG1(lambda, meanService, serviceCv, arrivalCv, rho),
            _ => MM1Calculator.Compute(lambda, mu),
        };
    }

    private static (double Rho, double Lq, double L, double Wq, double W) ComputeMG1(
        double lambda,
        double meanService,
        double serviceCv,
        double rho)
    {
        double serviceVariance = (serviceCv * serviceCv) * meanService * meanService;
        double secondMoment = serviceVariance + meanService * meanService;

        double wq = (lambda * secondMoment) / (2.0 * (1.0 - rho));
        double w = wq + meanService;
        double lq = lambda * wq;
        double l = lambda * w;

        return (rho, lq, l, wq, w);
    }

    private static (double Rho, double Lq, double L, double Wq, double W) ComputeGG1(
        double lambda,
        double meanService,
        double serviceCv,
        double arrivalCv,
        double rho)
    {
        double ca2 = arrivalCv * arrivalCv;
        double cs2 = serviceCv * serviceCv;

        double wq = (rho / (1.0 - rho)) * ((ca2 + cs2) / 2.0) * meanService;
        double w = wq + meanService;
        double lq = lambda * wq;
        double l = lambda * w;

        return (rho, lq, l, wq, w);
    }
}
