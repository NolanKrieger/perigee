namespace Perigee.Sim;

public enum BodyType { Star, Home, Rocky, Desert, GasGiant, IceGiant, IceDwarf, Moon, IceMoon, Asteroid, Comet }

/// <summary>Exponential atmosphere: ρ(h) = Rho0·e^(−h/ScaleHeight), zero above Height.</summary>
public sealed class Atmosphere
{
    public double Height;        // m; above this there is no air and craft go on rails
    public double ScaleHeight;   // m
    public double Rho0;          // kg/m³ at the surface
    public string Composition = "N2/O2";
    public bool HasCo2;          // a scoop or surface intake can take CO₂ from it
    public bool HasXenon;        // gas/ice giants: a scoop collects xenon in the upper atmosphere

    public double Density(double altitude) => altitude >= Height ? 0 : altitude <= 0 ? Rho0 : Rho0 * Math.Exp(-altitude / ScaleHeight);
    /// <summary>Pressure relative to the surface (used for engine thrust/Isp interpolation).</summary>
    public double PressureRatio(double altitude) => Density(altitude) / Rho0;
}

public enum Resource { Methalox, Rcs, Xenon, Water, Ice, Co2, Ore, Metal }

/// <summary>A resource deposit along an arc of a body's circumference (GDD §9): hidden until scanned.</summary>
public sealed class Deposit
{
    public Resource Kind;          // Ice, Co2 (dry ice / carbonate) or Ore
    public double Richness;        // 0.5..4 t/day per drill at full richness (proposal)
    public bool Revealed;          // a drill has landed on it: exact richness known
    public double ArcStart, ArcEnd; // rad, counter-clockwise; may wrap past 2π
    public bool Contains(double angle)
    {
        double a = MathD.WrapAngle(angle - ArcStart);
        double len = MathD.WrapAngle(ArcEnd - ArcStart);
        if (len == 0) len = MathD.TwoPi;
        return a <= len;
    }
}

/// <summary>A star, planet, moon, asteroid or comet. Orbit is relative to Parent; the star has none.</summary>
public sealed class Body
{
    public int Id;
    public string Name = "";
    public int Parent = -1;
    public BodyType Type;
    public double Gm;                 // m³/s²
    public double Radius;             // m (mean; terrain is added on top)
    public double RotationPeriod;     // s, positive = counter-clockwise
    public double Rotation0;          // surface angle offset at t=0 (rad)
    public Atmosphere? Atmo;
    public Conic? Orbit;              // around Parent
    public double Soi;                // sphere-of-influence radius (m); +∞ for the star
    public double[] Terrain = Array.Empty<double>();   // height above Radius (m) at N evenly spaced angles
    public double SeaLevel = double.NegativeInfinity;  // terrain below this is ocean (home only)
    public double LaunchSiteAngle = double.NaN;         // surface angle of the flat launch site (home only)
    public double Magnetosphere;      // storm-safe radius (home: 3× radius)
    public double Luminosity = 1;     // star only, relative to Sol
    public int PaletteIndex;
    public List<Deposit> Deposits = new();
    public bool Scanned;
    public double TerrainMax;         // highest terrain (m above Radius)

    public double SurfaceGravity => Gm / (Radius * Radius);
    public double SurfaceSpeed => RotationPeriod == 0 ? 0 : MathD.TwoPi * Radius / RotationPeriod;
    public double AtmoHeight => Atmo?.Height ?? 0;
    /// <summary>Altitude below which a coasting craft leaves the rails: the top of the atmosphere, or just above the terrain.</summary>
    public double ActiveZoneHeight => Atmo != null ? Atmo.Height : TerrainMax + 2000;
    public double LowOrbitTop => 1.5 * Radius;   // market-node band (GDD §10)
    public double CircularSpeed(double r) => Math.Sqrt(Gm / r);

    /// <summary>Angle of the surface's reference meridian at time t (rad).</summary>
    public double SurfaceAngle(double t) => RotationPeriod == 0 ? Rotation0 : Rotation0 + MathD.TwoPi * t / RotationPeriod;

    /// <summary>Terrain height (m above Radius) at an inertial angle, at time t (rotation applied), linear interpolation.</summary>
    public double TerrainHeightAt(double inertialAngle, double t) => TerrainHeightLocal(inertialAngle - SurfaceAngle(t));

    /// <summary>Terrain height at a body-fixed angle (rad).</summary>
    /// <summary>Levelled patches on the surface (off-world pads, GDD §9): (centre angle, half-width in rad, height in m). Blended over a second half-width outside.</summary>
    public List<(double angle, double halfWidth, double height)> FlatSpots = new();

    public double TerrainHeightLocal(double localAngle)
    {
        double h = RawTerrainHeightLocal(localAngle);
        foreach (var (a, hw, height) in FlatSpots)
        {
            double d = Math.Abs(MathD.WrapPi(localAngle - a));
            if (d <= hw) return height;
            if (d <= 2 * hw) { double f = (d - hw) / hw; return height * (1 - f) + h * f; }
        }
        return h;
    }

    public double RawTerrainHeightLocal(double localAngle)
    {
        int n = Terrain.Length;
        if (n == 0) return 0;
        double u = MathD.WrapAngle(localAngle) / MathD.TwoPi * n;
        int i = (int)Math.Floor(u); double f = u - i;
        i %= n; int j = (i + 1) % n;
        return Terrain[i] * (1 - f) + Terrain[j] * f;
    }

    /// <summary>Level the ground around an angle (idempotent): a pad kit is a levelled platform, so rockets stand and lift off cleanly.</summary>
    public void Flatten(double localAngle, double halfWidthM)
    {
        double hw = halfWidthM / Radius;
        foreach (var (a, w, _) in FlatSpots) if (Math.Abs(MathD.WrapPi(localAngle - a)) <= w) return;
        FlatSpots.Add((MathD.WrapAngle(localAngle), hw, RawTerrainHeightLocal(localAngle)));
    }

    public bool IsOceanAt(double localAngle) => TerrainHeightLocal(localAngle) < SeaLevel;

    /// <summary>Surface radius (terrain or sea) at a body-fixed angle.</summary>
    public double SurfaceRadiusLocal(double localAngle) => Radius + Math.Max(TerrainHeightLocal(localAngle), double.IsNegativeInfinity(SeaLevel) ? double.NegativeInfinity : SeaLevel);

    public override string ToString() => $"{Name}#{Id}";
}
