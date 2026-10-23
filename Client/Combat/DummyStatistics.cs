namespace AionDPS.Combat;

/// <summary>Welch's t-test of "B minus A": the difference, its standard error, t, the Welch-Satterthwaite degrees of
/// freedom, the two-sided p and the 95 % interval of the difference.</summary>
public readonly record struct WelchResult(double Difference, double StandardError, double T, double Df, double P, double Low, double High)
{
    /// <summary>True when the interval does not contain 0.</summary>
    public bool Significant => Low > 0 || High < 0;
}

/// <summary>A rate with its Wilson interval (see <see cref="DummyStatistics.Wilson"/>).</summary>
public readonly record struct RateInterval(double Rate, double Low, double High);

/// <summary>A least-squares line y = Intercept + Slope * x. The standard error and interval of the slope need at
/// least three points (two points fit any line exactly), otherwise they are null.</summary>
public readonly record struct LineFit(int N, double Slope, double Intercept, double? SlopeStandardError, double? SlopeLow, double? SlopeHigh);

/// <summary>
/// The small statistics kit behind the dummy test: mean, sample SD, median, Welch's t-test, the Wilson interval, a
/// least-squares slope with its error. Pure and self-contained (no library on the machine to lean on), checked
/// against hand-computed values in <see cref="SelfCheckDummyTest"/>. The Student t distribution is the exact
/// one (regularized incomplete beta, Numerical Recipes' continued fraction), switching to the normal only for
/// df above 2000 where the two cannot be told apart.
/// </summary>
public static class DummyStatistics
{
    /// <summary>The z of a 95 % two-sided interval under the normal.</summary>
    public const double Z95 = 1.959963984540054;

    /// <summary>Mean; 0 for an empty list (callers check the count first).</summary>
    public static double Mean(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        double sum = 0;
        foreach (double v in values)
        {
            sum += v;
        }

        return sum / values.Count;
    }

    /// <summary>Sample standard deviation (n - 1 in the divisor); 0 below two values.</summary>
    public static double SampleSd(IReadOnlyList<double> values) => Math.Sqrt(SampleVariance(values));

    public static double SampleVariance(IReadOnlyList<double> values)
    {
        if (values.Count < 2)
        {
            return 0;
        }

        double mean = Mean(values);
        double sum = 0;
        foreach (double v in values)
        {
            sum += (v - mean) * (v - mean);
        }

        return sum / (values.Count - 1);
    }

