namespace Perigee.Sim;

public enum CraftMode { Rails, Active, Landed, Held }

/// <summary>A placed part at runtime: definition, position in the craft frame, attachment, resources and condition.</summary>
public sealed class Part
{
    public int Id;
    public PartDef Def = null!;
    public Vec2d Pos;              // craft frame (x right, y up = nose), metres
    public bool Flip;              // radial part on the left side (art mirrored)
    public int Parent = -1;        // part Id
    public AttachKind Attach;
    public double[] Res = new double[8];   // tonnes by Resource
    public double Temp = 290;      // K
    public double Ablator;         // t left on heat shields
    public bool Deployed;          // chute / legs / grid fins / solar
    public bool Fired;             // decoupler fired, fairing jettisoned
    public bool Running;           // engine ignited
    public bool Flameout;          // engine running with no fuel this tick
    public bool Destroyed;
    public bool Offline;           // fried by a storm (M11)
    public double OfflineUntil = double.NaN;   // game time when it reboots
    public int ContractId = -1;    // a client payload provided for this contract
    public int ClientOf = -1;      // belongs to this contract's client craft
    public double ChuteRamp;       // 0..1 canopy opening
    public double HeatFlux;        // W/m² received this tick (reentry glow)
    public double ExposedArea;     // m² facing the airflow this tick
    public double LastThrust;      // N this tick (plumes)
    public Recipe Recipe;          // refinery setting (M9)

    public double Get(Resource r) => Res[(int)r];
    /// <summary>Space left for a resource: dedicated capacity, or a surface tank's shared tonnage.</summary>
    public double Room(Resource r) => Def.StorageT > 0 ? Math.Max(0, Def.StorageT - ResourceMass) : Math.Max(0, Capacity(r) - Get(r));
    public bool CanHold(Resource r) => Def.StorageT > 0 || Capacity(r) > 0;
    public void Set(Resource r, double t) => Res[(int)r] = Math.Max(0, t);
    public double Capacity(Resource r) => Def.Capacity.TryGetValue(r, out var c) ? c : 0;
    public double ResourceMass { get { double s = 0; for (int i = 0; i < Res.Length; i++) s += Res[i]; return s; } }
    /// <summary>Mass in tonnes.</summary>
    public double Mass => Def.DryMass + ResourceMass + Ablator;
    public bool IsEngine => Def.Engine != null;

    /// <summary>Outline vertices in the craft frame (flip applied).</summary>
    public IEnumerable<Vec2d> LocalOutline()
    {
        foreach (var v in Def.Outline) yield return Pos + new Vec2d(Flip ? -v.X : v.X, v.Y);
    }
}

public struct LandedState
{
    public double LocalAngle;    // body-fixed angle of the craft's centre of mass
    public double Radius;        // distance of the centre of mass from the body centre
    public double AngleOffset;   // craft Angle = LocalAngle + surface rotation + AngleOffset
    public bool Water;
    public bool OnDroneShip;
}

/// <summary>Δv readout for one stage (GDD §5 live stats): the same fuel-feed and mass model the sim burns with.</summary>
public sealed record StageInfo(int Stage, double StartMass, double EndMass, double DeltaVVac, double DeltaVSl, double BurnTime, double TwrSurface, double ThrustVac, double Fuel);

/// <summary>
/// One vehicle, piece of debris or NPC station. Position/velocity are of the centre of mass, relative to BodyId in that body's
/// inertial frame. Parts live in the craft frame; the craft rotates by Angle (nose = +y local = world angle Angle + π/2).
/// </summary>
public sealed class Craft
{
    public int Id;
    public string Name = "";
    public string DesignName = "";
    public CraftMode Mode = CraftMode.Rails;
    public int BodyId;
    public RailsState Rails;
    public Vec2d Pos, Vel;
    public double Angle, AngVel;
    public bool Destroyed;
    public bool Npc;
    public bool Debris;
    public bool ContractPayload => Parts.Any(p => !p.Destroyed && p.ContractId >= 0);   // carries an accepted contract's payload
    public bool WasBooster;           // came home through a flashback (T5)
    public bool TouchedByPlayer;      // debris the player grabbed/pushed (cleanup contracts)
    public double ReachedAltitude;
    public double Charge;             // battery state, kWh (M9)
    public double Scooped;            // tonnes collected by scoops (M9)
    public long LaunchCostPaid, SalesTotal;
    public List<Part> Parts = new();
    public List<List<int>> Stages = new();   // part ids
    public int NextStage;
    public int RootId;
    public double Throttle;
    public bool RcsOn, LegsDown;
    public double Mass, DryMass, Moi;        // t, t, t·m²
    public Vec2d Com;                        // craft frame
    public LandedState? Landed;
    public double CoastTimer;
    /// <summary>Integrated thrust acceleration (m/s): the Δv actually spent, for the readout-vs-burn gate.</summary>
    public double DeltaVSpent;
    public int TargetCraft = -1, TargetBody = -1;
    public Capture? Capture;
    public List<PumpJob> Pumps = new();
    public double PumpedTotal;
    /// <summary>Names of craft docked at a port id, so undocking gives them back.</summary>
    public Dictionary<int, string> DockedNames = new();
    public string DockedName = "";
    public double LaunchTime = double.NaN;
    public int LaunchPadBody = -1;
    internal int NextPartId;

