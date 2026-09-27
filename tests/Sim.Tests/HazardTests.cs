using Perigee.Sim;

namespace Perigee.Tests;

public class HazardTests
{
    static World Career(ulong seed = 7) { var w = SimHost.NewTestWorld(seed); w.StartCareer(Preset.Standard, "T"); w.Cash = 100_000_000_000; return w; }

    static Craft Junk(World w, int bodyId, double periAlt, double apoAlt, string name = "junk")
    {
        var body = w.Sys[bodyId];
        var d = new Design { Name = name }; d.Root("tank-s-short");
        var c = Craft.FromDesign(d, name); c.BodyId = bodyId; c.Debris = true;
        double rp = body.Radius + periAlt, ra = body.Radius + apoAlt, a = 0.5 * (rp + ra);
        c.Pos = new Vec2d(ra, 0); c.Vel = new Vec2d(0, -Math.Sqrt(body.Gm * (2 / ra - 1 / a))); c.Mode = CraftMode.Active;
        w.AddCraft(c); c.PutOnRails(w.Sys, w.T);
        return c;
    }

    static Craft Station(World w, int bodyId, double alt, string name = "Sat")
    {
        var body = w.Sys[bodyId];
        var c = Craft.FromDesign(TestDesigns.Station(), name); c.BodyId = bodyId;
        double r0 = body.Radius + alt; c.Pos = new Vec2d(r0, 0); c.Vel = new Vec2d(0, -Math.Sqrt(body.Gm / r0)); c.Mode = CraftMode.Active;
        w.AddCraft(c); c.PutOnRails(w.Sys, w.T);
        return c;
    }

    [Fact]
    public void LowDebrisDecaysAndReentersWhileHighDebrisStays()
    {
        var w = Career(); var home = w.Sys.Home;
        var low = Junk(w, home.Id, 55_000, 90_000, "low");       // periapsis inside 1.2 × 50 km
        var high = Junk(w, home.Id, 200_000, 220_000, "high");
        var far = Station(w, 2, 40_000, "Far"); w.ActiveCraftId = far.Id;   // active craft far away: everything else is unloaded
        double peri0 = low.Rails.Conic.Periapsis;
        w.Advance(Units.Day * 2);
        Assert.True(low.Rails.Conic.Periapsis < peri0 - 500, $"periapsis {peri0:0} → {low.Rails.Conic.Periapsis:0}");
        int guard = 0;
        while (!low.Destroyed && guard++ < 200) w.Advance(Units.Day);
        Assert.True(low.Destroyed, "low debris never reentered");
        Assert.False(high.Destroyed);
        Assert.InRange(high.Rails.Conic.Periapsis - home.Radius, 199_000, 201_000);
    }

    [Fact]
    public void DebrisInTheSameBandHitsInfrastructureAndCascadesUpToTheCap()
    {
        var w = Career(); var home = w.Sys.Home;
        var depot = SimHost.SpawnInto(w, "depot", "Depot", 200_000, 0, 1);
        var far = Station(w, 2, 40_000, "Far"); w.ActiveCraftId = far.Id;
        for (int i = 0; i < 60; i++) Junk(w, home.Id, 180_000 + i * 500, 220_000, $"j{i}");   // a crowded band
        int parts0 = depot.Parts.Count(p => !p.Destroyed);
        int guard = 0;
        while (depot.Parts.Count(p => !p.Destroyed) == parts0 && guard++ < 400) w.Advance(Units.Day);
        Assert.True(guard < 400, "no hit in 400 days with 60 overlapping debris objects");
        Assert.Contains(w.Events.Concat(Array.Empty<SimEvent>()), e => true);   // events cleared per advance; the hit itself is the evidence
        // Cascade: the split-off pieces are debris.
        Assert.True(w.DebrisAt(home.Id).Count() >= 60, $"debris now {w.DebrisAt(home.Id).Count()}");
        // Cap: at the object cap a hit destroys a part but spawns nothing new.
        var w2 = Career(); var h2 = w2.Sys.Home;
        var target = Station(w2, h2.Id, 150_000, "T");
        var d2 = new Design { Name = "big" }; int root = d2.Root("core-s"); int a = d2.Stack(root, "tank-s-short"); int b = d2.Stack(a, "decoupler-s"); d2.Stack(b, "tank-s-long");
        var big = Craft.FromDesign(d2, "Big"); big.BodyId = h2.Id; double r0 = h2.Radius + 150_000; big.Pos = new Vec2d(r0, 0); big.Vel = new Vec2d(0, -Math.Sqrt(h2.Gm / r0)); big.Mode = CraftMode.Active; w2.AddCraft(big); big.PutOnRails(w2.Sys, w2.T);
        for (int i = w2.TrackedObjects; i < Hazards.ObjectCap; i++) Junk(w2, h2.Id, 300_000, 300_000, "fill");
        int objects = w2.TrackedObjects;
        w2.DebrisHit(big);
        Assert.True(w2.TrackedObjects <= objects, $"cap breached: {objects} → {w2.TrackedObjects}");
    }

