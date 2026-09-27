using Perigee.Sim;

namespace Perigee.Tests;

public class MiningTests
{
    static World Career() { var w = SimHost.NewTestWorld(); w.StartCareer(Preset.Standard, "T"); return w; }

    /// <summary>A landed outpost craft at a body-fixed angle, built from part ids (radial extras hang on the core).</summary>
    static Craft LandOutpost(World w, int bodyId, double localAngle, string name, params string[] extras)
    {
        var d = new Design { Name = name };
        int core = d.Root("outpost-core");
        int side = 1;
        foreach (var id in extras) { var def = Parts.Get(id); if (def.RadialMount) d.Radial(core, id, side, 0.2 * side); else d.Stack(core, id, below: false); side = -side; }
        var c = w.Launch(d, name, bodyId, localAngle);
        return c;
    }

    [Fact]
    public void ScannerMapsDepositsAfterOneFullOrbit()
    {
        var w = Career();
        var vell = w.Sys[2];
        var d = new Design { Name = "scan" }; int core = d.Root("core-s"); d.Radial(core, "scanner", 1, 0);
        var c = Craft.FromDesign(d, "Scanner"); c.BodyId = vell.Id;
        double r0 = vell.Radius + 40_000; c.Pos = new Vec2d(r0, 0); c.Vel = new Vec2d(0, -Math.Sqrt(vell.Gm / r0)); c.Mode = CraftMode.Active;
        w.AddCraft(c); c.PutOnRails(w.Sys, w.T); w.ActiveCraftId = c.Id;
        double period = c.Rails.Conic.Period;
        w.Advance(period * 0.9);
        Assert.False(vell.Scanned);
        w.Advance(period * 0.2);
        Assert.True(vell.Scanned);
        // No scanner: never.
        var w2 = Career(); var v2 = w2.Sys[2];
        var c2 = SimHost.SpawnInto(w2, "probe", "P", 40_000, 0, 1); c2.BodyId = v2.Id; c2.Pos = new Vec2d(r0, 0); c2.Vel = new Vec2d(0, -Math.Sqrt(v2.Gm / r0)); c2.Mode = CraftMode.Active; c2.PutOnRails(w2.Sys, w2.T);
        w2.Advance(period * 1.2);
        Assert.False(v2.Scanned);
    }

    [Fact]
    public void DrillsFollowRichnessAndPowerAndRevealTheDeposit()
    {
        var w = Career();
        var vell = w.Sys[2];
        var ice = vell.Deposits.First(d => d.Kind == Resource.Ice);
        double inside = 0.5 * (ice.ArcStart + ice.ArcEnd);
        // One drill, two RTGs (3 kW ≥ 3 kW needed), a surface tank.
        var c = LandOutpost(w, vell.Id, inside, "O1", "drill", "rtg", "rtg", "surface-tank");
        Assert.False(ice.Revealed);
        w.Advance(Units.Day);
        Assert.True(ice.Revealed);
        Assert.Equal(1.0 * ice.Richness, c.Resource(Resource.Ice), 3);
        // Half the power → half the rate.
        var w2 = Career(); var v2 = w2.Sys[2]; var ice2 = v2.Deposits.First(d => d.Kind == Resource.Ice);
        var c2 = LandOutpost(w2, v2.Id, inside, "O2", "drill", "rtg", "surface-tank");
        w2.Advance(Units.Day);
        Assert.Equal(0.5 * ice2.Richness, c2.Resource(Resource.Ice), 3);
        // Outside the arc: nothing.
        var w3 = Career(); var v3 = w3.Sys[2];
        var c3 = LandOutpost(w3, v3.Id, inside + Math.PI, "O3", "drill", "rtg", "rtg", "surface-tank");
        w3.Advance(Units.Day);
        Assert.Equal(0, c3.Resource(Resource.Ice));
    }

    [Fact]
    public void RefineryChainBalancesMassAndCapsAtStorage()
    {
        var w = Career();
        var vell = w.Sys[2];
        var c = LandOutpost(w, vell.Id, 0.3, "R", "refinery-s", "rtg", "rtg", "rtg", "rtg", "rtg", "rtg", "rtg", "rtg", "rtg", "rtg", "rtg", "rtg", "rtg", "rtg", "surface-tank");
        var tank = c.Parts.First(p => p.Def.StorageT > 0);
        tank.Set(Resource.Water, 4.5); tank.Set(Resource.Co2, 5.5);
        var refinery = c.Parts.First(p => p.Def.Has(PartFlags.Refinery));
        refinery.Recipe = Recipe.Methalox;
        w.Advance(Units.Day * 2);   // 2 t/day × 2 days = 4 t
        Assert.Equal(4.0, c.Resource(Resource.Methalox), 3);
        Assert.Equal(4.5 - 1.8, c.Resource(Resource.Water), 3);
        Assert.Equal(5.5 - 2.2, c.Resource(Resource.Co2), 3);
        w.Advance(Units.Day * 10);   // inputs run out at 10 t of methalox
        Assert.Equal(10.0, c.Resource(Resource.Methalox), 3);
        Assert.Equal(0, c.Resource(Resource.Water), 6);
        // Storage cap: a full tank stops production.
        tank.Set(Resource.Ore, 40 - tank.ResourceMass);   // fill the shared tank
        refinery.Recipe = Recipe.Metal;
        double metal0 = c.Resource(Resource.Metal);
        w.Advance(Units.Day);
        Assert.Equal(metal0, c.Resource(Resource.Metal), 6);   // no room: 40 t shared tank is full
    }

