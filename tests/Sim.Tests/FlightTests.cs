using Perigee.Sim;

namespace Perigee.Tests;

public class FlightTests
{
    static World NewWorld() => SimHost.NewTestWorld();

    static Craft LaunchOnPad(World w, Design d, string name = "Test")
    {
        var home = w.Sys.Home;
        return w.Launch(d, name, home.Id, home.LaunchSiteAngle);
    }

    /// <summary>Put a craft in a circular orbit under active physics (vacuum), for burn tests.</summary>
    static Craft InOrbit(World w, Design d, double altitude = 150_000)
    {
        var home = w.Sys.Home;
        var c = Craft.FromDesign(d, "Orbiter");
        c.BodyId = home.Id;
        double r = home.Radius + altitude;
        c.Pos = new Vec2d(r, 0); c.Vel = new Vec2d(0, -Math.Sqrt(home.Gm / r));   // clockwise like the test system
        c.Angle = MathD.WrapAngle(-Math.PI / 2 + Math.PI / 2 - Math.PI / 2);      // nose along +y? set prograde below
        c.Mode = CraftMode.Active;
        w.AddCraft(c); w.ActiveCraftId = c.Id;
        // Nose prograde: velocity direction = -y → Angle + π/2 = -π/2.
        c.Angle = MathD.WrapAngle(-Math.PI);
        return c;
    }

    [Fact]
    public void CatalogueIsComplete()
    {
        Assert.InRange(Parts.All.Count, 55, 80);
        foreach (var p in Parts.All)
        {
            Assert.True(p.Art.Length > 0, $"{p.Id} has no art");
            Assert.True(p.Outline.Length >= 3, $"{p.Id} has no outline");
            if (p.Has(PartFlags.ClientPayload)) { Assert.True(p.Cost == 0 && p.DryMass > 0, $"{p.Id} is a client payload: free"); continue; }
            Assert.True(p.Cost > 0 && p.DryMass > 0, $"{p.Id} has zero cost or mass");
            Assert.InRange(p.RdMultiplier, 8, 15);
        }
        // GDD §10 starter set is pre-unlocked.
        foreach (var id in new[] { "core-s", "tank-s-short", "tank-s-long", "eng-kestrel", "eng-sparrow", "decoupler-s", "nose-s", "fin", "chute-nose", "legs-s" })
            Assert.True(Parts.Get(id).Starter, $"{id} should be a starter part");
        // Anchor prices (§10).
        Assert.Equal(600_000, Parts.Get("core-s").Cost);
        Assert.Equal(1_200_000, Parts.Get("eng-kestrel").Cost);
        Assert.Equal(5_500_000, Parts.Get("eng-condor").Cost);
        Assert.Equal(18_000_000, Parts.Get("eng-titan").Cost);
        Assert.Equal(3_000_000, Parts.Get("depot-ctrl").Cost);
        Assert.Equal(6_000_000, Parts.Get("outpost-core").Cost);
        Assert.Equal(40_000_000, Parts.Get("pad-kit").Cost);
        Assert.Equal(30_000_000, Parts.Get("fabricator").Cost);
    }

    [Fact]
    public void DesignRoundTripsThroughJsonAndAutoStages()
    {
        var d = TestDesigns.Orbital();
        var json = d.ToJson();
        var d2 = Design.FromJson(json);
        Assert.Equal(d.Parts.Count, d2.Parts.Count);
        Assert.Equal(d.Stages.Count, d2.Stages.Count);
        var auto = TestDesigns.Orbital(); auto.AutoStage();
        Assert.Equal(4, auto.Stages.Count);   // Kestrel · decoupler+Sparrow · capsule decoupler · chute
        Assert.Equal("eng-kestrel", auto.Parts[auto.Stages[0][0]].DefId);
        Assert.Contains(auto.Stages[1], i => auto.Parts[i].DefId == "decoupler-s");
        Assert.Contains(auto.Stages[1], i => auto.Parts[i].DefId == "eng-sparrow");
        Assert.Single(auto.Stages[2]);
        Assert.Equal("decoupler-s", auto.Parts[auto.Stages[2][0]].DefId);
        Assert.Equal("chute-nose", auto.Parts[auto.Stages[3][0]].DefId);
        Assert.Equal(d.Stages.Select(s => string.Join(",", s.OrderBy(x => x))), auto.Stages.Select(s => string.Join(",", s.OrderBy(x => x))));
    }

