namespace Perigee.Sim;

/// <summary>
/// Procedural star systems (GDD §12): a G/K star, 6–10 planets (inner rocky, home, Mars-likes, a belt, gas giants, ice giants,
/// icy dwarfs), 8–25 moons, 1–3 comets, terrain, palettes and hidden resource deposits, then the guarantees: ice + ore within
/// ≈ 2,500 m/s of low home orbit and a CO₂ source within ~5 km/s. Deterministic per seed. Orbits and rotations run clockwise.
/// </summary>
public static class SystemGenerator
{
    public const double IceOreBudget = 2500;     // m/s from low home orbit to the surface (GDD §12 proposal)
    public const double Co2Budget = 5000;        // m/s
    public const double MaxReachBudget = 16000;  // every body reachable within this from low home orbit

    public static StarSystem Generate(ulong seed)
    {
        var rng = new Rng(seed);
        var names = new NameGen(rng.Fork());
        var sys = new StarSystem { Seed = seed };

        // ---- star: home sits where a year is 400 days; luminosity sets solar-panel output (1/d²).
        bool kType = rng.Chance(0.35);
        double lum = kType ? rng.Range(0.3, 0.7) : rng.Range(0.8, 1.3);
        double aHome = 1.71e10 * Math.Sqrt(lum);                     // habitable-zone distance, scaled world
        double starGm = 4 * Math.PI * Math.PI * aHome * aHome * aHome / (Units.Year * Units.Year);
        var star = new Body { Name = names.Next(2, 3), Type = BodyType.Star, Gm = starGm, Radius = 3.5e7 * Math.Pow(lum, 0.4), Luminosity = lum, PaletteIndex = kType ? 1 : 0 };
        sys.Add(star);
        sys.StarName = star.Name;
        sys.StormRateFactor = rng.Range(0.7, 1.4) * (kType ? 1.15 : 1.0);

        // ---- planet slots, inner to outer.
        int inner = rng.Next(0, 4), marsLike = rng.Next(0, 3), giants = rng.Next(1, 4), iceGiants = rng.Next(0, 3);
        int total = 1 + inner + marsLike + giants + iceGiants;
        int dwarfs = Math.Max(0, Math.Min(2, 10 - total));
        if (total + dwarfs < 6) dwarfs = 6 - total;
        var slots = new List<BodyType>();
        for (int i = 0; i < inner; i++) slots.Add(BodyType.Rocky);
        slots.Add(BodyType.Home);
        for (int i = 0; i < marsLike; i++) slots.Add(BodyType.Desert);
        for (int i = 0; i < giants; i++) slots.Add(BodyType.GasGiant);
        for (int i = 0; i < iceGiants; i++) slots.Add(BodyType.IceGiant);
        for (int i = 0; i < dwarfs; i++) slots.Add(BodyType.IceDwarf);
        while (slots.Count > 10) slots.RemoveAt(slots.Count - 1);

        // Orbit radii: geometric spacing outward from home, inward for inner planets.
        int homeIndex = slots.IndexOf(BodyType.Home);
        var radii = new double[slots.Count];
        radii[homeIndex] = aHome;
        for (int i = homeIndex - 1; i >= 0; i--) radii[i] = radii[i + 1] / rng.Range(1.45, 1.9);
        for (int i = homeIndex + 1; i < slots.Count; i++) radii[i] = Math.Min(radii[i - 1] * rng.Range(1.5, 2.3), 40 * aHome);
        for (int i = homeIndex + 2; i < slots.Count; i++) if (radii[i] < radii[i - 1] * 1.25) radii[i] = radii[i - 1] * 1.25;

        var planets = new List<Body>();
        for (int i = 0; i < slots.Count; i++)
        {
            var b = MakePlanet(slots[i], rng, names);
            b.Parent = star.Id;
            double e = slots[i] == BodyType.Home ? rng.Range(0.005, 0.03) : rng.Range(0.01, 0.12);
            b.Orbit = new Conic(star.Gm, radii[i] * (1 - e * e), e, rng.Range(0, MathD.TwoPi), rng.Range(-Units.Year, 0), -1);
            sys.Add(b);
            if (slots[i] == BodyType.Home) sys.HomeId = b.Id;
            planets.Add(b);
        }
        EnforcePlanetSpacing(planets, star, rng);

        // ---- moons
        foreach (var p in planets)
        {
            int count = p.Type switch
            {
                BodyType.Home => rng.Chance(0.4) ? 0 : 1,
                BodyType.GasGiant => rng.Next(2, 7),
                BodyType.IceGiant => rng.Next(1, 5),
                BodyType.Desert => rng.Next(0, 3),
                BodyType.Rocky => rng.Chance(0.3) ? 1 : 0,
                BodyType.IceDwarf => rng.Chance(0.4) ? 1 : 0,
                _ => 0
            };
            AddMoons(sys, p, count, rng, names);
        }

        // ---- belt between the last inner/Mars-like orbit and the first giant (or beyond home).
        double beltInner = radii[Math.Min(homeIndex + marsLike, slots.Count - 1)] * 1.25;
        int firstGiant = slots.FindIndex(t => t is BodyType.GasGiant or BodyType.IceGiant);
        double beltOuter = firstGiant > 0 ? radii[firstGiant] * 0.72 : beltInner * 1.6;
        if (beltOuter < beltInner * 1.15) beltOuter = beltInner * 1.3;
        int asteroids = rng.Next(15, 41);
        for (int i = 0; i < asteroids; i++)
        {
            double a = rng.Range(beltInner, beltOuter);
            double radius = rng.Range(1000, 15000);
            var ast = new Body { Name = names.Next(2, 3), Type = BodyType.Asteroid, Radius = radius, Gm = 6.674e-11 * 2200 * 4.0 / 3.0 * Math.PI * radius * radius * radius, Parent = star.Id, RotationPeriod = -rng.Range(1, 8) * 3600, PaletteIndex = rng.Next(0, 3) };
            double e = rng.Range(0, 0.15);
            ast.Orbit = new Conic(star.Gm, a * (1 - e * e), e, rng.Range(0, MathD.TwoPi), rng.Range(-Units.Year * 3, 0), -1);
            bool icy = a > 0.5 * (beltInner + beltOuter) && rng.Chance(0.7);
            AddDeposit(ast, icy ? Resource.Ice : Resource.Ore, rng, 0.3, 0.8);
            if (rng.Chance(0.5)) AddDeposit(ast, icy ? Resource.Ore : Resource.Ice, rng, 0.2, 0.5);
            sys.Add(ast);
        }

        // ---- comets
        int comets = rng.Next(1, 4);
        for (int i = 0; i < comets; i++)
        {
            double rp = aHome * rng.Range(0.55, 0.95);
            double e = rng.Range(0.6, 0.92);
            double p = rp * (1 + e);
            double radius = rng.Range(2000, 8000);
            var c = new Body { Name = names.Next(2, 3), Type = BodyType.Comet, Radius = radius, Gm = 6.674e-11 * 600 * 4.0 / 3.0 * Math.PI * radius * radius * radius, Parent = star.Id, RotationPeriod = -rng.Range(0.5, 3) * 3600, PaletteIndex = 0 };
            c.Orbit = new Conic(star.Gm, p, e, rng.Range(0, MathD.TwoPi), rng.Range(-Units.Year * 5, 0), -1);
            AddDeposit(c, Resource.Ice, rng, 0.5, 1.0);
            AddDeposit(c, Resource.Co2, rng, 0.3, 0.7);
            sys.Add(c);
        }

        // ---- terrain, then the derived numbers.
        var trng = rng.Fork();
        foreach (var b in sys.Bodies) Terrain.Generate(b, trng.Fork());
        sys.Build();

        // ---- guarantees
        EnsureIceAndOreNearHome(sys, rng, names);
        EnsureCo2Source(sys, rng);
        sys.Build();
        return sys;
    }

