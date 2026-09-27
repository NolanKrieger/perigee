namespace Perigee.Sim;

public enum ContractType { Deploy, FuelDelivery, Refuel, Survey, Retrieve, DebrisCleanup, Relocate, Supply }
public enum ContractState { Offered, Accepted, Completed, Failed, Expired }
public enum SurveyKind { Flyby, Orbit, Scan, Land }

/// <summary>A contract (GDD §11): procedural client, objective, deadline, reward, optional 20% advance, failure penalty.</summary>
public sealed class Contract
{
    public int Id;
    public ContractType Type;
    public ContractState State = ContractState.Offered;
    public string Client = "", Title = "", Objective = "", Hint = "";
    public int Tutorial;                 // 1..10 for T1–T10, else 0
    public int TargetBody = -1;
    public int ClientCraft = -1;         // NPC station/satellite (spawned on acceptance)
    public string PayloadPart = "";      // client payload part id (Deploy/Retrieve)
    public Resource Resource = Resource.Methalox;
    public double Tonnes;                // FuelDelivery/Refuel/Supply
    public double OrbitMin, OrbitMax;    // altitude window (Deploy/Relocate)
    public int Count;                    // debris to deorbit
    public SurveyKind Survey;
    public long Reward, Advance, Penalty;
    public double OfferedAt, Deadline;   // game time; Deadline counts from acceptance
    public double AcceptedAt = double.NaN, ResolvedAt = double.NaN;
    public bool TookAdvance;
    public double DurationDays;
    // progress
    public double Progress;              // 0..1 for the board
    public bool Flag1, Flag2;            // type-specific milestones (e.g. T1 reached 20 km)
    public double Delivered;
    public double Baseline;              // amount already there when accepted (refuel, depot sales)
    public int Done;
    public bool WarnedDeadline;

    public bool Active => State == ContractState.Accepted;
    public double TimeLeft(double t) => State == ContractState.Accepted ? Deadline - t : OfferedAt + Contracts.OfferLifetime - t;
    public override string ToString() => $"#{Id} {Title} [{State}]";
}

/// <summary>The board, generation, tutorial chain and completion rules (GDD §11). Everything is checked from world state, never from memory.</summary>
public static class Contracts
{
    public const double OfferLifetime = 5 * Units.Day;
    public const int MinOffers = 3, MaxOffers = 6, MaxAccepted = 5;
    public static readonly string[] Clients = { "Halcyon Logistics", "Orbital Assurance Co.", "Ministry of Orbits", "Cerulean Survey", "Northreach Mining", "Vantage Relay", "Kestrel & Daughters", "Tidewater Propellants", "Meridian Science Trust", "Farlight Freight", "Auric Metals", "Skyward Cooperative" };

    // ---------------------------------------------------------------- tutorial chain (GDD §11 table)