    [Fact]
    public void MassPropertiesAndPadPlacement()
    {
        var w = NewWorld();
        var c = LaunchOnPad(w, TestDesigns.Sounding());
        double expected = c.Parts.Sum(p => p.Def.DryMass + p.Def.FuelCapacity);
        Assert.Equal(expected, c.Mass, 1e-9);
        Assert.True(c.Moi > 0);
        Assert.Equal(CraftMode.Landed, c.Mode);
        var home = w.Sys.Home;
        double ground = home.SurfaceRadiusLocal(home.LaunchSiteAngle);
        double lowest = c.Alive.SelectMany(p => p.LocalOutline()).Min(v => c.LocalToWorld(v).Length);
        Assert.Equal(ground, lowest, 0.01);
        // Sits still on the rotating surface for a while.
        w.Advance(5);
        Assert.Equal(CraftMode.Landed, c.Mode);
        Assert.Equal(ground, c.Alive.SelectMany(p => p.LocalOutline()).Min(v => c.LocalToWorld(v).Length), 0.01);
    }

    [Fact]
    public void DeltaVReadoutMatchesSimulatedBurnWithinOnePercent()
    {
        var w = NewWorld();
        var c = InOrbit(w, TestDesigns.Orbital());
        var report = c.StageReport();
        Assert.Equal(2, report.Count);   // Kestrel stage, then Sparrow stage
        double predicted = report.Sum(s => s.DeltaVVac);
        Assert.InRange(predicted, 3500, 6000);
        w.Apply(Command.Stage); w.Apply(Command.ThrottleFull);
        int guard = 0;
        while (guard++ < 200_000)
        {
            w.Advance(Units.PhysicsDt);
            var craft = w.Active!;
            if (craft.Parts.Any(p => p.IsEngine && p.Running && p.Flameout))
            {
                if (craft.NextStage < craft.Stages.Count - 1) w.Apply(Command.Stage);
                else break;
            }
            if (craft.Mode == CraftMode.Rails) craft.TakeOffRails(w.T);   // keep integrating between stages
        }
        var final = w.Active!;
        Assert.True(final.Fuel < 1e-3, $"fuel left {final.Fuel}");
        Assert.True(Math.Abs(final.DeltaVSpent - predicted) / predicted < 0.01, $"readout {predicted:0} vs burned {final.DeltaVSpent:0}");
    }

    [Fact]
    public void StagingSeparatesTheBoosterAndKeepsControl()
    {
        var w = NewWorld();
        var c = LaunchOnPad(w, TestDesigns.Orbital(), "Orbiter");
        w.Apply(Command.ThrottleFull);
        w.Apply(Command.Stage);
        Assert.True(c.Parts.First(p => p.Def.Id == "eng-kestrel").Running);
        for (int i = 0; i < 150; i++) w.Advance(Units.PhysicsDt);
        Assert.Equal(CraftMode.Active, c.Mode);
        int before = w.Crafts.Count;
        w.Apply(Command.Stage);
        Assert.Equal(before + 1, w.Crafts.Count);
        Assert.Equal(c.Id, w.ActiveCraftId);
        Assert.True(c.HasControl);
        Assert.DoesNotContain(c.Parts, p => p.Def.Id == "eng-kestrel");
        var booster = w.Crafts.Last();
        Assert.True(booster.IsDebris);
        Assert.Contains(booster.Parts, p => p.Def.Id == "eng-kestrel");
        Assert.True(c.Parts.First(p => p.Def.Id == "eng-sparrow").Running);
        // Both keep moving; the upper stage is now lighter and keeps its velocity.
        Assert.InRange(c.Mass, 0.9, 1.6);
    }

    [Fact]
    public void SoftLandingOnLegsAndCrashWithout()
    {
        var w = NewWorld();
        var home = w.Sys.Home;
        // Drop the sounding rocket from 40 m above the pad: legs (12 m/s) survive the ~28 m/s... no: use 3 m so the impact is ~7.7 m/s.
        var c = LaunchOnPad(w, TestDesigns.Sounding());
        c.Mode = CraftMode.Active; c.Landed = null;
        c.Pos = c.Pos.Normalized() * (c.Pos.Length + 3);
        int guard = 0;
        while (c.Mode == CraftMode.Active && guard++ < 2000) w.Advance(Units.PhysicsDt);
        Assert.Equal(CraftMode.Landed, c.Mode);
        Assert.Equal(c.Parts.Count, c.Alive.Count());
        Assert.Contains(w.Events.Concat(Array.Empty<SimEvent>()), e => e.Kind == SimEventKind.Landed);

        // Same drop from 60 m (~34 m/s): legs break, the engine breaks, the craft is a wreck.
        var w2 = NewWorld();
        var c2 = LaunchOnPad(w2, TestDesigns.Sounding());
        c2.Mode = CraftMode.Active; c2.Landed = null;
        c2.Pos = c2.Pos.Normalized() * (c2.Pos.Length + 60);
        guard = 0;
        bool crashed = false;
        while (guard++ < 3000) { w2.Advance(Units.PhysicsDt); if (w2.Events.Any(e => e.Kind == SimEventKind.Crashed)) crashed = true; if (w2.Active == null || w2.Active.Mode != CraftMode.Active) break; }
        Assert.True(crashed, "should have crashed");
        Assert.True(w2.Crafts.Any(x => x.Parts.Count < 7) || w2.Crafts.Count > 1, "parts should have been destroyed");
    }

