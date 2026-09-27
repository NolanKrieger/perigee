namespace Perigee.Sim;

// Save format (GDD §18: one autosave per career, JSON + gzip). Plain data classes: every field of the sim that cannot be recomputed.
// Version bumps go through SaveStore.Migrate.

public sealed class SaveData
{
    public int Version { get; set; } = SaveStore.CurrentVersion;
    public string Game { get; set; } = "Perigee Fuel Co.";
    public string SavedAt { get; set; } = "";
    public string SystemKind { get; set; } = "generated";   // "generated" (from Seed) or "test" (the hand-made system)
    public ulong Seed { get; set; }
    public double T { get; set; }
    public ulong[] Rng { get; set; } = new ulong[4];
    public int ActiveCraftId { get; set; } = -1;
    public int WarpIndex { get; set; }
    public int NextCraftId { get; set; }
    public long Cash { get; set; }
    // Economy
    public string Preset { get; set; } = "Standard";
    public string Agency { get; set; } = "";
    public List<string> Unlocked { get; set; } = new();
    public int ExtraHomePads { get; set; }
    public int LastUpkeepDay { get; set; }
    public long LastUpkeepCharged { get; set; }
    public double InsolventSince { get; set; } = double.NaN;
    public bool GameOver { get; set; }
    public string GameOverCause { get; set; } = "";
    public long PeakScore { get; set; }
    public Dictionary<int, int> Exploration { get; set; } = new();
    public List<long[]> CashHistory { get; set; } = new();
    public long TotalEarned { get; set; }
    public long TotalSpent { get; set; }
    public double DepotSalesTonnes { get; set; }
    public int TutorialStep { get; set; } = 1;
    public bool TutorialEnabled { get; set; } = true;
    public int NextContractId { get; set; } = 1;
    public bool BoardEverFilled { get; set; }
    public double PlayedSeconds { get; set; }
    public string? LastBoosterDesign { get; set; }
    // Market
    public List<NodeSave> Nodes { get; set; } = new();
    public List<PriceSave> PriceHistory { get; set; } = new();
    public double DepthScale { get; set; } = 1;
    // Contracts
    public List<ContractSave> Contracts { get; set; } = new();
    // Craft
    public List<CraftSave> Crafts { get; set; } = new();
    // Bodies (mutable bits only)
    public List<BodySave> Bodies { get; set; } = new();
    // Recovery
    public List<HeldSave> HeldBoosters { get; set; } = new();
    public List<double[]> DroneShips { get; set; } = new();
    // Hazards
    public int Storm { get; set; }
    public double StormStart { get; set; } = double.NaN;
    public double StormEnd { get; set; } = double.NaN;
    public int StormsSoFar { get; set; }
    public double StormFryChance { get; set; } = Hazards.FryChance;
    public List<int> StruckThisStorm { get; set; } = new();
    public int LastStormStrikeDay { get; set; } = -1;
    // Achievements / stats (M13 reads these)
    public Dictionary<string, double> Stats { get; set; } = new();
}

public sealed class NodeSave { public int Kind { get; set; } public int BodyId { get; set; } public double DeltaVFromHome { get; set; } public string Name { get; set; } = ""; public Dictionary<int, double[]> Glut { get; set; } = new(); }
public sealed class PriceSave { public int Day { get; set; } public int Node { get; set; } public int Res { get; set; } public long Price { get; set; } }

public sealed class ContractSave
{
    public int Id { get; set; } public int Type { get; set; } public int State { get; set; }
    public string Client { get; set; } = ""; public string Title { get; set; } = ""; public string Objective { get; set; } = ""; public string Hint { get; set; } = "";
    public int Tutorial { get; set; } public int TargetBody { get; set; } = -1; public int ClientCraft { get; set; } = -1; public string PayloadPart { get; set; } = "";
    public int Resource { get; set; } public double Tonnes { get; set; } public double OrbitMin { get; set; } public double OrbitMax { get; set; } public int Count { get; set; } public int Survey { get; set; }
    public long Reward { get; set; } public long Advance { get; set; } public long Penalty { get; set; }
    public double OfferedAt { get; set; } public double Deadline { get; set; } public double AcceptedAt { get; set; } = double.NaN; public double ResolvedAt { get; set; } = double.NaN;
    public bool TookAdvance { get; set; } public double DurationDays { get; set; } public double Progress { get; set; } public bool Flag1 { get; set; } public bool Flag2 { get; set; }
    public double Delivered { get; set; } public double Baseline { get; set; } public int Done { get; set; } public bool WarnedDeadline { get; set; }
}

public sealed class PartSave
{
    public int Id { get; set; } public string Def { get; set; } = ""; public double X { get; set; } public double Y { get; set; } public bool Flip { get; set; }
    public int Parent { get; set; } = -1; public int Attach { get; set; } public double[] Res { get; set; } = new double[8];
    public double Temp { get; set; } = 290; public double Ablator { get; set; } public bool Deployed { get; set; } public bool Fired { get; set; } public bool Running { get; set; }
    public bool Destroyed { get; set; } public bool Offline { get; set; } public double OfflineUntil { get; set; } = double.NaN;
    public int ContractId { get; set; } = -1; public int ClientOf { get; set; } = -1; public double ChuteRamp { get; set; } public int Recipe { get; set; }
}

public sealed class CraftSave
{
    public int Id { get; set; } public string Name { get; set; } = ""; public string DesignName { get; set; } = ""; public int Mode { get; set; } public int BodyId { get; set; }
    public double[]? Rails { get; set; }   // mu, p, e, omega, tp, dir
    public double PosX { get; set; } public double PosY { get; set; } public double VelX { get; set; } public double VelY { get; set; }
    public double Angle { get; set; } public double AngVel { get; set; }
    public bool Npc { get; set; } public bool Debris { get; set; } public bool WasBooster { get; set; } public bool TouchedByPlayer { get; set; }
    public double ReachedAltitude { get; set; } public double Charge { get; set; } public double Scooped { get; set; }
    public long LaunchCostPaid { get; set; } public long SalesTotal { get; set; }
    public List<PartSave> Parts { get; set; } = new();
    public List<List<int>> Stages { get; set; } = new();
    public int NextStage { get; set; } public int RootId { get; set; } public int NextPartId { get; set; }
    public double Throttle { get; set; } public bool RcsOn { get; set; } public bool LegsDown { get; set; }
    public double[]? Landed { get; set; }   // localAngle, radius, angleOffset, water, onDroneShip
    public double CoastTimer { get; set; } public double DeltaVSpent { get; set; }
    public int TargetCraft { get; set; } = -1; public int TargetBody { get; set; } = -1;
    public int[]? Capture { get; set; }     // otherCraft, myPort, otherPort + Timer stored separately
    public double CaptureTimer { get; set; }
    public List<double[]> Pumps { get; set; } = new();   // source, destination, resource, rate, moved
    public double PumpedTotal { get; set; }
    public Dictionary<int, string> DockedNames { get; set; } = new();
    public string DockedName { get; set; } = "";
    public double LaunchTime { get; set; } = double.NaN; public int LaunchPadBody { get; set; } = -1;
    public double HeldTime { get; set; } public double OrbitEntryTime { get; set; } = double.NaN;
}

public sealed class BodySave { public int Id { get; set; } public bool Scanned { get; set; } public List<bool> Revealed { get; set; } = new(); public List<double[]> FlatSpots { get; set; } = new(); }
public sealed class HeldSave { public int CraftId { get; set; } public double SepTime { get; set; } public string Name { get; set; } = ""; }