    [Fact]
    public void SolarPowerFallsWithDistanceAndAveragesTheNight()
    {
        var w = Career();
        var home = w.Sys.Home; var oxid = w.Sys[3];
        var pHome = new Design { Name = "p" }; int k = pHome.Root("outpost-core"); pHome.Radial(k, "solar", 1, 0);
        var ch = w.Launch(pHome, "H", home.Id, home.LaunchSiteAngle);
        var co = w.Launch(pHome, "O", oxid.Id, 0.5);
        double genHome = Mining.AveragePowerKw(w.Sys, home, w.T, ch.Parts);
        double genOxid = Mining.AveragePowerKw(w.Sys, oxid, w.T, co.Parts);
        double aHome = home.Orbit!.Value.A;
        double dHome = (w.Sys.Position(home.Id, w.T) - w.Sys.Position(0, w.T)).Length, dOxid = (w.Sys.Position(oxid.Id, w.T) - w.Sys.Position(0, w.T)).Length;
        Assert.Equal(2.5 * Mining.NightFactor * (aHome / dHome) * (aHome / dHome), genHome, 3);   // night averaged, 1/d² in home-orbit radii
        Assert.Equal((dOxid / dHome) * (dOxid / dHome), genHome / genOxid, 3);
    }

    [Fact]
    public void BackgroundProductionRunsUnderWarpAndStopsWhenOffline()
    {
        var w = Career();
        var vell = w.Sys[2];
        var ice = vell.Deposits.First(d => d.Kind == Resource.Ice);
        double inside = 0.5 * (ice.ArcStart + ice.ArcEnd);
        var c = LandOutpost(w, vell.Id, inside, "O", "drill", "rtg", "rtg", "surface-tank");
        // Something else is active far away, so the outpost is unloaded and time passes on the rails path.
        var sat = SimHost.SpawnInto(w, "station", "Sat", 300_000, 0, 1); w.ActiveCraftId = sat.Id;
        w.SetWarp(6);
        double t0 = w.T;
        while (w.T < t0 + 3 * Units.Day) w.Advance(Units.PhysicsDt * w.EffectiveWarp);
        double days = (w.T - t0) / Units.Day;
        Assert.Equal(days * ice.Richness, c.Resource(Resource.Ice), 2);
        double before = c.Resource(Resource.Ice);
        foreach (var p in c.Parts.Where(p => p.Def.Has(PartFlags.OutpostCore))) p.Offline = true;
        w.Advance(Units.Day);
        Assert.Equal(before, c.Resource(Resource.Ice), 6);
    }

    [Fact]
    public void ScoopCollectsXenonInAGasGiantAtmosphere()
    {
        var sys = TestSystems.Basic();
        var giant = new Body { Name = "Jov", Type = BodyType.GasGiant, Gm = 1e14, Radius = 3_000_000, RotationPeriod = -36_000, Parent = 0,
            Atmo = new Atmosphere { Height = 664_000, ScaleHeight = 28_000, Rho0 = 0.6, Composition = "H2/He", HasXenon = true } };
        giant.Orbit = new Conic(sys.Star.Gm, sys.Home.Orbit!.Value.A * 5, 0.01, 1.0, 0, -1);
        sys.Add(giant); Terrain.Generate(giant, new Rng(5)); sys.Build();
        var w = new World(sys, 3); w.StartCareer(Preset.Standard, "T");
        var d = new Design { Name = "scoop" }; int core = d.Root("core-s"); d.Stack(core, "scoop", below: false); int t = d.Stack(core, "xenon-tank-s"); d.Radial(t, "bin-s", 1, 0);
        var c = Craft.FromDesign(d, "Scooper"); c.BodyId = giant.Id;
        // Grazing pass: start above the atmosphere on a path whose periapsis is ~300 km deep in the upper air.
        double rp = giant.Radius + 640_000, ra = giant.Radius + 3_000_000, a = 0.5 * (rp + ra);
        c.Pos = new Vec2d(0, ra); c.Vel = new Vec2d(Math.Sqrt(giant.Gm * (2 / ra - 1 / a)), 0); c.Mode = CraftMode.Active;
        foreach (var p in c.Parts) foreach (Resource rr in Enum.GetValues<Resource>()) if (p.Capacity(rr) > 0) p.Set(rr, 0);
        w.AddCraft(c); c.PutOnRails(w.Sys, w.T); w.ActiveCraftId = c.Id;
        double period = c.Rails.Conic.Period;
        int guard = 0;
        while (w.T < period && guard++ < 4_000_000) w.Advance(Units.PhysicsDt * Math.Max(1, w.EffectiveWarp));
        Assert.True(c.Scooped > 0.02 && c.Scooped < 1, $"scooped {c.Scooped:0.0000} t");
        Assert.True(c.Resource(Resource.Xenon) > 0.02, $"xenon aboard {c.Resource(Resource.Xenon):0.0000} t");
        Assert.False(c.Destroyed);
    }

