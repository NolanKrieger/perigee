using Perigee.Sim;
var w = SimHost.NewTestWorld(7); w.SystemKind = "test"; w.StartCareer(Preset.Standard, "Probe");
foreach (var name in new[] { "sounding", "orbital", "comsat", "recoverable", "tanker", "tanker2", "depot", "tankerm", "lander", "outpost", "scanprobe", "padbase", "hopper2", "clawtanker", "junk", "station" })
{
    var d = SimHost.DesignByName(name);
    long rd = w.LockedParts(d).Sum(id => w.RdPrice(Parts.Get(id)));
    Console.WriteLine($"{name,-12} launch {Units.FormatMoney(w.LaunchCost(d)),10}  R&D still needed {Units.FormatMoney(rd),10}  dry parts {Units.FormatMoney(d.Parts.Sum(p => w.PartPrice(Parts.Get(p.DefId)))),10}  locked: {string.Join(",", w.LockedParts(d))}");
}
var lo = w.Market.Nodes.First(n => n.Kind == NodeKind.LowOrbit && n.BodyId == w.Sys.Home.Id);
Console.WriteLine($"low-orbit node {lo.Name} multiplier {lo.Multiplier}  price/t now {Units.FormatMoney(w.Market.Price(lo, Resource.Methalox, w.T))}  10 t sale {Units.FormatMoney(w.Market.Quote(lo, Resource.Methalox, 10, w.T))}  home surface price/t {Units.FormatMoney(Market.BasePrice(Resource.Methalox))}");
foreach (var n in w.Market.Nodes) Console.WriteLine($"  node {n.Name,-22} {n.Kind,-12} mult {n.Multiplier:0.0} methalox/t {Units.FormatMoney(w.Market.Price(n, Resource.Methalox, w.T))}");
Console.WriteLine($"upkeep/day: {Units.FormatMoney(w.DailyUpkeep())}  contracts on board: {string.Join(" | ", w.Offers.Select(o => $"{o.Title} {Units.FormatMoney(o.Reward)}"))}");
