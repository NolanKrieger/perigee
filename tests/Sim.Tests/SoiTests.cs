using Perigee.Sim;

namespace Perigee.Tests;

public class SoiTests
{
    /// <summary>RK4 two-body integration in the parent's frame, with the child moving analytically; returns the first time the craft is within the child's SOI.</summary>
    static (double t, Vec2d r, Vec2d v) BruteForceEncounter(StarSystem sys, int parentId, int childId, Vec2d r, Vec2d v, double t0, double tMax, double dt)
    {
        double mu = sys[parentId].Gm;
        var child = sys[childId];
        Vec2d Acc(Vec2d p) => -p * (mu / Math.Pow(p.Length, 3));
        double t = t0;
        Vec2d rp = r, vp = v; double tp = t;
        while (t < tMax)
        {
            var k1v = Acc(r); var k1r = v;
            var k2v = Acc(r + k1r * (dt / 2)); var k2r = v + k1v * (dt / 2);
            var k3v = Acc(r + k2r * (dt / 2)); var k3r = v + k2v * (dt / 2);
            var k4v = Acc(r + k3r * dt); var k4r = v + k3v * dt;
            rp = r; vp = v; tp = t;
            r += (k1r + k2r * 2 + k3r * 2 + k4r) * (dt / 6);
            v += (k1v + k2v * 2 + k3v * 2 + k4v) * (dt / 6);
            t += dt;
            var cpos = sys.LocalState(childId, t).r;
            if ((r - cpos).Length <= child.Soi)
            {
                // linear interpolation of the crossing inside the last step
                double dPrev = (rp - sys.LocalState(childId, tp).r).Length - child.Soi;
                double dNow = (r - cpos).Length - child.Soi;
                double f = dPrev / (dPrev - dNow);
                return (tp + f * dt, Vec2d.Lerp(rp, r, f), Vec2d.Lerp(vp, v, f));
            }
        }
        return (double.NaN, r, v);
    }

    [Fact]
    public void HomeSoiMatchesGdd()
    {
        var sys = TestSystems.Basic();
        Assert.InRange(sys.Home.Soi / 1e6, 50, 75);   // GDD §4: ≈ 60 Mm
        Assert.True(sys[2].Soi < sys.Home.Soi);
        Assert.True(double.IsPositiveInfinity(sys.Star.Soi));
    }

    [Fact]
    public void MoonEncounterTimeMatchesBruteForce()
    {
        var sys = TestSystems.Basic();
        var home = sys.Home; var moon = sys[2];
        double t0 = 1000;
        // Hohmann-style transfer from 100 km: burn prograde at the point opposite the moon's arrival position.
        double r1 = home.Radius + 100_000, r2 = moon.Orbit!.Value.A;
        double a = 0.5 * (r1 + r2);
        double tof = Math.PI * Math.Sqrt(a * a * a / home.Gm);
        double moonAngleAtArrival = moon.Orbit.Value.ThetaOf(moon.Orbit.Value.TrueAnomalyAt(t0 + tof));
        double depAngle = moonAngleAtArrival - Math.PI;
        var r = Vec2d.FromPolar(r1, depAngle);
        double vt = Math.Sqrt(home.Gm * (2 / r1 - 1 / a));
        var v = Vec2d.FromAngle(depAngle).Perp * vt;
        var rails = new RailsState(home.Id, Conic.FromState(home.Gm, r, v, t0));
        var p = Patcher.NextEvent(sys, rails, t0, t0 + 10 * Units.Day);
        Assert.Equal(PatchEnd.EnterSoi, p.End);
        Assert.Equal(moon.Id, p.NextBodyId);
        var bf = BruteForceEncounter(sys, home.Id, moon.Id, r, v, t0, t0 + 10 * Units.Day, 0.5);
        Assert.False(double.IsNaN(bf.t), "brute force never met the moon");
        Assert.True(Math.Abs(bf.t - p.TEnd) < 1.0, $"encounter time: patcher {p.TEnd:0.000} vs brute force {bf.t:0.000}");
        // The converted state in the moon frame agrees with the integrated relative state.
        var next = Patcher.Transition(sys, p);
        var (rm, vm) = next.Conic.StateAt(p.TEnd);
        var (cr, cv) = sys.LocalState(moon.Id, p.TEnd);
        var relBf = bf.r - cr; var relVbf = bf.v - cv;
        Assert.True((rm - relBf).Length < 50, $"relative position {rm} vs {relBf}");
        Assert.True((vm - relVbf).Length < 0.05, $"relative velocity {vm} vs {relVbf}");
        Assert.Equal(moon.Soi, rm.Length, moon.Soi * 1e-6);
    }

    [Fact]
    public void ExitAndReentryChainThroughThePatcher()
    {
        var sys = TestSystems.Basic();
        var home = sys.Home;
        // Hyperbolic departure from home: 100 km, 1.5× circular speed (escape is √2 ≈ 1.414×).
        double r1 = home.Radius + 100_000;
        var rails = new RailsState(home.Id, Conic.FromState(home.Gm, new Vec2d(r1, 0), new Vec2d(0, Math.Sqrt(home.Gm / r1) * 1.5), 0));
        var patches = Patcher.Predict(sys, rails, 0, 3);
        Assert.Equal(PatchEnd.ExitSoi, patches[0].End);
        Assert.Equal(sys.Star.Id, patches[0].NextBodyId);
        Assert.True(patches.Count >= 2);
        Assert.Equal(sys.Star.Id, patches[1].BodyId);
        Assert.True(patches[1].Conic.IsEllipse, "heliocentric leg should be bound");
        Assert.Equal(home.Soi, patches[0].EndPos.Length, home.Soi * 1e-6);
    }

    [Fact]
    public void AtmosphereArrivalIsTheInboundCrossing()
    {
        var sys = TestSystems.Basic();
        var home = sys.Home;
        double r1 = home.Radius + 300_000;
        // Elliptic orbit with periapsis inside the atmosphere: apoapsis 300 km, periapsis 20 km.
        double rp = home.Radius + 20_000, ra = r1;
        double a = 0.5 * (rp + ra);
        double vApo = Math.Sqrt(home.Gm * (2 / ra - 1 / a));
        var rails = new RailsState(home.Id, Conic.FromState(home.Gm, new Vec2d(ra, 0), new Vec2d(0, vApo), 0));
        var p = Patcher.NextEvent(sys, rails, 0, 10 * Units.Day);
        Assert.Equal(PatchEnd.ActiveZone, p.End);
        double zone = home.Radius + home.Atmo!.Height;
        Assert.Equal(zone, p.EndPos.Length, 1e-3 * zone);
        Assert.True(Vec2d.Dot(p.EndPos, p.EndVel) < 0, "must be descending at atmosphere entry");
        Assert.True(p.TEnd > 0 && p.TEnd < rails.Conic.Period / 2);
    }
}
