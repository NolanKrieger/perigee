namespace Perigee.Sim;

public enum PartCategory { Core, Tank, Cargo, Engine, Rcs, Structure, Docking, Aero, Recovery, Power, Survey, Base }
public enum Diameter { S, M, L }

[Flags]
public enum PartFlags : long
{
    None = 0,
    ProbeCore = 1L << 0, Hardened = 1L << 1, DepotController = 1L << 2, DockingPort = 1L << 3, HeavyPort = 1L << 4, Claw = 1L << 5,
    Fin = 1L << 6, GridFin = 1L << 7, HeatShield = 1L << 8, Parachute = 1L << 9, Drogue = 1L << 10, Legs = 1L << 11,
    Solar = 1L << 12, Battery = 1L << 13, Rtg = 1L << 14, Scanner = 1L << 15, Drill = 1L << 16, Refinery = 1L << 17, Scoop = 1L << 18,
    OutpostCore = 1L << 19, Storage = 1L << 20, PadKit = 1L << 21, Fabricator = 1L << 22, Decoupler = 1L << 23, RadialDecoupler = 1L << 24,
    Fairing = 1L << 25, NoseCone = 1L << 26, Truss = 1L << 27, Adapter = 1L << 28, CargoBin = 1L << 29, Engine = 1L << 30, Rcs = 1L << 31,
    Tank = 1L << 32, FuelBlocker = 1L << 33, Sensor = 1L << 34, ClientPayload = 1L << 35,
}

public sealed class EngineDef
{
    public double ThrustVac, ThrustSl;   // N
    public double IspVac, IspSl;         // s
    public double MinThrottle = 1.0;     // 1 = fixed full; 0.2 = deep throttle
    public bool Restartable = true;
    public Resource Fuel = Resource.Methalox;
    public double PowerKw;               // ion drive
    public double Gimbal = 0.06;         // rad, steering authority
    public double Thrust(double pressureRatio) => ThrustVac + (ThrustSl - ThrustVac) * pressureRatio;
    public double Isp(double pressureRatio) => IspVac + (IspSl - IspVac) * pressureRatio;
}

/// <summary>One polygon of part art in part-local metres (y up), with a flat fill.</summary>
public sealed record ArtPoly(Vec2d[] Points, float R, float G, float B);

/// <summary>Static description of a part (GDD §5). Everything a part has: mass, cost, R&D price, drag profile, max temperature, impact tolerance and polygon art.</summary>
public sealed class PartDef
{
    public string Id = "", Name = "", Description = "";
    public PartCategory Cat;
    public Diameter Dia = Diameter.S;
    public double Width, Height;           // m: bounding size (x across, y along the rocket axis)
    public double DryMass;                 // t
    public long Cost;                      // cents, unit price
    public double RdMultiplier = 10;       // R&D unlock = RdMultiplier × Cost (GDD: 8–15×)
    public bool Starter;                   // pre-unlocked (GDD §10)
    public Dictionary<Resource, double> Capacity = new();   // t
    public EngineDef? Engine;
    public double RcsThrust;               // N per block
    public double Cd = 0.6;                // drag coefficient when exposed
    public double MaxTemp = 1250;          // K
    public double ImpactTolerance = 8;     // m/s
    public double Torque;                  // N·m reaction wheel
    public double PowerOutKw;              // solar (at 1 AU-equivalent) or RTG
    public double BatteryKwh;
    public double ChuteArea, ChuteMaxSpeed;    // m² effective Cd·A, m/s max safe deploy
    public double Ablator;                 // t of ablator on heat shields
    public double FinArea;                 // m² lift surface (fins, grid fins)
    public double DrillRate, RefineRate;   // t/day (drill: per unit richness)
    public double ScoopRate;               // t/s at the reference density and speed (Mining.ScoopReference*)
    public double PowerUseKw;              // refineries
    public double StorageT;                // surface storage capacity
    public bool TopNode = true, BottomNode = true;   // stack nodes
    public bool RadialMount;               // this part attaches to the side of another
    public bool SideNodes = true;          // other parts may attach to its sides
    public PartFlags Flags;
    public ArtPoly[] Art = Array.Empty<ArtPoly>();
    public Vec2d[] Outline = Array.Empty<Vec2d>();   // collision/drag silhouette, part-local metres