    public static Contract Tutorial(int n, World w)
    {
        var home = w.Sys.Home;
        var c = new Contract { Tutorial = n, Client = "Perigee Fuel Co. — Flight Ops", DurationDays = 40 };
        switch (n)
        {
            case 1: c.Type = ContractType.Survey; c.Title = "T1 · Sounding Rocket"; c.Objective = "Reach 20 km and land safely on the home world."; c.Reward = 4_000_000; c.TargetBody = home.Id; c.OrbitMin = 20_000; c.Hint = "Build a small rocket: Core S, a short tank, a Kestrel, a nose parachute and legs. Z = full throttle, Space = stage. Deploy the chute (P) below 140 m/s on the way down."; break;
            case 2: c.Type = ContractType.Survey; c.Title = "T2 · Space Is Up"; c.Objective = "Cross the 50 km atmosphere line."; c.Reward = 6_000_000; c.TargetBody = home.Id; c.OrbitMin = 50_000; c.Hint = "Fly straight up through the thick air, then tilt with D. Drag is the enemy below 10 km: don't go too fast too low."; break;
            case 3: c.Type = ContractType.Survey; c.Title = "T3 · First Orbit"; c.Objective = "Reach a stable orbit around the home world (periapsis above the atmosphere)."; c.Reward = 12_000_000; c.TargetBody = home.Id; c.Survey = SurveyKind.Orbit; c.Hint = "Watch the predicted path (orange) and the Ap/Pe markers. Tilt over gradually, coast to apoapsis, then burn horizontally until Pe clears 50 km."; break;
            case 4: c.Type = ContractType.Deploy; c.Title = "T4 · Comsat"; c.Objective = "Put the client comsat into a 200–300 km circular orbit."; c.Reward = 18_000_000; c.TargetBody = home.Id; c.OrbitMin = 200_000; c.OrbitMax = 300_000; c.PayloadPart = "payload-s"; c.Hint = "The comsat is in the Cargo tab, free. Put it under a fairing. Circularise with short burns: prograde at Ap raises Pe, at Pe raises Ap."; break;
            case 5: c.Type = ContractType.Retrieve; c.Title = "T5 · Bring It Back"; c.Objective = "Land a booster back at the pad (booster flashback)."; c.Reward = 15_000_000; c.TargetBody = home.Id; c.Hint = "Give the booster grid fins and legs. After staging, once the upper stage coasts, press Enter to fly the booster: burn back towards the pad, deploy fins (G), and land on the engine."; break;
            case 6: c.Type = ContractType.Refuel; c.Title = "T6 · Handshake"; c.Objective = "Dock with the client test target in low orbit."; c.Reward = 22_000_000; c.TargetBody = home.Id; c.Tonnes = 0; c.Hint = "Right-click the target to set it, match orbits so the closest approach is small, then use RCS (R, then IJKL) to close at under 0.5 m/s with the ports lined up."; break;
            case 7: c.Type = ContractType.Refuel; c.Title = "T7 · Top Up"; c.Objective = "Dock with the client satellite and pump 2 t of methalox into it."; c.Reward = 20_000_000; c.TargetBody = home.Id; c.Tonnes = 2; c.Hint = "After docking press F: pick your tank as the source, theirs as the destination, Pump."; break;
            case 8: c.Type = ContractType.Supply; c.Title = "T8 · Gas Station"; c.Objective = "Build a depot (Depot Controller) in low orbit and sell 10 t of methalox from it."; c.Reward = 30_000_000; c.TargetBody = home.Id; c.Resource = Resource.Methalox; c.Tonnes = 10; c.Hint = "Buy the Depot Controller in R&D. A depot in low orbit sells at about 8× the home price (F panel). Its cryocooler stops boil-off; it costs $1k/day."; break;
            case 9: c.Type = ContractType.Survey; c.Title = "T9 · Touchdown"; c.Objective = "Land a probe on the nearest landable body."; c.Reward = 40_000_000; c.Survey = SurveyKind.Land; c.TargetBody = NearestLandable(w); c.Hint = "Burn prograde in low orbit until the predicted path enters the target's sphere of influence. Warp stops there; then burn retrograde at periapsis to capture and land on legs."; break;
            case 10: c.Type = ContractType.Survey; c.Title = "T10 · Dig"; c.Objective = "Set up an outpost with a drill on the nearest ice deposit."; c.Reward = 60_000_000; c.Survey = SurveyKind.Scan; c.TargetBody = NearestIce(w); c.Hint = "Orbit once with a Resource Scanner to reveal deposits, then land an Outpost Core with a Drill inside the ice arc."; break;
        }
        c.Advance = c.Reward / 5; c.Penalty = c.Reward / 10;
        return c;
    }

    public static int NearestLandable(World w)
    {
        Body? best = null; double bestDv = double.PositiveInfinity;
        foreach (var b in w.Sys.Bodies)
        {
            if (b.Id == w.Sys.HomeId || !SystemGenerator.IsLandable(b) || b.Type == BodyType.Star) continue;
            double dv = SystemGenerator.DvToSurface(w.Sys, b);
            if (dv < bestDv) { bestDv = dv; best = b; }
        }
        return best?.Id ?? -1;
    }

