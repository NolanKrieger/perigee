using System.IO.Compression;
using System.Text.Json;

namespace Perigee.Sim;

/// <summary>JSON + gzip, atomic writes (temp file + rename), a version with migration, and a `.bak` of the previous save (GDD §18).</summary>
public static class SaveStore
{
    public const int CurrentVersion = 1;
    static readonly JsonSerializerOptions Options = new() { WriteIndented = false, NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };

    public static string Serialize(SaveData d) => JsonSerializer.Serialize(d, Options);
    public static SaveData Deserialize(string json)
    {
        using var doc = JsonDocument.Parse(json);
        int version = doc.RootElement.TryGetProperty("Version", out var v) ? v.GetInt32() : 0;
        if (version > CurrentVersion) throw new InvalidDataException($"save is version {version}; this build reads up to {CurrentVersion}");
        string migrated = version < CurrentVersion ? Migrate(json, version) : json;
        return JsonSerializer.Deserialize<SaveData>(migrated, Options) ?? throw new InvalidDataException("empty save");
    }

    /// <summary>Upgrade an older save's JSON step by step to the current version.</summary>
    public static string Migrate(string json, int fromVersion)
    {
        // Version 0 never shipped; the switch is the place future bumps land.
        return fromVersion switch { 0 => json, _ => json };
    }

    public static byte[] Pack(SaveData d)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        using (var sw = new StreamWriter(gz)) sw.Write(Serialize(d));
        return ms.ToArray();
    }

    public static SaveData Unpack(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var gz = new GZipStream(ms, CompressionMode.Decompress);
        using var sr = new StreamReader(gz);
        return Deserialize(sr.ReadToEnd());
    }

    /// <summary>Write atomically: bytes go to `path.tmp`, the previous file becomes `path.bak`, then the temp file is renamed into place.</summary>
    public static void Write(string path, SaveData d)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        string tmp = path + ".tmp";
        File.WriteAllBytes(tmp, Pack(d));
        if (File.Exists(path)) File.Copy(path, path + ".bak", overwrite: true);
        File.Move(tmp, path, overwrite: true);
    }

    public static SaveData Read(string path) => Unpack(File.ReadAllBytes(path));

    /// <summary>Read the save, or the backup if the main file is damaged.</summary>
    public static SaveData ReadOrBackup(string path)
    {
        try { return Read(path); }
        catch when (File.Exists(path + ".bak")) { return Read(path + ".bak"); }
    }
}

public sealed partial class World
{
    /// <summary>Design of the last booster recovered at ≥ 85% (the Hat Trick streak counts consecutive recoveries of the same design).</summary>
    public string LastBoosterDesign = "";
    public string SystemKind = "generated";   // "test" for the hand-made system (scripts, tests, demo)
    public double PlayedSeconds;              // real seconds of play (the game layer accumulates it)