    // Rails bookkeeping: the next known event and how far ahead we have searched.
    internal Patch? Next;
    internal double SearchedUntil = double.NegativeInfinity;
    internal double ZoneAfter = double.NegativeInfinity;

    double _stateT = double.NaN; object? _stateRails; Vec2d _stateR, _stateV;
    public (Vec2d r, Vec2d v) StateAt(double t)
    {
        if (Mode != CraftMode.Rails) return (Pos, Vel);
        if (t == _stateT && ReferenceEquals(_stateRails, Rails)) return (_stateR, _stateV);   // one Kepler solve per craft per instant
        var s = Rails.StateAt(t);
        _stateT = t; _stateRails = Rails; _stateR = s.r; _stateV = s.v;
        return s;
    }
    public Vec2d NoseDir => Vec2d.FromAngle(Angle + Math.PI / 2);
    public Vec2d RightDir => Vec2d.FromAngle(Angle);
    public bool HasControl => FlashbackControl || Parts.Any(p => !p.Destroyed && !p.Offline && p.Def.Has(PartFlags.ProbeCore));
    /// <summary>A core is aboard but knocked out by a storm (GDD §14): the craft rides the rails until it reboots.</summary>
    public bool Fried => !FlashbackControl && Parts.Any(p => !p.Destroyed && p.Offline && p.Def.Has(PartFlags.ProbeCore)) && !HasControl;
    /// <summary>The player is flying this core-less booster home in a flashback.</summary>
    public bool FlashbackControl;
    public double HeldTime;
    public double OrbitEntryTime = double.NaN;
    /// <summary>A core is aboard (working or knocked out by a storm). Without one the object is debris.</summary>
    public bool HasCore => FlashbackControl || Parts.Any(p => !p.Destroyed && p.Def.Has(PartFlags.ProbeCore));
    public bool IsDebris => Debris || !HasCore;
    public IEnumerable<Part> Alive => Parts.Where(p => !p.Destroyed);
    public Part? Find(int id) { foreach (var p in Parts) if (p.Id == id) return p; return null; }
    public Part? Root => Find(RootId);

    /// <summary>Craft-frame point → position relative to the body (world orientation).</summary>
    public Vec2d LocalToWorld(Vec2d local) => Pos + (local - Com).Rotated(Angle);
    public Vec2d PartWorld(Part p) => LocalToWorld(p.Pos);

    public double Resource(Resource r) { double s = 0; foreach (var p in Parts) if (!p.Destroyed) s += p.Res[(int)r]; return s; }
    public double Fuel => Resource(Sim.Resource.Methalox);
    public long PartsCost => Parts.Where(p => !p.Destroyed).Sum(p => p.Def.Cost);

    public void PutOnRails(StarSystem sys, double t)
    {
        Rails = new RailsState(BodyId, Conic.FromState(sys[BodyId].Gm, Pos, Vel, t));
        Mode = CraftMode.Rails;
        Next = null; SearchedUntil = double.NegativeInfinity; CoastTimer = 0;
        OrbitEntryTime = t;
    }

    public void TakeOffRails(double t)
    {
        if (Mode != CraftMode.Rails) return;
        (Pos, Vel) = Rails.StateAt(t);
        Mode = CraftMode.Active;
        Next = null; CoastTimer = 0;
    }

    // ---------------------------------------------------------------- construction

    public static Craft FromDesign(Design d, string name)
    {
        var c = new Craft { Name = name, DesignName = d.Name };
        for (int i = 0; i < d.Parts.Count; i++)
        {
            var dp = d.Parts[i];
            var def = Sim.Parts.Get(dp.DefId);
            var p = new Part { Id = i, Def = def, Pos = new Vec2d(dp.X, dp.Y), Flip = dp.Flip, Parent = dp.Parent, Attach = dp.Attach, Ablator = def.Ablator };
            if (def.StorageT <= 0)
                foreach (var (res, cap) in def.Capacity)
                    if (res is Sim.Resource.Methalox or Sim.Resource.Rcs or Sim.Resource.Xenon) p.Res[(int)res] = cap;   // propellant tanks launch full; cargo bins and surface storage empty
            c.Parts.Add(p);
        }
        c.NextPartId = d.Parts.Count;
        c.RootId = 0;
        foreach (var st in d.Stages) c.Stages.Add(st.ToList());
        c.UpdateMass();
        return c;
    }

