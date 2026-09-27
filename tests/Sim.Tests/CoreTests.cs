using Perigee.Sim;

namespace Perigee.Tests;

public class CoreTests
{
    [Fact]
    public void VectorAlgebra()
    {
        var a = new Vec2d(3, 4);
        Assert.Equal(5, a.Length, 12);
        Assert.Equal(new Vec2d(-4, 3), a.Perp);
        Assert.Equal(0, Vec2d.Dot(a, a.Perp), 12);
        Assert.Equal(25, Vec2d.Cross(a, a.Perp), 12);
        var r = Vec2d.UnitX.Rotated(Math.PI / 2);
        Assert.Equal(0, r.X, 12); Assert.Equal(1, r.Y, 12);
        Assert.Equal(Math.PI / 2, MathD.WrapPi(Math.PI / 2 + 4 * Math.PI), 12);
        Assert.Equal(-Math.PI / 2, MathD.WrapPi(3 * Math.PI / 2), 12);
    }

    [Fact]
    public void RngIsDeterministicAndUniform()
    {
        var a = new Rng(42); var b = new Rng(42);
        for (int i = 0; i < 1000; i++) Assert.Equal(a.NextULong(), b.NextULong());
        var r = new Rng(7);
        double sum = 0; int n = 200000; int lo = 0;
        for (int i = 0; i < n; i++) { double d = r.NextDouble(); sum += d; if (d < 0.5) lo++; Assert.True(d >= 0 && d < 1); }
        Assert.InRange(sum / n, 0.49, 0.51);
        Assert.InRange(lo / (double)n, 0.49, 0.51);
        var (s0, s1, s2, s3) = r.State;
        var c = new Rng(s0, s1, s2, s3);
        Assert.Equal(r.NextULong(), c.NextULong());   // state round-trips (saves)
    }

    [Fact]
    public void Formatting()
    {
        Assert.Equal("Day 1  00:00:00", Units.FormatDate(0));
        Assert.Equal("Day 2  01:02:03", Units.FormatDate(Units.Day + 3723));
        Assert.Equal("$1.50M", Units.FormatMoney(150_000_000));
        Assert.Equal("-$12.0k", Units.FormatMoney(-1_200_000));
        Assert.Equal("1.2 km", Units.FormatDistance(1234));
    }
}