    static Body MakePlanet(BodyType type, Rng rng, NameGen names)
    {
        var b = new Body { Name = names.Next(2, 3), Type = type, PaletteIndex = rng.Next(0, 3) };
        switch (type)
        {
            case BodyType.Home:
                b.Gm = 1.99e12; b.Radius = 450_000; b.RotationPeriod = -Units.Day;
                b.Atmo = new Atmosphere { Height = 50_000, ScaleHeight = 6_000, Rho0 = 1.225 };
                b.Magnetosphere = 3 * b.Radius; b.SeaLevel = 0;
                break;
            case BodyType.Rocky:
                b.Radius = rng.Range(150_000, 400_000); b.Gm = rng.Range(3, 12) * b.Radius * b.Radius; b.RotationPeriod = -rng.Range(0.5, 40) * Units.Day;
                if (rng.Chance(0.4)) b.Atmo = new Atmosphere { Height = 80_000, ScaleHeight = 12_000, Rho0 = rng.Range(3, 8), Composition = "CO2", HasCo2 = true };
                break;
            case BodyType.Desert:
                b.Radius = rng.Range(240_000, 360_000); b.Gm = rng.Range(3.5, 5.5) * b.Radius * b.Radius; b.RotationPeriod = -rng.Range(0.8, 1.6) * Units.Day;
                b.Atmo = new Atmosphere { Height = 30_000, ScaleHeight = 8_000, Rho0 = rng.Range(0.01, 0.04), Composition = "CO2", HasCo2 = true };
                AddDeposit(b, Resource.Co2, rng, 0.3, 0.8); AddDeposit(b, Resource.Ore, rng, 0.3, 0.7);
                if (rng.Chance(0.5)) AddDeposit(b, Resource.Ice, rng, 0.2, 0.5);
                break;
            case BodyType.GasGiant:
                b.Radius = rng.Range(2.0e6, 4.0e6); b.Gm = rng.Range(15, 30) * b.Radius * b.Radius; b.RotationPeriod = -rng.Range(0.3, 0.7) * Units.Day;
                b.Atmo = new Atmosphere { Height = 690_000, ScaleHeight = 28_000, Rho0 = 0.6, Composition = "H2/He/Xe", HasXenon = true };   // ρ ≈ 1e-11 at the top: a scoop pass survives
                break;
            case BodyType.IceGiant:
                b.Radius = rng.Range(1.2e6, 2.0e6); b.Gm = rng.Range(9, 15) * b.Radius * b.Radius; b.RotationPeriod = -rng.Range(0.5, 0.9) * Units.Day;
                b.Atmo = new Atmosphere { Height = 535_000, ScaleHeight = 22_000, Rho0 = 0.4, Composition = "H2/He/CH4/Xe", HasXenon = true };
                break;
            case BodyType.IceDwarf:
                b.Radius = rng.Range(60_000, 200_000); b.Gm = rng.Range(0.4, 1.5) * b.Radius * b.Radius; b.RotationPeriod = -rng.Range(0.5, 10) * Units.Day;
                AddDeposit(b, Resource.Ice, rng, 0.5, 1.0); if (rng.Chance(0.6)) AddDeposit(b, Resource.Co2, rng, 0.2, 0.6); if (rng.Chance(0.4)) AddDeposit(b, Resource.Ore, rng, 0.1, 0.4);
                break;
        }
        return b;
    }