    public static int NearestIce(World w)
    {
        Body? best = null; double bestDv = double.PositiveInfinity;
        foreach (var b in w.Sys.Bodies)
        {
            if (b.Id == w.Sys.HomeId || !SystemGenerator.IsLandable(b) || !SystemGenerator.Has(b, Resource.Ice)) continue;
            double dv = SystemGenerator.DvToSurface(w.Sys, b);
            if (dv < bestDv) { bestDv = dv; best = b; }
        }
        return best?.Id ?? NearestLandable(w);
    }

    // ---------------------------------------------------------------- procedural offers

    /// <summary>Bodies the player has reached (any exploration flag) plus the next one out by Δv.</summary>
    public static List<int> ReachableTargets(World w)
    {
        var reached = new HashSet<int> { w.Sys.HomeId };
        foreach (var (id, e) in w.Exploration) if (id >= 0 && e != Explored.None) reached.Add(id);
        var next = w.Sys.Bodies.Where(b => b.Type != BodyType.Star && !reached.Contains(b.Id)).OrderBy(b => DeltaV.LowToLow(w.Sys, w.Sys.HomeId, b.Id)).FirstOrDefault();
        var list = reached.ToList();
        if (next != null) list.Add(next.Id);
        return list;
    }

    public static Contract Generate(World w, Rng rng)
    {
        var targets = ReachableTargets(w);
        int body = rng.Pick(targets);
        var b = w.Sys[body];
        bool home = body == w.Sys.HomeId;
        bool landable = SystemGenerator.IsLandable(b) && b.Type != BodyType.Star;
        var types = new List<ContractType> { ContractType.Deploy, ContractType.FuelDelivery, ContractType.Refuel, ContractType.Relocate };
        if (!home) { types.Add(ContractType.Survey); types.Add(ContractType.Supply); types.Add(ContractType.Retrieve); }
        if (w.Crafts.Count(c => c.Debris && !c.Destroyed && c.BodyId == body) >= 3) types.Add(ContractType.DebrisCleanup);
        var type = rng.Pick(types);
        var c = new Contract { Type = type, Client = rng.Pick(Clients), TargetBody = body, DurationDays = rng.Range(25, 90) };
        double mult = 1 + DeltaV.LowToLow(w.Sys, w.Sys.HomeId, body) / 2500.0;
        double lowTop = b.LowOrbitTop - b.Radius, floor = b.ActiveZoneHeight + 20_000;
        switch (type)
        {
            case ContractType.Deploy:
                c.PayloadPart = rng.Chance(0.6) ? "payload-s" : "payload-m";
                c.OrbitMin = LowBiased(rng, floor, Math.Max(floor + 50_000, lowTop * 0.7));
                c.OrbitMax = c.OrbitMin + Math.Round(rng.Range(60_000, 150_000) / 10_000) * 10_000;
                c.Title = $"Deploy {(c.PayloadPart == "payload-s" ? "a relay" : "a survey platform")} at {b.Name}";
                c.Objective = $"Put the client's {(c.PayloadPart == "payload-s" ? "0.5 t relay" : "2 t platform")} into a {c.OrbitMin / 1000:0}–{c.OrbitMax / 1000:0} km orbit around {b.Name}.";
                c.Reward = (long)(12_000_000 * mult * (c.PayloadPart == "payload-s" ? 1 : 1.8));
                break;
            case ContractType.FuelDelivery:
                c.Resource = Resource.Methalox;
                c.Tonnes = rng.Next(2, 9);
                c.Title = $"Deliver {c.Tonnes:0.#} t of {Name(c.Resource)} to {b.Name} orbit";
                c.Objective = $"Bring {c.Tonnes:0.#} t of {Name(c.Resource)} to the client station in low {b.Name} orbit and pump it across.";
                c.Reward = (long)(c.Tonnes * Market.BasePrice(c.Resource) * (1 + 5 * (Math.Exp(DeltaV.HomeSurfaceToLowOrbitOf(w.Sys, body) / 3000) - 1)) * 1.6);
                break;
            case ContractType.Refuel:
                c.Resource = Resource.Methalox; c.Tonnes = rng.Next(1, 4);
                c.Title = $"Refuel a satellite at {b.Name}";
                c.Objective = $"Dock with the client satellite in low {b.Name} orbit and top up its tank ({c.Tonnes:0.#} t).";
                c.Reward = (long)(9_000_000 * mult + c.Tonnes * 1_500_000);
                break;
            case ContractType.Survey:
                c.Survey = landable ? rng.Pick(new[] { SurveyKind.Flyby, SurveyKind.Orbit, SurveyKind.Scan, SurveyKind.Land }) : rng.Pick(new[] { SurveyKind.Flyby, SurveyKind.Orbit, SurveyKind.Scan });
                c.Title = $"{c.Survey} {b.Name}";
                c.Objective = c.Survey switch { SurveyKind.Flyby => $"Fly a probe through {b.Name}'s sphere of influence.", SurveyKind.Orbit => $"Put a probe into orbit around {b.Name}.", SurveyKind.Scan => $"Map {b.Name}'s deposits: one full orbit with a Resource Scanner.", _ => $"Land a probe on {b.Name}." };
                c.Reward = (long)((c.Survey switch { SurveyKind.Flyby => 5_000_000, SurveyKind.Orbit => 9_000_000, SurveyKind.Scan => 14_000_000, _ => 20_000_000 }) * mult);
                break;
            case ContractType.Retrieve:
                c.PayloadPart = "payload-s";
                c.Title = $"Retrieve a derelict from {b.Name} orbit";
                c.Objective = $"Bring the client's derelict package down to the home surface intact (grab it with a claw or dock with it).";
                c.Reward = (long)(16_000_000 * mult);
                break;
            case ContractType.DebrisCleanup:
                c.Count = rng.Next(2, 5);
                c.OrbitMin = floor; c.OrbitMax = lowTop;
                c.Title = $"Clean up {c.Count} debris objects at {b.Name}";
                c.Objective = $"Deorbit {c.Count} debris objects from {b.Name}'s low orbit band (claw + retrograde burn).";
                c.Reward = (long)(c.Count * 6_000_000 * mult);
                break;
            case ContractType.Relocate:
                c.OrbitMin = LowBiased(rng, floor, lowTop * 0.8);
                c.OrbitMax = c.OrbitMin + 80_000;
                c.Title = $"Relocate a satellite at {b.Name}";
                c.Objective = $"Dock with the client satellite and move it to a {c.OrbitMin / 1000:0}–{c.OrbitMax / 1000:0} km orbit, then undock.";
                c.Reward = (long)(14_000_000 * mult);
                break;
            case ContractType.Supply:
                c.Resource = rng.Chance(0.6) ? Resource.Water : Resource.Metal; c.Tonnes = rng.Next(1, 5);
                c.Title = $"Supply {c.Tonnes:0.#} t of {Name(c.Resource)} at {b.Name}";
                c.Objective = $"Deliver {c.Tonnes:0.#} t of {Name(c.Resource)} to the client outpost's orbital station at {b.Name}.";
                c.Reward = (long)(c.Tonnes * Market.BasePrice(c.Resource) * 8 * mult + 6_000_000);
                break;
        }
        c.Reward = (long)(c.Reward * w.Preset.ContractPay / 1000) * 1000;
        c.Advance = c.Reward / 5; c.Penalty = c.Reward / 10;
        return c;
    }

