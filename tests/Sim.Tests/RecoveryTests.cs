using Perigee.Sim;

namespace Perigee.Tests;

public class RecoveryTests
{
    static World NewWorld() => SimHost.NewTestWorld();

    static Craft LandAt(World w, Design d, double localAngle, bool water = false)
    {
        var home = w.Sys.Home;
        var c = w.Launch(d, "L", home.Id, localAngle);
        if (water) { var L = c.Landed!.Value; L.Water = true; c.Landed = L; }
        return c;
    }

    [Fact]
    public void RefundRatesFollowTheGddTable()
    {
        var w = NewWorld();
        var home = w.Sys.Home;
        var d = TestDesigns.Sounding();
        Assert.Equal(0.90, w.RefundRate(LandAt(w, d, home.LaunchSiteAngle)));
        Assert.Equal(0.90, w.RefundRate(LandAt(w, d, home.LaunchSiteAngle + 4000 / home.Radius)));
        Assert.Equal(0.50, w.RefundRate(LandAt(w, d, home.LaunchSiteAngle + 12000 / home.Radius)));
        Assert.Equal(0.0, w.RefundRate(LandAt(w, d, home.LaunchSiteAngle + 12000 / home.Radius, water: true)));
        var ship = LandAt(w, d, home.LaunchSiteAngle + 30000 / home.Radius);
        var L = ship.Landed!.Value; L.OnDroneShip = true; ship.Landed = L;
        Assert.Equal(0.85, w.RefundRate(ship));
        // Not landed → nothing.
        var flying = w.Launch(d, "F", home.Id, home.LaunchSiteAngle); flying.Mode = CraftMode.Active; flying.Landed = null;
        Assert.Equal(0.0, w.RefundRate(flying));
    }

    [Fact]
    public void RecoveryPaysPartsAndPropellantAndRemovesTheCraft()
    {
        var w = NewWorld();
        var home = w.Sys.Home;
        var c = LandAt(w, TestDesigns.Sounding(), home.LaunchSiteAngle);
        long parts = c.PartsCost;
        double fuel = c.Fuel;
        long cash0 = w.Cash;
        int n = w.Crafts.Count;
        Assert.True(w.Recover(c));
        Assert.Equal(cash0 + (long)(parts * 0.9) + (long)(fuel * 100_000), w.Cash);
        Assert.Equal(n - 1, w.Crafts.Count);
        Assert.Contains(w.Events, e => e.Kind == SimEventKind.Recovered);
    }

    [Fact]
    public void StagingHoldsTheBoosterAndTheFlashbackRewindsTheClock()
    {
        var w = NewWorld();
        var home = w.Sys.Home;
        var c = w.Launch(TestDesigns.Recoverable(), "R", home.Id, home.LaunchSiteAngle);
        w.Apply(Command.ThrottleFull); w.Apply(Command.Stage);
        void Upright() { c.Angle = MathD.WrapAngle(c.Pos.Angle - Math.PI / 2); c.AngVel = 0; }   // the test is about the flashback, not flying: keep it vertical
        for (int i = 0; i < 2500; i++) { Upright(); w.Advance(Units.PhysicsDt); }   // 50 s up
        double tSep = w.T;
        w.Apply(Command.Stage);
        Assert.Single(w.HeldBoosters);
        var booster = w.Find(w.HeldBoosters[0].CraftId)!;
        Assert.Equal(CraftMode.Held, booster.Mode);
        Assert.False(w.FlashbackOffered);   // the upper stage is still flying in the air
        // Push the upper stage out of the atmosphere and let it coast onto the rails.
        w.Input = new InputState();
        c.Throttle = 1;
        int guard = 0;
        while (c.Mode != CraftMode.Rails && guard++ < 20000)
        {
            if (c.Mode == CraftMode.Active) Upright();
            w.Advance(Units.PhysicsDt);
            if (c.Mode == CraftMode.Active && c.Pos.Length - home.Radius > 55_000 && c.Throttle > 0) { c.Throttle = 0; }
        }
        Assert.True(c.Mode == CraftMode.Rails, $"upper stage {c.Mode} alt={c.Pos.Length - home.Radius:0} fuel={c.Fuel:0.00} thr={c.Throttle} engines={c.Parts.Count(p => p.IsEngine && p.Running)} destroyed={c.Destroyed}");
        Assert.True(w.FlashbackOffered);
        double tReturn = w.T;
        Assert.True(w.StartFlashback());
        Assert.True(w.InFlashback);
        Assert.Equal(tSep, w.T, 1e-9);
        Assert.Equal(booster.Id, w.ActiveCraftId);
        Assert.Equal(CraftMode.Active, booster.Mode);
        Assert.True(booster.HasControl);
        Assert.False(w.IsPresent(c));
        // Let the booster fall until it crashes or lands, then the clock returns (and catches up if the fall took longer than the replayed interval).
        guard = 0;
        double tBoosterEnd = 0;
        while (w.InFlashback && guard++ < 60000) { w.Advance(Units.PhysicsDt); if (w.InFlashback) tBoosterEnd = w.T; }
        Assert.False(w.InFlashback);
        Assert.Equal(Math.Max(tReturn, tBoosterEnd), w.T, 0.05);
        Assert.Equal(c.Id, w.ActiveCraftId);
        Assert.Empty(w.HeldBoosters);
        Assert.True(w.IsPresent(c));
    }

