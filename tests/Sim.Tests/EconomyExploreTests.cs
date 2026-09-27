using Perigee.Sim;

namespace Perigee.Tests;

public class EconomyExploreTests
{
    [Fact(Skip = "exploratory: run with --filter EconomyExplore to print the price-scaling sweep")]
    public void Sweep() { }

    [Fact]
    public void PrintSweep()
    {
        if (Environment.GetEnvironmentVariable("PERIGEE_SWEEP") != "1") return;
        var sys = TestSystems.Basic();
        var tanks = Parts.All.Where(p => p.Has(PartFlags.Tank) && p.FuelCapacity > 0).ToList();
        var vacEngines = new[] { "eng-sparrow", "eng-heron", "eng-lander" }.Select(Parts.Get).ToList();
        var baseTank = tanks.ToDictionary(p => p.Id, p => p.Cost);
        var baseEng = vacEngines.ToDictionary(p => p.Id, p => p.Cost);
        var allEngines = Parts.All.Where(p => p.Engine != null).ToList();
        var baseIsp = allEngines.ToDictionary(p => p.Id, p => (p.Engine!.IspVac, p.Engine.IspSl));
        foreach (double ik in new[] { 1.0, 0.85, 0.7 })
        foreach (double tk in new[] { 1.0, 2.0 })
        foreach (double ek in new[] { 1.0, 2.0 })
        {
            foreach (var e in allEngines) { e.Engine!.IspVac = baseIsp[e.Id].IspVac * ik; e.Engine.IspSl = baseIsp[e.Id].IspSl * ik; }
            foreach (var t in tanks) t.Cost = (long)(baseTank[t.Id] * tk);
            foreach (var e in vacEngines) e.Cost = (long)(baseEng[e.Id] * ek);
            var m = EconomySim.Compare(sys, sys[2], 0.6);
            var n = EconomySim.Compare(sys, sys[3], 0.6);
            Console.WriteLine($"isp×{ik} tank×{tk} vacEng×{ek}: moon direct {Units.FormatMoney(m.Direct)} depot {Units.FormatMoney(m.Depot)} ratio {m.DepotRatio:0.00} mined {m.MinedRatio:0.00} | neighbour direct {Units.FormatMoney(n.Direct)} depot {Units.FormatMoney(n.Depot)} ratio {n.DepotRatio:0.00} mined {n.MinedRatio:0.00} | fuel/t {Units.FormatMoney(m.FuelPriceInOrbit)}");
            Console.WriteLine($"     moon direct: {m.DirectDesc}\n     moon depot: {m.DepotDesc}\n     nb direct: {n.DirectDesc}\n     nb depot: {n.DepotDesc}");
        }
        foreach (var t in tanks) t.Cost = baseTank[t.Id];
        foreach (var e in vacEngines) e.Cost = baseEng[e.Id];
        foreach (var e in allEngines) { e.Engine!.IspVac = baseIsp[e.Id].IspVac; e.Engine.IspSl = baseIsp[e.Id].IspSl; }
    }
}