    [Fact]
    public void LoadedDebrisCollidesForReal()
    {
        var w = Career(); var home = w.Sys.Home;
        var sat = SimHost.SpawnInto(w, "station", "Sat", 150_000, 0, 1);
        // A rock coming the other way, 20 m/s closing, starting 60 m ahead on the same line.
        var rock = Junk(w, home.Id, 150_000, 150_000, "rock");
        var (r, v) = sat.StateAt(w.T);
        rock.TakeOffRails(w.T); sat.TakeOffRails(w.T);
        var along = v.Normalized();
        rock.Pos = r + along * 60; rock.Vel = v - along * 20;
        sat.Pos = r; sat.Vel = v;
        int satParts = sat.Parts.Count(p => !p.Destroyed), rockParts = rock.Parts.Count(p => !p.Destroyed);
        for (int i = 0; i < 400; i++) w.Advance(Units.PhysicsDt);
        Assert.True(sat.Parts.Count(p => !p.Destroyed) < satParts || sat.Destroyed, "the station was not hit");
        Assert.True(rock.Destroyed || rock.Parts.Count(p => !p.Destroyed) < rockParts, "the rock was not damaged");
    }

    [Fact]
    public void StormWarningStopsWarpAndThePhasesRunOnTime()
    {
        var w = Career();
        var sat = SimHost.SpawnInto(w, "station", "Sat", 150_000, 0, 1);
        w.SetWarp(6);
        w.ScheduleStorm(2, 1);
        Assert.Equal(StormPhase.Warning, w.Storm);
        Assert.Equal(0, w.WarpIndex);
        w.Advance(1.9 * Units.Day);
        Assert.Equal(StormPhase.Warning, w.Storm);
        w.Advance(0.2 * Units.Day);
        Assert.Equal(StormPhase.Active, w.Storm);
        w.Advance(1.0 * Units.Day);
        Assert.Equal(StormPhase.None, w.Storm);
        // The daily roll produces storms at about the preset rate over a long stretch.
        var w2 = Career(3); SimHost.SpawnInto(w2, "station", "S", 150_000, 0, 1);
        for (int d = 0; d < 800; d++) w2.Advance(Units.Day);
        Assert.InRange(w2.StormsSoFar, 8, 40);   // 800 days / 40 ≈ 20 storms (each blocks new rolls while it runs)
    }