    public void UpdateMass()
    {
        double m = 0, dry = 0; double cx = 0, cy = 0;
        foreach (var p in Parts)
        {
            if (p.Destroyed) continue;
            double pm = p.Mass;
            m += pm; dry += p.Def.DryMass;
            cx += pm * p.Pos.X; cy += pm * p.Pos.Y;
        }
        Mass = m; DryMass = dry;
        if (m <= 0) { Com = Vec2d.Zero; Moi = 1e-6; return; }
        Com = new Vec2d(cx / m, cy / m);
        double moi = 0;
        foreach (var p in Parts)
        {
            if (p.Destroyed) continue;
            double pm = p.Mass;
            double d2 = (p.Pos - Com).LengthSq;
            moi += pm * (d2 + (p.Def.Width * p.Def.Width + p.Def.Height * p.Def.Height) / 12);
        }
        Moi = Math.Max(moi, 1e-6);
    }

    // ---------------------------------------------------------------- structure queries

    public IEnumerable<Part> Children(Part parent) { foreach (var p in Parts) if (!p.Destroyed && p.Parent == parent.Id) yield return p; }

    /// <summary>Neighbours through attachments (parent and children).</summary>
    public IEnumerable<Part> Neighbours(Part p)
    {
        var parent = p.Parent >= 0 ? Find(p.Parent) : null;
        if (parent != null && !parent.Destroyed) yield return parent;
        foreach (var ch in Children(p)) yield return ch;
    }

    /// <summary>Tanks an engine can draw from: connected through attachments without passing through decouplers or docking ports.</summary>
    public List<Part> Feed(Part engine, Resource fuel)
    {
        var seen = new HashSet<int> { engine.Id };
        var stack = new Stack<Part>(); stack.Push(engine);
        var tanks = new List<Part>();
        while (stack.Count > 0)
        {
            var p = stack.Pop();
            if (p != engine && p.Capacity(fuel) > 0) tanks.Add(p);
            if (p != engine && p.Def.Has(PartFlags.FuelBlocker | PartFlags.DockingPort | PartFlags.Claw)) continue;
            foreach (var n in Neighbours(p)) if (seen.Add(n.Id)) stack.Push(n);
        }
        return tanks;
    }

    /// <summary>Parts in the subtree rooted at `root` (including it), following parent links.</summary>
    public List<Part> Subtree(Part root)
    {
        var list = new List<Part>();
        var stack = new Stack<Part>(); stack.Push(root);
        while (stack.Count > 0)
        {
            var p = stack.Pop();
            list.Add(p);
            foreach (var ch in Children(p)) stack.Push(ch);
        }
        return list;
    }

    /// <summary>Lowest point of the craft in the craft frame (for placing it on a pad).</summary>
    public double MinLocalY()
    {
        double m = double.PositiveInfinity;
        foreach (var p in Alive) foreach (var v in p.LocalOutline()) m = Math.Min(m, v.Y);
        return double.IsPositiveInfinity(m) ? 0 : m;
    }

    public double Height()
    {
        double lo = double.PositiveInfinity, hi = double.NegativeInfinity;
        foreach (var p in Alive) foreach (var v in p.LocalOutline()) { lo = Math.Min(lo, v.Y); hi = Math.Max(hi, v.Y); }
        return double.IsPositiveInfinity(lo) ? 0 : hi - lo;
    }

    // ---------------------------------------------------------------- Δv report (GDD §5 live stats; gate M3: within 1% of a simulated burn)