    [Fact]
    public void LandingOnWaterAtTheDroneShipCountsAsTheShip()
    {
        var w = NewWorld();
        var home = w.Sys.Home;
        // Find ocean 60 km downrange and park a drone ship there.
        double ang = home.LaunchSiteAngle - 60000.0 / home.Radius;
        int guard = 0;
        while (!home.IsOceanAt(ang) && guard++ < 100000) ang -= 100.0 / home.Radius;
        w.DroneShips.Add((home.Id, MathD.WrapAngle(ang)));
        var c = w.Launch(TestDesigns.Sounding(), "S", home.Id, MathD.WrapAngle(ang));
        c.Mode = CraftMode.Active; c.Landed = null;
        c.Pos = c.Pos.Normalized() * (c.Pos.Length + 2);
        guard = 0;
        while (c.Mode == CraftMode.Active && guard++ < 2000) w.Advance(Units.PhysicsDt);
        Assert.Equal(CraftMode.Landed, c.Mode);
        Assert.True(c.Landed!.Value.OnDroneShip);
        Assert.Equal(0.85, w.RefundRate(c));
        // 200 m away it is just the sea.
        var far = w.Launch(TestDesigns.Sounding(), "S2", home.Id, MathD.WrapAngle(ang - 200.0 / home.Radius));
        far.Mode = CraftMode.Active; far.Landed = null; far.Pos = far.Pos.Normalized() * (far.Pos.Length + 2);
        guard = 0;
        while (far.Mode == CraftMode.Active && guard++ < 2000) w.Advance(Units.PhysicsDt);
        Assert.True(far.Landed!.Value.Water && !far.Landed.Value.OnDroneShip);
        Assert.Equal(0.0, w.RefundRate(far));
    }

    [Fact]
    public void SkippingABoosterLosesIt()
    {
        var w = NewWorld();
        var home = w.Sys.Home;
        var c = w.Launch(TestDesigns.Recoverable(), "R", home.Id, home.LaunchSiteAngle);
        w.Apply(Command.ThrottleFull); w.Apply(Command.Stage);
        for (int i = 0; i < 1000; i++) w.Advance(Units.PhysicsDt);
        w.Apply(Command.Stage);
        int id = w.HeldBoosters[0].CraftId;
        w.SkipBooster();
        Assert.Empty(w.HeldBoosters);
        Assert.Null(w.Find(id));
    }

    [Fact]
    public void BoosterFlashbackPadLandingRegressionFlight()
    {
        var res = ScriptTests.RunScript("booster-flashback.flight", out var log);
        Assert.True(res.Passed, string.Join("\n", res.Failures.Concat(log)));
    }
}
