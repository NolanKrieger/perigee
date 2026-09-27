namespace Perigee.Sim;

/// <summary>Hand-made systems for tests and the M1 view (the generator arrives in M6). Numbers follow GDD §4 for home.</summary>
public static class TestSystems
{
    public static Body MakeHome()
    {
        var home = new Body
        {
            Name = "Home", Type = BodyType.Home, Gm = 1.99e12, Radius = 450_000, RotationPeriod = -Units.Day,
            Atmo = new Atmosphere { Height = 50_000, ScaleHeight = 6_000, Rho0 = 1.225 },
            Magnetosphere = 3 * 450_000, SeaLevel = 0,
        };
        return home;
    }

    /// <summary>Star + home (with a moon) + a rocky neighbour with two moons + one asteroid. Deterministic terrain.
    /// Orbits and rotations run clockwise (Dir = -1, negative periods) so "prograde" reads rightward when a body is drawn below the craft.</summary>
    public static StarSystem Basic()
    {
        var sys = new StarSystem { Seed = 1, StarName = "Sol-test" };
        var star = new Body { Name = "Kestra", Type = BodyType.Star, Gm = 1.327e20 * 0.02, Radius = 6.96e8 * 0.05, Luminosity = 1 };
        sys.Add(star);
        var home = MakeHome();
        home.Parent = star.Id;
        // Home year ≈ 400 days: a = (GM T²/4π²)^(1/3)
        double year = Units.Year;
        double aHome = Math.Cbrt(star.Gm * year * year / (4 * Math.PI * Math.PI));
        home.Orbit = new Conic(star.Gm, aHome * (1 - 0.0004), 0.02, 0.3, 0, -1);
        sys.Add(home);
        sys.HomeId = home.Id;
        var moon = new Body { Name = "Vell", Type = BodyType.Moon, Gm = 1.99e12 * 0.0123 * 1.2, Radius = 190_000, RotationPeriod = 0, Parent = home.Id };
        moon.Orbit = new Conic(home.Gm, 12.0e6 * (1 - 0.0025), 0.05, 1.1, 0, -1);
        moon.RotationPeriod = -moon.Orbit.Value.Period;   // tidally locked
        sys.Add(moon);
        var red = new Body { Name = "Oxid", Type = BodyType.Desert, Gm = 1.99e12 * 0.35, Radius = 320_000, RotationPeriod = -Units.Day * 1.1, Parent = star.Id,
            Atmo = new Atmosphere { Height = 30_000, ScaleHeight = 8_000, Rho0 = 0.02, Composition = "CO2", HasCo2 = true } };
        red.Orbit = new Conic(star.Gm, aHome * 1.52 * (1 - 0.0081), 0.09, 2.0, 0, -1);
        sys.Add(red);
        var m1 = new Body { Name = "Oxid I", Type = BodyType.Moon, Gm = 1.99e12 * 0.002, Radius = 60_000, Parent = red.Id };
        m1.Orbit = new Conic(red.Gm, 2.5e6, 0.01, 0.4, 0, -1); m1.RotationPeriod = -m1.Orbit.Value.Period; sys.Add(m1);
        var m2 = new Body { Name = "Oxid II", Type = BodyType.IceMoon, Gm = 1.99e12 * 0.004, Radius = 80_000, Parent = red.Id };
        m2.Orbit = new Conic(red.Gm, 6.0e6, 0.02, 3.6, 0, -1); m2.RotationPeriod = -m2.Orbit.Value.Period; sys.Add(m2);
        var ast = new Body { Name = "Bryn", Type = BodyType.Asteroid, Gm = 6.674e-11 * 3e16, Radius = 4_000, RotationPeriod = -3 * 3600, Parent = star.Id };
        ast.Orbit = new Conic(star.Gm, aHome * 2.6, 0.15, 4.5, 0, -1);
        sys.Add(ast);
        moon.Deposits.Add(new Deposit { Kind = Resource.Ice, Richness = 2.5, ArcStart = 0.8, ArcEnd = 1.6 });
        moon.Deposits.Add(new Deposit { Kind = Resource.Ore, Richness = 2.0, ArcStart = 3.0, ArcEnd = 4.2 });
        red.Deposits.Add(new Deposit { Kind = Resource.Co2, Richness = 3.0, ArcStart = 0.2, ArcEnd = 2.0 });
        red.Deposits.Add(new Deposit { Kind = Resource.Ore, Richness = 1.5, ArcStart = 4.0, ArcEnd = 5.0 });
        red.Deposits.Add(new Deposit { Kind = Resource.Ice, Richness = 2.0, ArcStart = 1.5, ArcEnd = 2.5 });   // overlaps the CO₂ arc on 1.5–2.0
        m2.Deposits.Add(new Deposit { Kind = Resource.Ice, Richness = 3.5, ArcStart = 1.0, ArcEnd = 4.0 });
        ast.Deposits.Add(new Deposit { Kind = Resource.Ore, Richness = 3.0, ArcStart = 0, ArcEnd = 3.0 });
        var rng = new Rng(1234);
        foreach (var b in sys.Bodies) Terrain.Generate(b, rng.Fork());
        sys.Build();
        return sys;
    }
}
