using Perigee.Sim;

namespace Perigee.Tests;

public class OrbitTests
{
    const double Mu = 1.99e12;

    static void AssertClose(Vec2d a, Vec2d b, double rel, string what)
    {
        double scale = Math.Max(a.Length, b.Length);
        Assert.True((a - b).Length <= rel * scale, $"{what}: {a} vs {b} (rel err {(a - b).Length / scale:e2})");
    }

    public static IEnumerable<object[]> States()
    {
        // (rx, ry, vx, vy): circular, elliptic (both directions), high-e, parabolic-ish, hyperbolic, retrograde hyperbolic
        double r0 = 545_000, vc = Math.Sqrt(Mu / r0);
        yield return new object[] { r0, 0.0, 0.0, vc };
        yield return new object[] { r0, 0.0, 0.0, -vc };
        yield return new object[] { r0, 0.0, 200.0, vc * 1.2 };
        yield return new object[] { 0.0, r0 * 2, -vc * 0.5, 100.0 };
        yield return new object[] { r0, 0.0, 0.0, vc * 1.4 };            // e ≈ 0.96
        yield return new object[] { r0, 0.0, 0.0, vc * Math.Sqrt(2) };    // parabolic (numerically)
        yield return new object[] { r0, 0.0, 300.0, vc * 1.7 };           // hyperbolic
        yield return new object[] { r0, r0, -vc * 1.6, -vc * 0.2 };       // retrograde hyperbolic
        yield return new object[] { 3e6, 1e6, 50.0, 900.0 };
    }

    [Theory]
    [MemberData(nameof(States))]
    public void StateElementsRoundTrip(double rx, double ry, double vx, double vy)
    {
        var r = new Vec2d(rx, ry); var v = new Vec2d(vx, vy);
        double t0 = 12345.678;
        var c = Conic.FromState(Mu, r, v, t0);
        var (r1, v1) = c.StateAt(t0);
        AssertClose(r, r1, 1e-9, "position");
        AssertClose(v, v1, 1e-9, "velocity");
        // Propagate ahead and re-derive: elements must agree.
        double dt = c.IsEllipse ? c.Period * 0.37 : 5000;
        var (r2, v2) = c.StateAt(t0 + dt);
        var c2 = Conic.FromState(Mu, r2, v2, t0 + dt);
        Assert.Equal(c.E, c2.E, 9);
        Assert.Equal(c.P, c2.P, c.P * 1e-9);
        Assert.Equal(c.Dir, c2.Dir);
        var (r3, v3) = c2.StateAt(t0);
        AssertClose(r, r3, 1e-8, "position after forward/back");
        AssertClose(v, v3, 1e-8, "velocity after forward/back");
    }

    [Theory]
    [MemberData(nameof(States))]
    public void EnergyAndAngularMomentumConservedOnRails(double rx, double ry, double vx, double vy)
    {
        var r = new Vec2d(rx, ry); var v = new Vec2d(vx, vy);
        var c = Conic.FromState(Mu, r, v, 0);
        double e0 = v.LengthSq / 2 - Mu / r.Length, h0 = Vec2d.Cross(r, v);
        double span = c.IsEllipse ? 3 * c.Period : 20000;
        for (int i = 1; i <= 200; i++)
        {
            var (ri, vi) = c.StateAt(span * i / 200.0);
            double ei = vi.LengthSq / 2 - Mu / ri.Length, hi = Vec2d.Cross(ri, vi);
            Assert.True(Math.Abs(ei - e0) <= 1e-9 * (Math.Abs(e0) + Mu / r.Length), $"energy drift {ei - e0:e2} at step {i}");
            Assert.True(Math.Abs(hi - h0) <= 1e-9 * Math.Abs(h0), $"angular momentum drift {hi - h0:e2} at step {i}");
        }
    }

    [Fact]
    public void KeplerSolversAgreeWithTheirEquations()
    {
        var rng = new Rng(3);
        for (int i = 0; i < 5000; i++)
        {
            double e = rng.NextDouble() * 0.999;
            double m = rng.Range(-20, 20);
            double ea = Kepler.SolveEllipse(m, e);
            Assert.True(Math.Abs(ea - e * Math.Sin(ea) - m) < 1e-11, $"ellipse e={e} M={m} residual {ea - e * Math.Sin(ea) - m:e2}");
            double eh = 1 + rng.NextDouble() * 9;
            double mh = rng.Range(-50, 50);
            double ha = Kepler.SolveHyperbola(mh, eh);
            Assert.True(Math.Abs(eh * Math.Sinh(ha) - ha - mh) < 1e-9 * Math.Max(1, Math.Abs(mh)), $"hyperbola e={eh} M={mh}");
            double mp = rng.Range(-50, 50);
            double d = Kepler.SolveParabola(mp);
            Assert.True(Math.Abs(d + d * d * d / 3 - mp) < 1e-9 * Math.Max(1, Math.Abs(mp)), $"parabola M={mp}");
        }
        // Extreme eccentricity near 1 still converges.
        double eaX = Kepler.SolveEllipse(0.01, 0.999999);
        Assert.True(Math.Abs(eaX - 0.999999 * Math.Sin(eaX) - 0.01) < 1e-10);
    }

    [Fact]
    public void PeriodAndApsidesMatchTextbook()
    {
        double r0 = 545_000;
        var c = Conic.FromState(Mu, new Vec2d(r0, 0), new Vec2d(0, Math.Sqrt(Mu / r0)), 0);
        Assert.Equal(r0, c.Periapsis, r0 * 1e-12);
        Assert.Equal(r0, c.Apoapsis, r0 * 1e-9);
        Assert.Equal(MathD.TwoPi * Math.Sqrt(r0 * r0 * r0 / Mu), c.Period, 1e-6);
        // GDD §4: low home orbit ≈ 1,950 m/s, period ≈ 28 min at 60–120 km.
        double v = Math.Sqrt(Mu / 540_000);
        Assert.InRange(v, 1900, 2000);
        Assert.InRange(c.Period / 60, 26, 30);
    }

    [Fact]
    public void TimeAtTrueAnomalyIsFirstAfter()
    {
        double r0 = 600_000;
        var c = Conic.FromState(Mu, new Vec2d(r0, 0), new Vec2d(0, Math.Sqrt(Mu / r0) * 1.1), 100);
        double nu = 1.0;
        double t1 = c.TimeAtTrueAnomaly(nu, 100);
        Assert.True(t1 >= 100);
        Assert.Equal(nu, c.TrueAnomalyAt(t1), 1e-9);
        double t2 = c.TimeAtTrueAnomaly(nu, t1 + 1);
        Assert.Equal(t1 + c.Period, t2, 1e-6);
        double nuR = c.TrueAnomalyAtRadius(c.Apoapsis * 0.9);
        Assert.Equal(c.Apoapsis * 0.9, c.RadiusAt(nuR), 1e-6 * c.Apoapsis);
        Assert.True(double.IsNaN(c.TrueAnomalyAtRadius(c.Apoapsis * 1.1)));
    }
}
