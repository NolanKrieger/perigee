namespace Perigee.Sim;

/// <summary>
/// The whole simulation state and its clock. Coasting craft ride exact conics; `Advance` moves the clock and processes
/// sphere-of-influence changes in time order, stopping the clock exactly at an event that must interrupt warp.
/// </summary>
public sealed partial class World
{
    public static readonly double[] WarpLevels = { 1, 5, 10, 50, 100, 1000, 10000, 100000 };
    public const double EventChunk = 5 * Units.Day;

    public StarSystem Sys;
    public double T;
    public Rng Rng;
    public List<Craft> Crafts = new();
    public int ActiveCraftId = -1;
    public int WarpIndex;
    public List<SimEvent> Events = new();
    int _nextCraftId;

    public double Warp => WarpLevels[WarpIndex];
    Craft? _activeCache;
    public Craft? Active
    {
        get
        {
            if (ActiveCraftId < 0) return null;
            if (_activeCache != null && _activeCache.Id == ActiveCraftId && !_activeCache.Destroyed) return _activeCache;   // ids are never reused
            _activeCache = Crafts.FirstOrDefault(c => c.Id == ActiveCraftId);
            return _activeCache;
        }
    }

    public World(StarSystem sys, ulong seed)
    {
        Sys = sys;
        Rng = new Rng(seed);
    }

    public Craft AddCraft(Craft c)
    {
        c.Id = _nextCraftId++;
        Crafts.Add(c);
        if (ActiveCraftId < 0) ActiveCraftId = c.Id;
        return c;
    }

    public Craft? Find(int id) => Crafts.FirstOrDefault(c => c.Id == id);

    public void SetWarp(int index) => WarpIndex = Math.Clamp(index, 0, WarpLevels.Length - 1);
    /// <summary>Cents. Economy arrives in M7; scripts and the HUD read it from day one.</summary>
    public long Cash;

    /// <summary>
    /// Advance game time by dt seconds. Rails events (SOI changes, atmosphere arrival) are applied in order. When craft are under
    /// active physics the step is split into 50 Hz substeps. If the active craft changes SOI or reaches its active zone, the clock
    /// stops exactly there, warp drops to 1× and the remaining dt is dropped. Returns the time actually advanced.
    /// </summary>
    public double Advance(double dt)
    {
        Events.Clear();
        double start = T, target = T + dt;
        _steppedThisAdvance.Clear();
        if (Input.Any) WakeActive();
        if (!PhysicsCrafts().Any()) { AdvanceRailsTo(target); RailsPumps(T - start); DayBoundaries(); AfterAdvance(T - start); return T - start; }
        int guard = 0;
        while (T < target - 1e-9 && guard++ < 100000)
        {
            double t0 = T;
            double step = Math.Min(Units.PhysicsDt, target - T);
            bool stopped = AdvanceRailsTo(t0 + step);
            double actual = T - t0;
            if (actual > 0) StepPhysics(actual);
            if (stopped) break;
        }
        RailsPumps(T - start);
        DayBoundaries();
        AfterAdvance(T - start);
        return T - start;
    }

    void AfterAdvance(double elapsed)
    {
        foreach (var c in Crafts) if (!c.Destroyed && c.Mode == CraftMode.Rails) OrbitCredit(c);   // credit orbits completed inside this advance
        if (!InFlashback) StormStep();
        if (!InFlashback) Production(elapsed);
        if (Contracts.Count > 0 && !InFlashback) CheckContracts();
    }

    /// <summary>Charge upkeep etc. for every day boundary crossed since the last one (none during a flashback rewind).</summary>
    void DayBoundaries()
    {
        if (InFlashback || Market.Nodes.Count == 0) return;
        int day = (int)Math.Floor(T / Units.Day);
        int guard = 0;
        while (day > LastUpkeepDay && guard++ < 100000) { LastUpkeepDay++; DayTick(LastUpkeepDay); }
    }

    /// <summary>Docked craft ride the rails as one (GDD §7), so pumping keeps going under warp.</summary>
    readonly HashSet<int> _steppedThisAdvance = new();
    void RailsPumps(double dt)
    {
        if (dt <= 0) return;
        foreach (var c in Crafts) if (c.Mode == CraftMode.Rails && !c.Destroyed && c.Pumps.Count > 0 && !_steppedThisAdvance.Contains(c.Id)) PumpStep(c, dt);
        _steppedThisAdvance.Clear();
    }

    /// <summary>Move the clock to `target`, applying rails events in order. Returns true if it stopped early on an active-craft event.</summary>
    bool AdvanceRailsTo(double target)
    {
        int guard = 0;
        while (guard++ < 10000)
        {
            Craft? first = null;
            foreach (var c in Crafts)
            {
                if (c.Mode != CraftMode.Rails || c.Destroyed || InFlashback) continue;
                EnsureSearched(c, target);
                if (c.Next!.End != PatchEnd.Horizon && c.Next.TEnd <= target && (first == null || c.Next.TEnd < first.Next!.TEnd)) first = c;
            }
            if (first == null) break;
            var p = first.Next!;
            T = Math.Max(T, p.TEnd);
            bool stop = ApplyRailsEvent(first, p);
            if (stop)
            {
                if (WarpIndex > 0) { WarpIndex = 0; Events.Add(new SimEvent(SimEventKind.WarpStopped, first.Id, "warp stopped", T)); }
                PhysicsWarp = 1;
                return true;
            }
        }
        T = target;
        return false;
    }