    [Fact]
    public void ParachuteTearsWhenFastAndFloatsWhenSlow()
    {
        var w = NewWorld();
        var home = w.Sys.Home;
        var c = LaunchOnPad(w, TestDesigns.Probe());
        c.Mode = CraftMode.Active; c.Landed = null;
        // 8 km up, falling at 400 m/s → chute tears.
        c.Pos = c.Pos.Normalized() * (home.Radius + 8000);
        c.Vel = Flight.SurfaceVelocity(home, c.Pos) - c.Pos.Normalized() * 400;
        w.Apply(Command.DeployChutes);
        w.Advance(Units.PhysicsDt);
        Assert.Contains(w.Events, e => e.Kind == SimEventKind.PartDestroyed && e.Text.Contains("tore"));

        // Fresh probe at 4 km falling 90 m/s → opens, slows to a soft landing.
        var w2 = NewWorld();
        var c2 = LaunchOnPad(w2, TestDesigns.Probe());
        c2.Mode = CraftMode.Active; c2.Landed = null;
        c2.Pos = c2.Pos.Normalized() * (home.Radius + home.TerrainHeightLocal(home.LaunchSiteAngle) + 4000);
        c2.Vel = Flight.SurfaceVelocity(home, c2.Pos) - c2.Pos.Normalized() * 90;
        w2.Apply(Command.DeployChutes);
        int guard = 0; double minSpeed = double.MaxValue;
        while (c2.Mode == CraftMode.Active && guard++ < 20000)
        {
            w2.Advance(Units.PhysicsDt);
            double s = (c2.Vel - Flight.SurfaceVelocity(home, c2.Pos)).Length;
            if (c2.Pos.Length - home.Radius < 3500) minSpeed = Math.Min(minSpeed, s);
        }
        Assert.Equal(CraftMode.Landed, c2.Mode);
        Assert.True(minSpeed < 15, $"terminal speed under the chute was {minSpeed:0.0} m/s");
        Assert.Equal(c2.Parts.Count, c2.Alive.Count());
    }

    [Fact]
    public void NoseConeShadowsTheTankBehindIt()
    {
        var w = NewWorld();
        var home = w.Sys.Home;
        var d = new Design { Name = "Cone" };
        int core = d.Root("core-s");
        int nose = d.Stack(core, "nose-s", below: false);
        int tank = d.Stack(core, "tank-s-long");
        var c = LaunchOnPad(w, d);
        c.Mode = CraftMode.Active; c.Landed = null;
        c.Pos = c.Pos.Normalized() * (home.Radius + 5000);
        c.Vel = Flight.SurfaceVelocity(home, c.Pos) + c.Pos.Normalized() * 300;   // nose-first, going up
        w.Advance(Units.PhysicsDt);
        var pn = c.Parts.First(p => p.Def.Id == "nose-s"); var pt = c.Parts.First(p => p.Def.Id == "tank-s-long");
        Assert.True(pn.ExposedArea > 0.5, $"nose exposed {pn.ExposedArea}");
        Assert.True(pt.ExposedArea < 1e-6, $"tank should be shadowed, exposed {pt.ExposedArea}");
        // Sideways flight exposes the tank's whole length.
        c.Angle = MathD.WrapAngle(c.Angle + Math.PI / 2);
        w.Advance(Units.PhysicsDt);
        Assert.True(pt.ExposedArea > 2.5, $"side-on tank exposed {pt.ExposedArea}");
    }

