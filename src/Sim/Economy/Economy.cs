namespace Perigee.Sim;

[Flags]
public enum Explored { None = 0, Flyby = 1, Orbit = 2, Landed = 4 }

/// <summary>Money (GDD §10), presets (§15), bankruptcy with the 10-day grace (§10, decision #44) and score (§3, decision #51).</summary>
public sealed partial class World
{
    public Preset Preset = Preset.Standard;
    public Market Market = new();
    public HashSet<string> Unlocked = new();
    public string AgencyName = "Perigee Fuel Co.";
    /// <summary>Infrastructure bought at home: extra pads (each $4k/day) and drone ships ($2k/day each, see DroneShips).</summary>
    public int ExtraHomePads;
    public int LastUpkeepDay;
    public long LastUpkeepCharged;
    public double InsolventSince = double.NaN;
    public bool GameOver;
    public string GameOverCause = "";
    public long PeakScore;
    public Dictionary<int, Explored> Exploration = new();
    public List<(int day, long cash)> CashHistory = new();
    public long TotalEarned, TotalSpent;

    public void StartCareer(Preset preset, string agency)
    {
        Preset = preset; AgencyName = agency;
        Cash = preset.StartingCash;
        Market.DepthScale = preset.MarketDepth;
        Market.Build(Sys);
        Unlocked.Clear();
        foreach (var p in Sim.Parts.All) if (p.Starter) Unlocked.Add(p.Id);
        LastUpkeepDay = (int)Math.Floor(T / Units.Day);
        Market.Snapshot(LastUpkeepDay);
        CashHistory.Add((LastUpkeepDay, Cash));
        RefreshBoard();
    }

    // ---------------------------------------------------------------- prices

    public long PartPrice(PartDef p) => (long)(p.Cost * Preset.PartPrices);
    public long RdPrice(PartDef p) => (long)(p.RdPrice * Preset.PartPrices);
    public bool IsUnlocked(string partId) => Unlocked.Contains(partId) || (Sim.Parts.TryGet(partId, out var cp) && cp.Has(PartFlags.ClientPayload));

    /// <summary>R&D unlock (decision #13): price is the only gate.</summary>
    public bool BuyRd(string partId)
    {
        if (Unlocked.Contains(partId) || !Sim.Parts.TryGet(partId, out var def)) return false;
        long price = RdPrice(def);
        if (!TrySpend(price, $"R&D {def.Name}")) return false;
        Unlocked.Add(partId);
        return true;
    }

    /// <summary>What a launch of this design costs at home: parts at unit price plus the propellant in its tanks at home prices.</summary>
    public long LaunchCost(Design d)
    {
        long parts = 0; long fuel = 0;
        foreach (var dp in d.Parts)
        {
            var def = Sim.Parts.Get(dp.DefId);
            parts += PartPrice(def);
            if (def.StorageT > 0) continue;   // surface storage launches empty (Craft.FromDesign fills propellant tanks only)
            foreach (var (r, cap) in def.Capacity) if (r is Resource.Methalox or Resource.Rcs or Resource.Xenon) fuel += (long)(cap * Market.BasePrice(r));
        }
        return parts + fuel;
    }

    public IEnumerable<string> LockedParts(Design d) => d.Parts.Select(p => p.DefId).Distinct().Where(id => !IsUnlocked(id));

    public bool TrySpend(long cents, string what)
    {
        if (cents < 0) return false;
        if (Cash - cents < 0) { Emit(SimEventKind.Info, Active ?? new Craft { Id = -1 }, $"Not enough cash for {what} ({Units.FormatMoney(cents)})"); return false; }
        Cash -= cents; TotalSpent += cents; SaveRequested = true;
        return true;
    }

    public void Earn(long cents, string what)
    {
        Cash += cents; TotalEarned += cents;
        Emit(SimEventKind.Info, Active ?? new Craft { Id = -1 }, $"+{Units.FormatMoney(cents)}: {what}");
    }

