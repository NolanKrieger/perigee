using Perigee.Sim;

namespace Perigee.Tests;

public class DockingTests
{
    static World NewWorld() => SimHost.NewTestWorld();

    [Fact]
    public void PortsCaptureAlignAndHardDockAfterASecond()
    {
        var w = NewWorld();
        var station = SimHost.SpawnInto(w, "station", "Station", 120_000, 0, 0.1);
        w.ActiveCraftId = -1;
        var tanker = SimHost.SpawnInto(w, "tanker", "Tanker", 120_000, 0, 1);
        w.ActiveCraftId = tanker.Id;
        tanker.TakeOffRails(w.T); station.TakeOffRails(w.T);
        // Put the tanker's port face 0.3 m from the station's, nose facing it, matching velocity.
        var sp = station.Parts.First(p => p.Def.Id == "port"); var tp = tanker.Parts.First(p => p.Def.Id == "port");
        tanker.Angle = MathD.WrapAngle(station.Angle + Math.PI);   // opposite orientation: ports face each other
        tanker.UpdateMass();
        var (facePos, faceDir) = Docking.Face(station, sp);
        var (myPos, _) = Docking.Face(tanker, tp);
        tanker.Pos += facePos + faceDir * 0.3 - myPos;
        tanker.Vel = station.Vel - faceDir * 0.2;
        int crafts = w.Crafts.Count;
        int offset = tanker.NextPartId;
        for (int i = 0; i < 30; i++) w.Advance(Units.PhysicsDt);
        Assert.NotNull(tanker.Capture);
        for (int i = 0; i < 60; i++) w.Advance(Units.PhysicsDt);
        Assert.Null(tanker.Capture);
        Assert.Equal(crafts - 1, w.Crafts.Count);
        Assert.Contains(tanker.Parts, p => p.Def.Id == "solar");
        Assert.Equal(2, tanker.Parts.Count(p => p.Def.Id == "port" && p.Fired));
        // Structure: the station's port hangs from ours; the tree is connected.
        var otherPort = tanker.Parts.First(p => p.Def.Id == "port" && p.Id != tp.Id);
        Assert.Equal(tp.Id, otherPort.Parent);
        Assert.Equal(tanker.Parts.Count, tanker.Subtree(tanker.Root!).Count);

        // Pump 0.5 t/s through a standard port: the station's tank fills from ours.
        var myTank = tanker.Parts.First(p => p.Def.Id == "tank-s-short" && p.Id < offset);
        var theirTank = tanker.Parts.First(p => p.Def.Id == "tank-s-short" && p.Id >= offset);
        double before = theirTank.Get(Resource.Methalox);
        var job = w.StartPump(tanker, myTank.Id, theirTank.Id, Resource.Methalox);
        Assert.NotNull(job);
        Assert.Equal(Docking.StandardRate, job!.Rate, 6);
        for (int i = 0; i < 25; i++) w.Advance(Units.PhysicsDt);
        double moved = theirTank.Get(Resource.Methalox) - before;
        Assert.True(moved > 0.24 && moved < 0.26, $"moved {moved:0.000} t in 0.5 s at 0.5 t/s");
        while (tanker.Pumps.Count > 0) w.Advance(Units.PhysicsDt);
        Assert.Equal(theirTank.Capacity(Resource.Methalox), theirTank.Get(Resource.Methalox), 1e-6);

        // Undock: two crafts again, drifting apart, ports free.
        var nc = w.Undock(tanker, tp.Id);
        Assert.NotNull(nc);
        Assert.Equal("Station", nc!.Name);
        Assert.Equal(crafts, w.Crafts.Count);
        Assert.DoesNotContain(tanker.Parts, p => p.Fired && p.Def.Has(PartFlags.DockingPort));
        for (int i = 0; i < 100; i++) w.Advance(Units.PhysicsDt);
        Assert.True(w.Distance(tanker, nc) > 0.5);
    }

    [Fact]
    public void MisalignedOrFastPortsDoNotCapture()
    {
        var w = NewWorld();
        var station = SimHost.SpawnInto(w, "station", "Station", 120_000, 0, 0.1);
        w.ActiveCraftId = -1;
        var tanker = SimHost.SpawnInto(w, "tanker", "Tanker", 120_000, 0, 1);
        w.ActiveCraftId = tanker.Id;
        tanker.TakeOffRails(w.T); station.TakeOffRails(w.T);
        var sp = station.Parts.First(p => p.Def.Id == "port"); var tp = tanker.Parts.First(p => p.Def.Id == "port");
        tanker.Angle = MathD.WrapAngle(station.Angle + Math.PI + MathD.Rad(12));   // 12° off
        tanker.UpdateMass();
        var (facePos, faceDir) = Docking.Face(station, sp);
        tanker.Pos += facePos + faceDir * 0.3 - Docking.Face(tanker, tp).pos;
        tanker.Vel = station.Vel;
        for (int i = 0; i < 10; i++) w.Advance(Units.PhysicsDt);
        Assert.Null(tanker.Capture);
        tanker.Angle = MathD.WrapAngle(station.Angle + Math.PI);
        tanker.Pos += facePos + faceDir * 0.3 - Docking.Face(tanker, tp).pos;
        tanker.Vel = station.Vel - faceDir * 1.5;   // too fast
        w.Advance(Units.PhysicsDt);
        Assert.Null(tanker.Capture);
    }

