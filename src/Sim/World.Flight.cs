namespace Perigee.Sim;

/// <summary>World: active physics, commands, launches, staging and breakup.</summary>
public sealed partial class World
{
    public const int MaxPhysicsWarp = 4;
    public int PhysicsWarp = 1;
    public InputState Input;
    readonly List<Craft> _spawned = new();

    /// <summary>True when the active craft is under active physics (landed or flying), so time runs at physics warp.</summary>
    public bool ActiveIsPhysics => Active is { Mode: CraftMode.Active or CraftMode.Landed };
    /// <summary>Time multiplier the clock should use this tick.</summary>
    public double EffectiveWarp => ActiveIsPhysics ? PhysicsWarp : Warp;

    public void Emit(SimEventKind kind, Craft c, string text)
    {
        Events.Add(new SimEvent(kind, c.Id, text, T));
        switch (kind)
        {
            case SimEventKind.Launch: Count("launches"); SaveRequested = true; break;
            case SimEventKind.Landed: Count("landings"); SaveRequested = true; break;
            case SimEventKind.Docked: Count("dockings"); SaveRequested = true; break;
            case SimEventKind.Recovered: Count("recoveries"); SaveRequested = true; break;
            case SimEventKind.WarpStopped: SaveRequested = true; break;
            case SimEventKind.Crashed: Count("crashes"); break;
        }
    }



    // ---------------------------------------------------------------- warp keys (GDD §4)

    public void WarpUp()
    {
        if (ActiveIsPhysics) PhysicsWarp = Math.Min(MaxPhysicsWarp, PhysicsWarp + 1);
        else SetWarp(WarpIndex + 1);
    }
    public void WarpDown()
    {
        if (ActiveIsPhysics) PhysicsWarp = Math.Max(1, PhysicsWarp - 1);
        else SetWarp(WarpIndex - 1);
    }
    public void WarpReset() { PhysicsWarp = 1; WarpIndex = 0; }

    // ---------------------------------------------------------------- commands

    public void Apply(Command cmd)
    {
        var c = Active;
        switch (cmd)
        {
            case Command.WarpUp: WarpUp(); return;
            case Command.WarpDown: WarpDown(); return;
            case Command.WarpReset: WarpReset(); return;
        }
        if (c == null || c.Destroyed) return;
        if (c.Mode == CraftMode.Rails && cmd is Command.Stage or Command.ThrottleFull or Command.DeployChutes or Command.ToggleLegs) WakeActive();
        switch (cmd)
        {
            case Command.Stage: Stage(c); break;
            case Command.ThrottleFull: c.Throttle = 1; break;
            case Command.ThrottleCut: c.Throttle = 0; break;
            case Command.ToggleRcs: c.RcsOn = !c.RcsOn; break;
            case Command.ToggleLegs:
                c.LegsDown = !c.LegsDown;
                foreach (var p in c.Parts) if (!p.Destroyed && p.Def.Has(PartFlags.Legs | PartFlags.GridFin)) p.Deployed = c.LegsDown;
                break;
            case Command.DeployChutes:
                foreach (var p in c.Parts) if (!p.Destroyed && p.Def.Has(PartFlags.Parachute) && !p.Deployed) p.Deployed = true;
                break;
        }
    }

    public void SetThrottle(double v) { if (Active is { } c) { c.Throttle = MathD.Clamp(v, 0, 1); if (c.Mode == CraftMode.Rails && v > 0) WakeActive(); } }

    /// <summary>Any control input on a coasting craft takes it off the rails (physics resumes, rails warp drops to 1×).</summary>
    public void WakeActive()
    {
        var c = Active;
        if (c == null || c.Mode != CraftMode.Rails) return;
        c.TakeOffRails(T);
        WarpIndex = 0; PhysicsWarp = 1;
    }

    /// <summary>Build a craft from a design and put it on the launch pad of a body (landed, upright).</summary>
    public Craft Launch(Design design, string name, int bodyId, double padLocalAngle)
    {
        var body = Sys[bodyId];
        var c = Craft.FromDesign(design, name);
        c.BodyId = bodyId;
        c.LaunchPadBody = bodyId;
        TagPayloads(c);
        c.UpdateMass();
        double ground = body.SurfaceRadiusLocal(padLocalAngle);
        double radius = ground + (c.Com.Y - c.MinLocalY());
        c.Landed = new LandedState { LocalAngle = padLocalAngle, Radius = radius, AngleOffset = -Math.PI / 2 };
        c.Mode = CraftMode.Landed;
        double rot = body.SurfaceAngle(T);
        c.Angle = MathD.WrapAngle(padLocalAngle + rot - Math.PI / 2);
        c.Pos = Vec2d.FromPolar(radius, padLocalAngle + rot);
        c.Vel = Flight.SurfaceVelocity(body, c.Pos);
        foreach (var p in c.Parts) if (p.Def.Has(PartFlags.Legs)) p.Deployed = true;
        c.LegsDown = true;
        AddCraft(c);
        ActiveCraftId = c.Id;
        PhysicsWarp = 1; WarpIndex = 0;
        return c;
    }