    /// <summary>Pay for and launch a design (GDD §10). Blocked when a part is locked or cash would go below $0.</summary>
    public Craft? TryLaunch(Design d, string name, int bodyId, double padAngle)
    {
        if (bodyId != Sys.HomeId)
        {
            var pad = PadAt(bodyId);
            if (pad == null) { Emit(SimEventKind.Info, Active ?? new Craft { Id = -1 }, $"No operational pad on {Sys[bodyId].Name} (needs an outpost with a Pad Kit and a Fabricator)"); return null; }
            return TryLaunchAtPad(d, name, pad);
        }
        var locked = LockedParts(d).ToList();
        if (locked.Count > 0) { Emit(SimEventKind.Info, Active ?? new Craft { Id = -1 }, $"Locked parts: {string.Join(", ", locked.Select(id => Sim.Parts.Get(id).Name))}"); return null; }
        long cost = LaunchCost(d);
        if (!TrySpend(cost, $"launch of {d.Name}")) return null;
        var c = Launch(d, name, bodyId, padAngle);
        c.LaunchCostPaid = cost;
        return c;
    }

    // ---------------------------------------------------------------- upkeep & bankruptcy

    public long DailyUpkeep()
    {
        long u = 0;
        u += ExtraHomePads * 400_000;
        u += DroneShips.Count * 200_000;
        foreach (var c in Crafts)
        {
            if (c.Destroyed) continue;
            u += c.Parts.Count(p => !p.Destroyed && p.Def.Has(PartFlags.DepotController)) * 100_000;
            u += c.Parts.Count(p => !p.Destroyed && p.Def.Has(PartFlags.OutpostCore)) * 300_000;
            u += c.Parts.Count(p => !p.Destroyed && p.Def.Has(PartFlags.PadKit)) * 800_000;
        }
        return (long)(u * Preset.Upkeep);
    }

    /// <summary>Cash runway in days (∞ when there is no upkeep).</summary>
    public double Runway => DailyUpkeep() <= 0 ? double.PositiveInfinity : Math.Max(0, Cash) / (double)DailyUpkeep();

    /// <summary>An earning craft (GDD §10 grace): a non-infrastructure craft carrying sellable cargo or an accepted contract payload.</summary>
    public bool HasEarningCraft()
    {
        foreach (var c in Crafts)
        {
            if (c.Destroyed || c.Npc || c.Debris) continue;
            bool infra = c.Parts.Any(p => !p.Destroyed && p.Def.Has(PartFlags.DepotController | PartFlags.OutpostCore | PartFlags.PadKit));
            if (infra) continue;
            foreach (var r in new[] { Resource.Methalox, Resource.Rcs, Resource.Xenon, Resource.Water, Resource.Metal })
                if (c.Resource(r) > 0.01 && c.Parts.Any(p => !p.Destroyed && p.Capacity(r) > 0)) return true;
            if (c.ContractPayload) return true;
        }
        return false;
    }

    /// <summary>Called at every day boundary: charge upkeep, snapshot the ledger, run the insolvency clock.</summary>
    void DayTick(int day)
    {
        long upkeep = DailyUpkeep();
        if (upkeep > 0) { Cash -= upkeep; TotalSpent += upkeep; LastUpkeepCharged = upkeep; Emit(SimEventKind.Info, Active ?? new Craft { Id = -1 }, $"Day {day + 1}: upkeep {Units.FormatMoney(upkeep)}"); }
        Market.Snapshot(day);
        CashHistory.Add((day, Cash));
        CashHistory.RemoveAll(h => h.day < day - 400);
        Exploration.TryAdd(-1, Explored.None);
        RefreshBoard();
        HazardsDay();
        CheckContracts();
        CheckInsolvency();
        CheckStuck();
        var score = Score();
        if (score > PeakScore) PeakScore = score;
    }