    /// <summary>Orbit credit (GDD §3): a player craft that has coasted one full closed orbit above the active zone.</summary>
    void OrbitCredit(Craft c)
    {
        if (c.Npc || c.Debris || c.Mode != CraftMode.Rails) return;
        var body = Sys[c.BodyId];
        var conic = c.Rails.Conic;
        if (!conic.IsEllipse || conic.Periapsis < body.Radius + body.ActiveZoneHeight || conic.Apoapsis > body.Soi) return;
        if (double.IsNaN(c.OrbitEntryTime)) c.OrbitEntryTime = T;
        if (T - c.OrbitEntryTime >= conic.Period)
        {
            RecordExploration(c.BodyId, Explored.Orbit);
            // GDD §9: one full orbit with a resource scanner maps the body's deposits.
            if (!body.Scanned && c.Parts.Any(p => !p.Destroyed && !p.Offline && p.Def.Has(PartFlags.Scanner))) { body.Scanned = true; Emit(SimEventKind.Info, c, $"{c.Name} mapped the deposits of {body.Name}"); }
        }
    }

    void EnsureSearched(Craft c, double until)
    {
        OrbitCredit(c);
        if (c.Next != null && c.Next.End != PatchEnd.Horizon) return;
        if (c.Next != null && c.SearchedUntil >= until) return;
        double from = c.Next != null ? c.SearchedUntil : T;
        double to = Math.Max(until, from + EventChunk);
        var p = Patcher.NextEvent(Sys, c.Rails, from, to, c.ZoneAfter);
        // Keep the leg's start at the real current time for drawing.
        if (c.Next != null) p.TStart = c.Next.TStart;
        c.Next = p;
        c.SearchedUntil = to;
    }

    /// <summary>Apply a rails event. Returns true when it must interrupt warp (the active craft changed frame or left the rails).</summary>
    bool ApplyRailsEvent(Craft c, Patch p)
    {
        switch (p.End)
        {
            case PatchEnd.ExitSoi:
            case PatchEnd.EnterSoi:
            {
                var next = Patcher.Transition(Sys, p);
                string text = p.End == PatchEnd.EnterSoi ? $"{c.Name} entered the sphere of influence of {Sys[next.BodyId].Name}" : $"{c.Name} left the sphere of influence of {Sys[p.BodyId].Name}";
                c.Rails = next; c.BodyId = next.BodyId; c.Next = null; c.SearchedUntil = double.NegativeInfinity; c.ZoneAfter = double.NegativeInfinity;
                c.OrbitEntryTime = T;
                Events.Add(new SimEvent(SimEventKind.SoiChange, c.Id, text, T));
                if (p.End == PatchEnd.EnterSoi && !c.Npc && !c.Debris) RecordExploration(next.BodyId, Explored.Flyby);
                return c.Id == ActiveCraftId;
            }
            case PatchEnd.ActiveZone:
            {
                var body = Sys[c.BodyId];
                if (c.Id == ActiveCraftId || HasLoadedNeighbour(c))
                {
                    c.TakeOffRails(T);
                    Events.Add(new SimEvent(SimEventKind.ActiveZone, c.Id, body.Atmo != null ? $"{c.Name} entering the atmosphere of {body.Name}" : $"{c.Name} approaching the surface of {body.Name}", T));
                    return c.Id == ActiveCraftId;
                }
                // Unloaded craft: an airless pass above the highest terrain is harmless; anything else is lost (no physics off-screen).
                if (body.Atmo == null && c.Rails.Conic.Periapsis > body.Radius + body.TerrainMax + 100)
                {
                    c.ZoneAfter = c.Rails.Conic.TimeAtTrueAnomaly(0, T) + 1;
                    c.Next = null; c.SearchedUntil = double.NegativeInfinity;
                    return false;
                }
                c.Destroyed = true;
                Events.Add(new SimEvent(SimEventKind.Crashed, c.Id, body.Atmo != null ? $"{c.Name} was lost in the atmosphere of {body.Name}" : $"{c.Name} crashed into {body.Name}", T));
                if (c.Debris && c.TouchedByPlayer) OnDebrisDeorbited(c);
                return false;
            }
        }
        return false;
    }

    /// <summary>Predicted path of a craft from now (rails legs), up to 3 patches (GDD §4).</summary>
    public List<Patch> Predict(Craft c, int patches = 3)
    {
        var s = c.Mode == CraftMode.Rails ? c.Rails : new RailsState(c.BodyId, Conic.FromState(Sys[c.BodyId].Gm, c.Pos, c.Vel, T));
        return Patcher.Predict(Sys, s, T, patches);
    }

    /// <summary>Absolute (star-frame) position of a craft now.</summary>
    public Vec2d AbsolutePosition(Craft c)
    {
        var (r, _) = c.StateAt(T);
        return r + Sys.Position(c.BodyId, T);
    }
}
