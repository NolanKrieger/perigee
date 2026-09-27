namespace Perigee.Sim;

/// <summary>
/// Atmosphere (GDD §4): drag per exposed part with a 2D shadow test along the airflow, flat-plate fin lift, a transonic drag
/// bump, parachutes, and the heat flux handed to <see cref="Heat"/>.
/// </summary>
public static class Aero
{
    public const double SpeedOfSound = 330;
    public const double ChuteOpenTime = 1.5;

    public static void Apply(World w, Craft c, Body body, double rho, double dt, ref Vec2d force, ref double torque, out double airspeed)
    {
        var vAtm = Flight.SurfaceVelocity(body, c.Pos);
        var vAir = c.Vel - vAtm;
        airspeed = vAir.Length;
        if (airspeed < 0.05) return;
        var dir = vAir / airspeed;          // direction of motion through the air
        var flow = -dir;                    // direction the air moves relative to the craft
        var lat = dir.Perp;                 // lateral axis for the shadow test
        double q = 0.5 * rho * airspeed * airspeed;
        double mach = airspeed / SpeedOfSound;
        double bump = 1 + 0.6 * Math.Exp(-MathD.Sq((mach - 1.05) / 0.25));

        // Project every part onto the lateral axis; front-most parts shadow the ones behind them.
        var items = new List<(Part p, double lo, double hi, double front, Vec2d centre)>();
        foreach (var p in c.Parts)
        {
            if (p.Destroyed) continue;
            double lo = double.PositiveInfinity, hi = double.NegativeInfinity, front = double.NegativeInfinity;
            foreach (var lv in p.LocalOutline())
            {
                var wv = (lv - c.Com).Rotated(c.Angle);
                double s = Vec2d.Dot(wv, lat), f = Vec2d.Dot(wv, dir);
                if (s < lo) lo = s; if (s > hi) hi = s; if (f > front) front = f;
            }
            items.Add((p, lo, hi, front, (p.Pos - c.Com).Rotated(c.Angle)));
        }
        items.Sort((a, b) => b.front.CompareTo(a.front));
        var covered = new List<(double lo, double hi)>();
        foreach (var it in items)
        {
            double exposed = ExposedLength(it.lo, it.hi, covered);
            covered.Add((it.lo, it.hi));
            var p = it.p;
            // Depth into the screen: fins and leg struts are thin; everything else is roughly as deep as it is wide.
            double depth = p.Def.Has(PartFlags.Fin | PartFlags.GridFin) ? 0.05 : p.Def.Has(PartFlags.Legs) ? 0.15 : p.Def.Width;
            double area = exposed * depth;
            p.ExposedArea = area;
            if (area <= 0) continue;
            double cd = p.Def.Cd * bump;
            var fd = flow * (q * cd * area);
            force += fd;
            torque += Vec2d.Cross(it.centre, fd);
            p.HeatFlux = Heat.Flux(rho, airspeed) * MathD.Clamp(exposed / Math.Max(it.hi - it.lo, 1e-6), 0, 1);
        }

        // Fins: flat-plate lift perpendicular to the flow, pushing the plate away from the side the air hits.
        var ex = c.RightDir;
        double alpha = Math.Atan2(Vec2d.Cross(dir, c.NoseDir), Vec2d.Dot(dir, c.NoseDir));   // angle of attack (signed)
        foreach (var p in c.Parts)
        {
            if (p.Destroyed || p.Def.FinArea <= 0) continue;
            if (p.Def.Has(PartFlags.GridFin) && !p.Deployed) continue;
            double cl = 1.1 * Math.Sin(2 * alpha);
            double lift = q * p.Def.FinArea * Math.Abs(cl);
            var fl = ex * (Math.Sign(Vec2d.Dot(ex, flow)) * lift);
            var arm = (p.Pos - c.Com).Rotated(c.Angle);
            force += fl;
            torque += Vec2d.Cross(arm, fl);
        }

        // Heat shields are blunt bodies that want to face the airflow: a restoring torque towards shield-first (SFS-style passive stability).
        foreach (var p in c.Parts)
        {
            if (p.Destroyed || !p.Def.Has(PartFlags.HeatShield)) continue;
            var faceLocal = p.Attach == AttachKind.Top ? new Vec2d(0, 1) : new Vec2d(0, -1);   // the free face points away from its parent
            var face = faceLocal.Rotated(c.Angle);
            double a = Math.Atan2(Vec2d.Cross(face, flow), Vec2d.Dot(face, flow));               // signed angle from the face to the oncoming air
            torque += q * p.Def.Width * p.Def.Width * 0.8 * Math.Sin(a) * p.Def.Width;
        }

        // Parachutes: canopy opens over 1.5 s; deploying above the safe speed tears it.
        foreach (var p in c.Parts)
        {
            if (p.Destroyed || !p.Def.Has(PartFlags.Parachute) || !p.Deployed) continue;
            if (p.ChuteRamp == 0 && airspeed > p.Def.ChuteMaxSpeed) { w.DestroyPart(c, p, $"{p.Def.Name} tore open at {airspeed:0} m/s"); continue; }
            p.ChuteRamp = Math.Min(1, p.ChuteRamp + dt / ChuteOpenTime);
            double area = p.Def.ChuteArea * p.ChuteRamp * (0.4 + 0.6 * p.ChuteRamp);
            var fc = flow * (q * area);
            var arm = (p.Pos - c.Com).Rotated(c.Angle) + c.NoseDir * (p.Def.Has(PartFlags.Drogue) ? 4 : 8);   // canopy rides above the part
            force += fc;
            torque += Vec2d.Cross(arm, fc);
        }
    }

