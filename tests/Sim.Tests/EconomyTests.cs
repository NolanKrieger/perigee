using Perigee.Sim;

namespace Perigee.Tests;

public class EconomyTests
{
    static World Career(Preset? preset = null)
    {
        var w = SimHost.NewTestWorld();
        w.StartCareer(preset ?? Preset.Standard, "Test Co.");
        return w;
    }

    [Fact]
    public void PresetsAndStarterSet()
    {
        var w = Career();
        Assert.Equal(80_000_000, w.Cash);
        Assert.True(w.IsUnlocked("core-s") && w.IsUnlocked("eng-kestrel") && !w.IsUnlocked("eng-condor"));
        var b = Career(Preset.Brutal);
        Assert.Equal(40_000_000, b.Cash);
        Assert.Equal((long)(Parts.Get("eng-condor").Cost * 1.3), b.PartPrice(Parts.Get("eng-condor")));
    }

    [Fact]
    public void RdIsGatedByPriceOnly()
    {
        var w = Career();
        var condor = Parts.Get("eng-condor");
        Assert.InRange(w.RdPrice(condor) / (double)condor.Cost, 8, 15);
        Assert.True(w.BuyRd("eng-condor"));
        Assert.True(w.IsUnlocked("eng-condor"));
        Assert.Equal(80_000_000 - w.RdPrice(condor), w.Cash);
        w.Cash = 1000;
        Assert.False(w.BuyRd("eng-titan"));
        Assert.False(w.IsUnlocked("eng-titan"));
    }

    [Fact]
    public void LaunchCostsPartsPlusFuelAndBlocksWhenBroke()
    {
        var w = Career();
        var d = TestDesigns.Sounding();
        long expected = d.Parts.Sum(p => Parts.Get(p.DefId).Cost) + (long)(0.6 * 100_000);   // sounding rocket: 0.6 t of methalox
        Assert.Equal(expected, w.LaunchCost(d));
        var c = w.TryLaunch(d, "S", w.Sys.HomeId, w.Sys.Home.LaunchSiteAngle);
        Assert.NotNull(c);
        Assert.Equal(80_000_000 - expected, w.Cash);
        w.Cash = expected - 1;
        Assert.Null(w.TryLaunch(d, "S2", w.Sys.HomeId, w.Sys.Home.LaunchSiteAngle));
        // Locked parts block too.
        var r = TestDesigns.Recoverable();
        w.Cash = 1_000_000_000;
        Assert.Null(w.TryLaunch(r, "R", w.Sys.HomeId, w.Sys.Home.LaunchSiteAngle));
        Assert.Contains("eng-condor", w.LockedParts(r));
    }

    [Fact]
    public void MarketMultiplierAndSaturation()
    {
        var w = Career();
        var lho = w.Market.OrbitNode(w.Sys.HomeId)!;
        Assert.InRange(lho.Multiplier, 7, 9);   // GDD: low home orbit ≈ ×8
        var moon = w.Market.OrbitNode(2)!;
        Assert.InRange(moon.Multiplier, 10, 16);   // ≈ ×13
        Assert.True(w.Market.Home.Multiplier == 1);
        long p0 = w.Market.Price(lho, Resource.Methalox, w.T);
        Assert.InRange(p0, 700_000, 900_000);
        // Selling 50 t (= K) halves the next-tonne price; the quote integrates the curve.
        long rev = w.Market.Sell(lho, Resource.Methalox, 50, w.T);
        Assert.InRange(rev, (long)(p0 * 50 * 0.6), (long)(p0 * 50 * 0.75));   // ln 2 ≈ 0.693 of the naive price
        Assert.InRange(w.Market.Price(lho, Resource.Methalox, w.T), p0 * 0.49, p0 * 0.51);
        // Recovery: 10-day half-life.
        Assert.InRange(w.Market.Price(lho, Resource.Methalox, w.T + 10 * Units.Day), p0 * 0.66, p0 * 0.68);
        Assert.InRange(w.Market.Price(lho, Resource.Methalox, w.T + 60 * Units.Day), p0 * 0.98, p0 * 1.0);
        Assert.Equal(0, w.Market.Quote(lho, Resource.Ice, 5, w.T));   // raw resources don't sell
    }

    [Fact]
    public void OnlyDepotsSellInOrbitAndAnyoneAtHome()
    {
        var w = Career();
        var home = w.Sys.Home;
        // A plain craft in low orbit cannot sell; a depot can.
        var plain = SimHost.SpawnInto(w, "tanker", "Plain", 100_000, 0, 1);
        Assert.Equal(0, w.SellFrom(plain, Resource.Methalox, 0.5));
        var d = new Design { Name = "Depot" };
        int core = d.Root("depot-ctrl");
        d.Stack(core, "tank-m-medium");
        var depot = Craft.FromDesign(d, "Depot");
        depot.BodyId = home.Id; depot.Mode = CraftMode.Active;
        double r0 = home.Radius + 100_000; depot.Pos = new Vec2d(r0, 0); depot.Vel = new Vec2d(0, -Math.Sqrt(home.Gm / r0));
        w.AddCraft(depot); w.ActiveCraftId = depot.Id;
        for (int i = 0; i < 60; i++) w.Advance(Units.PhysicsDt);
        long cash = w.Cash;
        long rev = w.SellFrom(depot, Resource.Methalox, 2);
        Assert.True(rev > 1_000_000, $"revenue {rev}");
        Assert.Equal(cash + rev, w.Cash);
        Assert.InRange(depot.Fuel, 3.99, 4.01);
        // Landed at home: fixed base price, no glut.
        var landed = w.Launch(TestDesigns.Sounding(), "L", home.Id, home.LaunchSiteAngle);
        long rev2 = w.SellFrom(landed, Resource.Methalox, 0.6);
        Assert.Equal((long)(0.6 * 100_000), rev2);
    }