    public bool Has(PartFlags f) => (Flags & f) != 0;
    public double FuelCapacity => Capacity.TryGetValue(Resource.Methalox, out var v) ? v : 0;
    public long RdPrice => (long)(Cost * RdMultiplier);
    public static double DiaWidth(Diameter d) => d switch { Diameter.S => 1.0, Diameter.M => 2.0, _ => 3.5 };
    public override string ToString() => Id;
}

/// <summary>The fixed part catalogue (GDD §5, ~60 parts). Prices in cents follow the §10 anchor prices. Names are the GDD's placeholders.</summary>
public static class Parts
{
    static readonly Dictionary<string, PartDef> _byId = new();
    public static IReadOnlyList<PartDef> All { get; }

    public static PartDef Get(string id) => _byId.TryGetValue(id, out var p) ? p : throw new KeyNotFoundException($"no part '{id}'");
    public static bool TryGet(string id, out PartDef def) => _byId.TryGetValue(id, out def!);

    static long K(double thousands) => (long)Math.Round(thousands * 100_000);   // $k → cents

    static PartDef Add(PartDef p)
    {
        p.Outline = PartArt.Outline(p);
        p.Art = PartArt.Build(p);
        _byId[p.Id] = p;
        return p;
    }

    static Parts()
    {
        // ---- Probe cores
        Add(new PartDef { Id = "core-s", Name = "Core S", Cat = PartCategory.Core, Dia = Diameter.S, Width = 1.0, Height = 0.6, DryMass = 0.15, Cost = K(6), Starter = true,
            Torque = 6_000, BatteryKwh = 2, MaxTemp = 1300, ImpactTolerance = 8, Flags = PartFlags.ProbeCore | PartFlags.Battery, Description = "Small reaction wheel and battery. Every craft needs a core to be controlled." });
        Add(new PartDef { Id = "core-m", Name = "Core M", Cat = PartCategory.Core, Dia = Diameter.M, Width = 2.0, Height = 0.8, DryMass = 0.5, Cost = K(18), Torque = 45_000, BatteryKwh = 8, Flags = PartFlags.ProbeCore | PartFlags.Battery, Description = "Strong reaction wheel for big stacks." });
        Add(new PartDef { Id = "core-hard", Name = "Hardened Core", Cat = PartCategory.Core, Dia = Diameter.S, Width = 1.0, Height = 0.7, DryMass = 0.25, Cost = K(22), Torque = 6_000, BatteryKwh = 3, Flags = PartFlags.ProbeCore | PartFlags.Hardened | PartFlags.Battery, Description = "Storm-proof electronics. Immune to solar storms." });
        Add(new PartDef { Id = "depot-ctrl", Name = "Depot Controller", Cat = PartCategory.Core, Dia = Diameter.M, Width = 2.0, Height = 1.0, DryMass = 0.8, Cost = K(30), Torque = 20_000, BatteryKwh = 10, Flags = PartFlags.ProbeCore | PartFlags.DepotController | PartFlags.Battery, Description = "Turns a craft into a depot: sells to the local market, stops boil-off. $1k/day upkeep." });
        Add(new PartDef { Id = "depot-ctrl-hard", Name = "Hardened Depot Controller", Cat = PartCategory.Core, Dia = Diameter.M, Width = 2.0, Height = 1.0, DryMass = 1.0, Cost = K(90), Torque = 20_000, BatteryKwh = 10, Flags = PartFlags.ProbeCore | PartFlags.DepotController | PartFlags.Hardened | PartFlags.Battery, Description = "Depot controller that shrugs off solar storms." });

        // ---- Methalox tanks (dry mass 10% of fuel)
        Tank("tank-s-short", "Tank S short", Diameter.S, 1.2, 0.6, K(2), starter: true);
        Tank("tank-s-long", "Tank S long", Diameter.S, 3.0, 1.5, K(4.5), starter: true);
        Tank("tank-m-short", "Tank M short", Diameter.M, 1.5, 3, K(9));
        Tank("tank-m-medium", "Tank M medium", Diameter.M, 3.0, 6, K(16));
        Tank("tank-m-long", "Tank M long", Diameter.M, 6.0, 12, K(30));
        Tank("tank-l-medium", "Tank L medium", Diameter.L, 4.0, 25, K(60));
        Tank("tank-l-long", "Tank L long", Diameter.L, 8.0, 50, K(110));
        Tank("tank-l-jumbo", "Tank L jumbo", Diameter.L, 16.0, 100, K(200));
        // ---- Other tanks
        Add(new PartDef { Id = "rcs-tank-s", Name = "RCS tank S", Cat = PartCategory.Tank, Dia = Diameter.S, Width = 1.0, Height = 0.5, DryMass = 0.04, Cost = K(3), Capacity = { [Resource.Rcs] = 0.3 }, Flags = PartFlags.Tank, Description = "Inline peroxide tank for RCS thrusters." });
        Add(new PartDef { Id = "rcs-tank-radial", Name = "RCS tank (radial)", Cat = PartCategory.Tank, Width = 0.4, Height = 0.8, DryMass = 0.03, Cost = K(2.5), Capacity = { [Resource.Rcs] = 0.15 }, RadialMount = true, TopNode = false, BottomNode = false, SideNodes = false, Flags = PartFlags.Tank });
        Add(new PartDef { Id = "xenon-tank-s", Name = "Xenon tank S", Cat = PartCategory.Tank, Dia = Diameter.S, Width = 1.0, Height = 0.6, DryMass = 0.08, Cost = K(8), Capacity = { [Resource.Xenon] = 0.4 }, Flags = PartFlags.Tank });
        Add(new PartDef { Id = "xenon-tank-radial", Name = "Xenon tank (radial)", Cat = PartCategory.Tank, Width = 0.4, Height = 0.8, DryMass = 0.04, Cost = K(5), Capacity = { [Resource.Xenon] = 0.15 }, RadialMount = true, TopNode = false, BottomNode = false, SideNodes = false, Flags = PartFlags.Tank });

        // ---- Cargo
        Add(new PartDef { Id = "bin-s", Name = "Resource bin S", Cat = PartCategory.Cargo, Dia = Diameter.S, Width = 1.0, Height = 1.2, DryMass = 0.12, Cost = K(4), Capacity = { [Resource.Ice] = 1.0, [Resource.Ore] = 1.0, [Resource.Co2] = 1.0, [Resource.Water] = 1.0, [Resource.Metal] = 1.0 }, Flags = PartFlags.CargoBin, Description = "Holds 1 t of ice, ore, CO₂, water or metal." });
        Add(new PartDef { Id = "bin-m", Name = "Resource bin M", Cat = PartCategory.Cargo, Dia = Diameter.M, Width = 2.0, Height = 2.0, DryMass = 0.5, Cost = K(12), Capacity = { [Resource.Ice] = 6, [Resource.Ore] = 6, [Resource.Co2] = 6, [Resource.Water] = 6, [Resource.Metal] = 6 }, Flags = PartFlags.CargoBin, Description = "Holds 6 t of raw resources or products." });
        Fairing("fairing-s", "Fairing S", Diameter.S, 2.0, 0.08, K(3));
        Fairing("fairing-m", "Fairing M", Diameter.M, 3.5, 0.25, K(7));
        Fairing("fairing-l", "Fairing L", Diameter.L, 6.0, 0.7, K(16));

        // ---- Engines (GDD §5 Isp values)
        Engine("eng-kestrel", "Kestrel", Diameter.S, 0.45, K(12), thrustVac: 76_000, thrustSl: 66_000, ispVac: 320, ispSl: 290, minThrottle: 0.4, starter: true, desc: "S sea-level engine. Throttles to 40%.");
        Engine("eng-sparrow", "Sparrow", Diameter.S, 0.22, K(24), thrustVac: 22_000, thrustSl: 6_000, ispVac: 345, ispSl: 120, minThrottle: 0.2, starter: true, desc: "S vacuum engine. Poor in air.");
        Engine("eng-condor", "Condor", Diameter.M, 2.2, K(55), thrustVac: 420_000, thrustSl: 380_000, ispVac: 330, ispSl: 300, minThrottle: 0.15, desc: "M sea-level engine with deep throttle and unlimited restarts: the reusable booster engine.");
        Engine("eng-heron", "Heron", Diameter.M, 1.0, K(90), thrustVac: 130_000, thrustSl: 30_000, ispVac: 355, ispSl: 110, minThrottle: 0.2, desc: "M vacuum engine.");
        Engine("eng-titan", "Titan", Diameter.L, 7.5, K(180), thrustVac: 1_700_000, thrustSl: 1_550_000, ispVac: 325, ispSl: 295, minThrottle: 0.5, desc: "L heavy lifter engine.");
        Engine("eng-lander", "Lander", Diameter.S, 0.18, K(32), thrustVac: 12_000, thrustSl: 3_000, ispVac: 320, ispSl: 100, minThrottle: 0.05, desc: "S deep-throttle engine for soft landings.");
        var ion = Engine("eng-ion", "Ion drive", Diameter.S, 0.25, K(45), thrustVac: 60, thrustSl: 0, ispVac: 4000, ispSl: 4000, minThrottle: 0.0, desc: "Xenon + power. Tiny thrust, enormous efficiency.");
        ion.Engine!.Fuel = Resource.Xenon; ion.Engine.PowerKw = 6; ion.Engine.Gimbal = 0;

        // ---- RCS (HTP, Isp 160)
        Add(new PartDef { Id = "rcs-block", Name = "RCS block", Cat = PartCategory.Rcs, Width = 0.25, Height = 0.25, DryMass = 0.02, Cost = K(1.5), RcsThrust = 400, RadialMount = true, TopNode = false, BottomNode = false, SideNodes = false, Flags = PartFlags.Rcs, Description = "Two-way thruster block. Needs RCS propellant." });
        Add(new PartDef { Id = "rcs-linear", Name = "Linear RCS thruster", Cat = PartCategory.Rcs, Width = 0.2, Height = 0.4, DryMass = 0.015, Cost = K(1.2), RcsThrust = 600, RadialMount = true, TopNode = false, BottomNode = false, SideNodes = false, Flags = PartFlags.Rcs });

        // ---- Structure
        Decoupler("decoupler-s", "Decoupler S", Diameter.S, 0.05, K(1.5), starter: true);
        Decoupler("decoupler-m", "Decoupler M", Diameter.M, 0.15, K(3.5));
        Decoupler("decoupler-l", "Decoupler L", Diameter.L, 0.4, K(8));
        Add(new PartDef { Id = "decoupler-radial", Name = "Radial decoupler", Cat = PartCategory.Structure, Width = 0.3, Height = 0.6, DryMass = 0.04, Cost = K(2), RadialMount = true, TopNode = false, BottomNode = false, SideNodes = true, Flags = PartFlags.RadialDecoupler | PartFlags.FuelBlocker, Description = "Holds a side booster and throws it clear when staged." });
        Add(new PartDef { Id = "adapter-sm", Name = "Adapter S–M", Cat = PartCategory.Structure, Dia = Diameter.M, Width = 2.0, Height = 0.8, DryMass = 0.12, Cost = K(3), Flags = PartFlags.Adapter, Description = "Narrows a 2 m stack to 1 m." });
        Add(new PartDef { Id = "adapter-ml", Name = "Adapter M–L", Cat = PartCategory.Structure, Dia = Diameter.L, Width = 3.5, Height = 1.2, DryMass = 0.35, Cost = K(6), Flags = PartFlags.Adapter });
        NoseCone("nose-s", "Nose cone S", Diameter.S, 1.2, 0.05, K(1), starter: true);
        NoseCone("nose-m", "Nose cone M", Diameter.M, 2.2, 0.15, K(2.5));
        NoseCone("nose-l", "Nose cone L", Diameter.L, 3.5, 0.4, K(5));
        Add(new PartDef { Id = "truss", Name = "Truss", Cat = PartCategory.Structure, Dia = Diameter.S, Width = 1.0, Height = 2.0, DryMass = 0.08, Cost = K(2), Cd = 0.3, Flags = PartFlags.Truss, Description = "Light open structure." });

        // ---- Docking
        Add(new PartDef { Id = "port", Name = "Docking port", Cat = PartCategory.Docking, Dia = Diameter.S, Width = 1.0, Height = 0.3, DryMass = 0.06, Cost = K(4), Flags = PartFlags.DockingPort, Description = "Standard port: captures within 0.5 m, 5°, 0.5 m/s. Pumps 0.5 t/s." });
        Add(new PartDef { Id = "port-heavy", Name = "Heavy port", Cat = PartCategory.Docking, Dia = Diameter.M, Width = 2.0, Height = 0.4, DryMass = 0.25, Cost = K(12), Flags = PartFlags.DockingPort | PartFlags.HeavyPort, Description = "Larger port. Pumps 2 t/s." });
        Add(new PartDef { Id = "claw", Name = "Claw", Cat = PartCategory.Docking, Dia = Diameter.S, Width = 1.0, Height = 0.7, DryMass = 0.15, Cost = K(15), Flags = PartFlags.Claw, Description = "Grabs anything, including debris. Can't pump fuel." });

        // ---- Aero & recovery
        Add(new PartDef { Id = "fin", Name = "Fin", Cat = PartCategory.Aero, Width = 0.6, Height = 1.2, DryMass = 0.03, Cost = K(0.8), Starter = true, FinArea = 0.5, Cd = 0.05, RadialMount = true, TopNode = false, BottomNode = false, SideNodes = false, Flags = PartFlags.Fin, Description = "Adds aerodynamic stability. Mount low on the rocket." });
        Add(new PartDef { Id = "grid-fin", Name = "Grid fin", Cat = PartCategory.Aero, Width = 0.5, Height = 0.9, DryMass = 0.06, Cost = K(4), FinArea = 0.9, Cd = 0.08, RadialMount = true, TopNode = false, BottomNode = false, SideNodes = false, MaxTemp = 1500, Flags = PartFlags.GridFin, Description = "Deployable steering fin for booster return." });
        HeatShield("shield-s", "Heat shield S", Diameter.S, 0.12, 0.1, K(3));
        HeatShield("shield-m", "Heat shield M", Diameter.M, 0.45, 0.35, K(8));
        HeatShield("shield-l", "Heat shield L", Diameter.L, 1.2, 1.0, K(18));
        Add(new PartDef { Id = "chute-nose", Name = "Parachute (nose)", Cat = PartCategory.Recovery, Dia = Diameter.S, Width = 1.0, Height = 0.5, DryMass = 0.05, Cost = K(1.5), Starter = true, ChuteArea = 110, ChuteMaxSpeed = 140, BottomNode = true, TopNode = false, Cd = 0.3, Flags = PartFlags.Parachute, Description = "Main chute in a conical nose cap. Safe to open below 140 m/s." });
        Add(new PartDef { Id = "chute-radial", Name = "Parachute (radial)", Cat = PartCategory.Recovery, Width = 0.4, Height = 0.6, DryMass = 0.05, Cost = K(1.8), ChuteArea = 90, ChuteMaxSpeed = 140, RadialMount = true, TopNode = false, BottomNode = false, SideNodes = false, Flags = PartFlags.Parachute, Description = "Radial main chute. Safe to open below 140 m/s." });
        Add(new PartDef { Id = "drogue", Name = "Drogue chute", Cat = PartCategory.Recovery, Width = 0.35, Height = 0.5, DryMass = 0.03, Cost = K(1.2), ChuteArea = 6, ChuteMaxSpeed = 400, RadialMount = true, TopNode = false, BottomNode = false, SideNodes = false, MaxTemp = 1500, Flags = PartFlags.Parachute | PartFlags.Drogue, Description = "Small chute that opens safely at 400 m/s to slow you before the mains." });
        Legs("legs-s", "Landing legs S", Diameter.S, 0.08, K(1.5), 14, starter: true);
        Legs("legs-m", "Landing legs M", Diameter.M, 0.3, K(4), 14);
        Legs("legs-l", "Landing legs L", Diameter.L, 0.8, K(10), 14);

        // ---- Power
        Add(new PartDef { Id = "solar", Name = "Solar panel", Cat = PartCategory.Power, Width = 0.3, Height = 1.0, DryMass = 0.04, Cost = K(3), PowerOutKw = 2.5, RadialMount = true, TopNode = false, BottomNode = false, SideNodes = false, Flags = PartFlags.Solar, Description = "Folding panel. Output falls with the square of the distance from the star." });
        Add(new PartDef { Id = "battery", Name = "Battery", Cat = PartCategory.Power, Width = 0.4, Height = 0.6, DryMass = 0.1, Cost = K(2.5), BatteryKwh = 20, RadialMount = true, TopNode = false, BottomNode = false, SideNodes = false, Flags = PartFlags.Battery });
        Add(new PartDef { Id = "rtg", Name = "RTG", Cat = PartCategory.Power, Width = 0.5, Height = 0.9, DryMass = 0.3, Cost = K(60), PowerOutKw = 1.5, RadialMount = true, TopNode = false, BottomNode = false, SideNodes = false, Flags = PartFlags.Rtg, Description = "Works in shadow and the outer system. Expensive." });

        // ---- Survey & mining
        Add(new PartDef { Id = "scanner", Name = "Resource scanner", Cat = PartCategory.Survey, Width = 0.4, Height = 0.6, DryMass = 0.08, Cost = K(20), RadialMount = true, TopNode = false, BottomNode = false, SideNodes = false, Flags = PartFlags.Scanner, Description = "One full orbit maps a body's deposits." });
        Add(new PartDef { Id = "drill", Name = "Drill", Cat = PartCategory.Survey, Width = 0.6, Height = 1.4, DryMass = 0.4, Cost = K(35), DrillRate = 1.0, RadialMount = true, TopNode = false, BottomNode = false, SideNodes = false, Flags = PartFlags.Drill, Description = "Extracts the deposit underneath it, 0.5–4 t/day by richness. Needs 3 kW." });
        Add(new PartDef { Id = "refinery-s", Name = "Refinery S", Cat = PartCategory.Survey, Dia = Diameter.S, Width = 1.0, Height = 1.5, DryMass = 0.8, Cost = K(60), RefineRate = 2.0, PowerUseKw = 20, Flags = PartFlags.Refinery, Description = "Refines ice→water, water+CO₂→methalox, water→RCS propellant, ore→metal. Power-hungry." });
        Add(new PartDef { Id = "refinery-l", Name = "Refinery L", Cat = PartCategory.Survey, Dia = Diameter.M, Width = 2.0, Height = 2.5, DryMass = 3.0, Cost = K(220), RefineRate = 10.0, PowerUseKw = 80, Flags = PartFlags.Refinery, Description = "Refines 10 t/day. Needs 80 kW." });
        Add(new PartDef { Id = "scoop", Name = "Atmospheric scoop", Cat = PartCategory.Survey, Dia = Diameter.S, Width = 1.0, Height = 1.0, DryMass = 0.3, Cost = K(40), ScoopRate = 0.001, TopNode = false, Cd = 1.2, MaxTemp = 1800, Flags = PartFlags.Scoop, Description = "Fly it through the top of a gas giant's atmosphere to collect xenon (or a CO₂ atmosphere for CO₂): about 0.1 t per pass, more when deeper and faster, but hotter." });

        // ---- Client payloads (contracts provide them free; never sold)
        Add(new PartDef { Id = "payload-s", Name = "Client relay", Cat = PartCategory.Cargo, Dia = Diameter.S, Width = 1.0, Height = 1.0, DryMass = 0.5, Cost = 0, Flags = PartFlags.CargoBin | PartFlags.ClientPayload, Description = "A client's 0.5 t relay satellite. Provided free with a Deploy contract." });
        Add(new PartDef { Id = "payload-m", Name = "Client platform", Cat = PartCategory.Cargo, Dia = Diameter.M, Width = 2.0, Height = 1.4, DryMass = 2.0, Cost = 0, Flags = PartFlags.CargoBin | PartFlags.ClientPayload, Description = "A client's 2 t survey platform. Provided free with a Deploy contract." });

        // ---- Base
        Add(new PartDef { Id = "outpost-core", Name = "Outpost Core", Cat = PartCategory.Base, Dia = Diameter.M, Width = 2.0, Height = 1.6, DryMass = 2.0, Cost = K(60), Torque = 10_000, BatteryKwh = 40, Flags = PartFlags.OutpostCore | PartFlags.ProbeCore | PartFlags.Battery, Description = "Land it on flat ground and it becomes a permanent base. $3k/day upkeep." });
        Add(new PartDef { Id = "outpost-core-hard", Name = "Hardened Outpost Core", Cat = PartCategory.Base, Dia = Diameter.M, Width = 2.0, Height = 1.6, DryMass = 2.4, Cost = K(180), Torque = 10_000, BatteryKwh = 40, Flags = PartFlags.OutpostCore | PartFlags.ProbeCore | PartFlags.Hardened | PartFlags.Battery });
        Add(new PartDef { Id = "surface-tank", Name = "Surface storage tank", Cat = PartCategory.Base, Dia = Diameter.M, Width = 2.0, Height = 2.0, DryMass = 1.0, Cost = K(25), StorageT = 40, Flags = PartFlags.Storage, Capacity = { [Resource.Methalox] = 40, [Resource.Rcs] = 40, [Resource.Water] = 40, [Resource.Ice] = 40, [Resource.Co2] = 40, [Resource.Ore] = 40, [Resource.Metal] = 40, [Resource.Xenon] = 40 }, Description = "40 t of outpost storage for any resource (shared)." });
        Add(new PartDef { Id = "pad-kit", Name = "Pad Kit", Cat = PartCategory.Base, Dia = Diameter.M, Width = 2.0, Height = 1.2, DryMass = 4.0, Cost = K(400), Flags = PartFlags.PadKit, Description = "Off-world launch pad. With a Fabricator next to an Outpost Core you can build and launch rockets there. $8k/day upkeep." });
        Add(new PartDef { Id = "fabricator", Name = "Fabricator", Cat = PartCategory.Base, Dia = Diameter.M, Width = 2.0, Height = 1.8, DryMass = 3.5, Cost = K(300), Flags = PartFlags.Fabricator, Description = "Builds parts from metal (1.5× part mass) plus 50% of the cash price." });

        All = _byId.Values.ToList();
    }