    static double ExposedLength(double lo, double hi, List<(double lo, double hi)> covered)
    {
        if (hi <= lo) return 0;
        // Subtract the union of covering intervals.
        var segs = new List<(double a, double b)> { (lo, hi) };
        foreach (var (clo, chi) in covered)
        {
            var next = new List<(double a, double b)>();
            foreach (var (a, b) in segs)
            {
                if (chi <= a || clo >= b) { next.Add((a, b)); continue; }
                if (clo > a) next.Add((a, clo));
                if (chi < b) next.Add((chi, b));
            }
            segs = next;
            if (segs.Count == 0) break;
        }
        double len = 0; foreach (var (a, b) in segs) len += b - a;
        return len;
    }
}

/// <summary>Reentry heating (GDD §4): flux ∝ √ρ·v³ on exposed leading parts; parts store heat and radiate; heat shields ablate.</summary>
public static class Heat
{
    /// <summary>Sutton–Graves form scaled for this world's 2 km/s orbits (BALANCE.md).</summary>
    public const double K = 3.2e-3;
    public const double Ambient = 250;
    public const double HeatCapacity = 900;      // J/(kg·K)
    public const double SkinFraction = 0.15;     // share of a part's mass that heats up on entry (skin, not the whole tank)
    public const double AblatorHeat = 2.5e6;     // J/kg of ablator burned
    public const double ShieldHoldTemp = 1400;   // K: ablating shields sit here until the ablator is gone

    public static double Flux(double rho, double v) => K * Math.Sqrt(rho) * v * v * v;

    /// <summary>
    /// Stagnation (recovery) temperature of the air hitting a part: no part can be heated above it. Physically 290 K + v²/2cp;
    /// with cp halved so this world's 2 km/s entries bite like Earth's 8 km/s ones (BALANCE.md): 1 km/s ascent → 1,290 K ceiling
    /// reached only after minutes, 1.9 km/s entry → 3,900 K ceiling that bare tanks hit in ~20 s.
    /// </summary>
    public static double StagnationTemp(double airspeed) => 290 + airspeed * airspeed / 1000;

    public static void Apply(Craft c, double rho, double airspeed, double dt)
    {
        double tStag = StagnationTemp(airspeed);
        foreach (var p in c.Parts)
        {
            if (p.Destroyed) continue;
            double area = 2 * p.Def.Width * p.Def.Height + 1e-3;
            double capacity = Math.Max(p.Mass * SkinFraction, 0.04) * 1000 * HeatCapacity;   // floor: even a 50 kg nose cap has ~40 kg of skin/structure soaking heat
            // Heating drives the part towards the stagnation temperature and stops there (ascent at 1 km/s tops out near 800 K;
            // orbital entry at 1.9 km/s can reach 2,000 K, which only shields survive).
            double drive = MathD.Clamp((tStag - p.Temp) / Math.Max(tStag - 290, 1), 0, 1);
            double qin = p.HeatFlux * p.ExposedArea * drive;
            double qrad = 5.67e-8 * 0.85 * area * (Math.Pow(p.Temp, 4) - Math.Pow(Ambient, 4));
            // Dense air carries heat away (cooling on the pad and under chutes); it scales with density so it is nothing at entry altitudes.
            double qconv = rho > 0 ? 12 * (1 + airspeed * 0.02) * (rho / 1.225) * area * (p.Temp - 290) : 0;
            double net = qin - qrad - qconv;
            if (p.Def.Has(PartFlags.HeatShield) && p.Ablator > 0 && p.Temp >= ShieldHoldTemp && net > 0)
            {
                p.Ablator = Math.Max(0, p.Ablator - net * dt / AblatorHeat / 1000);
                net = 0;
            }
            p.Temp = Math.Max(Ambient, p.Temp + net / capacity * dt);
        }
    }
}
