namespace Perigee.Sim;

/// <summary>What a refinery makes (GDD §8 chains). Auto = methalox when water and CO₂ are in store, else water from ice, else metal from ore.</summary>
public enum Recipe { Auto, Water, Methalox, Rcs, Metal }

/// <summary>A landed craft carrying an Outpost Core, plus every other craft landed within 200 m of it (GDD §9: "modules join by landing within 200 m").</summary>
public sealed class Outpost
{
    public Craft Core = null!;
    public List<Craft> Members = new();   // includes Core
    public Body Body = null!;
    public double PowerGen, PowerNeed;    // kW, averaged over day and night
    public Dictionary<Resource, double> Rates = new();   // net t/day by resource (last production step)
    public bool Offline;

    public IEnumerable<Part> Parts => Members.SelectMany(c => c.Parts).Where(p => !p.Destroyed);
    public double Stored(Resource r) => Parts.Sum(p => p.Get(r));
    public double Room(Resource r) => Parts.Sum(p => p.Room(r));
    public string Name => Core.Name;
}

public static class Mining
{
    public const double JoinRadius = 200;          // m along the surface
    public const double NightFactor = 0.5;          // GDD §9: night on a rotating body is averaged
    public const double DrillPowerKw = 3;           // per drill (part description)
    public const double ScoopReferenceRho = 3e-11;   // kg/m³ at which ScoopRate applies (the survivable top of a giant's atmosphere)
    public const double ScoopReferenceSpeed = 20000; // m/s
    public const double MetalPerOre = 0.6;          // t metal per t ore (proposal, BALANCE.md)

    /// <summary>Inputs per tonne of output for a recipe (GDD §8: CO₂ + 2 H₂O → CH₄ + 2 O₂ ⇒ 1 t methalox ≈ 0.55 t CO₂ + 0.45 t water).</summary>
    public static (Resource output, (Resource res, double t)[] inputs) Chain(Recipe r) => r switch
    {
        Recipe.Water => (Resource.Water, new[] { (Resource.Ice, 1.0) }),
        Recipe.Methalox => (Resource.Methalox, new[] { (Resource.Water, 0.45), (Resource.Co2, 0.55) }),
        Recipe.Rcs => (Resource.Rcs, new[] { (Resource.Water, 1.0) }),
        Recipe.Metal => (Resource.Metal, new[] { (Resource.Ore, 1.0 / MetalPerOre) }),
        _ => throw new ArgumentException("Auto must be resolved first"),
    };

    /// <summary>Solar irradiance factor at a body: 1/d² with d in home-orbit radii, times the star's luminosity.</summary>
    public static double SolarFactor(StarSystem sys, Body body, double t)
    {
        double aHome = sys.Home.Orbit?.A ?? 1;
        var star = sys.Star;
        double d = (sys.Position(body.Id, t) - sys.Position(star.Id, t)).Length / aHome;
        return star.Luminosity / Math.Max(1e-6, d * d);
    }

    /// <summary>Continuous power an outpost can count on: solar × irradiance × night average + RTGs. Deployed state does not matter on the ground.</summary>
    public static double AveragePowerKw(StarSystem sys, Body body, double t, IEnumerable<Part> parts)
    {
        double solar = SolarFactor(sys, body, t) * NightFactor;
        double kw = 0;
        foreach (var p in parts)
        {
            if (p.Destroyed || p.Offline) continue;
            if (p.Def.Has(PartFlags.Solar)) kw += p.Def.PowerOutKw * solar;
            else if (p.Def.Has(PartFlags.Rtg)) kw += p.Def.PowerOutKw;
        }
        return kw;
    }
}

public sealed partial class World
{
    /// <summary>Every outpost right now: landed craft with a working Outpost Core, grouped with landed craft within 200 m on the same body.</summary>
    readonly Dictionary<int, (double gen, double need, Dictionary<Resource, double> rates)> _outpostStats = new();