    static void Tank(string id, string name, Diameter d, double height, double fuel, long cost, bool starter = false)
    {
        Add(new PartDef { Id = id, Name = name, Cat = PartCategory.Tank, Dia = d, Width = PartDef.DiaWidth(d), Height = height, DryMass = fuel * 0.1, Cost = cost, Starter = starter,
            Capacity = { [Resource.Methalox] = fuel }, Flags = PartFlags.Tank, Description = $"Holds {fuel} t of methalox." });
    }

    static PartDef Engine(string id, string name, Diameter d, double mass, long cost, double thrustVac, double thrustSl, double ispVac, double ispSl, double minThrottle, bool starter = false, string desc = "")
    {
        double w = PartDef.DiaWidth(d);
        return Add(new PartDef { Id = id, Name = name, Cat = PartCategory.Engine, Dia = d, Width = w * 0.9, Height = w * 0.7, DryMass = mass, Cost = cost, Starter = starter,
            Engine = new EngineDef { ThrustVac = thrustVac, ThrustSl = thrustSl, IspVac = ispVac, IspSl = ispSl, MinThrottle = minThrottle },
            MaxTemp = 1800, ImpactTolerance = 7, Flags = PartFlags.Engine, Description = desc });
    }

    static void Decoupler(string id, string name, Diameter d, double mass, long cost, bool starter = false)
    {
        Add(new PartDef { Id = id, Name = name, Cat = PartCategory.Structure, Dia = d, Width = PartDef.DiaWidth(d), Height = 0.25, DryMass = mass, Cost = cost, Starter = starter,
            Flags = PartFlags.Decoupler | PartFlags.FuelBlocker, Description = "Separates the stack below it when staged. Fuel does not flow through it." });
    }