    /// <summary>Instant game over at $0 (decision #14), except while an earning craft is in flight (decision #44: up to N days).</summary>
    public void CheckInsolvency()
    {
        if (GameOver) return;
        if (Cash > 0) { InsolventSince = double.NaN; return; }
        if (!HasEarningCraft()) { EndCareer(double.IsNaN(InsolventSince) ? "cash ran out" : "insolvent with nothing left to sell"); return; }
        if (double.IsNaN(InsolventSince)) InsolventSince = T;
        if (T - InsolventSince >= Preset.GraceDays * Units.Day) EndCareer($"insolvent for {Preset.GraceDays} days");
    }

    public int InsolventDay => double.IsNaN(InsolventSince) ? 0 : (int)Math.Floor((T - InsolventSince) / Units.Day) + 1;

    // ---------------------------------------------------------------- stuck careers (no soft-locks short of bankruptcy)
    public double StuckSince = double.NaN;
    public int StuckDay => double.IsNaN(StuckSince) ? 0 : (int)Math.Floor((T - StuckSince) / Units.Day) + 1;
    /// <summary>The cheapest launch that can still earn: a starter sounding rocket at today's prices.</summary>
    public long MinLaunchCost() => LaunchCost(TestDesigns.Sounding());
    /// <summary>Nothing can bring money in: no launch affordable even with every open advance, no earning craft, no contract in progress, no outpost or depot stock.</summary>
    public bool IsStuck()
    {
        if (GameOver || Cash <= 0) return false;
        if (HasEarningCraft() || AcceptedContracts.Any()) return false;
        if (Outposts().Count > 0) return false;
        foreach (var c in Crafts) if (!c.Destroyed && !c.Npc && !c.Debris && c.Parts.Any(p => !p.Destroyed && p.Def.Has(PartFlags.DepotController)) && Enum.GetValues<Resource>().Any(r => Market.Sellable(r) && c.Resource(r) > 0.01)) return false;
        long reach = Cash + Offers.Sum(o => o.Advance);
        return reach < MinLaunchCost();
    }
    /// <summary>Daily: a career that cannot fund any launch and has no way to earn ends after the grace period, like insolvency (GDD §10 spirit; recorded in ARCHITECTURE.md).</summary>
    public void CheckStuck()
    {
        if (GameOver) return;
        if (!IsStuck()) { StuckSince = double.NaN; return; }
        if (double.IsNaN(StuckSince)) { StuckSince = T; StopWarpFor($"STUCK: cash cannot fund any launch and nothing can earn — {Preset.GraceDays} days to find a contract advance or a sale"); }
        if (T - StuckSince >= Preset.GraceDays * Units.Day) EndCareer("stuck: could not fund a launch and had nothing to sell");
    }

    /// <summary>The game layer autosaves when this is set (GDD §18 triggers: warp stops, docking, landing, recovery, purchases).</summary>
    public bool SaveRequested;

    public void EndCareer(string cause)
    {
        if (GameOver) return;
        GameOver = true; GameOverCause = cause;
        Emit(SimEventKind.Info, Active ?? new Craft { Id = -1 }, $"CAREER OVER: {cause}");
    }

    // ---------------------------------------------------------------- score (GDD §3)

    public long InfrastructureValue()
    {
        long v = 0;
        v += ExtraHomePads * 400_000 * 20;   // an extra pad's build cost is 20 days of its upkeep (proposal)
        v += DroneShips.Count * 6_000_000;
        foreach (var c in Crafts)
        {
            if (c.Destroyed) continue;
            foreach (var p in c.Parts) if (!p.Destroyed && p.Def.Has(PartFlags.DepotController | PartFlags.OutpostCore | PartFlags.PadKit | PartFlags.Fabricator | PartFlags.Storage)) v += PartPrice(p.Def);
        }
        return v / 2;   // at 50% of build cost
    }