    static void AddMoons(StarSystem sys, Body p, int count, Rng rng, NameGen names)
    {
        if (count <= 0) return;
        double hill = HillRadius(sys, p);
        double soi = p.Orbit!.Value.A * Math.Pow(p.Gm / sys[p.Parent].Gm, 0.4);
        double aMax = Math.Min(0.4 * hill, 0.5 * soi);
        double a = Math.Max(3.2 * p.Radius, RocheLimit(p, 1200)) * rng.Range(1.1, 1.6);   // above the Roche limit of the fluffiest moon
        if (aMax < a * 1.2) return;
        Body? prev = null; double prevA = 0, prevE = 0, prevSoi = 0;
        for (int i = 0; i < count && a < aMax; i++)
        {
            if (sys.Bodies.Count(b => b.Type is BodyType.Moon or BodyType.IceMoon) >= 25) break;   // GDD §12: 8–25 moons total
            bool icy = p.Type is BodyType.GasGiant or BodyType.IceGiant or BodyType.IceDwarf ? rng.Chance(0.7) : rng.Chance(0.3);
            double maxR = Math.Min(300_000, p.Radius * 0.45);
            double radius = rng.Range(Math.Min(20_000, maxR * 0.5), maxR);
            double density = icy ? rng.Range(1200, 2200) : rng.Range(2500, 3500);
            a = Math.Max(a, RocheLimit(p, density) * 1.08);
            var m = new Body { Name = names.Next(2, 3), Type = icy ? BodyType.IceMoon : BodyType.Moon, Radius = radius, Gm = 6.674e-11 * density * 4.0 / 3.0 * Math.PI * radius * radius * radius, Parent = p.Id, PaletteIndex = rng.Next(0, 3) };
            double e = rng.Range(0.0, 0.05);
            // Keep clear of the previous moon: SOIs must not touch and the pair must sit ≥ 3.5 mutual Hill radii apart.
            if (prev != null)
            {
                for (int k = 0; k < 40; k++)
                {
                    double soiNew = a * Math.Pow(m.Gm / p.Gm, 0.4);
                    double mutual = 0.5 * (prevA + a) * Math.Cbrt((prev.Gm + m.Gm) / (3 * p.Gm));
                    double need = Math.Max(1.2 * (prevSoi + soiNew) + prevA * prevE + a * e, 3.7 * mutual);
                    if (a - prevA >= need) break;
                    a *= 1.12;
                }
                if (a >= aMax) break;
            }
            m.Orbit = new Conic(p.Gm, a * (1 - e * e), e, rng.Range(0, MathD.TwoPi), rng.Range(-Units.Day * 30, 0), -1);
            m.RotationPeriod = -m.Orbit.Value.Period;   // tidally locked
            if (p.Type == BodyType.Home)
            {
                // Home's moon is random: barren (ore) or rich (ice + ore, maybe CO₂).
                bool rich = rng.Chance(0.5);
                AddDeposit(m, Resource.Ore, rng, 0.3, 0.7);
                if (rich) { AddDeposit(m, Resource.Ice, rng, 0.3, 0.8); if (rng.Chance(0.5)) AddDeposit(m, Resource.Co2, rng, 0.2, 0.5); }
            }
            else
            {
                if (icy) AddDeposit(m, Resource.Ice, rng, 0.4, 1.0); else AddDeposit(m, Resource.Ore, rng, 0.3, 0.8);
                if (rng.Chance(0.5)) AddDeposit(m, icy ? Resource.Ore : Resource.Ice, rng, 0.2, 0.5);
                if (rng.Chance(0.35)) AddDeposit(m, Resource.Co2, rng, 0.2, 0.5);
            }
            sys.Add(m);
            prev = m; prevA = a; prevE = e; prevSoi = a * Math.Pow(m.Gm / p.Gm, 0.4);
            // Next moon: spaced by mutual Hill radii.
            double mHill = a * Math.Pow(m.Gm / (3 * p.Gm), 1.0 / 3.0);
            a = a * rng.Range(1.6, 2.4) + 4 * mHill;
        }
    }

