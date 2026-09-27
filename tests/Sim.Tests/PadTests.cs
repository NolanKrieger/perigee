using Perigee.Sim;

namespace Perigee.Tests;

public class PadTests
{
    static World Career() { var w = SimHost.NewTestWorld(); w.StartCareer(Preset.Standard, "T"); w.Cash = 100_000_000_000; foreach (var p in Parts.All) w.Unlocked.Add(p.Id); return w; }

    static Craft LandBase(World w, int bodyId, double angle, bool kit = true, bool fab = true, double metal = 20, double methalox = 10)
    {
        var d = new Design { Name = "Base" };
        int core = d.Root("outpost-core");
        int st = d.Stack(core, "surface-tank");
        if (kit) d.Stack(core, "pad-kit", below: false);
        if (fab) d.Radial(st, "fabricator", 1, 0);
        d.Radial(st, "rtg", -1, 0);
        var c = w.Launch(d, "Base", bodyId, angle);
        var tank = c.Parts.First(p => p.Def.StorageT > 0);
        tank.Set(Resource.Metal, metal); tank.Set(Resource.Methalox, methalox);
        c.UpdateMass();
        return c;
    }

    [Fact]
    public void APadNeedsACoreAKitAndAFabricatorWithin200m()
    {
        var w = Career(); var vell = w.Sys[2];
        LandBase(w, vell.Id, 1.0, kit: false, fab: true);
        Assert.Empty(w.LaunchPads());
        var w2 = Career();
        LandBase(w2, 2, 1.0, kit: true, fab: false);
        Assert.Empty(w2.LaunchPads());
        // Fabricator on a separate module that landed 150 m away: joins the outpost, pad is operational.
        var w3 = Career(); var v3 = w3.Sys[2];
        LandBase(w3, v3.Id, 1.0, kit: true, fab: false);
        var fd = new Design { Name = "Fab" }; int c0 = fd.Root("core-s"); fd.Stack(c0, "fabricator");
        w3.Launch(fd, "Fab", v3.Id, 1.0 + 150.0 / v3.Radius);
        var pad = Assert.Single(w3.LaunchPads());
        Assert.Equal(2, pad.Outpost.Members.Count);
        Assert.Equal(v3.Id, pad.Body.Id);
        // ...but not 400 m away.
        var w4 = Career(); var v4 = w4.Sys[2];
        LandBase(w4, v4.Id, 1.0, kit: true, fab: false);
        w4.Launch(fd, "Fab", v4.Id, 1.0 + 400.0 / v4.Radius);
        Assert.Empty(w4.LaunchPads());
    }

    [Fact]
    public void OffWorldLaunchSplitsCostIntoMetalAndCashAndFuelsFromStorage()
    {
        var w = Career(); var vell = w.Sys[2];
        var b = LandBase(w, vell.Id, 1.0, metal: 20, methalox: 10);
        var pad = w.PadAt(vell.Id)!;
        var d = TestDesigns.Hopper2();
        var q = w.PadQuote(d, pad);
        double dry = d.Parts.Sum(p => Parts.Get(p.DefId).DryMass);
        long price = d.Parts.Sum(p => w.PartPrice(Parts.Get(p.DefId)));
        Assert.Equal(dry * Pads.MetalPerTonne, q.MetalNeeded, 9);
        Assert.InRange(q.Cash, (long)(price * Pads.CashShare) - 10, (long)(price * Pads.CashShare) + 10);   // sum of halves vs half of sum: equal up to rounding
        Assert.Equal(20, q.MetalAvailable, 9);
        long cash0 = w.Cash;
        var c = w.TryLaunch(d, "Hop", vell.Id, 0);
        Assert.NotNull(c);
        Assert.Equal(cash0 - q.Cash, w.Cash);
        Assert.Equal(20 - q.MetalNeeded, b.Resource(Resource.Metal), 6);
        Assert.Equal(1.5, c!.Resource(Resource.Methalox), 6);               // long S tank filled from the base's 10 t
        Assert.Equal(10 - 1.5, b.Resource(Resource.Methalox), 6);
        Assert.Equal(CraftMode.Landed, c.Mode);
        Assert.Equal(vell.Id, c.BodyId);
        Assert.True(Math.Abs(MathD.WrapPi(c.Landed!.Value.LocalAngle - 1.0)) * vell.Radius < 60, "launched beside the kit");
        // Metal short → refused, nothing spent.
        var w2 = Career(); var v2 = w2.Sys[2];
        LandBase(w2, v2.Id, 1.0, metal: 0.1);
        long cash2 = w2.Cash;
        Assert.Null(w2.TryLaunch(d, "Hop", v2.Id, 0));
        Assert.Equal(cash2, w2.Cash);
        // No pad → refused.
        var w3 = Career();
        Assert.Null(w3.TryLaunch(d, "Hop", 2, 0));
    }