    static void NoseCone(string id, string name, Diameter d, double height, double mass, long cost, bool starter = false)
    {
        Add(new PartDef { Id = id, Name = name, Cat = PartCategory.Structure, Dia = d, Width = PartDef.DiaWidth(d), Height = height, DryMass = mass, Cost = cost, Starter = starter,
            Cd = 0.25, TopNode = false, MaxTemp = 1400, Flags = PartFlags.NoseCone, Description = "Cuts drag. Parts behind it are shielded from the airflow." });
    }

    static void Fairing(string id, string name, Diameter d, double height, double mass, long cost)
    {
        Add(new PartDef { Id = id, Name = name, Cat = PartCategory.Cargo, Dia = d, Width = PartDef.DiaWidth(d) * 1.15, Height = height, DryMass = mass, Cost = cost,
            Cd = 0.3, TopNode = false, MaxTemp = 1400, Flags = PartFlags.Fairing | PartFlags.NoseCone, Description = "Shields a payload from drag and heat; jettison it when staged." });
    }

    static void HeatShield(string id, string name, Diameter d, double mass, double ablator, long cost)
    {
        Add(new PartDef { Id = id, Name = name, Cat = PartCategory.Aero, Dia = d, Width = PartDef.DiaWidth(d) * 1.05, Height = 0.3, DryMass = mass, Cost = cost, Ablator = ablator,
            Cd = 1.1, MaxTemp = 3200, ImpactTolerance = 10, Flags = PartFlags.HeatShield, Description = "Takes reentry heat; the ablator burns away to keep it cool." });
    }

    static void Legs(string id, string name, Diameter d, double mass, long cost, double tolerance, bool starter = false)
    {
        Add(new PartDef { Id = id, Name = name, Cat = PartCategory.Recovery, Dia = d, Width = PartDef.DiaWidth(d) * 1.4, Height = 1.9 * PartDef.DiaWidth(d), DryMass = mass, Cost = cost, Starter = starter,
            ImpactTolerance = tolerance, Cd = 0.4, RadialMount = true, TopNode = false, BottomNode = false, SideNodes = false, Flags = PartFlags.Legs, Description = $"Landing legs. Survive touchdown up to {tolerance} m/s." });
    }
}