    static double HillRadius(StarSystem sys, Body b)
    {
        var parent = sys[b.Parent];
        double a = b.Orbit!.Value.IsEllipse ? b.Orbit.Value.A : b.Orbit.Value.Periapsis;
        return a * (1 - b.Orbit.Value.E) * Math.Pow(b.Gm / (3 * parent.Gm), 1.0 / 3.0);
    }

    /// <summary>Roche limit for a rubble-pile satellite of density ρm around this body.</summary>
    public static double RocheLimit(Body p, double moonDensity)
    {
        double rhoP = p.Gm / 6.674e-11 / (4.0 / 3.0 * Math.PI * p.Radius * p.Radius * p.Radius);
        return 2.44 * p.Radius * Math.Cbrt(rhoP / moonDensity);
    }

    /// <summary>Planets must sit at least 8 mutual Hill radii apart; push outer ones out until they do.</summary>
    static void EnforcePlanetSpacing(List<Body> planets, Body star, Rng rng)
    {
        for (int pass = 0; pass < 20; pass++)
        {
            bool moved = false;
            for (int i = 1; i < planets.Count; i++)
            {
                var a = planets[i - 1]; var b = planets[i];
                double ra = a.Orbit!.Value.A, rb = b.Orbit!.Value.A;
                double mutual = 0.5 * (ra + rb) * Math.Cbrt((a.Gm + b.Gm) / (3 * star.Gm));
                double soiA = ra * Math.Pow(a.Gm / star.Gm, 0.4), soiB = rb * Math.Pow(b.Gm / star.Gm, 0.4);
                double need = 8 * mutual + ra * a.Orbit.Value.E + rb * b.Orbit.Value.E + 1.2 * (soiA + soiB);
                if (rb - ra < need)
                {
                    double target = ra + need * 1.05;
                    var o = b.Orbit.Value;
                    b.Orbit = new Conic(o.Mu, target * (1 - o.E * o.E), o.E, o.Omega, o.Tp, o.Dir);
                    moved = true;
                }
            }
            if (!moved) break;
        }
    }

    static void AddDeposit(Body b, Resource kind, Rng rng, double richLo, double richHi)
    {
        double start = rng.Range(0, MathD.TwoPi);
        double len = rng.Range(0.15, 0.6) * MathD.TwoPi;
        b.Deposits.Add(new Deposit { Kind = kind, Richness = rng.Range(richLo, richHi) * 4, ArcStart = start, ArcEnd = MathD.WrapAngle(start + len) });
    }

