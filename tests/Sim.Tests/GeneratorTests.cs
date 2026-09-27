using Perigee.Sim;

namespace Perigee.Tests;

public class GeneratorTests
{
    [Fact]
    public void SameSeedSameSystem()
    {
        var a = SystemGenerator.Generate(12345);
        var b = SystemGenerator.Generate(12345);
        Assert.Equal(a.Bodies.Count, b.Bodies.Count);
        for (int i = 0; i < a.Bodies.Count; i++)
        {
            Assert.Equal(a.Bodies[i].Name, b.Bodies[i].Name);
            Assert.Equal(a.Bodies[i].Gm, b.Bodies[i].Gm);
            Assert.Equal(a.Bodies[i].Orbit?.P, b.Bodies[i].Orbit?.P);
            Assert.Equal(a.Bodies[i].Terrain.Length, b.Bodies[i].Terrain.Length);
            if (a.Bodies[i].Terrain.Length > 100) Assert.Equal(a.Bodies[i].Terrain[100], b.Bodies[i].Terrain[100]);
        }
        Assert.NotEqual(a.Bodies.Count + a.Bodies.Sum(x => x.Name.Length), SystemGenerator.Generate(54321).Bodies.Count + SystemGenerator.Generate(54321).Bodies.Sum(x => x.Name.Length));
    }

    [Fact]
    public void HomeIsAlwaysEarthLikeWithALaunchSite()
    {
        for (ulong seed = 1; seed <= 25; seed++)
        {
            var sys = SystemGenerator.Generate(seed);
            var home = sys.Home;
            Assert.Equal(BodyType.Home, home.Type);
            Assert.Equal(450_000, home.Radius);
            Assert.InRange(home.SurfaceGravity, 9.7, 9.9);
            Assert.NotNull(home.Atmo);
            Assert.False(double.IsNaN(home.LaunchSiteAngle));
            Assert.True(home.TerrainHeightLocal(home.LaunchSiteAngle) > 0, "launch site must be on land");
            Assert.InRange(home.Orbit!.Value.Period / Units.Day, 380, 420);   // ≈ 400-day year
            Assert.True(home.RotationPeriod < 0, "clockwise rotation");
            Assert.InRange(home.Soi / 1e6, 30, 120);
        }
    }

    [Fact]
    public void ThousandSeedsPassValidation()
    {
        var failures = new List<string>();
        int planetsMin = int.MaxValue, planetsMax = 0, moonsMax = 0;
        double iceDvMax = 0;
        for (ulong seed = 1; seed <= 1000; seed++)
        {
            var sys = SystemGenerator.Generate(seed);
            var issues = SystemValidator.Check(sys);
            if (issues.Count > 0) failures.Add($"seed {seed}: {string.Join("; ", issues)}");
            int planets = sys.Bodies.Count(b => b.Parent == 0 && b.Type is BodyType.Home or BodyType.Rocky or BodyType.Desert or BodyType.GasGiant or BodyType.IceGiant or BodyType.IceDwarf);
            planetsMin = Math.Min(planetsMin, planets); planetsMax = Math.Max(planetsMax, planets);
            moonsMax = Math.Max(moonsMax, sys.Bodies.Count(b => b.Type is BodyType.Moon or BodyType.IceMoon));
            SystemGenerator.NearestWithIceAndOre(sys, out double dv);
            iceDvMax = Math.Max(iceDvMax, dv);
        }
        Assert.True(failures.Count == 0, $"{failures.Count} seeds failed, e.g.\n{string.Join("\n", failures.Take(8))}");
        Assert.InRange(planetsMin, 6, 10);
        Assert.InRange(planetsMax, 6, 10);
        Assert.True(moonsMax <= 25);
        Assert.True(iceDvMax <= SystemGenerator.IceOreBudget, $"worst ice+ore Δv {iceDvMax:0}");
    }

    [Fact]
    public void DepositsAreHiddenUntilScannedAndCoverArcs()
    {
        var sys = SystemGenerator.Generate(77);
        var body = sys.Bodies.First(b => b.Deposits.Count > 0);
        Assert.False(body.Scanned);
        var d = body.Deposits[0];
        Assert.True(d.Contains(d.ArcStart + 0.01));
        Assert.True(d.Richness > 0);
        int hits = 0;
        for (int i = 0; i < 360; i++) if (d.Contains(i * Math.PI / 180)) hits++;
        Assert.InRange(hits, 40, 230);
    }

    [Fact]
    public void GasGiantsHaveXenonAndMarsLikesHaveCo2Air()
    {
        int giants = 0, deserts = 0;
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var sys = SystemGenerator.Generate(seed);
            foreach (var b in sys.Bodies)
            {
                if (b.Type is BodyType.GasGiant or BodyType.IceGiant) { giants++; Assert.True(b.Atmo!.HasXenon); }
                if (b.Type == BodyType.Desert) { deserts++; Assert.True(b.Atmo!.HasCo2); }
            }
        }
        Assert.True(giants > 30 && deserts > 10);
    }
}