    /// <summary>Snapshot everything that cannot be recomputed. Refuses during a booster flashback (that state is transient by design).</summary>
    public SaveData ToSave()
    {
        if (InFlashback) throw new InvalidOperationException("cannot save during a booster flashback");
        var d = new SaveData
        {
            SavedAt = DateTime.UtcNow.ToString("o"), SystemKind = SystemKind, Seed = Sys.Seed, T = T, ActiveCraftId = ActiveCraftId, WarpIndex = WarpIndex, NextCraftId = _nextCraftId, Cash = Cash,
            Preset = Preset.Name, Agency = AgencyName, Unlocked = Unlocked.OrderBy(x => x).ToList(), ExtraHomePads = ExtraHomePads, LastUpkeepDay = LastUpkeepDay, LastUpkeepCharged = LastUpkeepCharged,
            InsolventSince = InsolventSince, GameOver = GameOver, GameOverCause = GameOverCause, PeakScore = PeakScore, TotalEarned = TotalEarned, TotalSpent = TotalSpent,
            LastBoosterDesign = LastBoosterDesign, DepotSalesTonnes = DepotSalesTonnes, TutorialStep = TutorialStep, TutorialEnabled = TutorialEnabled, NextContractId = _nextContractId, BoardEverFilled = _boardEverFilled, PlayedSeconds = PlayedSeconds,
            DepthScale = Market.DepthScale, Storm = (int)Storm, StormStart = StormStart, StormEnd = StormEnd, StormsSoFar = StormsSoFar, StormFryChance = StormFryChance, StruckThisStorm = _struckThisStorm.OrderBy(x => x).ToList(), LastStormStrikeDay = _lastStormStrikeDay,
        };
        var (s0, s1, s2, s3) = Rng.State; d.Rng = new[] { s0, s1, s2, s3 };
        foreach (var (id, e) in Exploration) d.Exploration[id] = (int)e;
        foreach (var (day, cash) in CashHistory) d.CashHistory.Add(new[] { day, cash });
        foreach (var n in Market.Nodes)
        {
            var ns = new NodeSave { Kind = (int)n.Kind, BodyId = n.BodyId, DeltaVFromHome = n.DeltaVFromHome, Name = n.Name };
            foreach (var (r, g) in n.Glut) ns.Glut[(int)r] = new[] { g.glut, g.at };
            d.Nodes.Add(ns);
        }
        foreach (var (day, node, res, price) in Market.History) d.PriceHistory.Add(new PriceSave { Day = day, Node = node, Res = (int)res, Price = price });
        foreach (var k in Contracts) d.Contracts.Add(new ContractSave
        {
            Id = k.Id, Type = (int)k.Type, State = (int)k.State, Client = k.Client, Title = k.Title, Objective = k.Objective, Hint = k.Hint, Tutorial = k.Tutorial, TargetBody = k.TargetBody, ClientCraft = k.ClientCraft,
            PayloadPart = k.PayloadPart, Resource = (int)k.Resource, Tonnes = k.Tonnes, OrbitMin = k.OrbitMin, OrbitMax = k.OrbitMax, Count = k.Count, Survey = (int)k.Survey, Reward = k.Reward, Advance = k.Advance, Penalty = k.Penalty,
            OfferedAt = k.OfferedAt, Deadline = k.Deadline, AcceptedAt = k.AcceptedAt, ResolvedAt = k.ResolvedAt, TookAdvance = k.TookAdvance, DurationDays = k.DurationDays, Progress = k.Progress, Flag1 = k.Flag1, Flag2 = k.Flag2,
            Delivered = k.Delivered, Baseline = k.Baseline, Done = k.Done, WarnedDeadline = k.WarnedDeadline,
        });
        foreach (var c in Crafts)
        {
            if (c.Destroyed) continue;
            var cs = new CraftSave
            {
                Id = c.Id, Name = c.Name, DesignName = c.DesignName, Mode = (int)c.Mode, BodyId = c.BodyId, PosX = c.Pos.X, PosY = c.Pos.Y, VelX = c.Vel.X, VelY = c.Vel.Y, Angle = c.Angle, AngVel = c.AngVel,
                Npc = c.Npc, Debris = c.Debris, WasBooster = c.WasBooster, TouchedByPlayer = c.TouchedByPlayer, ReachedAltitude = c.ReachedAltitude, Charge = c.Charge, Scooped = c.Scooped, LaunchCostPaid = c.LaunchCostPaid, SalesTotal = c.SalesTotal,
                NextStage = c.NextStage, RootId = c.RootId, NextPartId = c.NextPartId, Throttle = c.Throttle, RcsOn = c.RcsOn, LegsDown = c.LegsDown, CoastTimer = c.CoastTimer, DeltaVSpent = c.DeltaVSpent,
                TargetCraft = c.TargetCraft, TargetBody = c.TargetBody, PumpedTotal = c.PumpedTotal, DockedNames = new Dictionary<int, string>(c.DockedNames), DockedName = c.DockedName, LaunchTime = c.LaunchTime, LaunchPadBody = c.LaunchPadBody,
                HeldTime = c.HeldTime, OrbitEntryTime = c.OrbitEntryTime,
            };
            if (c.Mode == CraftMode.Rails) { var o = c.Rails.Conic; cs.Rails = new[] { o.Mu, o.P, o.E, o.Omega, o.Tp, o.Dir }; }
            if (c.Landed is { } L) cs.Landed = new[] { L.LocalAngle, L.Radius, L.AngleOffset, L.Water ? 1 : 0, L.OnDroneShip ? 1 : 0 };
            if (c.Capture is { } cap) { cs.Capture = new[] { cap.OtherCraft, cap.MyPort, cap.OtherPort }; cs.CaptureTimer = cap.Timer; }
            foreach (var j in c.Pumps) cs.Pumps.Add(new[] { j.Source, j.Destination, (int)j.Resource, j.Rate, j.Moved });
            foreach (var st in c.Stages) cs.Stages.Add(new List<int>(st));
            foreach (var p in c.Parts) cs.Parts.Add(new PartSave
            {
                Id = p.Id, Def = p.Def.Id, X = p.Pos.X, Y = p.Pos.Y, Flip = p.Flip, Parent = p.Parent, Attach = (int)p.Attach, Res = (double[])p.Res.Clone(), Temp = p.Temp, Ablator = p.Ablator, Deployed = p.Deployed, Fired = p.Fired, Running = p.Running,
                Destroyed = p.Destroyed, Offline = p.Offline, OfflineUntil = p.OfflineUntil, ContractId = p.ContractId, ClientOf = p.ClientOf, ChuteRamp = p.ChuteRamp, Recipe = (int)p.Recipe,
            });
            d.Crafts.Add(cs);
        }
        foreach (var b in Sys.Bodies)
        {
            if (!b.Scanned && b.FlatSpots.Count == 0 && !b.Deposits.Any(x => x.Revealed)) continue;
            var bs = new BodySave { Id = b.Id, Scanned = b.Scanned, Revealed = b.Deposits.Select(x => x.Revealed).ToList() };
            foreach (var (a, hw, h) in b.FlatSpots) bs.FlatSpots.Add(new[] { a, hw, h });
            d.Bodies.Add(bs);
        }
        foreach (var h in HeldBoosters) d.HeldBoosters.Add(new HeldSave { CraftId = h.CraftId, SepTime = h.SepTime, Name = h.Name });
        foreach (var (bodyId, ang) in DroneShips) d.DroneShips.Add(new[] { bodyId, ang });
        foreach (var (k, v) in CareerStats) d.Stats[k] = v;
        return d;
    }

