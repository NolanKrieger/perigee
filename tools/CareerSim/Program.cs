using System.Text.RegularExpressions;
using Perigee.Sim;

// Career model (GDD §3 arc, §23 design risk). One Standard career is played as a sequence of scripted missions with REAL economics:
// launches and orbital set-ups are paid at launch cost, R&D is bought part by part when a mission first needs it, contracts pay,
// sales hit the live market, upkeep is charged. When the next step is unaffordable the model does what a player does: repeat
// supply trips (tanker → depot → sale) until it is. Play time is accounted per scripted tick by the warp a player would use:
// hands-on flying (physics with throttle or input) at 1×, idle physics at 4×, rails coasts at the top rails warp, never slower
// than the script itself ran; plus 120 s per launch (builder) and 60 s per mission (menus). A scripted pilot never crashes and
// never flies a bad approach, so real play takes longer: read the hours as a lower bound and the trip counts as the signal.
// Usage: dotnet run --project tools/CareerSim -- [--hours=50] [--seed=7] [--out=docs/career-sim.md]
double targetHours = 50; ulong seed = 7; string outPath = "docs/career-sim.md"; string presetName = "Standard";
foreach (var a in args) { if (a.StartsWith("--hours=")) targetHours = double.Parse(a[8..]); else if (a.StartsWith("--seed=")) seed = ulong.Parse(a[7..]); else if (a.StartsWith("--out=")) outPath = a[6..]; else if (a.StartsWith("--preset=")) presetName = a[9..]; }
var preset = Preset.All.First(p => p.Name.Equals(presetName, StringComparison.OrdinalIgnoreCase));
const double LaunchOverheadS = 120, MissionOverheadS = 60, TankerRefund = 0.90; const int MaxGrind = 120, MaxWaitDays = 150, MaxPriceWaitDays = 20;
const long MinTripProfit = 1_500_000;   // cents: a trip is worth flying when the 10 t sale beats its net cost by $15k
const long SpawnBooster = 2_200_000;   // cents: what a recovered S/M booster costs per orbital set-up the scripts spawn instead of flying up (10% of parts + propellant)
string scriptsDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "scripts");
if (!Directory.Exists(scriptsDir)) scriptsDir = Path.Combine(Directory.GetCurrentDirectory(), "scripts");

