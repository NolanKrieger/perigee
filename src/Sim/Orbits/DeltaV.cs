namespace Perigee.Sim;

/// <summary>
/// Δv estimates between bodies (Hohmann legs through the common ancestor, patched with v∞; no Oberth stacking across levels).
/// Used for market multipliers (GDD §10), exploration scoring (§3) and generator guarantees (§12). Not flight guidance.
/// </summary>
public static class DeltaV
{
    /// <summary>GDD §4: launch from the home surface to low home orbit including losses.</summary>
    public const double HomeSurfaceToLowOrbit = 2600;

    /// <summary>Radius of a "low orbit" of a body: above the atmosphere plus 10% of the radius.</summary>
    public static double LowOrbitRadius(Body b) => b.Radius * 1.1 + b.AtmoHeight;

    /// <summary>Low orbit of `from` → low orbit of `to`.</summary>
    public static double LowToLow(StarSystem sys, int from, int to)
    {
        if (from == to) return 0;
        var up = Chain(sys, from); var down = Chain(sys, to);
        int common = up.First(down.Contains);
        int iu = up.IndexOf(common), id = down.IndexOf(common);
        // Top-level children on each side (direct children of common), or -1 when that side is the common body itself.
        int topU = iu > 0 ? up[iu - 1] : -1;
        int topD = id > 0 ? down[id - 1] : -1;
        var c = sys[common];

        // Transfer in the common frame between the two top-level orbits (or a low orbit of common).
        double r1 = topU >= 0 ? MeanRadius(sys[topU]) : LowOrbitRadius(c);
        double r2 = topD >= 0 ? MeanRadius(sys[topD]) : LowOrbitRadius(c);
        double vinf1, vinf2;
        Hohmann(c.Gm, r1, r2, out vinf1, out vinf2);

        double dv = 0;
        // Ascend from `from` to topU, ending with v∞ = vinf1 relative to topU (or a plain burn at low common orbit).
        if (topU < 0) dv += vinf1;
        else dv += Ascend(sys, up, 0, iu - 1, vinf1);
        if (topD < 0) dv += vinf2;
        else dv += Ascend(sys, down, 0, id - 1, vinf2);
        return dv;
    }

    /// <summary>Home surface → low orbit of `to` (market node Δv).</summary>
    public static double HomeSurfaceToLowOrbitOf(StarSystem sys, int to) => HomeSurfaceToLowOrbit + LowToLow(sys, sys.HomeId, to);

    /// <summary>Low orbit → surface (or back): ~15% over circular speed for airless bodies; thin/thick atmospheres cut the landing burn.</summary>
    public static double LandingFromLowOrbit(Body b)
    {
        double v = b.CircularSpeed(LowOrbitRadius(b));
        if (b.Atmo == null) return 1.15 * v;
        double thick = MathD.Clamp(b.Atmo.Rho0 / 1.225, 0, 1);
        return v * (1.15 - 0.95 * thick) + 150;   // chutes do most of the work in thick air
    }

    /// <summary>Δv for the chain path[start..end] (each a child of the next), leaving the last with hyperbolic excess vinfTop relative to it.</summary>
    static double Ascend(StarSystem sys, List<int> path, int start, int end, double vinfTop)
    {
        double dv = 0;
        // Walk from the top down: the excess needed at level k is the departure burn from a low orbit of level k+... Do it bottom-up with the
        // required v∞ at each level computed top-down first.
        var need = new double[end - start + 1];
        need[end - start] = vinfTop;
        for (int k = end - 1; k >= start; k--)
        {
            // To leave body path[k+1]'s SOI with the excess need[k+1], a craft at path[k]'s orbit radius about path[k+1] must reach
            // v = sqrt(vesc² + vinf²) there; relative to path[k] that is a v∞ of |v - vcirc|.
            var parent = sys[path[k + 1]];
            double r = MeanRadius(sys[path[k]]);
            double vc = parent.CircularSpeed(r);
            double vneed = Math.Sqrt(2 * vc * vc + need[k + 1 - start] * need[k + 1 - start]);
            need[k - start] = Math.Abs(vneed - vc);
        }
        // Departure burn from low orbit of the bottom body.
        var b0 = sys[path[start]];
        double rl = LowOrbitRadius(b0);
        double vl = b0.CircularSpeed(rl);
        dv += Math.Sqrt(2 * vl * vl + need[0] * need[0]) - vl;
        return dv;
    }

    static double MeanRadius(Body b) => b.Orbit!.Value.IsEllipse ? b.Orbit.Value.A : b.Orbit.Value.Periapsis;

    static void Hohmann(double mu, double r1, double r2, out double dv1, out double dv2)
    {
        double a = 0.5 * (r1 + r2);
        double v1 = Math.Sqrt(mu / r1), v2 = Math.Sqrt(mu / r2);
        double vt1 = Math.Sqrt(mu * (2 / r1 - 1 / a)), vt2 = Math.Sqrt(mu * (2 / r2 - 1 / a));
        dv1 = Math.Abs(vt1 - v1); dv2 = Math.Abs(v2 - vt2);
    }

    static List<int> Chain(StarSystem sys, int id) { var l = new List<int>(); for (int b = id; b >= 0; b = sys[b].Parent) l.Add(b); return l; }
}