    [Fact]
    public void PartialLocalFuelIsAllowed()
    {
        var w = Career(); var vell = w.Sys[2];
        LandBase(w, vell.Id, 1.0, metal: 20, methalox: 0.4);
        var c = w.TryLaunch(TestDesigns.Hopper2(), "Hop", vell.Id, 0)!;
        Assert.Equal(0.4, c.Resource(Resource.Methalox), 6);
    }

    [Fact]
    public void PadSurfaceMarketSellsFromTheWholeOutpost()
    {
        var w = Career(); var vell = w.Sys[2];
        var b = LandBase(w, vell.Id, 1.0);
        var tank = b.Parts.First(p => p.Def.StorageT > 0);
        tank.Set(Resource.Water, 5);
        var node = w.NodeFor(b);
        Assert.NotNull(node);
        Assert.Equal(NodeKind.PadSurface, node!.Kind);
        Assert.True(node.Multiplier > 5, $"multiplier {node.Multiplier:0.0}");
        long cash0 = w.Cash;
        long rev = w.SellFrom(b, Resource.Water, 3);
        Assert.True(rev > 0);
        Assert.Equal(cash0 + rev, w.Cash);
        Assert.Equal(2, b.Resource(Resource.Water), 6);
        // A lander parked away from any pad has no market.
        var w2 = Career();
        var lone = w2.Launch(TestDesigns.Lander(), "L", 2, 3.0);
        Assert.Null(w2.NodeFor(lone));
        Assert.Equal(0, w2.SellFrom(lone, Resource.Methalox, 1));
    }

    [Fact]
    public void RecoveryAtAnOffWorldPadReturnsMetalAndPaysTheCashShare()
    {
        var w = Career(); var vell = w.Sys[2];
        var b = LandBase(w, vell.Id, 1.0, metal: 20, methalox: 10);
        var d = TestDesigns.Hopper2();
        var c = w.TryLaunch(d, "Hop", vell.Id, 0)!;
        double metalAfterBuild = b.Resource(Resource.Metal);
        double fuelAfterBuild = b.Resource(Resource.Methalox);
        Assert.Equal(0.90, w.RefundRate(c));
        long cash0 = w.Cash;
        long value = w.RecoveryValue(c);
        Assert.Equal((long)(c.PartsCost * Pads.CashShare * 0.9), value);
        Assert.True(w.Recover(c));
        Assert.Equal(cash0 + value, w.Cash);
        double metalBack = d.Parts.Sum(p => Parts.Get(p.DefId).DryMass) * Pads.MetalPerTonne * 0.9;
        Assert.Equal(metalAfterBuild + metalBack, b.Resource(Resource.Metal), 6);
        Assert.Equal(fuelAfterBuild + 1.5, b.Resource(Resource.Methalox), 6);   // the unburnt tank went back into the store
        // Far from the pad: no refund off-world.
        var far = w.Launch(d, "Far", vell.Id, 1.0 + 20_000.0 / vell.Radius);
        Assert.Equal(0, w.RefundRate(far));
    }

    [Fact]
    public void PadUpkeepIsChargedDaily()
    {
        var w = Career(); var vell = w.Sys[2];
        LandBase(w, vell.Id, 1.0);
        Assert.Equal((300_000 + 800_000) * 1.0, w.DailyUpkeep(), 0);   // outpost $3k + pad $8k (Standard ×1)
    }
}