var w = SimHost.NewTestWorld(seed); w.SystemKind = "test";
w.StartCareer(preset, "Career Model");
var host = new SimHost(w) { ChargeLaunches = true };
double playS = 0, handsOnS = 0; int supplyTrips = 0, missions = 0, launches = 0, relays = 0, daysWaited = 0, priceWaitDays = 0, stormWaitDays = 0; long rdSpent = 0, launchSpent = 0, refunds = 0, sales = 0;
host.BeforeTick = x =>
{
    double game = Units.PhysicsDt * x.EffectiveWarp;
    bool physics = x.ActiveIsPhysics;
    bool hands = physics && x.Active != null && (x.Active.Throttle > 0 || x.Input.Any);
    double playerWarp = Math.Max(x.EffectiveWarp, physics ? (hands ? 1 : World.MaxPhysicsWarp) : World.WarpLevels[^1]);
    playS += game / playerWarp; if (hands) handsOnS += game;
};
var rows = new List<string>(); string phase = "1 Sounding rockets"; var phaseEnd = new Dictionary<string, double>();
void Row(string what, string note) { rows.Add($"| {phase} | {what} | {(int)(w.T / Units.Day) + 1} | {playS / 3600:0.0} | {handsOnS / 3600:0.0} | {Units.FormatMoney(w.Cash)} | {Units.FormatMoney(w.Score())} | {supplyTrips} | {note} |"); Console.WriteLine(rows[^1]); }
void Phase(string p) { phaseEnd[phase] = playS / 3600; phase = p; }
string Text(string file) => File.ReadAllText(Path.Combine(scriptsDir, file));
// What a segment will spend before it flies: every launch/spawn at launch cost plus the R&D its designs still need.
(long cost, int launches, int spawns, List<string> rd) Needs(string text)
{
    long cost = 0; int n = 0, sp = 0; var rd = new List<string>();
    foreach (var line in text.Split('\n'))
    {
        var wds = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (wds.Length < 2 || (wds[0] != "launch" && wds[0] != "spawn")) continue;
        var d = SimHost.DesignByName(wds[1]); if (!d.Parts.Any(p => Parts.Get(p.DefId).Has(PartFlags.ProbeCore))) continue;   // debris set-ups are not the player's launches
        cost += w.LaunchCost(d); if (wds[0] == "spawn") { sp++; cost += SpawnBooster; } else n++;
        foreach (var id in w.LockedParts(d)) if (!rd.Contains(id)) { rd.Add(id); cost += w.RdPrice(Parts.Get(id)); }
        // An M-class stage spawned in orbit would have needed an M booster: its engine, legs and grid fins are R&D the player pays once.
        if (wds[0] == "spawn" && d.Parts.Any(p => Parts.Get(p.DefId).Dia == Diameter.M))
            foreach (var id in new[] { "eng-condor", "tank-m-long", "adapter-sm", "legs-m", "grid-fin" }) if (!w.IsUnlocked(id) && !rd.Contains(id)) { rd.Add(id); cost += w.RdPrice(Parts.Get(id)); }
    }
    return (cost, n, sp, rd);
}
// A generated "Deploy a relay at <home>" offer the starter relay stack can fly (window floor ≤ 220 km).
Contract? HomeRelayOffer() => w.Offers.FirstOrDefault(o => o.Type == ContractType.Deploy && o.TargetBody == w.Sys.HomeId && o.PayloadPart == "payload-s" && o.OrbitMin <= 220_000);
bool DeployHome(Contract k)
{
    double peri = k.OrbitMin + 5_000, apo = Math.Min(k.OrbitMax - 15_000, k.OrbitMin + 45_000);
    var text = Text(Path.Combine("templates", "deploy-home.flight")).Replace("{ID}", k.Id.ToString()).Replace("{APO_LO}", ((long)(apo * 0.6)).ToString()).Replace("{APO}", ((long)apo).ToString()).Replace("{PERI}", ((long)peri).ToString()).Replace("{DONE}", (w.Contracts.Count(c => c.State == ContractState.Completed) + 1).ToString());
    if (!Run($"relay {k.OrbitMin / 1000:0}–{k.OrbitMax / 1000:0} km", text, grind: false)) return false;
    relays++;
    return true;
}
/// <summary>Earn until cash ≥ need: supply trips once a depot exists, otherwise home relay contracts as the board offers them.</summary>
bool Grind(long need, string forWhat)
{
    int trips = 0, flights = 0, waited = 0, idle = 0; long startCash = w.Cash;
    while (w.Cash < need && !w.GameOver)
    {
        bool depot = w.Crafts.Any(c => c.Name == "Depot" && !c.Destroyed);
        if (depot)
        {
            if (++trips > MaxGrind) { long perTrip = (w.Cash - startCash) / Math.Max(1, trips - 1); Row("grind", $"STOPPED: {MaxGrind} supply trips did not fund {forWhat} ({Units.FormatMoney(w.Cash)} of {Units.FormatMoney(need)}; {Units.FormatMoney(perTrip)} per trip → {(perTrip > 0 ? ((need - w.Cash) / perTrip).ToString() : "∞")} more trips)"); return false; }
            long before = w.Cash;
            if (!Supply()) return false;
            if (w.Cash <= before) { Row("grind", $"DEAD END: a supply trip lost money ({Units.FormatMoney(before)} → {Units.FormatMoney(w.Cash)}); grinding cannot fund {forWhat}"); return false; }
        }
        else
        {
            var k = HomeRelayOffer();
            if (k == null) { if (++idle > MaxWaitDays) { Row("grind", $"STOPPED: no flyable home relay offer for {MaxWaitDays} days in a row while {Units.FormatMoney(need)} is needed for {forWhat} ({Units.FormatMoney(w.Cash)} in hand)"); return false; } waited++; daysWaited++; Day(); continue; }
            idle = 0;
            long before = w.Cash;
            if (!DeployHome(k)) return false;
            flights++;
            if (w.Cash <= before) { Row("grind", $"DEAD END: a relay contract lost money ({Units.FormatMoney(before)} → {Units.FormatMoney(w.Cash)})"); return false; }
        }
        Day();
    }
    if (trips + flights > 0) Row("grind", $"{(trips > 0 ? $"{trips} supply trip{(trips == 1 ? "" : "s")}" : "")}{(trips > 0 && flights > 0 ? " + " : "")}{(flights > 0 ? $"{flights} home relay contract{(flights == 1 ? "" : "s")} ({waited} days waiting for offers)" : "")} to afford {forWhat} ({Units.FormatMoney(need)})");
    return true;
}
/// <summary>Missions start from home orbit: make a home-orbit craft (the depot) active so `spawn … orbit N` places set-ups around home, not around whatever the last landing left active.</summary>
void ActivateHome()
{
    var home = w.Crafts.FirstOrDefault(c => c.Name == "Depot" && !c.Destroyed) ?? w.Crafts.FirstOrDefault(c => !c.Destroyed && !c.IsDebris && c.BodyId == w.Sys.HomeId && c.Mode == CraftMode.Rails);
    if (home != null) w.ActiveCraftId = home.Id;
}
bool Run(string label, string text, bool grind = true, bool fromHome = true)
{
    var (cost, n, sp, rd) = Needs(text);
    // Keep a month of upkeep and one more supply trip in hand: spending to the last dollar walks straight into the insolvency grace,
    // and a depot owner with no tanker money is stuck by the M12 rule.
    var reserveTanker = SimHost.DesignByName("tankerm");
    cost += 30 * w.DailyUpkeep() + w.LaunchCost(reserveTanker) + SpawnBooster;
    if (grind && !Grind(cost, label)) return false;
    if (fromHome) ActivateHome();
    foreach (var id in rd) { long price = w.RdPrice(Parts.Get(id)); if (!w.BuyRd(id)) { Row(label, $"DEAD END: R&D {id} refused ({Units.FormatMoney(price)} with {Units.FormatMoney(w.Cash)})"); return false; } rdSpent += price; }
    if (sp > 0 && !w.TrySpend(sp * SpawnBooster, "boosters for orbital set-ups")) { Row(label, $"DEAD END: cannot afford the boosters for {sp} orbital set-ups"); return false; }
    // The regression scripts count contracts on a fresh world; here relay contracts complete in between, so counts become lower bounds.
    text = Regex.Replace(text, @"contracts_(done|active) == (\d+)", "contracts_$1 >= $2");
    // Standalone missions reuse craft names the tutorial left behind (a Lander on Vell, an Outpost): give this mission's craft their own names.
    if (fromHome) text = Regex.Replace(text, @"\b(Lander|Outpost|ScanProbe|PadBase|Sunlit|Sheltered|Sweeper|Junk\d|Hop)\b", m => $"{m.Value}{missions + 1}");
    var res = FlightScript.Parse(text, label).Run(host);
    playS += (n + sp) * LaunchOverheadS + MissionOverheadS; launches += n + sp; missions++;
    if (!res.Passed) { Row(label, $"FAILED: {string.Join("; ", res.Failures.Take(2))}"); return false; }
    return true;
}
bool Supply()
{
    var text = Text("supply-loop.flight");
    var (cost, _, _, _) = Needs(text);
    // Read the ledger first (GDD §2 "day to day"): a trip only flies when 10 t at the home low-orbit node clears the trip's net cost.
    var td = SimHost.DesignByName("tankerm");
    long net = w.LaunchCost(td) + SpawnBooster - (long)(TankerRefund * td.Parts.Sum(p => w.PartPrice(Parts.Get(p.DefId))));
    var node = w.Market.Nodes.First(n => n.Kind == NodeKind.LowOrbit && n.BodyId == w.Sys.Home.Id);
    int waitedHere = 0;
    while (w.Market.Quote(node, Resource.Methalox, 10, w.T) < net + MinTripProfit && waitedHere < MaxPriceWaitDays && !w.GameOver) { waitedHere++; priceWaitDays++; Day(); }
    if (w.Market.Quote(node, Resource.Methalox, 10, w.T) < net) { Row("supply", $"STOPPED: the home node cannot pay for another trip ({Units.FormatMoney(w.Market.Quote(node, Resource.Methalox, 10, w.T))} for 10 t vs {Units.FormatMoney(net)} net trip cost after {waitedHere} days of recovery)"); return false; }
    // Nobody launches a tanker into a storm or to a fried depot: wait for the all-clear and the reboot (players get both on the HUD).
    Craft? DepotCraft() => w.Crafts.FirstOrDefault(c => c.Name == "Depot" && !c.Destroyed);
    for (int guard = 0; guard < 20 && !w.GameOver && (w.Storm != StormPhase.None || (DepotCraft() is { } dc && (dc.Fried || !dc.HasControl))); guard++) { stormWaitDays++; Day(); }
    launchSpent += cost; long cashIn = w.Cash;
    if (!Run("supply trip", text, grind: false))
    {
        var dc = DepotCraft();
        Row("supply", $"diagnostics: storm={w.Storm} depot={(dc == null ? "none" : $"mode {dc.Mode} fried {dc.Fried} control {dc.HasControl} debris {dc.IsDebris} body {dc.BodyId}")} crafts={w.Crafts.Count(c => !c.Destroyed)} debris={w.Crafts.Count(c => !c.Destroyed && c.IsDebris)}");
        return false;
    }
    supplyTrips++; sales += w.Cash - cashIn + cost;
    // The empty tanker comes home for its refund (GDD §2/§6: 90% at the landing zone). The return is not flown by the script:
    // it is charged as the T5 pad landing was measured (200 s hands-on + 40 s of deorbit at physics warp) and refunded on dry parts only.
    var tanker = w.Crafts.LastOrDefault(c => c.Name == "TankerM" && !c.Destroyed);
    if (tanker != null)
    {
        long refund = (long)(TankerRefund * tanker.Parts.Where(p => !p.Destroyed).Sum(p => w.PartPrice(p.Def)));
        w.Cash += refund; refunds += refund; w.Count("recoveries"); tanker.Destroyed = true;
        playS += 240; handsOnS += 200;
    }
    return true;
}
void Day()
{
    var depot = w.Crafts.FirstOrDefault(c => c.Name == "Depot" && !c.Destroyed); if (depot != null) w.ActiveCraftId = depot.Id;
    double t0 = w.T; w.SetWarp(7);
    while (w.T < t0 + Units.Day && !w.GameOver) w.Advance(Units.PhysicsDt * w.EffectiveWarp);
    playS += 10; w.SetWarp(0);
}
bool Week(string note)
{
    var depot = w.Crafts.FirstOrDefault(c => c.Name == "Depot" && !c.Destroyed); if (depot != null) w.ActiveCraftId = depot.Id;   // watch a rails craft so the week warps
    double t0 = w.T; w.SetWarp(7);
    while (w.T < t0 + 7 * Units.Day && !w.GameOver) w.Advance(Units.PhysicsDt * w.EffectiveWarp);
    playS += 60; w.SetWarp(0);
    Row("week", note);
    return !w.GameOver;
}