    [Fact]
    public void IonDriveNeedsPowerAndBatteriesBridgeTheShadow()
    {
        var w = Career();
        var home = w.Sys.Home;
        var d = new Design { Name = "ion" }; int core = d.Root("core-s"); int t = d.Stack(core, "tank-s-short"); int e = d.Stack(t, "eng-ion"); d.Radial(t, "solar", 1, 0); d.Radial(t, "solar", -1, 0); d.Stage(e);
        var c = Craft.FromDesign(d, "Ion"); c.BodyId = home.Id;
        foreach (var p in c.Parts) if (p.Capacity(Resource.Xenon) > 0) p.Set(Resource.Xenon, p.Capacity(Resource.Xenon));
        double r0 = home.Radius + 300_000; c.Pos = new Vec2d(r0, 0); c.Vel = new Vec2d(0, -Math.Sqrt(home.Gm / r0)); c.Mode = CraftMode.Active;
        w.AddCraft(c); c.PutOnRails(w.Sys, w.T); w.ActiveCraftId = c.Id;
        Assert.True(w.GenerationKw(c) < 6 || w.InShadow(c) == false);   // two panels give 5 kW at home: not enough alone for a 6 kW drive
        Assert.False(w.HasPower(c, 6));
        c.Charge = 2;   // charged batteries bridge it
        Assert.True(w.HasPower(c, 6));
    }

    /// <summary>M9 gate: an outpost on Oxid, where ice and CO₂ arcs overlap, runs the whole chain to methalox in the background.</summary>
    [Fact]
    public void OffWorldMethaloxChainProducesFuelInTheBackground()
    {
        var w = Career();
        var oxid = w.Sys[3];
        var ice = oxid.Deposits.First(d => d.Kind == Resource.Ice); var co2 = oxid.Deposits.First(d => d.Kind == Resource.Co2);
        double site = 1.75;   // inside both arcs (ice 1.5–2.5, CO₂ 0.2–2.0)
        Assert.True(ice.Contains(site) && co2.Contains(site));
        var extras = new List<string> { "drill", "drill", "refinery-s", "refinery-s", "surface-tank", "surface-tank" };
        for (int i = 0; i < 40; i++) extras.Add("rtg");   // 60 kW: 2 drills (6) + 2 refineries (40)
        var c = LandOutpost(w, oxid.Id, site, "Oxid Base", extras.ToArray());
        c.Parts.Where(p => p.Def.Has(PartFlags.Refinery)).First().Recipe = Recipe.Water;
        c.Parts.Where(p => p.Def.Has(PartFlags.Refinery)).Last().Recipe = Recipe.Methalox;
        var sat = SimHost.SpawnInto(w, "station", "Sat", 300_000, 0, 1); w.ActiveCraftId = sat.Id;
        w.SetWarp(7);
        double t0 = w.T;
        while (w.T < t0 + 12 * Units.Day) w.Advance(Units.PhysicsDt * w.EffectiveWarp);
        double fuel = c.Resource(Resource.Methalox);
        Assert.True(fuel >= 8, $"only {fuel:0.00} t of methalox after 12 days");
        // Mass balance: everything mined either sits in store or went through the chain (drills: ice + CO₂ at their richness over 30 days).
        double days = (w.T - t0) / Units.Day;
        double mined = days * (ice.Richness + co2.Richness) * 1.0;
        double stored = c.Resource(Resource.Ice) + c.Resource(Resource.Water) + c.Resource(Resource.Co2) + c.Resource(Resource.Methalox);
        Assert.Equal(mined, stored, 1);
        var o = w.Outposts().Single();
        string state = $"rates: {string.Join(", ", o.Rates.Select(kv => $"{kv.Key}={kv.Value:0.00}"))}; stored ice={c.Resource(Resource.Ice):0.00} water={c.Resource(Resource.Water):0.00} co2={c.Resource(Resource.Co2):0.00} methalox={fuel:0.00}; power {o.PowerGen:0.0}/{o.PowerNeed:0.0}";
        Assert.True(o.Rates.TryGetValue(Resource.Methalox, out var mr) && mr > 0.2, state);
        Assert.True(o.PowerGen >= o.PowerNeed, $"power {o.PowerGen:0.0}/{o.PowerNeed:0.0} kW");
    }
}