    /// <summary>Window floors cluster low (r² on the span, rounded to 10 km): the board keeps offering orbits a starter-class rocket can reach while high windows still appear.</summary>
    static double LowBiased(Rng rng, double floor, double top) { double r = rng.Range(0.0, 1.0); return Math.Round((floor + (Math.Max(top, floor) - floor) * r * r) / 10_000) * 10_000; }

    public static string Name(Resource r) => r switch { Resource.Methalox => "methalox", Resource.Rcs => "RCS propellant", Resource.Xenon => "xenon", Resource.Water => "water", Resource.Metal => "metal", _ => r.ToString().ToLowerInvariant() };
}

public sealed partial class World
{
    public List<Contract> Contracts = new();
    int _nextContractId = 1;
    bool _boardEverFilled;
    public double DepotSalesTonnes;
    public int TutorialStep = 1;   // next tutorial contract to offer
    public bool TutorialEnabled = true;

    public IEnumerable<Contract> Offers => Contracts.Where(c => c.State == ContractState.Offered);
    public IEnumerable<Contract> AcceptedContracts => Contracts.Where(c => c.State == ContractState.Accepted);

    /// <summary>Fill the board (called at career start and every day): expire stale offers, keep 3–6 open, offer the next tutorial step.</summary>
    public void RefreshBoard()
    {
        foreach (var c in Contracts.Where(c => c.State == ContractState.Offered && c.Tutorial == 0 && T - c.OfferedAt > Sim.Contracts.OfferLifetime).ToList()) c.State = ContractState.Expired;
        if (TutorialEnabled && TutorialStep <= 10 && !Contracts.Any(c => c.Tutorial == TutorialStep))
        {
            var t = Sim.Contracts.Tutorial(TutorialStep, this);
            t.Id = _nextContractId++; t.OfferedAt = T;
            t.Reward = (long)(t.Reward * Preset.ContractPay); t.Advance = t.Reward / 5; t.Penalty = t.Reward / 10;
            Contracts.Add(t);
        }
        int open = Offers.Count();
        int want = Contracts.Count == 0 || !_boardEverFilled ? Sim.Contracts.MinOffers : Math.Clamp(open + Rng.Next(1, 4), Sim.Contracts.MinOffers, Sim.Contracts.MaxOffers);
        _boardEverFilled = true;
        int guard = 0;
        while (Offers.Count() < want && guard++ < 20)
        {
            var c = Sim.Contracts.Generate(this, Rng);
            c.Id = _nextContractId++; c.OfferedAt = T;
            Contracts.Add(c);
        }
    }