    public List<Outpost> Outposts()
    {
        var result = new List<Outpost>();
        var taken = new HashSet<int>();
        foreach (var core in Crafts)
        {
            if (core.Destroyed || core.Mode != CraftMode.Landed || core.Landed is not { } L || !core.Parts.Any(p => !p.Destroyed && p.Def.Has(PartFlags.OutpostCore))) continue;
            if (taken.Contains(core.Id)) continue;
            var body = Sys[core.BodyId];
            var o = new Outpost { Core = core, Body = body };
            o.Members.Add(core); taken.Add(core.Id);
            foreach (var c in Crafts)
            {
                if (c == core || c.Destroyed || c.Mode != CraftMode.Landed || c.BodyId != core.BodyId || c.Landed is not { } M || taken.Contains(c.Id)) continue;
                double dist = Math.Abs(MathD.WrapPi(M.LocalAngle - L.LocalAngle)) * body.Radius;
                if (dist <= Mining.JoinRadius) { o.Members.Add(c); taken.Add(c.Id); }
            }
            o.Offline = core.Parts.Any(p => !p.Destroyed && p.Def.Has(PartFlags.OutpostCore) && p.Offline) && !core.Parts.Any(p => !p.Destroyed && p.Def.Has(PartFlags.OutpostCore) && !p.Offline);
            if (_outpostStats.TryGetValue(core.Id, out var st)) { o.PowerGen = st.gen; o.PowerNeed = st.need; o.Rates = new Dictionary<Resource, double>(st.rates); }
            result.Add(o);
        }
        return result;
    }

    /// <summary>Background production for `dt` seconds of game time (GDD §9: whenever time passes, warp included, capped by storage).</summary>
    public void Production(double dt)
    {
        if (dt <= 0) return;
        foreach (var o in Outposts()) ProduceAt(o, dt);
    }

    void ProduceAt(Outpost o, double dt)
    {
        double days = dt / Units.Day;
        var body = o.Body;
        var parts = o.Parts.ToList();
        var drills = parts.Where(p => p.Def.Has(PartFlags.Drill) && !p.Offline).ToList();
        var refineries = parts.Where(p => p.Def.Has(PartFlags.Refinery) && !p.Offline).ToList();
        o.Rates.Clear();
        if (o.Offline) { o.PowerGen = 0; o.PowerNeed = 0; _outpostStats[o.Core.Id] = (0, 0, new Dictionary<Resource, double>()); return; }
        // Deposits under the core (each drill takes one deposit, round-robin when arcs overlap); a landed drill reveals the richness.
        double local = o.Core.Landed!.Value.LocalAngle;
        var under = body.Deposits.Where(d => d.Contains(local)).ToList();
        if (drills.Count > 0) foreach (var d in under) d.Revealed = true;
        o.PowerGen = Mining.AveragePowerKw(Sys, body, T, parts);
        o.PowerNeed = drills.Count * (under.Count > 0 ? Mining.DrillPowerKw : 0) + refineries.Sum(r => r.Def.PowerUseKw);
        double scale = o.PowerNeed <= 0 ? 1 : Math.Min(1, o.PowerGen / o.PowerNeed);
        // Drills.
        if (under.Count > 0)
            for (int i = 0; i < drills.Count; i++)
            {
                var d = under[i % under.Count];
                double amount = drills[i].Def.DrillRate * d.Richness * days * scale;
                double put = Store(o, d.Kind, amount);
                Rate(o, d.Kind, put / days);
            }
        // Refineries.
        foreach (var r in refineries)
        {
            var recipe = r.Recipe == Recipe.Auto ? AutoRecipe(o) : r.Recipe;
            if (recipe == Recipe.Auto) continue;
            var (output, inputs) = Mining.Chain(recipe);
            double want = r.Def.RefineRate * days * scale;
            double can = Math.Min(want, o.Room(output));
            foreach (var (res, per) in inputs) can = Math.Min(can, o.Stored(res) / per);
            if (can <= 1e-12) continue;
            foreach (var (res, per) in inputs) { Take(o, res, can * per); Rate(o, res, -can * per / days); }
            double made = Store(o, output, can);
            Rate(o, output, made / days);
            if (output == Resource.Methalox && o.Core.BodyId != Sys.HomeId) Count("refined_methalox", made);
        }
        foreach (var c in o.Members) c.UpdateMass();
        _outpostStats[o.Core.Id] = (o.PowerGen, o.PowerNeed, new Dictionary<Resource, double>(o.Rates));
    }

    static Recipe AutoRecipe(Outpost o)
    {
        if (o.Stored(Resource.Water) > 1e-9 && o.Stored(Resource.Co2) > 1e-9 && o.Room(Resource.Methalox) > 1e-9) return Recipe.Methalox;
        if (o.Stored(Resource.Ice) > 1e-9 && o.Room(Resource.Water) > 1e-9) return Recipe.Water;
        if (o.Stored(Resource.Ore) > 1e-9 && o.Room(Resource.Metal) > 1e-9) return Recipe.Metal;
        return Recipe.Auto;
    }