    public long StoredResourceValue()
    {
        long v = 0;
        foreach (var c in Crafts)
        {
            if (c.Destroyed || c.Npc) continue;
            var node = Market.NodeFor(Sys, c, T) ?? Market.Home;
            foreach (var r in new[] { Resource.Methalox, Resource.Rcs, Resource.Xenon, Resource.Water, Resource.Metal })
            {
                double q = c.Resource(r);
                if (q > 0) v += Market.Quote(node, r, q, T);
            }
        }
        return v;
    }

    public long NetWorth() => Cash + InfrastructureValue() + StoredResourceValue();

    public double BodyMultiplier(int bodyId) => bodyId == Sys.HomeId ? 0 : 1 + DeltaV.LowToLow(Sys, Sys.HomeId, bodyId) / 2000.0;

    public double ExplorationPoints()
    {
        double pts = 0;
        foreach (var (id, e) in Exploration)
        {
            if (id < 0 || id == Sys.HomeId) continue;
            double m = BodyMultiplier(id);
            if (e.HasFlag(Explored.Flyby)) pts += 10 * m;
            if (e.HasFlag(Explored.Orbit)) pts += 25 * m;
            if (e.HasFlag(Explored.Landed)) pts += 50 * m;
        }
        return pts;
    }

    public long Score() => NetWorth() + (long)(ExplorationPoints() * 1_000_000);

    public void RecordExploration(int bodyId, Explored what)
    {
        if (bodyId == Sys.HomeId) return;
        var cur = Exploration.TryGetValue(bodyId, out var e) ? e : Explored.None;
        if (cur.HasFlag(what)) return;
        Exploration[bodyId] = cur | what;
        string verb = what switch { Explored.Flyby => "first flyby of", Explored.Orbit => "first orbit of", _ => "first landing on" };
        Emit(SimEventKind.Info, Active ?? new Craft { Id = -1 }, $"Exploration: {verb} {Sys[bodyId].Name} (+{(what == Explored.Flyby ? 10 : what == Explored.Orbit ? 25 : 50) * BodyMultiplier(bodyId):0} pts)");
    }

    // ---------------------------------------------------------------- selling

    /// <summary>Sell from a craft's tanks at the node it is in. Depots sell in orbit; anyone sells landed at home or on an owned pad.</summary>
    public long SellFrom(Craft c, Resource r, double tonnes)
    {
        var node = NodeFor(c);
        if (node == null) return 0;
        bool depot = c.Parts.Any(p => !p.Destroyed && p.Def.Has(PartFlags.DepotController) && !p.Offline);
        if (node.Kind == NodeKind.LowOrbit && !depot) return 0;
        // At an off-world pad the whole outpost's storage is the stock (GDD §9); elsewhere just this craft's tanks.
        var pool = node.Kind == NodeKind.PadSurface ? Outposts().FirstOrDefault(o => o.Members.Contains(c))?.Members ?? new List<Craft> { c } : new List<Craft> { c };
        double have = pool.Sum(x => x.Resource(r));
        tonnes = Math.Min(tonnes, have);
        if (tonnes <= 0) return 0;
        long rev = Market.Sell(node, r, tonnes, T);
        // Drain proportionally.
        foreach (var x in pool) foreach (var p in x.Parts) { if (p.Destroyed || p.Get(r) <= 0) continue; p.Set(r, p.Get(r) - p.Get(r) / have * tonnes); }
        foreach (var x in pool) x.UpdateMass();
        Earn(rev, $"sold {tonnes:0.00} t of {r} at {node.Name}");
        Count("sales"); Count("tonnes_sold", tonnes); if (node.Kind == NodeKind.LowOrbit && depot) Count("depot_sales"); SaveRequested = true;
        c.SalesTotal += rev;
        if (node.Kind == NodeKind.LowOrbit && depot && r == Resource.Methalox) DepotSalesTonnes += tonnes;
        return rev;
    }
}