    [Fact]
    public void ClawGrabsDebrisAndCannotPumpThroughIt()
    {
        var w = NewWorld();
        var d = new Design { Name = "Grabber" };
        int core = d.Root("core-s");
        d.Stack(core, "claw", below: false);
        d.Stack(core, "tank-s-short");
        var grabber = Craft.FromDesign(d, "Grabber");
        var home = w.Sys.Home;
        double r = home.Radius + 120_000;
        grabber.BodyId = home.Id; grabber.Pos = new Vec2d(r, 0); grabber.Vel = new Vec2d(0, -Math.Sqrt(home.Gm / r)); grabber.Mode = CraftMode.Active;
        grabber.Angle = MathD.WrapAngle(-Math.PI);   // nose (claw) points prograde (-y)
        w.AddCraft(grabber); w.ActiveCraftId = grabber.Id;
        var debris = Craft.FromDesign(TestDesigns.Probe(), "Junk");
        debris.BodyId = home.Id; debris.Mode = CraftMode.Active; debris.Debris = true;
        var (clawPos, clawDir) = Docking.Face(grabber, grabber.Parts.First(p => p.Def.Id == "claw"));
        debris.UpdateMass();
        debris.Pos = clawPos + clawDir * 0.9; debris.Vel = grabber.Vel; debris.Angle = grabber.Angle;
        w.AddCraft(debris);
        int n = w.Crafts.Count;
        int offset = grabber.NextPartId;
        for (int i = 0; i < 5; i++) w.Advance(Units.PhysicsDt);
        Assert.Equal(n - 1, w.Crafts.Count);
        Assert.True(grabber.Parts.First(p => p.Def.Id == "claw").Fired);
        var mine = grabber.Parts.First(p => p.Def.Id == "tank-s-short" && p.Id < offset);
        var theirs = grabber.Parts.First(p => p.Def.Id == "tank-s-short" && p.Id >= offset);
        Assert.Null(w.StartPump(grabber, mine.Id, theirs.Id, Resource.Methalox));   // GDD §7: a claw can't pump
        var released = w.Undock(grabber, grabber.Parts.First(p => p.Def.Id == "claw").Id);
        Assert.NotNull(released);
        Assert.Equal(n, w.Crafts.Count);
    }

    [Fact]
    public void ClosestApproachMatchesBruteForceSampling()
    {
        var w = NewWorld();
        var home = w.Sys.Home;
        var station = SimHost.SpawnInto(w, "station", "Station", 150_000, 0, 0.1);
        w.ActiveCraftId = -1;
        var tanker = SimHost.SpawnInto(w, "tanker", "Tanker", 100_000, 40, 1);
        w.ActiveCraftId = tanker.Id;
        tanker.TargetCraft = station.Id;
        var ca = w.ClosestApproach(tanker);
        Assert.NotNull(ca);
        double best = double.MaxValue;
        for (double t = w.T; t < w.T + tanker.Rails.Conic.Period; t += 0.5)
            best = Math.Min(best, (tanker.Rails.Conic.PositionAt(t) - station.Rails.Conic.PositionAt(t)).Length);
        Assert.InRange(ca!.Value.dist, best - 50, best + 50);
        Assert.True(ca.Value.t >= 0 && ca.Value.t <= tanker.Rails.Conic.Period);
    }

    [Fact]
    public void BubbleLoadsNearbyCraftAndKeepsThemOffTheRails()
    {
        var w = NewWorld();
        var station = SimHost.SpawnInto(w, "station", "Station", 120_000, 0, 0.1);
        w.ActiveCraftId = -1;
        var tanker = SimHost.SpawnInto(w, "tanker", "Tanker", 120_000, -0.03, 1);   // ≈ 300 m behind
        w.ActiveCraftId = tanker.Id;
        Assert.Equal(CraftMode.Rails, station.Mode);
        tanker.TakeOffRails(w.T);
        w.Advance(Units.PhysicsDt);
        Assert.Equal(CraftMode.Active, station.Mode);   // pulled into the bubble
        for (int i = 0; i < 120; i++) w.Advance(Units.PhysicsDt);
        Assert.Equal(CraftMode.Active, tanker.Mode);   // a loaded neighbour keeps both in physics
        Assert.True(w.Distance(tanker, station) < 400);
    }

    [Fact]
    public void RendezvousDockPumpRegressionFlight()
    {
        var res = ScriptTests.RunScript("rendezvous-dock.flight", out var log);
        Assert.True(res.Passed, string.Join("\n", res.Failures.Concat(log)));
    }
}