    public bool Accept(Contract c, bool advance)
    {
        if (c.State != ContractState.Offered || AcceptedContracts.Count() >= Sim.Contracts.MaxAccepted) return false;
        c.State = ContractState.Accepted; c.AcceptedAt = T; c.Deadline = T + c.DurationDays * Units.Day; c.TookAdvance = advance;
        if (advance) Earn(c.Advance, $"advance on {c.Title}");
        SpawnClientCraft(c);
        c.Baseline = c.Tutorial == 8 ? DepotSalesTonnes : ClientAmount(c);
        Emit(SimEventKind.Info, Active ?? new Craft { Id = -1 }, $"Accepted: {c.Title}");
        return true;
    }

    public void Decline(Contract c) { if (c.State == ContractState.Offered) c.State = ContractState.Expired; }

    void Complete(Contract c)
    {
        c.State = ContractState.Completed; c.ResolvedAt = T; c.Progress = 1;
        Count("contracts_completed"); SaveRequested = true;
        Earn(c.Reward - (c.TookAdvance ? c.Advance : 0), $"contract complete: {c.Title}");
        if (c.Tutorial > 0 && c.Tutorial == TutorialStep) { TutorialStep++; RefreshBoard(); }   // the next lesson is offered at once
    }

    public void Fail(Contract c, string why)
    {
        if (c.State != ContractState.Accepted) return;
        c.State = ContractState.Failed; c.ResolvedAt = T;
        long penalty = c.Penalty + (c.TookAdvance ? c.Advance : 0);
        Cash -= penalty; TotalSpent += penalty;
        Emit(SimEventKind.Info, Active ?? new Craft { Id = -1 }, $"Contract failed ({why}): {c.Title} — penalty {Units.FormatMoney(penalty)}");
        CheckInsolvency();
    }