    [Fact]
    public void ReentryHeatBurnsAnUnshieldedProbeAndSparesAShieldedOne()
    {
        // A normal deorbit of a 2.5 t stack: 95 km orbit with the periapsis lowered to 15 km; start on the inbound leg at 49 km.
        // Heavy craft decelerate deep in the air, where the stagnation temperature exceeds what bare tanks survive.
        (bool intact, double peak) Run(bool shield)
        {
            var w = NewWorld();
            var home = w.Sys.Home;
            var d = new Design { Name = "Reentry" };
            int core = d.Root("core-s");
            int t1 = d.Stack(core, "tank-s-long");
            int t2 = d.Stack(t1, "tank-s-short");
            if (shield) d.Stack(t2, "shield-s");   // shield at the bottom: enters shield-first and its own torque keeps it that way
            var c = Craft.FromDesign(d, "R");
            c.BodyId = home.Id;
            double rp = home.Radius + 15_000, ra = home.Radius + 95_000, a = 0.5 * (rp + ra);
            var conic = Conic.FromState(home.Gm, new Vec2d(ra, 0), new Vec2d(0, -Math.Sqrt(home.Gm * (2 / ra - 1 / a))), 0);   // clockwise
            double nuZ = conic.TrueAnomalyAtRadius(home.Radius + 49_000);
            var (r0, v0) = conic.StateAtTrueAnomaly(-nuZ);
            c.Pos = r0; c.Vel = v0;
            c.Angle = MathD.WrapAngle(v0.Angle + Math.PI / 2);   // bottom of the stack forward
            c.Mode = CraftMode.Active;
            w.AddCraft(c); w.ActiveCraftId = c.Id;
            double maxT = 0; int guard = 0; int parts = c.Parts.Count;
            while (guard++ < 60000 && !c.Destroyed && c.Mode == CraftMode.Active)
            {
                w.Advance(Units.PhysicsDt);
                maxT = Math.Max(maxT, c.Parts.Where(p => !p.Destroyed).Select(p => p.Temp).DefaultIfEmpty(0).Max());
                if (c.Pos.Length - home.Radius < 5000) break;
                if ((c.Vel - Flight.SurfaceVelocity(home, c.Pos)).Length < 250) break;
            }
            bool intact = !c.Destroyed && c.Parts.Count == parts && c.Parts.All(p => !p.Destroyed);
            return (intact, maxT);
        }
        var bare = Run(false); var shielded = Run(true);
        Assert.False(bare.intact, $"bare stack should lose parts to heat (peak {bare.peak:0} K)");
        Assert.True(shielded.intact, $"shielded stack should come through intact (peak {shielded.peak:0} K)");
    }

    [Fact]
    public void ReactionWheelRotatesAndDampsOnRelease()
    {
        var w = NewWorld();
        var c = InOrbit(w, TestDesigns.Probe());
        double a0 = c.Angle;
        w.Input = new InputState { RotateRight = true };
        for (int i = 0; i < 100; i++) w.Advance(Units.PhysicsDt);
        Assert.True(c.AngVel < -0.05, $"should spin clockwise, ω={c.AngVel}");
        w.Input = InputState.None;
        for (int i = 0; i < 100; i++) w.Advance(Units.PhysicsDt);
        Assert.True(Math.Abs(c.AngVel) < 1e-3, $"should damp to a stop, ω={c.AngVel}");
        Assert.NotEqual(a0, c.Angle);
    }

    [Fact]
    public void CoastingCraftReturnsToRailsAndComesBackForTheAtmosphere()
    {
        var w = NewWorld();
        var home = w.Sys.Home;
        var c = InOrbit(w, TestDesigns.Probe(), 150_000);
        for (int i = 0; i < 100; i++) w.Advance(Units.PhysicsDt);
        Assert.Equal(CraftMode.Rails, c.Mode);
        // Lower the periapsis into the atmosphere with a retrograde burn: take it off rails, thrust, let it settle.
        c.TakeOffRails(w.T);
        c.Angle = MathD.WrapAngle(c.Vel.Angle - Math.PI - Math.PI / 2);   // nose retrograde
        w.Apply(Command.Stage); w.Apply(Command.ThrottleFull);
        while (c.Fuel > 0.2) w.Advance(Units.PhysicsDt);
        w.Apply(Command.ThrottleCut);
        for (int i = 0; i < 100; i++) w.Advance(Units.PhysicsDt);
        Assert.Equal(CraftMode.Rails, c.Mode);
        Assert.True(c.Rails.Conic.Periapsis < home.Radius + home.Atmo!.Height, "periapsis should be inside the atmosphere");
        w.SetWarp(5);
        int guard = 0;
        while (c.Mode == CraftMode.Rails && guard++ < 100000) w.Advance(Units.PhysicsDt * w.EffectiveWarp);
        Assert.Equal(CraftMode.Active, c.Mode);
        Assert.Equal(0, w.WarpIndex);
        Assert.InRange(c.Pos.Length - home.Radius, home.Atmo.Height - 500, home.Atmo.Height + 500);
    }
}