    /// <summary>Fire the craft's next stage: decouplers separate their subtrees, engines ignite, chutes deploy, fairings jettison.</summary>
    public void Stage(Craft c)
    {
        if (c.NextStage >= c.Stages.Count) return;
        var ids = c.Stages[c.NextStage];
        c.NextStage++;
        var separated = new List<Craft>();
        foreach (int pid in ids)
        {
            var p = c.Find(pid);
            if (p == null || p.Destroyed) continue;
            if (p.Def.Has(PartFlags.Decoupler | PartFlags.RadialDecoupler))
            {
                p.Fired = true;
                var sub = c.Subtree(p);
                if (sub.Count < c.Alive.Count())
                {
                    var nc = Split(c, sub, $"{c.Name} stage");
                    // Push the separated stage away along the attachment direction.
                    var away = p.Attach == AttachKind.Radial ? (c.RightDir * (p.Flip ? -1 : 1)) : (p.Attach == AttachKind.Top ? c.NoseDir : -c.NoseDir);
                    double dv = p.Def.Has(PartFlags.RadialDecoupler) ? 3.0 : 1.5;
                    nc.Vel += away * dv; c.Vel -= away * dv * (nc.Mass / Math.Max(c.Mass, 1e-6));
                    separated.Add(nc);
                }
            }
            else if (p.Def.Has(PartFlags.Fairing))
            {
                p.Fired = true;
                var nc = Split(c, new List<Part> { p }, "Fairing");
                nc.Vel += c.NoseDir.Perp * 2 + c.NoseDir * 1;
                nc.AngVel += 0.5;
                nc.Debris = true;
                separated.Add(nc);
            }
            else if (p.IsEngine) p.Running = true;
            else if (p.Def.Has(PartFlags.Parachute)) p.Deployed = true;
        }
        c.UpdateMass();
        // Control follows the craft that still has a probe core; a separated core-less stage rides the rails as debris,
        // unless it is a booster dropped by the player's craft: that one is held for its flashback (GDD §4).
        foreach (var nc in separated)
        {
            nc.UpdateMass();
            if (nc.HasControl) continue;
            if (!c.Npc && c.Id == ActiveCraftId && !InFlashback && IsBooster(nc)) HoldBooster(nc);
            else nc.Debris = true;
        }
        if (!c.HasControl)
        {
            var withCore = separated.FirstOrDefault(s => s.HasControl);
            if (withCore != null && c.Id == ActiveCraftId) ActiveCraftId = withCore.Id;
            c.Debris = true;
        }
        Emit(SimEventKind.Staging, c, $"{c.Name}: stage {c.NextStage} fired");
    }

    /// <summary>Move `parts` out of `c` into a new craft with the same motion. Returns the new craft.</summary>
    public Craft Split(Craft c, List<Part> parts, string name)
    {
        var nc = new Craft { Name = name, DesignName = c.DesignName, BodyId = c.BodyId, Mode = CraftMode.Active, Angle = c.Angle, AngVel = c.AngVel, Npc = c.Npc, LaunchPadBody = c.LaunchPadBody, LaunchTime = c.LaunchTime };
        var ids = new HashSet<int>(parts.Select(p => p.Id));
        // The craft frame's origin stays where it is in the world; each craft re-centres on its own centre of mass.
        var oldPos = c.Pos;
        var originWorld = c.Pos - c.Com.Rotated(c.Angle);
        foreach (var p in parts)
        {
            c.Parts.Remove(p);
            if (!ids.Contains(p.Parent)) p.Parent = -1;
            nc.Parts.Add(p);
        }
        nc.RootId = parts.OrderBy(p => p.Parent >= 0 && ids.Contains(p.Parent) ? 1 : 0).First().Id;
        nc.NextPartId = c.NextPartId;
        // Stages: entries that moved go with the new craft (kept at the same indices so future numbering still lines up).
        foreach (var st in c.Stages) nc.Stages.Add(st.Where(id => ids.Contains(id)).ToList());
        for (int i = 0; i < c.Stages.Count; i++) c.Stages[i] = c.Stages[i].Where(id => !ids.Contains(id)).ToList();
        nc.NextStage = c.NextStage;
        nc.UpdateMass();
        c.UpdateMass();
        c.Pos = originWorld + c.Com.Rotated(c.Angle);
        nc.Pos = originWorld + nc.Com.Rotated(nc.Angle);
        // Points on a rotating body move with ω × r.
        nc.Vel = c.Vel + (nc.Pos - oldPos).Perp * c.AngVel;
        c.Vel = c.Vel + (c.Pos - oldPos).Perp * c.AngVel;
        if (c.Mode == CraftMode.Landed && c.Landed is { } L)
        {
            // Splitting on the pad: both stay landed side by side.
            nc.Mode = CraftMode.Landed;
            nc.Landed = new LandedState { LocalAngle = L.LocalAngle, Radius = nc.Pos.Length, AngleOffset = L.AngleOffset, Water = L.Water };
            c.Landed = new LandedState { LocalAngle = L.LocalAngle, Radius = c.Pos.Length, AngleOffset = L.AngleOffset, Water = L.Water };
        }
        AddCraft(nc);
        return nc;
    }