    /// <summary>NPC client stations/satellites are real craft on rails (GDD §11).</summary>
    void SpawnClientCraft(Contract c)
    {
        if (c.Type is not (ContractType.FuelDelivery or ContractType.Refuel or ContractType.Relocate or ContractType.Supply or ContractType.Retrieve)) return;
        if (c.Tutorial == 5) return;   // T5 recovers the player's own booster: no client craft
        var body = Sys[c.TargetBody];
        var d = new Design { Name = "Client station" };
        int core = d.Root("core-s");
        d.Stack(core, "port", below: false);
        int tank = d.Stack(core, c.Tonnes > 6 ? "tank-m-long" : c.Tonnes > 1.5 ? "tank-m-medium" : "tank-s-long");
        if (c.Type is ContractType.Supply) { d.Stack(tank, "bin-m"); }
        if (c.Type == ContractType.Retrieve && c.PayloadPart.Length > 0) { d.Stack(tank, c.PayloadPart); }
        d.Pair(tank, "solar", 0.3);
        string cname = c.Tutorial == 6 ? "Test Target" : c.Tutorial == 7 ? "Client Sat" : c.Type == ContractType.Retrieve ? $"{c.Client} derelict" : $"{c.Client} {(c.Type == ContractType.Refuel ? "satellite" : "station")}";
        var craft = Craft.FromDesign(d, cname);
        craft.Npc = true;
        foreach (var p in craft.Parts) p.ClientOf = c.Id;
        // Client tanks start empty (they want fuel), except the derelict which is a plain object.
        foreach (var p in craft.Parts) for (int i = 0; i < p.Res.Length; i++) p.Res[i] = 0;
        if (c.Type == ContractType.Refuel && c.Tonnes > 0) { var t = craft.Parts.First(p => p.Def.Has(PartFlags.Tank)); t.Set(Resource.Methalox, Math.Max(0, t.Capacity(Resource.Methalox) - c.Tonnes)); }
        double alt = c.Tutorial > 0 ? body.ActiveZoneHeight + 70_000 : c.Type == ContractType.Relocate ? Math.Max(body.ActiveZoneHeight + 30_000, c.OrbitMin - 120_000) : body.ActiveZoneHeight + 70_000 + Rng.Range(0, 60_000);
        double r0 = body.Radius + alt;
        double theta = Rng.Range(0, MathD.TwoPi);
        craft.BodyId = body.Id;
        craft.Pos = Vec2d.FromPolar(r0, theta);
        craft.Vel = -craft.Pos.Perp.Normalized() * Math.Sqrt(body.Gm / r0);
        craft.Angle = MathD.WrapAngle((-craft.Vel).Angle - Math.PI / 2);
        craft.Mode = CraftMode.Active;
        craft.UpdateMass();
        AddCraft(craft);
        craft.PutOnRails(Sys, T);
        c.ClientCraft = craft.Id;
    }

    /// <summary>Evaluate every accepted contract against the world (called each tick after physics and at day boundaries).</summary>
    public void CheckContracts()
    {
        foreach (var c in Contracts.ToList())   // completing a lesson offers the next one (adds to the list)
        {
            if (c.State != ContractState.Accepted) continue;
            if (T >= c.Deadline) { Fail(c, "deadline passed"); continue; }
            if (!c.WarnedDeadline && c.Deadline - T < Units.Day) { c.WarnedDeadline = true; StopWarpFor($"{c.Title} is due in less than a day"); }
            if (Evaluate(c)) Complete(c);
        }
    }

    void StopWarpFor(string why)
    {
        if (WarpIndex > 0 || PhysicsWarp > 1) { WarpIndex = 0; PhysicsWarp = 1; Events.Add(new SimEvent(SimEventKind.WarpStopped, Active?.Id ?? -1, why, T)); }
        Emit(SimEventKind.Info, Active ?? new Craft { Id = -1 }, why);
    }

    IEnumerable<Craft> PlayerCraft() => Crafts.Where(c => !c.Destroyed && !c.Npc && !c.Debris && c.Mode != CraftMode.Held);