    /// <summary>Rebuild a world from a save: the star system comes back from its seed, everything else from the data.</summary>
    public static World FromSave(SaveData d)
    {
        var sys = d.SystemKind == "test" ? TestSystems.Basic() : SystemGenerator.Generate(d.Seed);
        var w = new World(sys, d.Seed) { SystemKind = d.SystemKind };
        w.Rng = new Rng(d.Rng[0], d.Rng[1], d.Rng[2], d.Rng[3]);
        w.T = d.T; w.ActiveCraftId = d.ActiveCraftId; w.WarpIndex = d.WarpIndex; w._nextCraftId = d.NextCraftId; w.Cash = d.Cash;
        w.Preset = Sim.Preset.ByName(d.Preset); w.AgencyName = d.Agency; w.Unlocked = new HashSet<string>(d.Unlocked); w.ExtraHomePads = d.ExtraHomePads; w.LastUpkeepDay = d.LastUpkeepDay; w.LastUpkeepCharged = d.LastUpkeepCharged;
        w.InsolventSince = d.InsolventSince; w.GameOver = d.GameOver; w.GameOverCause = d.GameOverCause; w.PeakScore = d.PeakScore; w.TotalEarned = d.TotalEarned; w.TotalSpent = d.TotalSpent;
        w.LastBoosterDesign = d.LastBoosterDesign ?? ""; w.DepotSalesTonnes = d.DepotSalesTonnes; w.TutorialStep = d.TutorialStep; w.TutorialEnabled = d.TutorialEnabled; w._nextContractId = d.NextContractId; w._boardEverFilled = d.BoardEverFilled; w.PlayedSeconds = d.PlayedSeconds;
        foreach (var (id, e) in d.Exploration) w.Exploration[id] = (Explored)e;
        foreach (var h in d.CashHistory) w.CashHistory.Add(((int)h[0], h[1]));
        w.Market.Nodes.Clear(); w.Market.DepthScale = d.DepthScale;
        foreach (var ns in d.Nodes)
        {
            var n = new MarketNode { Kind = (NodeKind)ns.Kind, BodyId = ns.BodyId, DeltaVFromHome = ns.DeltaVFromHome, Name = ns.Name };
            foreach (var (r, g) in ns.Glut) n.Glut[(Resource)r] = (g[0], g[1]);
            w.Market.Nodes.Add(n);
        }
        foreach (var p in d.PriceHistory) w.Market.History.Add((p.Day, p.Node, (Resource)p.Res, p.Price));
        foreach (var k in d.Contracts) w.Contracts.Add(new Contract
        {
            Id = k.Id, Type = (ContractType)k.Type, State = (ContractState)k.State, Client = k.Client, Title = k.Title, Objective = k.Objective, Hint = k.Hint, Tutorial = k.Tutorial, TargetBody = k.TargetBody, ClientCraft = k.ClientCraft,
            PayloadPart = k.PayloadPart, Resource = (Resource)k.Resource, Tonnes = k.Tonnes, OrbitMin = k.OrbitMin, OrbitMax = k.OrbitMax, Count = k.Count, Survey = (SurveyKind)k.Survey, Reward = k.Reward, Advance = k.Advance, Penalty = k.Penalty,
            OfferedAt = k.OfferedAt, Deadline = k.Deadline, AcceptedAt = k.AcceptedAt, ResolvedAt = k.ResolvedAt, TookAdvance = k.TookAdvance, DurationDays = k.DurationDays, Progress = k.Progress, Flag1 = k.Flag1, Flag2 = k.Flag2,
            Delivered = k.Delivered, Baseline = k.Baseline, Done = k.Done, WarnedDeadline = k.WarnedDeadline,
        });
        foreach (var cs in d.Crafts)
        {
            var c = new Craft
            {
                Id = cs.Id, Name = cs.Name, DesignName = cs.DesignName, Mode = (CraftMode)cs.Mode, BodyId = cs.BodyId, Pos = new Vec2d(cs.PosX, cs.PosY), Vel = new Vec2d(cs.VelX, cs.VelY), Angle = cs.Angle, AngVel = cs.AngVel,
                Npc = cs.Npc, Debris = cs.Debris, WasBooster = cs.WasBooster, TouchedByPlayer = cs.TouchedByPlayer, ReachedAltitude = cs.ReachedAltitude, Charge = cs.Charge, Scooped = cs.Scooped, LaunchCostPaid = cs.LaunchCostPaid, SalesTotal = cs.SalesTotal,
                NextStage = cs.NextStage, RootId = cs.RootId, NextPartId = cs.NextPartId, Throttle = cs.Throttle, RcsOn = cs.RcsOn, LegsDown = cs.LegsDown, CoastTimer = cs.CoastTimer, DeltaVSpent = cs.DeltaVSpent,
                TargetCraft = cs.TargetCraft, TargetBody = cs.TargetBody, PumpedTotal = cs.PumpedTotal, DockedNames = new Dictionary<int, string>(cs.DockedNames), DockedName = cs.DockedName, LaunchTime = cs.LaunchTime, LaunchPadBody = cs.LaunchPadBody,
                HeldTime = cs.HeldTime, OrbitEntryTime = cs.OrbitEntryTime,
            };
            if (cs.Rails != null) c.Rails = new RailsState(cs.BodyId, new Conic(cs.Rails[0], cs.Rails[1], cs.Rails[2], cs.Rails[3], cs.Rails[4], (int)cs.Rails[5]));
            if (cs.Landed != null) c.Landed = new LandedState { LocalAngle = cs.Landed[0], Radius = cs.Landed[1], AngleOffset = cs.Landed[2], Water = cs.Landed[3] > 0.5, OnDroneShip = cs.Landed[4] > 0.5 };
            if (cs.Capture != null) c.Capture = new Capture { OtherCraft = cs.Capture[0], MyPort = cs.Capture[1], OtherPort = cs.Capture[2], Timer = cs.CaptureTimer };
            foreach (var j in cs.Pumps) c.Pumps.Add(new PumpJob { Source = (int)j[0], Destination = (int)j[1], Resource = (Resource)(int)j[2], Rate = j[3], Moved = j[4] });
            foreach (var st in cs.Stages) c.Stages.Add(new List<int>(st));
            foreach (var ps in cs.Parts)
            {
                var p = new Part
                {
                    Id = ps.Id, Def = Sim.Parts.Get(ps.Def), Pos = new Vec2d(ps.X, ps.Y), Flip = ps.Flip, Parent = ps.Parent, Attach = (AttachKind)ps.Attach, Res = (double[])ps.Res.Clone(), Temp = ps.Temp, Ablator = ps.Ablator, Deployed = ps.Deployed, Fired = ps.Fired, Running = ps.Running,
                    Destroyed = ps.Destroyed, Offline = ps.Offline, OfflineUntil = ps.OfflineUntil, ContractId = ps.ContractId, ClientOf = ps.ClientOf, ChuteRamp = ps.ChuteRamp, Recipe = (Recipe)ps.Recipe,
                };
                c.Parts.Add(p);
            }
            c.UpdateMass();
            c.Next = null; c.SearchedUntil = double.NegativeInfinity;
            w.Crafts.Add(c);
        }
        foreach (var bs in d.Bodies)
        {
            var b = sys[bs.Id];
            b.Scanned = bs.Scanned;
            for (int i = 0; i < Math.Min(bs.Revealed.Count, b.Deposits.Count); i++) b.Deposits[i].Revealed = bs.Revealed[i];
            foreach (var f in bs.FlatSpots) b.FlatSpots.Add((f[0], f[1], f[2]));
        }
        foreach (var h in d.HeldBoosters) w.HeldBoosters.Add(new HeldBooster { CraftId = h.CraftId, SepTime = h.SepTime, Name = h.Name });
        foreach (var s in d.DroneShips) w.DroneShips.Add(((int)s[0], s[1]));
        w.Storm = (StormPhase)d.Storm; w.StormStart = d.StormStart; w.StormEnd = d.StormEnd; w.StormsSoFar = d.StormsSoFar; w.StormFryChance = d.StormFryChance; w._lastStormStrikeDay = d.LastStormStrikeDay;
        foreach (var id in d.StruckThisStorm) w._struckThisStorm.Add(id);
        foreach (var (k, v) in d.Stats) w.CareerStats[k] = v;
        return w;
    }

