using Perigee.Sim;

namespace Perigee.Tests;

public class ContractTests
{
    static World Career()
    {
        var w = SimHost.NewTestWorld();
        w.StartCareer(Preset.Standard, "Test");
        return w;
    }

    [Fact]
    public void CareerStartOffersT1AndFillsTheBoard()
    {
        var w = Career();
        var offers = w.Offers.ToList();
        Assert.InRange(offers.Count, Contracts.MinOffers, Contracts.MaxOffers);
        var t1 = Assert.Single(offers.Where(c => c.Tutorial == 1));
        Assert.Equal(4_000_000, t1.Reward);   // $40k (GDD §11), Standard pays ×1
        Assert.Equal(t1.Reward / 5, t1.Advance);
        Assert.Equal(t1.Reward / 10, t1.Penalty);
        Assert.DoesNotContain(offers, c => c.Tutorial > 1);
        // Procedural offers only target reached bodies (home) and the next one out (Vell).
        var reach = Contracts.ReachableTargets(w);
        Assert.Contains(w.Sys.HomeId, reach);
        Assert.Contains(2, reach);
        Assert.Equal(2, reach.Count);
        foreach (var o in offers.Where(c => c.Tutorial == 0)) Assert.Contains(o.TargetBody, reach);
    }

    [Fact]
    public void BoardRefreshesDailyAndExpiresStaleOffers()
    {
        var w = Career();
        var first = w.Offers.Where(c => c.Tutorial == 0).Select(c => c.Id).ToList();
        for (int d = 0; d < 6; d++) w.Advance(Units.Day);
        Assert.All(first, id => Assert.Equal(ContractState.Expired, w.Contracts.First(c => c.Id == id).State));
        Assert.InRange(w.Offers.Count(), Contracts.MinOffers, Contracts.MaxOffers);
        Assert.Contains(w.Offers, c => c.Tutorial == 1);   // tutorial offers never expire
    }

    [Fact]
    public void AcceptCapsAtFiveAndPaysTheAdvance()
    {
        var w = Career();
        for (int i = 0; i < 8; i++) w.Advance(Units.Day);   // more offers to pick from
        long cash = w.Cash;
        var t1 = w.Offers.First(c => c.Tutorial == 1);
        Assert.True(w.Accept(t1, advance: true));
        Assert.Equal(cash + t1.Advance, w.Cash);
        int tried = 0, refused = 0;
        for (int day = 0; day < 20; day++)
        {
            foreach (var o in w.Offers.ToList()) { tried++; if (!w.Accept(o, false)) refused++; }
            w.Advance(Units.Day);
        }
        Assert.Equal(Contracts.MaxAccepted, w.AcceptedContracts.Count());
        Assert.True(tried > Contracts.MaxAccepted && refused > 0, $"tried {tried}, refused {refused}");
    }

    [Fact]
    public void DeadlineFailsTheContractWithPenaltyAndRepaysTheAdvance()
    {
        var w = Career();
        var t1 = w.Offers.First(c => c.Tutorial == 1);
        long cash0 = w.Cash;
        w.Accept(t1, advance: true);
        w.Advance(Units.Day);
        Assert.Equal(ContractState.Accepted, t1.State);
        for (int d = 0; d < t1.DurationDays + 1; d++) w.Advance(Units.Day);
        Assert.Equal(ContractState.Failed, t1.State);
        // advance came in, then went out again with the 10% penalty; upkeep is zero with no infrastructure
        Assert.Equal(cash0 - t1.Penalty, w.Cash);
    }

    [Fact]
    public void T1CompletesAfterA20kmHopAndALanding()
    {
        var w = Career();
        var t1 = w.Offers.First(c => c.Tutorial == 1);
        w.Accept(t1, false);
        var home = w.Sys.Home;
        var c = w.Launch(TestDesigns.Sounding(), "S", home.Id, home.LaunchSiteAngle);
        // Teleport the same craft above 20 km (the contract only reads world state), then set it back down landed.
        c.Mode = CraftMode.Active; c.Landed = null;
        c.Pos = Vec2d.FromPolar(home.Radius + 21_000, c.Pos.Angle); c.Vel = Flight.SurfaceVelocity(home, c.Pos);
        w.CheckContracts();
        Assert.True(t1.Flag1);
        Assert.Equal(ContractState.Accepted, t1.State);
        w.Launch(TestDesigns.Sounding(), "S2", home.Id, home.LaunchSiteAngle);   // a different craft landing does not count
        w.CheckContracts();
        Assert.Equal(ContractState.Accepted, t1.State);
        c.Pos = Vec2d.FromPolar(home.SurfaceRadiusLocal(home.LaunchSiteAngle) + 2, c.Pos.Angle);
        c.Mode = CraftMode.Landed; c.Landed = new LandedState { LocalAngle = home.LaunchSiteAngle, Radius = c.Pos.Length, AngleOffset = -Math.PI / 2 };
        long cash = w.Cash;
        w.CheckContracts();
        Assert.Equal(ContractState.Completed, t1.State);
        Assert.Equal(cash + t1.Reward, w.Cash);
        Assert.Equal(2, w.TutorialStep);
        Assert.Contains(w.Offers, k => k.Tutorial == 2);   // the next lesson is offered at once
    }