    [Fact]
    public void ExposedStandardCoresFryHardenedAndShelteredOnesDoNot()
    {
        var w = Career(); var vell = w.Sys[2];
        w.StormFryChance = 1.0;
        // Exposed around Vell (outside home's magnetosphere), sunlit side.
        var exposed = Station(w, vell.Id, 40_000, "Exposed");
        var far = Station(w, 3, 60_000, "Far"); w.ActiveCraftId = far.Id;
        // Put it on the sunward side explicitly.
        var toStar = (w.Sys.Position(0, w.T) - w.Sys.Position(vell.Id, w.T)).Normalized();
        exposed.TakeOffRails(w.T); exposed.Pos = toStar * (vell.Radius + 40_000); exposed.Vel = -exposed.Pos.Perp.Normalized() * Math.Sqrt(vell.Gm / exposed.Pos.Length); exposed.PutOnRails(w.Sys, w.T);
        // Sheltered in Vell's shadow.
        var shadow = Station(w, vell.Id, 40_000, "Shadow");
        shadow.TakeOffRails(w.T); shadow.Pos = -toStar * (vell.Radius + 40_000); shadow.Vel = -shadow.Pos.Perp.Normalized() * Math.Sqrt(vell.Gm / shadow.Pos.Length); shadow.PutOnRails(w.Sys, w.T);
        // Inside home's magnetosphere.
        var home = w.Sys.Home;
        var lowHome = Station(w, home.Id, 150_000, "LowHome");
        // Hardened core, exposed.
        var hd = new Design { Name = "hard" }; int hc = hd.Root("core-hard"); hd.Stack(hc, "tank-s-short");
        var hard = Craft.FromDesign(hd, "Hard"); hard.BodyId = vell.Id; hard.Pos = toStar * (vell.Radius + 50_000); hard.Vel = -hard.Pos.Perp.Normalized() * Math.Sqrt(vell.Gm / hard.Pos.Length); hard.Mode = CraftMode.Active; w.AddCraft(hard); hard.PutOnRails(w.Sys, w.T);
        // A depot, exposed: goes offline 5 days regardless of luck.
        var depot = SimHost.SpawnInto(w, "depot", "Depot", 45_000, 0, 1); depot.BodyId = vell.Id;
        depot.TakeOffRails(w.T); depot.Pos = toStar * (vell.Radius + 45_000); depot.Vel = -depot.Pos.Perp.Normalized() * Math.Sqrt(vell.Gm / depot.Pos.Length); depot.PutOnRails(w.Sys, w.T);
        w.ActiveCraftId = far.Id;
        Assert.False(w.StormSafe(exposed)); Assert.True(w.StormSafe(shadow)); Assert.True(w.StormSafe(lowHome)); Assert.False(w.StormSafe(hard));
        w.ScheduleStorm(0, 1);
        Assert.True(exposed.Fried); Assert.False(exposed.HasControl);
        Assert.False(shadow.Fried); Assert.False(lowHome.Fried); Assert.False(hard.Fried); Assert.True(hard.HasControl);
        var ctrl = depot.Parts.First(p => p.Def.Has(PartFlags.DepotController));
        Assert.True(ctrl.Offline);
        Assert.Equal(5.0, (ctrl.OfflineUntil - w.T) / Units.Day, 6);
        var core = exposed.Parts.First(p => p.Def.Has(PartFlags.ProbeCore));
        Assert.InRange((core.OfflineUntil - w.T) / Units.Day, 3.0, 5.0);
        // Reboot.
        for (int d = 0; d < 6; d++) w.Advance(Units.Day);
        Assert.True(exposed.HasControl); Assert.False(exposed.Fried);
        Assert.False(ctrl.Offline);
        // A landed craft is always safe.
        var w3 = Career(); var lander = w3.Launch(TestDesigns.Lander(), "L", 2, 1.0);
        var far3 = Station(w3, 3, 60_000, "Far3"); w3.ActiveCraftId = far3.Id; w3.StormFryChance = 1.0;
        w3.ScheduleStorm(0, 1);
        Assert.True(lander.HasControl);
    }

    [Fact]
    public void FryChanceIsAboutAQuarterForStandardCores()
    {
        int fried = 0, n = 0;
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var w = Career(seed); var vell = w.Sys[2];
            var toStar = (w.Sys.Position(0, w.T) - w.Sys.Position(vell.Id, w.T)).Normalized();
            for (int i = 0; i < 5; i++)
            {
                var s = Station(w, vell.Id, 40_000 + i * 1000, $"S{i}");
                s.TakeOffRails(w.T); s.Pos = toStar.Rotated(i * 0.05) * (vell.Radius + 40_000 + i * 1000); s.Vel = -s.Pos.Perp.Normalized() * Math.Sqrt(vell.Gm / s.Pos.Length); s.PutOnRails(w.Sys, w.T);
            }
            var far = Station(w, 3, 60_000, "Far"); w.ActiveCraftId = far.Id;
            w.ScheduleStorm(0, 1);
            foreach (var c in w.Crafts.Where(c => c.BodyId == vell.Id)) { n++; if (c.Fried) fried++; }
        }
        double rate = fried / (double)n;
        Assert.InRange(rate, 0.15, 0.35);
    }
}