    [Fact]
    public void UpkeepChargesAtDayBoundariesAndRunwayFollows()
    {
        var w = Career();
        Assert.Equal(0, w.DailyUpkeep());
        Assert.True(double.IsPositiveInfinity(w.Runway));
        w.DroneShips.Add((w.Sys.HomeId, 0.5));
        var sat = SimHost.SpawnInto(w, "station", "S", 120_000, 0, 0.1);
        sat.Parts[0].Def = Parts.Get("depot-ctrl");   // make it a depot
        Assert.Equal(300_000, w.DailyUpkeep());
        Assert.InRange(w.Runway, 266, 267);
        long cash = w.Cash;
        w.Advance(Units.Day * 2.5);
        Assert.Equal(cash - 2 * 300_000, w.Cash);
        Assert.True(w.CashHistory.Count >= 3);
        Assert.True(w.Market.History.Count > 0);
    }

    [Fact]
    public void BankruptcyIsInstantWithoutAnEarningCraftAndGracedWithOne()
    {
        var w = Career();
        w.Cash = 0;
        w.CheckInsolvency();
        Assert.True(w.GameOver);
        var g = Career();
        var tanker = SimHost.SpawnInto(g, "tanker", "T", 120_000, 0, 1);   // carries sellable methalox
        g.Cash = -5;
        g.CheckInsolvency();
        Assert.False(g.GameOver);
        Assert.Equal(1, g.InsolventDay);
        g.Advance(9.5 * Units.Day);
        Assert.False(g.GameOver);
        g.Advance(1 * Units.Day);
        Assert.True(g.GameOver);
        Assert.Contains("10 days", g.GameOverCause);
        // Brutal: 3 days.
        var b = Career(Preset.Brutal);
        SimHost.SpawnInto(b, "tanker", "T", 120_000, 0, 1);
        b.Cash = -1; b.CheckInsolvency();
        b.Advance(3.2 * Units.Day);
        Assert.True(b.GameOver);
    }

    [Fact]
    public void ScoreIsNetWorthPlusExploration()
    {
        var w = Career();
        long base0 = w.Score();
        Assert.Equal(w.Cash, base0);
        w.RecordExploration(2, Explored.Flyby);
        Assert.True(w.Score() > base0);
        double m = w.BodyMultiplier(2);
        Assert.Equal(base0 + (long)(10 * m * 1_000_000), w.Score());
        w.RecordExploration(2, Explored.Flyby);   // no double credit
        Assert.Equal(base0 + (long)(10 * m * 1_000_000), w.Score());
        w.RecordExploration(2, Explored.Landed);
        Assert.Equal(base0 + (long)(60 * m * 1_000_000), w.Score());
    }

    /// <summary>
    /// GDD §2 balance targets (proposal: depot ≤ 60% of direct for the nearest body, ≤ 35% for a neighbour planet, mined ≈ half again).
    /// Measured 2026-09-23 with GDD Isp/prices: depots do NOT beat direct launches on cost at this world scale (see BALANCE.md,
    /// "§2 depot targets"). This test guards the economy model's actual numbers so a regression or a retune is visible.
    /// </summary>
    [Fact]
    public void DepotBalanceMeasured()
    {
        var sys = TestSystems.Basic();
        var moon = sys[2]; var neighbour = sys[3];
        var m = EconomySim.Compare(sys, moon, 0.6);
        var n = EconomySim.Compare(sys, neighbour, 0.6);
        Console.WriteLine($"moon: direct {Units.FormatMoney(m.Direct)} ({m.DirectDesc}) vs depot {Units.FormatMoney(m.Depot)} ({m.DepotDesc}) = {m.DepotRatio:0.00}; mined {m.MinedRatio:0.00}; fuel in orbit {Units.FormatMoney(m.FuelPriceInOrbit)}/t");
        Console.WriteLine($"neighbour: direct {Units.FormatMoney(n.Direct)} ({n.DirectDesc}) vs depot {Units.FormatMoney(n.Depot)} ({n.DepotDesc}) = {n.DepotRatio:0.00}; mined {n.MinedRatio:0.00}");
        // Orbital fuel costs 4–6× home fuel (the mass-ratio tax of lifting it), as the §10 ×8 multiplier assumes.
        Assert.InRange(m.FuelPriceInOrbit / (double)Market.BasePrice(Resource.Methalox), 3.5, 7.0);
        // Mined fuel always beats home-lifted fuel for the same mission.
        Assert.True(m.Mined < m.Depot && n.Mined < n.Depot);
        // Current state of the world (guard, not a goal): a depot moon mission costs within 25% of direct; a neighbour mission within 60%.
        Assert.True(m.DepotRatio <= 1.25, $"moon depot ratio {m.DepotRatio:0.00}");
        Assert.True(n.DepotRatio <= 1.60, $"neighbour depot ratio {n.DepotRatio:0.00}");
        Assert.True(n.MinedRatio <= 1.30, $"neighbour mined ratio {n.MinedRatio:0.00}");
    }
}