    [Fact]
    public void RefuelCountsOnlyWhatWasPumpedAfterAcceptance()
    {
        var w = Career();
        for (int i = 0; i < 12; i++) w.Advance(Units.Day);
        Contract? k = null;
        int guard = 0;
        while (k == null && guard++ < 400) { k = w.Offers.FirstOrDefault(c => c.Type == ContractType.Refuel); if (k == null) w.Advance(Units.Day); }
        Assert.NotNull(k);
        w.Accept(k!, false);
        var client = w.Find(k!.ClientCraft)!;
        Assert.True(client.Npc);
        Assert.Equal(CraftMode.Rails, client.Mode);
        Assert.Equal(k.TargetBody, client.BodyId);
        var tank = client.Parts.First(p => p.Def.Has(PartFlags.Tank));
        Assert.Equal(tank.Capacity(Resource.Methalox) - k.Tonnes, tank.Get(Resource.Methalox), 6);
        w.CheckContracts();
        Assert.Equal(ContractState.Accepted, k.State);   // the fuel that was already there does not count
        tank.Set(Resource.Methalox, tank.Capacity(Resource.Methalox));
        w.CheckContracts();
        Assert.Equal(ContractState.Completed, k.State);
    }

    [Fact]
    public void DeployCompletesWhenTheTaggedPayloadOrbitsInsideTheWindow()
    {
        var w = Career();
        Contract? k = null; int guard = 0;
        while (k == null && guard++ < 400) { k = w.Offers.FirstOrDefault(c => c.Type == ContractType.Deploy && c.TargetBody == w.Sys.HomeId); if (k == null) w.Advance(Units.Day); }
        Assert.NotNull(k);
        w.Accept(k!, false);
        Assert.Contains(w.AvailablePayloads(), p => p.part.Id == k!.PayloadPart);
        var d = new Design { Name = "D" };
        int core = d.Root("core-s"); d.Stack(core, k!.PayloadPart, below: false);
        var home = w.Sys.Home;
        var c = w.Launch(d, "D", home.Id, home.LaunchSiteAngle);
        Assert.Contains(c.Parts, p => p.ContractId == k.Id);
        Assert.Empty(w.AvailablePayloads());
        // Wrong orbit first, then inside the window.
        double rLow = home.Radius + k.OrbitMin - 30_000;
        c.Landed = null; c.Pos = new Vec2d(rLow, 0); c.Vel = new Vec2d(0, -Math.Sqrt(home.Gm / rLow)); c.Mode = CraftMode.Active; c.PutOnRails(w.Sys, w.T);
        w.CheckContracts();
        Assert.Equal(ContractState.Accepted, k.State);
        double rIn = home.Radius + 0.5 * (k.OrbitMin + k.OrbitMax);
        c.Pos = new Vec2d(rIn, 0); c.Vel = new Vec2d(0, -Math.Sqrt(home.Gm / rIn)); c.Mode = CraftMode.Active; c.PutOnRails(w.Sys, w.T);
        w.CheckContracts();
        Assert.Equal(ContractState.Completed, k.State);
    }

    [Fact]
    public void SurveyContractsReadTheExplorationLedger()
    {
        var w = Career();
        var k = new Contract { Id = 900, Type = ContractType.Survey, Survey = SurveyKind.Orbit, TargetBody = 2, State = ContractState.Offered, Title = "Orbit Vell", Reward = 100, DurationDays = 30 };
        w.Contracts.Add(k);
        w.Accept(k, false);
        w.CheckContracts();
        Assert.Equal(ContractState.Accepted, k.State);
        w.RecordExploration(2, Explored.Flyby);
        w.CheckContracts();
        Assert.Equal(ContractState.Accepted, k.State);
        w.RecordExploration(2, Explored.Orbit);
        w.CheckContracts();
        Assert.Equal(ContractState.Completed, k.State);
    }