    // ---------------------------------------------------------------- guarantees (GDD §12)

    public static bool Has(Body b, Resource r) => b.Deposits.Any(d => d.Kind == r);
    public static bool IsLandable(Body b) => b.Type is not (BodyType.Star or BodyType.GasGiant or BodyType.IceGiant);

    /// <summary>Δv from low home orbit to the surface of a body (transfer + capture + landing).</summary>
    public static double DvToSurface(StarSystem sys, Body b) => b.Id == sys.HomeId ? double.PositiveInfinity : DeltaV.LowToLow(sys, sys.HomeId, b.Id) + DeltaV.LandingFromLowOrbit(b);

    public static Body? NearestWithIceAndOre(StarSystem sys, out double dv)
    {
        Body? best = null; dv = double.PositiveInfinity;
        foreach (var b in sys.Bodies)
        {
            if (!IsLandable(b) || b.Id == sys.HomeId || !Has(b, Resource.Ice) || !Has(b, Resource.Ore)) continue;
            double d = DvToSurface(sys, b);
            if (d < dv) { dv = d; best = b; }
        }
        return best;
    }

    public static bool IsCo2Source(Body b) => Has(b, Resource.Co2) || (b.Atmo?.HasCo2 ?? false);

    public static Body? NearestCo2(StarSystem sys, out double dv)
    {
        Body? best = null; dv = double.PositiveInfinity;
        foreach (var b in sys.Bodies)
        {
            if (b.Id == sys.HomeId || b.Type == BodyType.Star || !IsCo2Source(b)) continue;
            double d = IsLandable(b) ? DvToSurface(sys, b) : DeltaV.LowToLow(sys, sys.HomeId, b.Id);
            if (d < dv) { dv = d; best = b; }
        }
        return best;
    }

    static void EnsureIceAndOreNearHome(StarSystem sys, Rng rng, NameGen names)
    {
        var b = NearestWithIceAndOre(sys, out double dv);
        if (b != null && dv <= IceOreBudget) return;
        // First try: enrich the cheapest landable body that is already within budget.
        Body? cheapest = null; double cheapestDv = double.PositiveInfinity;
        foreach (var x in sys.Bodies)
        {
            if (!IsLandable(x) || x.Id == sys.HomeId) continue;
            double d = DvToSurface(sys, x);
            if (d < cheapestDv) { cheapestDv = d; cheapest = x; }
        }
        if (cheapest != null && cheapestDv <= IceOreBudget)
        {
            if (!Has(cheapest, Resource.Ice)) AddDeposit(cheapest, Resource.Ice, rng, 0.3, 0.8);
            if (!Has(cheapest, Resource.Ore)) AddDeposit(cheapest, Resource.Ore, rng, 0.3, 0.7);
            return;
        }
        // Otherwise a near-home asteroid on an orbit next to home's: cheap to reach, trivial to land on.
        var home = sys.Home; var star = sys.Star;
        double aHome = home.Orbit!.Value.A;
        double a = aHome * (1 + (rng.Chance(0.5) ? 1 : -1) * rng.Range(0.05, 0.08));
        double radius = rng.Range(3000, 9000);
        var ast = new Body { Name = names.Next(2, 3), Type = BodyType.Asteroid, Radius = radius, Gm = 6.674e-11 * 2400 * 4.0 / 3.0 * Math.PI * radius * radius * radius, Parent = star.Id, RotationPeriod = -rng.Range(2, 6) * 3600, PaletteIndex = 1 };
        double e = rng.Range(0.0, 0.02);
        ast.Orbit = new Conic(star.Gm, a * (1 - e * e), e, rng.Range(0, MathD.TwoPi), rng.Range(-Units.Year, 0), -1);
        AddDeposit(ast, Resource.Ice, rng, 0.4, 0.9);
        AddDeposit(ast, Resource.Ore, rng, 0.4, 0.8);
        Terrain.Generate(ast, rng.Fork());
        sys.Add(ast);
        sys.Build();
    }

    static void EnsureCo2Source(StarSystem sys, Rng rng)
    {
        var b = NearestCo2(sys, out double dv);
        if (b != null && dv <= Co2Budget) return;
        // Carbonate rock on the cheapest landable body within budget (the ice+ore body qualifies by construction).
        Body? cheapest = null; double cheapestDv = double.PositiveInfinity;
        foreach (var x in sys.Bodies)
        {
            if (!IsLandable(x) || x.Id == sys.HomeId) continue;
            double d = DvToSurface(sys, x);
            if (d < cheapestDv) { cheapestDv = d; cheapest = x; }
        }
        if (cheapest != null) AddDeposit(cheapest, Resource.Co2, rng, 0.2, 0.6);
    }
}