    /// <summary>Career counters that outlive any one craft (achievements, logbook): launches, dockings, recoveries, debris deorbited...</summary>
    public Dictionary<string, double> CareerStats = new();
    public void Count(string stat, double amount = 1) => CareerStats[stat] = CareerStats.GetValueOrDefault(stat) + amount;

    /// <summary>A stable fingerprint of the sim state for round-trip and determinism tests (bit-exact doubles).</summary>
    public ulong StateHash()
    {
        ulong h = 14695981039346656037UL;
        void Mix(ulong v) { h ^= v; h *= 1099511628211UL; }
        void D(double v) => Mix((ulong)BitConverter.DoubleToInt64Bits(v));
        void I(long v) => Mix((ulong)v);
        D(T); I(Cash); I(ActiveCraftId); I(WarpIndex); I(_nextCraftId);
        var (s0, s1, s2, s3) = Rng.State; Mix(s0); Mix(s1); Mix(s2); Mix(s3);
        I(TutorialStep); I(_nextContractId); I(PeakScore); I(TotalEarned); I(TotalSpent); D(DepotSalesTonnes); I((long)Storm); D(StormStart); D(StormEnd); I(StormsSoFar);
        foreach (var (id, e) in Exploration.OrderBy(x => x.Key)) { I(id); I((long)e); }
        foreach (var c in Crafts.Where(c => !c.Destroyed).OrderBy(c => c.Id))
        {
            I(c.Id); I((long)c.Mode); I(c.BodyId); D(c.Angle); D(c.AngVel); D(c.Throttle); I(c.RcsOn ? 1 : 0); I(c.LegsDown ? 1 : 0); I(c.NextStage); I(c.Npc ? 1 : 0); I(c.Debris ? 1 : 0);
            var (r, v) = c.StateAt(T); D(r.X); D(r.Y); D(v.X); D(v.Y);
            if (c.Mode == CraftMode.Rails) { var o = c.Rails.Conic; D(o.P); D(o.E); D(o.Omega); D(o.Tp); }
            if (c.Landed is { } L) { D(L.LocalAngle); D(L.Radius); }
            foreach (var p in c.Parts) { I(p.Id); I(p.Destroyed ? 1 : 0); I(p.Deployed ? 1 : 0); I(p.Fired ? 1 : 0); I(p.Running ? 1 : 0); I(p.Offline ? 1 : 0); D(p.Temp); D(p.Ablator); foreach (var x in p.Res) D(x); }
        }
        foreach (var k in Contracts) { I(k.Id); I((long)k.State); D(k.Progress); D(k.Baseline); I(k.ClientCraft); }
        foreach (var n in Market.Nodes) { I((long)n.Kind); I(n.BodyId); foreach (var (r, g) in n.Glut.OrderBy(x => x.Key)) { I((long)r); D(g.glut); D(g.at); } }
        foreach (var b in Sys.Bodies) { I(b.Scanned ? 1 : 0); foreach (var dep in b.Deposits) I(dep.Revealed ? 1 : 0); foreach (var f in b.FlatSpots) { D(f.angle); D(f.height); } }
        return h;
    }
}
