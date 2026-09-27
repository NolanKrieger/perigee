using Perigee.Sim;

namespace Perigee.Tests;

public class WarpTests
{
    static World MakeWorld(out Craft lho, out Craft transfer)
    {
        var sys = TestSystems.Basic();
        var w = new World(sys, 7);
        var home = sys.Home; var moon = sys[2];
        double r1 = home.Radius + 100_000;
        lho = w.AddCraft(new Craft { Name = "Sat", BodyId = home.Id, Rails = new RailsState(home.Id, Conic.FromState(home.Gm, new Vec2d(r1, 0), new Vec2d(0, Math.Sqrt(home.Gm / r1)), 0)) });
        double r2 = moon.Orbit!.Value.A, a = 0.5 * (r1 + r2);
        double tof = Math.PI * Math.Sqrt(a * a * a / home.Gm);
        double arr = moon.Orbit.Value.ThetaOf(moon.Orbit.Value.TrueAnomalyAt(tof));
        var r = Vec2d.FromPolar(r1, arr - Math.PI);
        var v = Vec2d.FromAngle(arr - Math.PI).Perp * Math.Sqrt(home.Gm * (2 / r1 - 1 / a));
        transfer = w.AddCraft(new Craft { Name = "Tanker", BodyId = home.Id, Rails = new RailsState(home.Id, Conic.FromState(home.Gm, r, v, 0)) });
        return w;
    }

    [Fact]
    public void BigStepsAndSmallStepsAgreeExactly()
    {
        var w1 = MakeWorld(out var a1, out var b1);
        var w2 = MakeWorld(out var a2, out var b2);
        w1.ActiveCraftId = a1.Id; w2.ActiveCraftId = a2.Id;
        double total = 2 * Units.Day;
        w1.Advance(total);
        for (int i = 0; i < 2000; i++) w2.Advance(total / 2000);
        Assert.Equal(w1.T, w2.T, 1e-6);
        var (p1, v1) = a1.StateAt(w1.T); var (p2, v2) = a2.StateAt(w2.T);
        Assert.True((p1 - p2).Length < 1e-3, $"{p1} vs {p2}");   // rails are a function of time only; T differs by ~1e-9 s of float rounding
        Assert.True((v1 - v2).Length < 1e-6, $"{v1} vs {v2}");
        Assert.Equal(b1.BodyId, b2.BodyId);
        Assert.Equal(2, b1.BodyId);   // the tanker reached the moon in both
        var (q1, _) = b1.StateAt(w1.T); var (q2, _) = b2.StateAt(w2.T);
        Assert.True((q1 - q2).Length < 1e-3, $"{q1} vs {q2}");
    }

    [Fact]
    public void WarpStopsExactlyAtTheActiveCraftsSoiChange()
    {
        var w = MakeWorld(out var sat, out var tanker);
        w.ActiveCraftId = tanker.Id;
        var expected = Patcher.NextEvent(w.Sys, tanker.Rails, 0, 10 * Units.Day);
        Assert.Equal(PatchEnd.EnterSoi, expected.End);
        w.SetWarp(7);
        double advanced = w.Advance(3 * Units.Day);
        Assert.Equal(expected.TEnd, w.T, 1e-6);
        Assert.True(advanced < 3 * Units.Day);
        Assert.Equal(0, w.WarpIndex);
        Assert.Contains(w.Events, e => e.Kind == SimEventKind.SoiChange && e.CraftId == tanker.Id);
        Assert.Equal(2, tanker.BodyId);
        // Continuing works and the satellite is untouched.
        w.Advance(1000);
        Assert.Equal(expected.TEnd + 1000, w.T, 1e-6);
        Assert.Equal(1, sat.BodyId);
    }

    [Fact]
    public void DeltaVEstimatesAreSensible()
    {
        var sys = TestSystems.Basic();
        double toMoon = DeltaV.LowToLow(sys, sys.HomeId, 2);
        Assert.InRange(toMoon, 800, 1300);
        double toRed = DeltaV.LowToLow(sys, sys.HomeId, 3);
        Assert.InRange(toRed, 1500, 5000);
        Assert.True(DeltaV.HomeSurfaceToLowOrbitOf(sys, sys.HomeId) == DeltaV.HomeSurfaceToLowOrbit);
        Assert.Equal(DeltaV.LowToLow(sys, 2, sys.HomeId), toMoon, 1e-6);   // symmetric by construction
    }
}