    bool InWindow(Craft c, Contract k)
    {
        if (c.BodyId != k.TargetBody || c.Mode != CraftMode.Rails) return false;
        var body = Sys[c.BodyId]; var o = c.Rails.Conic;
        if (!o.IsEllipse) return false;
        double peri = o.Periapsis - body.Radius, apo = o.Apoapsis - body.Radius;
        return peri >= k.OrbitMin && apo <= k.OrbitMax;
    }

    bool Evaluate(Contract k)
    {
        var body = k.TargetBody >= 0 ? Sys[k.TargetBody] : null;
        switch (k.Tutorial)
        {
            case 1:
            {
                foreach (var c in PlayerCraft())
                {
                    if (c.BodyId != Sys.HomeId) continue;
                    double alt = c.StateAt(T).r.Length - Sys.Home.Radius;
                    if (alt >= 20_000) { c.ReachedAltitude = Math.Max(c.ReachedAltitude, alt); k.Flag1 = true; k.Progress = 0.5; }
                    if (c.ReachedAltitude >= 20_000 && c.Mode == CraftMode.Landed && c.HasControl) return true;
                }
                return false;
            }
            case 2:
                foreach (var c in PlayerCraft()) if (c.BodyId == Sys.HomeId && c.StateAt(T).r.Length - Sys.Home.Radius >= 50_000) return true;
                return false;
            case 3:
                foreach (var c in PlayerCraft()) if (c.BodyId == Sys.HomeId && c.Mode == CraftMode.Rails && c.Rails.Conic.IsEllipse && c.Rails.Conic.Periapsis >= Sys.Home.Radius + Sys.Home.AtmoHeight && c.HasControl) return true;
                return false;
            case 5:
                return k.Flag1;   // set by Recover() when a booster comes home to the pad zone
            case 6:
                return k.Flag1;   // set by Dock() with the client craft
            case 8:
                k.Progress = Math.Min(1, (DepotSalesTonnes - k.Baseline) / k.Tonnes);
                return DepotSalesTonnes - k.Baseline >= k.Tonnes - 1e-6;
            case 10:
            {
                if (body == null) return false;
                foreach (var c in PlayerCraft())
                {
                    if (c.BodyId != body.Id || c.Mode != CraftMode.Landed || c.Landed is not { } L) continue;
                    bool outpost = c.Parts.Any(p => !p.Destroyed && p.Def.Has(PartFlags.OutpostCore)) && c.Parts.Any(p => !p.Destroyed && p.Def.Has(PartFlags.Drill));
                    if (outpost && body.Deposits.Any(d => d.Kind == Resource.Ice && d.Contains(L.LocalAngle))) return true;
                }
                return false;
            }
        }
        switch (k.Type)
        {
            case ContractType.Deploy:
                foreach (var c in PlayerCraft()) if (c.Parts.Any(p => !p.Destroyed && p.Def.Id == k.PayloadPart && p.ContractId == k.Id) && InWindow(c, k)) return true;
                return false;
            case ContractType.Survey:
            {
                if (body == null) return false;
                if (k.Survey == SurveyKind.Scan) return body.Scanned;
                var e = Exploration.TryGetValue(body.Id, out var ex) ? ex : Explored.None;
                if (k.Survey == SurveyKind.Flyby) return e != Explored.None;
                if (k.Survey == SurveyKind.Orbit) return e.HasFlag(Explored.Orbit);
                return e.HasFlag(Explored.Landed);
            }
            case ContractType.FuelDelivery:
            case ContractType.Refuel:
            case ContractType.Supply:
            {
                double have = ClientAmount(k) - k.Baseline;
                k.Delivered = have; k.Progress = k.Tonnes > 0 ? Math.Clamp(have / k.Tonnes, 0, 1) : (k.Flag1 ? 1 : 0);
                if (k.Tonnes <= 0) return k.Flag1;
                return have >= k.Tonnes - 1e-6;
            }
            case ContractType.Relocate:
            {
                var client = FindClientPartsHolder(k);
                if (client == null || client.Destroyed) return false;
                bool alone = client.Parts.All(p => p.Destroyed || p.ClientOf == k.Id);
                return alone && client.Mode == CraftMode.Rails && InWindow(client, k);
            }
            case ContractType.Retrieve:
            {
                foreach (var c in PlayerCraft())
                    if (c.BodyId == Sys.HomeId && c.Mode == CraftMode.Landed && c.Parts.Any(p => !p.Destroyed && p.Def.Id == k.PayloadPart && p.ClientOf == k.Id)) return true;
                return false;
            }
            case ContractType.DebrisCleanup:
                k.Progress = k.Count > 0 ? Math.Min(1, k.Done / (double)k.Count) : 0;
                return k.Done >= k.Count;
        }
        return false;
    }