/// <summary>Validation across seeds (GDD §12): the list is empty when a system passes.</summary>
public static class SystemValidator
{
    public static List<string> Check(StarSystem sys)
    {
        var issues = new List<string>();
        var star = sys.Star;
        // Counts.
        int planets = sys.Bodies.Count(b => b.Parent == star.Id && b.Type is BodyType.Home or BodyType.Rocky or BodyType.Desert or BodyType.GasGiant or BodyType.IceGiant or BodyType.IceDwarf);
        int moons = sys.Bodies.Count(b => b.Type is BodyType.Moon or BodyType.IceMoon);
        int asteroids = sys.Bodies.Count(b => b.Type == BodyType.Asteroid);
        int comets = sys.Bodies.Count(b => b.Type == BodyType.Comet);
        if (planets < 6 || planets > 10) issues.Add($"{planets} planets (want 6–10)");
        if (moons > 25) issues.Add($"{moons} moons (want ≤ 25)");
        if (asteroids < 15) issues.Add($"{asteroids} asteroids (want ≥ 15)");
        if (comets < 1 || comets > 3) issues.Add($"{comets} comets");
        if (sys.HomeId < 0 || double.IsNaN(sys.Home.LaunchSiteAngle)) issues.Add("no launch site");
        // No overlapping SOIs among siblings (planets vs planets, moons of the same parent).
        var byParent = sys.Bodies.Where(b => b.Parent >= 0 && b.Type != BodyType.Asteroid && b.Type != BodyType.Comet).GroupBy(b => b.Parent);
        foreach (var g in byParent)
        {
            var list = g.OrderBy(b => b.Orbit!.Value.A).ToList();
            for (int i = 1; i < list.Count; i++)
            {
                var a = list[i - 1]; var b = list[i];
                double gap = b.Orbit!.Value.Periapsis - a.Orbit!.Value.Apoapsis;
                if (gap < a.Soi + b.Soi) issues.Add($"{a.Name} and {b.Name} SOIs overlap");
                var parent = sys[g.Key];
                double mutual = 0.5 * (a.Orbit.Value.A + b.Orbit.Value.A) * Math.Cbrt((a.Gm + b.Gm) / (3 * parent.Gm));
                double minSep = parent.Type == BodyType.Star ? 8 : 3.5;
                if (b.Orbit.Value.A - a.Orbit.Value.A < minSep * mutual) issues.Add($"{a.Name} and {b.Name} closer than {minSep} Hill radii");
            }
        }
        // Roche.
        foreach (var m in sys.Bodies.Where(b => b.Type is BodyType.Moon or BodyType.IceMoon))
        {
            var p = sys[m.Parent];
            double rho = m.Gm / 6.674e-11 / (4.0 / 3.0 * Math.PI * m.Radius * m.Radius * m.Radius);
            if (m.Orbit!.Value.Periapsis < SystemGenerator.RocheLimit(p, rho)) issues.Add($"{m.Name} inside {p.Name}'s Roche limit");
            if (m.Orbit.Value.Apoapsis > p.Soi * 0.6) issues.Add($"{m.Name} orbits outside {p.Name}'s stable zone");
        }
        // Reachability.
        foreach (var b in sys.Bodies)
        {
            if (b.Type == BodyType.Star || b.Id == sys.HomeId) continue;
            double dv = DeltaV.LowToLow(sys, sys.HomeId, b.Id);
            if (double.IsNaN(dv) || dv > SystemGenerator.MaxReachBudget) issues.Add($"{b.Name} needs {dv:0} m/s");
        }
        var ice = SystemGenerator.NearestWithIceAndOre(sys, out double iceDv);
        if (ice == null || iceDv > SystemGenerator.IceOreBudget) issues.Add($"no ice+ore body within {SystemGenerator.IceOreBudget} m/s (nearest {iceDv:0})");
        var co2 = SystemGenerator.NearestCo2(sys, out double co2Dv);
        if (co2 == null || co2Dv > SystemGenerator.Co2Budget) issues.Add($"no CO₂ source within {SystemGenerator.Co2Budget} m/s (nearest {co2Dv:0})");
        // Names unique.
        if (sys.Bodies.Select(b => b.Name).Distinct().Count() != sys.Bodies.Count) issues.Add("duplicate names");
        return issues;
    }
}