Console.WriteLine("| phase | step | day | play h | hands-on h | cash | score | supply trips | note |");
Console.WriteLine("|---|---|---|---|---|---|---|---|---|");
Row("start", $"{preset.Name} preset");
// Phases 1–4 are the tutorial chain, one lesson at a time (split on the lesson headers of the regression script).
var chain = Regex.Split(Text("tutorial-t1-t10.flight"), @"(?m)^(?=# -{10,} T\d+)");
foreach (var seg in chain)
{
    if (seg.Trim().Length == 0) continue;
    var m = Regex.Match(seg, @"^# -+ (T\d+[^\n]*)"); string label = m.Success ? m.Groups[1].Value.Split(':')[0].Trim() : "chain";
    if (label.StartsWith("T4")) Phase("2 Reuse"); else if (label.StartsWith("T6")) Phase("3 First depot"); else if (label.StartsWith("T10")) Phase("4 Mining");
    if (!Run(label, seg, fromHome: false)) goto done;   // lessons hand the active craft to each other
    Row(label, m.Success ? m.Groups[1].Value.Split(':')[0].Trim() + ":" + m.Groups[1].Value.Split(':').Skip(1).FirstOrDefault()?.Trim() : "");
    if (label.StartsWith("T8")) { for (int k = 0; k < 3 && Supply(); k++) { } Row("supply ×3", "first paid runs after the depot opens"); }
}
if (!Run("mining outpost", Text("outpost-setup.flight").Replace("tutorial 10\n", "").Replace("accept T10\n", "").Replace("outposts == 1", "outposts >= 1"))) goto done; Row("outpost", "scan orbit, full outpost (refinery + storage) landed in the ice arc");
if (!Week("outpost producing")) goto done;
Phase("5 Off-world pads");
if (!Run("off-world pad", Text("offworld-pad.flight"))) goto done; Row("pad", "pad kit levelled, hopper built and launched off-world");
if (!Week("pad running")) goto done;
Phase("6 The network");
int cycle = 0;
while (playS / 3600 < targetHours && !w.GameOver)
{
    cycle++;
    for (int k = 0; k < 3 && Supply(); k++) { }
    if (cycle % 2 == 1) { if (!Run("storm shelter", Text("storm-shelter.flight"))) goto done; }
    else if (!Run("debris cleanup", Text("debris-cleanup.flight"))) goto done;
    if (!Week($"cycle {cycle}: 3 supply trips + {(cycle % 2 == 1 ? "storm" : "debris")} contract")) goto done;
}
done:
phaseEnd[phase] = playS / 3600;
double hours = playS / 3600;
var arc = new Dictionary<string, (double lo, double hi)> { ["1 Sounding rockets"] = (0, 3), ["2 Reuse"] = (3, 8), ["3 First depot"] = (8, 14), ["4 Mining"] = (14, 24), ["5 Off-world pads"] = (24, 32), ["6 The network"] = (32, 50) };
var summary = new List<string>
{
    "# Career model (M12 gate)", "",
    $"{preset.Name} preset, seed {seed}, real economics through the flight harness (`tools/CareerSim`, `SimHost.ChargeLaunches`). Play time per scripted tick = game seconds ÷ the warp a player would use (hands-on flying 1×, idle physics {World.MaxPhysicsWarp}×, rails {World.WarpLevels[^1]:0}×, never slower than the script ran) + {LaunchOverheadS:0} s per launch + {MissionOverheadS:0} s per mission. Orbital set-ups the scripts spawn are paid at launch cost plus {Units.FormatMoney(SpawnBooster)} for the recovered booster they skip. Hands-on hours = time flying with throttle or input. A scripted pilot never crashes or re-flies an approach, so a real career takes longer than these hours.", "",
    "| phase | step | day | play h | hands-on h | cash | score | supply trips | note |", "|---|---|---|---|---|---|---|---|---|",
};
summary.AddRange(rows);
summary.Add(""); summary.Add("## Phase ends vs the §3 arc"); summary.Add(""); summary.Add("| phase | model ends at (play h) | §3 arc (h) |"); summary.Add("|---|---|---|");
foreach (var (k, v) in arc) summary.Add($"| {k} | {(phaseEnd.TryGetValue(k, out var h) ? h.ToString("0.0") : "not reached")} | {v.lo}–{v.hi} |");
summary.Add(""); summary.Add($"**Totals:** {hours:0.0} play-hours modelled ({handsOnS / 3600:0.0} hands-on), {missions} missions, {launches} launches ({relays} home relay contracts, {daysWaited} days waited for offers), {supplyTrips} repeat supply trips ({priceWaitDays} days waited for the home node's price to recover, {stormWaitDays} for storms or a fried depot) → **{(hours > 0 ? supplyTrips / hours : 0):0.00} repeat supply trips per play-hour** (GDD §23 metric); tanker launches cost {Units.FormatMoney(launchSpent)}, depot sales earned {Units.FormatMoney(sales)}, tanker refunds returned {Units.FormatMoney(refunds)} (net {Units.FormatMoney(sales + refunds - launchSpent)}, {(supplyTrips > 0 ? Units.FormatMoney((sales + refunds - launchSpent) / supplyTrips) : "—")} per trip). R&D spent {Units.FormatMoney(rdSpent)}. Final cash {Units.FormatMoney(w.Cash)}, peak score {Units.FormatMoney(w.PeakScore)}, career day {(int)(w.T / Units.Day) + 1}{(w.GameOver ? $", CAREER OVER: {w.GameOverCause}" : "")}.");
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
File.WriteAllLines(outPath, summary);
Console.WriteLine(summary[^1]);
return w.GameOver ? 1 : 0;