    /// <summary>Stage-by-stage Δv for the stages still to fire, starting with what is currently running. Uses the same feed and mass rules as the flight sim.</summary>
    public List<StageInfo> StageReport(double surfaceGravity = 9.81)
    {
        var report = new List<StageInfo>();
        var alive = new HashSet<int>(Alive.Select(p => p.Id));
        var res = Parts.ToDictionary(p => p.Id, p => (double[])p.Res.Clone());
        var running = new HashSet<int>(Alive.Where(p => p.IsEngine && p.Running).Select(p => p.Id));
        int stageIdx = NextStage - 1;
        double Mass() { double m = 0; foreach (var p in Parts) if (alive.Contains(p.Id)) m += p.Def.DryMass + p.Ablator + res[p.Id].Sum(); return m; }

        void Burn()
        {
            var engines = Parts.Where(p => alive.Contains(p.Id) && running.Contains(p.Id) && p.IsEngine).ToList();
            if (engines.Count == 0) return;
            double m0 = Mass();
            // Union of feeds per fuel; total flow at full throttle.
            double tVac = 0, tSl = 0, mdotVac = 0, mdotSl = 0;
            var tankIds = new HashSet<int>();
            var fuelKinds = new HashSet<Resource>();
            foreach (var e in engines)
            {
                var ed = e.Def.Engine!;
                var feed = FeedVirtual(e, ed.Fuel, alive);
                if (feed.Count == 0 || feed.Sum(t => res[t.Id][(int)ed.Fuel]) <= 1e-9) continue;
                foreach (var t in feed) tankIds.Add(t.Id);
                fuelKinds.Add(ed.Fuel);
                tVac += ed.ThrustVac; tSl += ed.ThrustSl;
                mdotVac += ed.ThrustVac / (ed.IspVac * Units.G0); mdotSl += ed.ThrustSl / (Math.Max(ed.IspSl, 1) * Units.G0);
            }
            if (mdotVac <= 0) return;
            double fuel = 0;
            foreach (var id in tankIds) foreach (var f in fuelKinds) fuel += res[id][(int)f];
            double m1 = m0 - fuel;
            double dvVac = tVac / mdotVac * Math.Log(m0 / m1);
            double dvSl = mdotSl > 0 ? tSl / mdotSl * Math.Log(m0 / m1) : 0;
            double burn = fuel * 1000 / mdotVac;
            double twr = tSl / (m0 * 1000 * surfaceGravity);
            report.Add(new StageInfo(stageIdx, m0, m1, dvVac, dvSl, burn, twr, tVac, fuel));
            foreach (var id in tankIds) foreach (var f in fuelKinds) res[id][(int)f] = 0;
        }

        Burn();
        for (int s = NextStage; s < Stages.Count; s++)
        {
            stageIdx = s;
            foreach (int pid in Stages[s])
            {
                var p = Find(pid);
                if (p == null || !alive.Contains(pid)) continue;
                if (p.Def.Has(PartFlags.Decoupler | PartFlags.RadialDecoupler))
                    foreach (var sp in SubtreeVirtual(p, alive)) alive.Remove(sp.Id);
                else if (p.Def.Has(PartFlags.Fairing)) alive.Remove(pid);
                else if (p.IsEngine) running.Add(pid);
            }
            Burn();
        }
        return report;
    }

    List<Part> FeedVirtual(Part engine, Resource fuel, HashSet<int> alive)
    {
        var seen = new HashSet<int> { engine.Id };
        var stack = new Stack<Part>(); stack.Push(engine);
        var tanks = new List<Part>();
        while (stack.Count > 0)
        {
            var p = stack.Pop();
            if (p != engine && p.Capacity(fuel) > 0) tanks.Add(p);
            if (p != engine && p.Def.Has(PartFlags.FuelBlocker | PartFlags.DockingPort | PartFlags.Claw)) continue;
            var parent = p.Parent >= 0 ? Find(p.Parent) : null;
            if (parent != null && alive.Contains(parent.Id) && seen.Add(parent.Id)) stack.Push(parent);
            foreach (var ch in Parts) if (ch.Parent == p.Id && alive.Contains(ch.Id) && seen.Add(ch.Id)) stack.Push(ch);
        }
        return tanks;
    }

    List<Part> SubtreeVirtual(Part root, HashSet<int> alive)
    {
        var list = new List<Part>();
        var stack = new Stack<Part>(); stack.Push(root);
        while (stack.Count > 0)
        {
            var p = stack.Pop();
            list.Add(p);
            foreach (var ch in Parts) if (ch.Parent == p.Id && alive.Contains(ch.Id)) stack.Push(ch);
        }
        return list;
    }

    /// <summary>Centre of mass of the parts that were here before the last `count` parts were appended (docking bookkeeping).</summary>
    internal Vec2d ComBefore(int appended)
    {
        double m = 0, cx = 0, cy = 0;
        for (int i = 0; i < Parts.Count - appended; i++) { var p = Parts[i]; if (p.Destroyed) continue; double pm = p.Mass; m += pm; cx += pm * p.Pos.X; cy += pm * p.Pos.Y; }
        return m > 0 ? new Vec2d(cx / m, cy / m) : Vec2d.Zero;
    }

    public override string ToString() => $"{Name}#{Id} {Mode} @body{BodyId}";
}
