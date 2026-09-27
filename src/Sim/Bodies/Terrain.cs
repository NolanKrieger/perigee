namespace Perigee.Sim;

/// <summary>Radial heightmaps (GDD §4): noise around the circumference, oceans at sea level on home, a flat launch site on land.</summary>
public static class Terrain
{
    public static int SamplesFor(Body b)
    {
        double circ = MathD.TwoPi * b.Radius;
        int n = (int)MathD.Clamp(circ / 90.0, 1024, 32768);
        return n;
    }

    public static void Generate(Body b, Rng rng)
    {
        if (b.Type == BodyType.Star) { b.Terrain = new double[64]; return; }
        int n = SamplesFor(b);
        double amp = b.Type switch
        {
            BodyType.Home => 6000, BodyType.Rocky => 5000, BodyType.Desert => 7000, BodyType.Moon => 4000, BodyType.IceMoon => 2500,
            BodyType.Asteroid => Math.Max(80, b.Radius * 0.12), BodyType.Comet => Math.Max(60, b.Radius * 0.15), BodyType.IceDwarf => 3000,
            BodyType.GasGiant => 0, BodyType.IceGiant => 0, _ => 3000
        };
        var h = Noise1D.Fractal(rng, n, 12, 5, 0.5);
        var t = new double[n];
        for (int i = 0; i < n; i++) t[i] = h[i] * amp;
        if (b.Type == BodyType.Home)
        {
            // Continents/oceans: push the distribution so ~55% of the circumference is below sea level.
            for (int i = 0; i < n; i++) t[i] = (h[i] - 0.05) * amp;
            b.SeaLevel = 0;
            // Flat launch site: the widest land stretch's centre; flatten ±20 km around it at +40 m.
            int best = 0, bestLen = 0, runStart = 0, run = 0;
            for (int i = 0; i < 2 * n; i++)
            {
                if (t[i % n] > 150) { if (run == 0) runStart = i; run++; if (run > bestLen) { bestLen = run; best = runStart; } }
                else run = 0;
            }
            int centre = (best + bestLen / 2) % n;
            double flatHalf = 20_000 / (MathD.TwoPi * b.Radius) * n;
            double blendHalf = flatHalf * 2.5;
            for (int k = -(int)blendHalf; k <= (int)blendHalf; k++)
            {
                int i = ((centre + k) % n + n) % n;
                double d = Math.Abs(k);
                double w = d <= flatHalf ? 1 : 1 - (d - flatHalf) / (blendHalf - flatHalf);
                t[i] = t[i] * (1 - w) + 40 * w;
            }
            b.LaunchSiteAngle = centre / (double)n * MathD.TwoPi;
        }
        b.Terrain = t;
        b.TerrainMax = t.Max();
    }
}
