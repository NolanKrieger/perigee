namespace Perigee.Sim;

/// <summary>Seeded 1D value noise with smooth interpolation, periodic over `period` samples so it wraps around a circumference.</summary>
public sealed class Noise1D
{
    readonly double[] _values;
    public Noise1D(Rng rng, int period)
    {
        _values = new double[period];
        for (int i = 0; i < period; i++) _values[i] = rng.NextDouble() * 2 - 1;
    }

    /// <summary>Noise at a position in "lattice units"; wraps every `period` units. Returns roughly [-1, 1].</summary>
    public double At(double x)
    {
        int n = _values.Length;
        double u = x % n; if (u < 0) u += n;
        int i = (int)Math.Floor(u); double f = u - i;
        f = f * f * (3 - 2 * f);
        int i0 = ((i - 1) % n + n) % n, i1 = i % n, i2 = (i + 1) % n, i3 = (i + 2) % n;
        // Catmull-Rom between the two centre samples for a rounder look than plain lerp.
        double p0 = _values[i0], p1 = _values[i1], p2 = _values[i2], p3 = _values[i3];
        double t = f;
        return 0.5 * (2 * p1 + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (-p0 + 3 * p1 - 3 * p2 + p3) * t * t * t);
    }

    /// <summary>Fractal sum of `octaves` layers; each `angle` in [0, 2π) maps to the wrapped lattice.</summary>
    public static double[] Fractal(Rng rng, int samples, int baseFrequency, int octaves, double persistence)
    {
        var result = new double[samples];
        double amp = 1, total = 0;
        int freq = baseFrequency;
        for (int o = 0; o < octaves; o++)
        {
            var layer = new Noise1D(rng, Math.Max(2, freq));
            for (int i = 0; i < samples; i++) result[i] += amp * layer.At(i / (double)samples * freq);
            total += amp; amp *= persistence; freq *= 2;
        }
        for (int i = 0; i < samples; i++) result[i] /= total;
        return result;
    }
}