    /// <summary>Median (the mean of the two middle values for an even count); 0 for an empty list.</summary>
    public static double Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        double[] sorted = values.ToArray();
        Array.Sort(sorted);
        int mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }

    /// <summary>Two-sided p of a Student t with <paramref name="df"/> degrees of freedom: P(|T| &gt;= |t|).</summary>
    public static double StudentTwoSidedP(double t, double df)
    {
        if (double.IsNaN(t) || df <= 0)
        {
            return 1;
        }

        if (double.IsInfinity(t))
        {
            return 0;
        }

        if (df > 2000)
        {
            return NormalTwoSidedP(t);
        }

        return RegularizedBeta(df / 2, 0.5, df / (df + t * t));
    }

    /// <summary>The t that leaves <c>1 - confidence</c> in the two tails (2.228 for df 10 at 95 %). Bisection on
    /// <see cref="StudentTwoSidedP"/>; the normal quantile for df above 2000.</summary>
    public static double StudentCritical(double df, double confidence = 0.95)
    {
        double alpha = 1 - confidence;
        if (df > 2000)
        {
            // Normal quantile by the same bisection on the normal tail.
            return Bisect(z => NormalTwoSidedP(z), alpha);
        }

        return Bisect(t => StudentTwoSidedP(t, Math.Max(df, 0.1)), alpha);
    }

    private static double Bisect(Func<double, double> twoSidedP, double alpha)
    {
        double lo = 0, hi = 1;
        while (twoSidedP(hi) > alpha && hi < 1e7)
        {
            hi *= 2;
        }

        for (int i = 0; i < 100; i++)
        {
            double mid = (lo + hi) / 2;
            if (twoSidedP(mid) > alpha)
            {
                lo = mid;
            }
            else
            {
                hi = mid;
            }
        }

        return (lo + hi) / 2;
    }

    /// <summary>P(|Z| &gt;= |z|) under the standard normal (erfc, Numerical Recipes' Chebyshev fit, error about 1e-7).</summary>
    public static double NormalTwoSidedP(double z)
    {
        double x = Math.Abs(z) / Math.Sqrt(2);
        double t = 1 / (1 + 0.5 * x);
        double poly = -x * x - 1.26551223 + t * (1.00002368 + t * (0.37409196 + t * (0.09678418 + t * (-0.18628806
            + t * (0.27886807 + t * (-1.13520398 + t * (1.48851587 + t * (-0.82215223 + t * 0.17087277))))))));
        return Math.Min(1, t * Math.Exp(poly));
    }

    /// <summary>Welch's t-test of B against A from summary numbers (sample SDs). Null below two values on a side.
    /// The interval is the difference +/- the Student critical value at the Welch-Satterthwaite df times the error.</summary>
    public static WelchResult? Welch(double meanA, double sdA, int nA, double meanB, double sdB, int nB, double confidence = 0.95)
    {
        if (nA < 2 || nB < 2)
        {
            return null;
        }

        double diff = meanB - meanA;
        double va = sdA * sdA / nA;
        double vb = sdB * sdB / nB;
        double se = Math.Sqrt(va + vb);
        if (se <= 0)
        {
            // No spread at all on either side: the difference is exact.
            double dfExact = nA + nB - 2;
            return new WelchResult(diff, 0, diff == 0 ? 0 : (diff > 0 ? double.PositiveInfinity : double.NegativeInfinity), dfExact, diff == 0 ? 1 : 0, diff, diff);
        }

        double t = diff / se;
        double df = (va + vb) * (va + vb) / (va * va / (nA - 1) + vb * vb / (nB - 1));
        double p = StudentTwoSidedP(t, df);
        double crit = StudentCritical(df, confidence);
        return new WelchResult(diff, se, t, df, p, diff - crit * se, diff + crit * se);
    }

    /// <summary>Welch's t-test on two raw samples.</summary>
    public static WelchResult? Welch(IReadOnlyList<double> a, IReadOnlyList<double> b) =>
        Welch(Mean(a), SampleSd(a), a.Count, Mean(b), SampleSd(b), b.Count);

    /// <summary>Wilson score interval for <paramref name="successes"/> out of <paramref name="n"/> (z = 1.96 for 95 %).
    /// With n = 0 the interval is the whole 0..1: nothing is known.</summary>
    public static RateInterval Wilson(int successes, int n, double z = Z95)
    {
        if (n <= 0)
        {
            return new RateInterval(0, 0, 1);
        }

        double p = (double)successes / n;
        double z2 = z * z;
        double denom = 1 + z2 / n;
        double center = (p + z2 / (2 * n)) / denom;
        double half = z * Math.Sqrt(p * (1 - p) / n + z2 / (4.0 * n * n)) / denom;
        return new RateInterval(p, Math.Max(0, center - half), Math.Min(1, center + half));
    }

    /// <summary>Two-sided p that two rates (k1 of n1, k2 of n2) are the same (pooled two-proportion z-test).</summary>
    public static double TwoProportionP(int k1, int n1, int k2, int n2)
    {
        if (n1 <= 0 || n2 <= 0)
        {
            return 1;
        }

        double pooled = (double)(k1 + k2) / (n1 + n2);
        double se = Math.Sqrt(pooled * (1 - pooled) * (1.0 / n1 + 1.0 / n2));
        if (se <= 0)
        {
            return 1;
        }

        return NormalTwoSidedP(((double)k2 / n2 - (double)k1 / n1) / se);
    }

    /// <summary>Ordinary least squares of <paramref name="ys"/> on <paramref name="xs"/>. Null below two points or when
    /// every x is the same. The slope's standard error (residual variance with n - 2 degrees of freedom) needs three points.</summary>
    public static LineFit? LinearFit(IReadOnlyList<double> xs, IReadOnlyList<double> ys)
    {
        int n = Math.Min(xs.Count, ys.Count);
        if (n < 2)
        {
            return null;
        }

        double mx = 0, my = 0;
        for (int i = 0; i < n; i++)
        {
            mx += xs[i];
            my += ys[i];
        }

        mx /= n;
        my /= n;
        double sxx = 0, sxy = 0;
        for (int i = 0; i < n; i++)
        {
            sxx += (xs[i] - mx) * (xs[i] - mx);
            sxy += (xs[i] - mx) * (ys[i] - my);
        }

        if (sxx <= 0)
        {
            return null;
        }

        double slope = sxy / sxx;
        double intercept = my - slope * mx;
        if (n < 3)
        {
            return new LineFit(n, slope, intercept, null, null, null);
        }

        double sse = 0;
        for (int i = 0; i < n; i++)
        {
            double r = ys[i] - (intercept + slope * xs[i]);
            sse += r * r;
        }

        double se = Math.Sqrt(sse / (n - 2) / sxx);
        double crit = StudentCritical(n - 2);
        return new LineFit(n, slope, intercept, se, slope - crit * se, slope + crit * se);
    }

    /// <summary>Regularized incomplete beta function I_x(a, b) (Numerical Recipes betai).</summary>
    private static double RegularizedBeta(double a, double b, double x)
    {
        if (x <= 0)
        {
            return 0;
        }

        if (x >= 1)
        {
            return 1;
        }

        double bt = Math.Exp(LogGamma(a + b) - LogGamma(a) - LogGamma(b) + a * Math.Log(x) + b * Math.Log(1 - x));
        return x < (a + 1) / (a + b + 2)
            ? bt * BetaContinuedFraction(a, b, x) / a
            : 1 - bt * BetaContinuedFraction(b, a, 1 - x) / b;
    }

    /// <summary>Continued fraction of the incomplete beta by the modified Lentz method.</summary>
    private static double BetaContinuedFraction(double a, double b, double x)
    {
        const double Tiny = 1e-300;
        double qab = a + b, qap = a + 1, qam = a - 1;
        double c = 1;
        double d = 1 - qab * x / qap;
        if (Math.Abs(d) < Tiny)
        {
            d = Tiny;
        }

        d = 1 / d;
        double h = d;
        for (int m = 1; m <= 1000; m++)
        {
            int m2 = 2 * m;
            double aa = m * (b - m) * x / ((qam + m2) * (a + m2));
            d = 1 + aa * d;
            if (Math.Abs(d) < Tiny)
            {
                d = Tiny;
            }

            c = 1 + aa / c;
            if (Math.Abs(c) < Tiny)
            {
                c = Tiny;
            }

            d = 1 / d;
            h *= d * c;
            aa = -(a + m) * (qab + m) * x / ((a + m2) * (qap + m2));
            d = 1 + aa * d;
            if (Math.Abs(d) < Tiny)
            {
                d = Tiny;
            }

            c = 1 + aa / c;
            if (Math.Abs(c) < Tiny)
            {
                c = Tiny;
            }

            d = 1 / d;
            double del = d * c;
            h *= del;
            if (Math.Abs(del - 1) < 3e-14)
            {
                break;
            }
        }

        return h;
    }

    private static readonly double[] LanczosCoefficients =
    {
        0.99999999999980993, 676.5203681218851, -1259.1392167224028, 771.32342877765313,
        -176.61502916214059, 12.507343278686905, -0.13857109526572012, 9.9843695780195716e-6, 1.5056327351493116e-7,
    };

    /// <summary>ln Gamma(x) for x &gt; 0 (Lanczos, g = 7).</summary>
    private static double LogGamma(double x)
    {
        if (x < 0.5)
        {
            return Math.Log(Math.PI / Math.Sin(Math.PI * x)) - LogGamma(1 - x);
        }

        x -= 1;
        double a = LanczosCoefficients[0];
        double t = x + 7.5;
        for (int i = 1; i < 9; i++)
        {
            a += LanczosCoefficients[i] / (x + i);
        }

        return 0.5 * Math.Log(2 * Math.PI) + (x + 0.5) * Math.Log(t) - t + Math.Log(a);
    }
}