    static void Rate(Outpost o, Resource r, double perDay) => o.Rates[r] = o.Rates.GetValueOrDefault(r) + perDay;

    /// <summary>Put up to `amount` into the outpost's storage (dedicated capacity first, then generic surface tanks). Returns what fit.</summary>
    static double Store(Outpost o, Resource r, double amount)
    {
        double left = amount;
        foreach (var p in o.Parts.OrderBy(p => p.Def.StorageT > 0 ? 1 : 0))
        {
            if (left <= 1e-12) break;
            double room = p.Room(r);
            if (room <= 0) continue;
            double put = Math.Min(room, left);
            p.Set(r, p.Get(r) + put); left -= put;
        }
        return amount - left;
    }

    static void Take(Outpost o, Resource r, double amount)
    {
        double left = amount;
        foreach (var p in o.Parts)
        {
            if (left <= 1e-12) break;
            double take = Math.Min(p.Get(r), left);
            p.Set(r, p.Get(r) - take); left -= take;
        }
    }

    /// <summary>Electricity in flight (ion drives, GDD §8): solar in sunlight at this distance + RTGs, with the craft's batteries as a buffer.</summary>
    public bool HasPower(Craft c, double kw)
    {
        double gen = GenerationKw(c);
        if (gen >= kw) return true;
        return c.Charge > 1e-6;
    }

    public double GenerationKw(Craft c)
    {
        var body = Sys[c.BodyId];
        double solar = Mining.SolarFactor(Sys, body, T) * (InShadow(c) ? 0 : 1);
        double kw = 0;
        foreach (var p in c.Parts)
        {
            if (p.Destroyed || p.Offline) continue;
            if (p.Def.Has(PartFlags.Solar)) kw += p.Def.PowerOutKw * solar;
            else if (p.Def.Has(PartFlags.Rtg)) kw += p.Def.PowerOutKw;
        }
        return kw;
    }

    /// <summary>Inside the SOI body's shadow cylinder (2D): behind the body as seen from the star and closer to the axis than its radius.</summary>
    public bool InShadow(Craft c)
    {
        var body = Sys[c.BodyId];
        if (body.Type == BodyType.Star) return false;
        var toStar = Sys.Position(Sys.Star.Id, T) - Sys.Position(body.Id, T);
        if (toStar.LengthSq < 1) return false;
        var dir = toStar.Normalized();
        var (r, _) = c.StateAt(T);
        double along = Vec2d.Dot(r, dir);
        double across = Math.Abs(Vec2d.Cross(r, dir));
        return along < 0 && across < body.Radius;
    }

    /// <summary>Battery bookkeeping per physics step: charge from surplus, drain for ion drives that are running.</summary>
    public void UpdateBattery(Craft c, double dt)
    {
        double cap = 0; foreach (var p in c.Parts) if (!p.Destroyed && !p.Offline) cap += p.Def.BatteryKwh;
        if (cap <= 0) { c.Charge = 0; return; }
        double use = 0;
        foreach (var p in c.Parts) if (!p.Destroyed && p.IsEngine && p.Running && p.LastThrust > 0 && p.Def.Engine!.PowerKw > 0) use += p.Def.Engine.PowerKw;
        c.Charge = MathD.Clamp(c.Charge + (GenerationKw(c) - use) * dt / 3600, 0, cap);
    }

    /// <summary>Atmospheric scoop (GDD §8): xenon from gas/ice-giant upper atmospheres, CO₂ from CO₂ atmospheres; mass flow scales with density and speed.</summary>
    public void Scoop(Craft c, Body body, double rho, double airspeed, double dt)
    {
        if (body.Atmo == null || rho <= 0) return;
        Resource? r = body.Atmo.HasXenon ? Resource.Xenon : body.Atmo.HasCo2 ? Resource.Co2 : null;
        if (r == null) return;
        double factor = (rho / Mining.ScoopReferenceRho) * (airspeed / Mining.ScoopReferenceSpeed) * (r == Resource.Co2 ? 10 : 1);
        foreach (var s in c.Parts)
        {
            if (s.Destroyed || !s.Def.Has(PartFlags.Scoop)) continue;
            double amount = s.Def.ScoopRate * factor * dt;
            double left = amount;
            foreach (var p in c.Parts) { if (left <= 1e-12) break; double room = p.Room(r.Value); if (room <= 0) continue; double put = Math.Min(room, left); p.Set(r.Value, p.Get(r.Value) + put); left -= put; }
            if (amount - left > 0) { c.Scooped += amount - left; Count("xenon_scooped", amount - left); }
        }
    }
}