    /// <summary>The client's parts may have been docked into a player craft: find who holds them now.</summary>
    Craft? FindClientPartsHolder(Contract k)
    {
        foreach (var c in Crafts) if (!c.Destroyed && c.Parts.Any(p => p.ClientOf == k.Id)) return c;
        return null;
    }

    /// <summary>Resource currently inside the client's own parts, wherever they are docked.</summary>
    double ClientAmount(Contract k)
    {
        double have = 0;
        foreach (var c in Crafts) { if (c.Destroyed) continue; foreach (var p in c.Parts) if (!p.Destroyed && p.ClientOf == k.Id) have += p.Get(k.Resource); }
        return have;
    }

    /// <summary>A launched design carrying a client payload part is flying it for the matching accepted Deploy contract.</summary>
    void TagPayloads(Craft c)
    {
        var used = new HashSet<int>(Crafts.Where(x => !x.Destroyed).SelectMany(x => x.Parts).Where(p => p.ContractId >= 0).Select(p => p.ContractId));
        foreach (var p in c.Parts)
        {
            if (!p.Def.Has(PartFlags.ClientPayload)) continue;
            var k = AcceptedContracts.FirstOrDefault(k => k.Type == ContractType.Deploy && k.PayloadPart == p.Def.Id && !used.Contains(k.Id));
            if (k != null) { p.ContractId = k.Id; used.Add(k.Id); }
        }
    }

    // ---------------------------------------------------------------- hooks from the sim

    void OnDocked(Craft a, Craft other)
    {
        foreach (var k in AcceptedContracts)
        {
            if (k.ClientCraft == other.Id || k.ClientCraft == a.Id) { if (k.Tutorial == 6 || (k.Type == ContractType.Refuel && k.Tonnes <= 0)) k.Flag1 = true; }
        }
    }

    void OnRecovered(Craft c, double rate)
    {
        foreach (var k in AcceptedContracts)
            if (k.Tutorial == 5 && c.WasBooster && rate >= 0.85) k.Flag1 = true;
    }

    void OnDebrisDeorbited(Craft debris)
    {
        Count("debris_deorbited");
        foreach (var k in AcceptedContracts)
            if (k.Type == ContractType.DebrisCleanup && debris.BodyId == k.TargetBody) k.Done++;
    }

    /// <summary>Client payload parts available in the builder for accepted Deploy contracts (provided free).</summary>
    public IEnumerable<(Contract contract, PartDef part)> AvailablePayloads()
    {
        foreach (var k in AcceptedContracts)
            if (k.Type == ContractType.Deploy && !Crafts.Any(c => !c.Destroyed && c.Parts.Any(p => p.ContractId == k.Id)))
                yield return (k, Sim.Parts.Get(k.PayloadPart));
    }

    public Contract? FindContract(string spec)
    {
        if (spec.Length > 1 && (spec[0] == 'T' || spec[0] == 't') && int.TryParse(spec[1..], out int tn)) return Contracts.FirstOrDefault(c => c.Tutorial == tn && c.State == ContractState.Offered) ?? Contracts.FirstOrDefault(c => c.Tutorial == tn);
        if (int.TryParse(spec, out int id)) return Contracts.FirstOrDefault(c => c.Id == id);
        return Contracts.FirstOrDefault(c => c.Title.StartsWith(spec, StringComparison.OrdinalIgnoreCase));
    }
}