    public void DestroyPart(Craft c, Part p, string why)
    {
        if (p.Destroyed) return;
        p.Destroyed = true;
        Emit(SimEventKind.PartDestroyed, c, $"{c.Name}: {why}");
    }

    /// <summary>After parts were destroyed: overheated parts die, the attachment tree may have split into pieces, each piece becomes its own craft.</summary>
    public void CheckDestroyed(Craft c)
    {
        foreach (var p in c.Parts) if (!p.Destroyed && p.Temp > p.Def.MaxTemp) DestroyPart(c, p, $"{p.Def.Name} burned up ({p.Temp:0} K)");
        ResolveBreakup(c);
    }

    public void ResolveBreakup(Craft c)
    {
        var alive = c.Alive.ToList();
        if (alive.Count == 0) { c.Destroyed = true; Emit(SimEventKind.Crashed, c, $"{c.Name} was destroyed"); if (c.Debris && c.TouchedByPlayer) OnDebrisDeorbited(c); return; }
        if (alive.Count == c.Parts.Count) return;
        c.Parts.RemoveAll(p => p.Destroyed);
        // Connected components over surviving attachments.
        var comp = new Dictionary<int, int>();
        int n = 0;
        foreach (var p in c.Parts)
        {
            if (comp.ContainsKey(p.Id)) continue;
            var stack = new Stack<Part>(); stack.Push(p); comp[p.Id] = n;
            while (stack.Count > 0)
            {
                var q = stack.Pop();
                foreach (var nb in c.Neighbours(q)) if (!comp.ContainsKey(nb.Id)) { comp[nb.Id] = n; stack.Push(nb); }
            }
            n++;
        }
        if (n <= 1) { c.UpdateMass(); return; }
        // The component with a probe core (or the largest) keeps the craft identity.
        var groups = Enumerable.Range(0, n).Select(k => c.Parts.Where(p => comp[p.Id] == k).ToList()).ToList();
        int keep = groups.FindIndex(g => g.Any(p => p.Def.Has(PartFlags.ProbeCore)));
        if (keep < 0) keep = groups.Select((g, i) => (g.Count, i)).Max().i;
        for (int k = 0; k < n; k++)
        {
            if (k == keep) continue;
            var nc = Split(c, groups[k], $"{c.Name} debris");
            nc.Debris = !nc.HasCore;
            nc.Vel += Vec2d.FromAngle(Rng.Range(0, MathD.TwoPi)) * Rng.Range(0.5, 2.0);
            nc.AngVel += Rng.Range(-1, 1);
        }
        c.UpdateMass();
        if (!c.HasCore) c.Debris = true;
    }

    // ---------------------------------------------------------------- active physics inside Advance

    /// <summary>Crafts that are under active physics this tick: every non-rails craft (the loaded bubble decides who leaves the rails).</summary>
    /// <summary>Who gets 50 Hz physics: every flying craft, plus landed craft that are active or inside the active craft's bubble. A base parked on a
    /// far moon is frozen to its surface and costs nothing while you fly elsewhere (its production runs from the outpost step instead).</summary>
    IEnumerable<Craft> PhysicsCrafts() => Crafts.Where(c => !c.Destroyed && IsPresent(c) && (c.Mode == CraftMode.Active || (c.Mode == CraftMode.Landed && ((c.Id == ActiveCraftId && (c.Throttle > 0 || Input.Any)) || NearActive(c)))));
    bool NearActive(Craft c) => Active is { Destroyed: false, Mode: CraftMode.Active } a && a != c && a.BodyId == c.BodyId && Distance(a, c) <= LoadedBubble;

    void StepPhysics(double dt)
    {
        if (Input.Any) WakeActive();
        if (!InFlashback) UpdateBubble();
        var list = PhysicsCrafts().ToList();
        foreach (var c in list)
        {
            var input = c.Id == ActiveCraftId ? Input : InputState.None;
            // Throttle keys are continuous: 50%/s ramp.
            if (c.Id == ActiveCraftId)
            {
                if (Input.ThrottleUp) c.Throttle = Math.Min(1, c.Throttle + dt * 0.5);
                if (Input.ThrottleDown) c.Throttle = Math.Max(0, c.Throttle - dt * 0.5);
            }
            Flight.Step(this, c, input, dt);
            PumpStep(c, dt);
            _steppedThisAdvance.Add(c.Id);
        }
        if (!InFlashback) { DockingStep(dt); CollisionStep(); }
        FlashbackStep();
    }
}