    [Fact]
    public void DebrisCleanupCountsTouchedDebrisLostInTheAtmosphere()
    {
        var w = Career();
        var home = w.Sys.Home;
        var k = new Contract { Id = 901, Type = ContractType.DebrisCleanup, Count = 1, TargetBody = home.Id, State = ContractState.Offered, Title = "Clean", Reward = 100, DurationDays = 30 };
        w.Contracts.Add(k);
        w.Accept(k, false);
        // Debris on a decaying orbit (periapsis inside the atmosphere), unloaded: the rails event removes it.
        var d = new Design { Name = "junk" }; d.Root("tank-s-short");
        var junk = Craft.FromDesign(d, "junk");
        junk.BodyId = home.Id; junk.Debris = true; junk.TouchedByPlayer = true;
        double ra = home.Radius + 200_000, rp = home.Radius + 20_000, a = 0.5 * (ra + rp);
        junk.Pos = new Vec2d(ra, 0); junk.Vel = new Vec2d(0, -Math.Sqrt(home.Gm * (2 / ra - 1 / a))); junk.Mode = CraftMode.Active;
        w.AddCraft(junk); junk.PutOnRails(w.Sys, w.T);
        // Something else must be active and far away so the junk stays unloaded.
        var sat = SimHost.SpawnInto(w, "station", "Sat", 300_000, 180, 1);
        w.ActiveCraftId = sat.Id;
        for (int i = 0; i < 200 && k.State == ContractState.Accepted; i++) w.Advance(60);
        Assert.True(junk.Destroyed);
        Assert.Equal(ContractState.Completed, k.State);
    }

    [Fact]
    public void RelocateNeedsTheClientAloneOnRailsInsideTheWindow()
    {
        var w = Career();
        Contract? k = null; int guard = 0;
        while (k == null && guard++ < 400) { k = w.Offers.FirstOrDefault(c => c.Type == ContractType.Relocate); if (k == null) w.Advance(Units.Day); }
        Assert.NotNull(k);
        w.Accept(k!, false);
        var client = w.Find(k!.ClientCraft)!;
        var body = w.Sys[client.BodyId];
        w.CheckContracts();
        Assert.Equal(ContractState.Accepted, k.State);
        double rIn = body.Radius + 0.5 * (k.OrbitMin + k.OrbitMax);
        client.Pos = new Vec2d(rIn, 0); client.Vel = new Vec2d(0, -Math.Sqrt(body.Gm / rIn)); client.Mode = CraftMode.Active; client.PutOnRails(w.Sys, w.T);
        w.CheckContracts();
        Assert.Equal(ContractState.Completed, k.State);
    }

    [Fact]
    public void EveryTutorialContractHasABodyAndAHint()
    {
        var w = Career();
        for (int n = 1; n <= 10; n++)
        {
            var t = Contracts.Tutorial(n, w);
            Assert.True(t.TargetBody >= 0, $"T{n} has no target body");
            Assert.False(string.IsNullOrWhiteSpace(t.Hint), $"T{n} has no hint");
            Assert.True(t.Reward > 0);
        }
        Assert.Equal(2, Contracts.NearestLandable(w));   // Vell
        Assert.Equal(2, Contracts.NearestIce(w));
    }

    [Fact]
    public void GeneratedOffersAreWellFormedAcrossManySeeds()
    {
        for (ulong seed = 1; seed <= 20; seed++)
        {
            var w = new World(SystemGenerator.Generate(seed), seed);
            w.StartCareer(Preset.Standard, "T");
            for (int i = 0; i < 30; i++)
            {
                var c = Contracts.Generate(w, w.Rng);
                Assert.True(c.Reward > 0, $"seed {seed}: {c.Title} pays nothing");
                Assert.True(c.TargetBody >= 0);
                Assert.False(string.IsNullOrWhiteSpace(c.Title));
                Assert.False(string.IsNullOrWhiteSpace(c.Objective));
                if (c.Type is ContractType.Deploy or ContractType.Relocate) Assert.True(c.OrbitMax > c.OrbitMin && c.OrbitMin > w.Sys[c.TargetBody].ActiveZoneHeight, $"seed {seed}: bad window {c.OrbitMin}-{c.OrbitMax}");
                if (c.Type is ContractType.FuelDelivery or ContractType.Refuel or ContractType.Supply) Assert.True(c.Tonnes > 0);
                if (c.Type == ContractType.Survey && c.Survey == SurveyKind.Land) Assert.True(SystemGenerator.IsLandable(w.Sys[c.TargetBody]));
            }
        }
    }
}
