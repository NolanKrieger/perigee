namespace Perigee.Sim;

/// <summary>A pump job on a (possibly docked) craft: moves `Resource` from one part to another at the port's rate (GDD §7).</summary>
public sealed class PumpJob
{
    public int Source, Destination;
    public Resource Resource;
    public double Rate;        // t/s
    public double Moved;
}

/// <summary>One craft holding another during the one-second soft capture before the hard dock (GDD §7).</summary>
public struct Capture
{
    public int OtherCraft, MyPort, OtherPort;
    public double Timer;
}

/// <summary>Docking, claws, undocking and pumping (GDD §7). Ports capture within 0.5 m, 5° and 0.5 m/s; claws grab anything within reach.</summary>
public static class Docking
{
    public const double CaptureDistance = 0.5, CaptureAngle = 5 * Math.PI / 180, CaptureSpeed = 0.5, SoftCaptureTime = 1.0;
    public const double ClawReach = 0.8, ClawSpeed = 1.0;
    public const double StandardRate = 0.5, HeavyRate = 2.0, InternalRate = 2.0;
    public const double UndockPush = 0.3;

    /// <summary>Direction a stack part's free face points (craft frame): away from its parent.</summary>
    public static Vec2d FaceLocal(Part p) => p.Attach == AttachKind.Top ? new Vec2d(0, 1) : p.Attach == AttachKind.Bottom ? new Vec2d(0, -1) : new Vec2d(p.Flip ? -1 : 1, 0);

    /// <summary>World-frame face position (the part's outer edge) and direction.</summary>
    public static (Vec2d pos, Vec2d dir) Face(Craft c, Part p)
    {
        var local = FaceLocal(p);
        var edge = p.Pos + local * (p.Attach == AttachKind.Radial ? p.Def.Width / 2 : p.Def.Height / 2);
        return (c.LocalToWorld(edge), local.Rotated(c.Angle));
    }

    public static bool IsFreePort(Craft c, Part p) => !p.Destroyed && p.Def.Has(PartFlags.DockingPort) && !p.Fired && !c.Parts.Any(q => q.Parent == p.Id && q.Attach == (p.Attach == AttachKind.Top ? AttachKind.Top : AttachKind.Bottom));
    public static bool IsFreeClaw(Part p) => !p.Destroyed && p.Def.Has(PartFlags.Claw) && !p.Fired;

    public static double PortRate(Part p) => p.Def.Has(PartFlags.HeavyPort) ? HeavyRate : StandardRate;
}

public sealed partial class World
{
    public const double LoadedBubble = 2500;   // m (GDD §4 proposal)

    /// <summary>Crafts under active physics: the active craft plus everything within the loaded bubble (docking and collisions are real there).</summary>
    IEnumerable<Craft> LoadedCrafts()
    {
        var a = Active;
        double ra = a != null && !a.Destroyed ? a.StateAt(T).r.Length : 0;
        foreach (var c in Crafts)
        {
            if (c.Destroyed) continue;
            if (c == a) { yield return c; continue; }
            if (a != null && !a.Destroyed && c.BodyId == a.BodyId && CouldBeNear(c, ra) && Distance(a, c) <= LoadedBubble) yield return c;
        }
    }

    /// <summary>Cheap radial test before a Kepler solve: an orbit that never comes within a bubble of radius `ra` cannot be loaded.</summary>
    static bool CouldBeNear(Craft c, double ra)
    {
        if (c.Mode != CraftMode.Rails) return true;
        var k = c.Rails.Conic;
        if (k.Periapsis > ra + LoadedBubble) return false;
        if (k.IsEllipse && k.Apoapsis < ra - LoadedBubble) return false;
        return true;
    }

    public double Distance(Craft a, Craft b)
    {
        var (ra, _) = a.StateAt(T); var (rb, _) = b.StateAt(T);
        if (a.BodyId == b.BodyId) return (ra - rb).Length;
        return (AbsolutePosition(a) - AbsolutePosition(b)).Length;
    }

    /// <summary>Rails craft that drift into the bubble get real physics; a loaded neighbour keeps a coasting craft off the rails.</summary>
    void UpdateBubble()
    {
        var a = Active;
        if (a == null || a.Destroyed) return;
        double ra = a.StateAt(T).r.Length;
        foreach (var c in Crafts)
        {
            if (c == a || c.Destroyed || c.Mode != CraftMode.Rails || c.BodyId != a.BodyId || !CouldBeNear(c, ra)) continue;
            if (Distance(a, c) <= LoadedBubble) { c.TakeOffRails(T); Emit(SimEventKind.Info, c, $"{c.Name} is within {LoadedBubble / 1000:0.0} km"); }
        }
    }

    public bool HasLoadedNeighbour(Craft c)
    {
        foreach (var o in Crafts)
            if (o != c && !o.Destroyed && o.BodyId == c.BodyId && o.Mode is CraftMode.Active or CraftMode.Landed && Distance(c, o) <= LoadedBubble) return true;
        return false;
    }

    // ---------------------------------------------------------------- targets

    /// <summary>Closest approach to the target craft along both predicted paths within one orbit (or until the SOI changes). Returns distance, time from now and relative speed.</summary>
    public (double dist, double t, double relSpeed)? ClosestApproach(Craft c)
    {
        var tc = c.TargetCraft >= 0 ? Find(c.TargetCraft) : null;
        if (tc == null || tc.Destroyed || tc.BodyId != c.BodyId) return null;
        var body = Sys[c.BodyId];
        var (r0, v0) = c.StateAt(T); var (r1, v1) = tc.StateAt(T);
        var ca = Conic.FromState(body.Gm, r0, v0, T); var cb = Conic.FromState(body.Gm, r1, v1, T);
        double horizon = Math.Min(ca.IsEllipse ? ca.Period : 2 * Units.Day, cb.IsEllipse ? cb.Period : 2 * Units.Day);
        horizon = Math.Max(horizon, 60);
        int n = 400;
        double bestT = T, bestD = double.MaxValue;
        for (int i = 0; i <= n; i++)
        {
            double t = T + horizon * i / n;
            double d = (ca.PositionAt(t) - cb.PositionAt(t)).Length;
            if (d < bestD) { bestD = d; bestT = t; }
        }
        // Refine around the best sample.
        double lo = Math.Max(T, bestT - horizon / n), hi = bestT + horizon / n;
        for (int i = 0; i < 40; i++)
        {
            double m1 = lo + (hi - lo) / 3, m2 = hi - (hi - lo) / 3;
            double d1 = (ca.PositionAt(m1) - cb.PositionAt(m1)).Length, d2 = (ca.PositionAt(m2) - cb.PositionAt(m2)).Length;
            if (d1 < d2) hi = m2; else lo = m1;
        }
        bestT = 0.5 * (lo + hi);
        var (pa, va) = ca.StateAt(bestT); var (pb, vb) = cb.StateAt(bestT);
        return ((pa - pb).Length, bestT - T, (va - vb).Length);
    }

    // ---------------------------------------------------------------- docking

    /// <summary>Called each physics tick for the active craft against the loaded ones: start soft captures, complete hard docks, grab with claws.</summary>
    void DockingStep(double dt)
    {
        var a = Active;
        if (a == null || a.Destroyed || a.Mode != CraftMode.Active) return;
        if (a.Capture is { } cap)
        {
            var other = Find(cap.OtherCraft);
            if (other == null || other.Destroyed) { a.Capture = null; return; }
            // Soft capture: hold the two crafts together, then hard-dock after a second.
            other.Vel = a.Vel; other.AngVel = 0;
            cap.Timer += dt;
            if (cap.Timer >= Docking.SoftCaptureTime) { a.Capture = null; Dock(a, cap.MyPort, other, cap.OtherPort); }
            else a.Capture = cap;
            return;
        }
        foreach (var other in LoadedCrafts().ToList())
        {
            if (other == a || other.Mode == CraftMode.Rails) continue;
            double relSpeed = (a.Vel - other.Vel).Length;
            foreach (var pa in a.Parts)
            {
                if (Docking.IsFreePort(a, pa))
                {
                    if (relSpeed > Docking.CaptureSpeed) continue;
                    var (posA, dirA) = Docking.Face(a, pa);
                    foreach (var pb in other.Parts)
                    {
                        if (!Docking.IsFreePort(other, pb)) continue;
                        var (posB, dirB) = Docking.Face(other, pb);
                        double d = (posA - posB).Length;
                        double ang = Math.Acos(MathD.Clamp(Vec2d.Dot(dirA, -dirB), -1, 1));
                        if (d <= Docking.CaptureDistance && ang <= Docking.CaptureAngle)
                        {
                            a.Capture = new Capture { OtherCraft = other.Id, MyPort = pa.Id, OtherPort = pb.Id, Timer = 0 };
                            other.Vel = a.Vel;
                            Emit(SimEventKind.Info, a, $"{a.Name}: soft capture with {other.Name}");
                            return;
                        }
                    }
                }
                else if (Docking.IsFreeClaw(pa))
                {
                    if (relSpeed > Docking.ClawSpeed) continue;
                    var (posA, _) = Docking.Face(a, pa);
                    foreach (var pb in other.Parts)
                    {
                        if (pb.Destroyed) continue;
                        double d = (posA - other.PartWorld(pb)).Length - Math.Max(pb.Def.Width, pb.Def.Height) / 2;
                        if (d <= Docking.ClawReach)
                        {
                            Emit(SimEventKind.Info, a, $"{a.Name}: claw grabbed {other.Name}");
                            Dock(a, pa.Id, other, pb.Id, claw: true);
                            return;
                        }
                    }
                }
            }
        }
    }

    /// <summary>Merge `other` into `a` at the two parts: the other craft's parts move into a's frame (snapped to exact alignment for ports).</summary>
    public void Dock(Craft a, int portAId, Craft other, int portBId, bool claw = false)
    {
        var pa = a.Find(portAId)!; var pb = other.Find(portBId)!;
        var (posA, dirA) = Docking.Face(a, pa);
        var (posB, dirB) = Docking.Face(other, pb);
        // Snap the other craft so its port face meets ours exactly (ports only; a claw keeps the grabbed pose).
        double rot = claw ? 0 : MathD.WrapPi(Math.Atan2(Vec2d.Cross(dirB, -dirA), Vec2d.Dot(dirB, -dirA)));
        int offset = a.NextPartId;
        var newParts = new List<Part>();
        foreach (var p in other.Parts)
        {
            // world = other.Pos + R(other.Angle)(p.Pos - other.Com); after rotating the other craft by rot about its port face and translating.
            var world = other.Pos + (p.Pos - other.Com).Rotated(other.Angle);
            if (!claw) world = posB + (world - posB).Rotated(rot) + (posA - posB);
            var local = (world - a.Pos).Rotated(-a.Angle) + a.Com;
            p.Pos = local; p.Id += offset; if (p.Parent >= 0) p.Parent += offset;
            if (!claw) p.Flip = p.Flip;   // art orientation is fine for 180° joins; general angles use the same polygons
            newParts.Add(p);
        }
        // Attach the other craft's tree under our port.
        var root = newParts.First(p => p.Id == other.RootId + offset);
        var pbNew = newParts.First(p => p.Id == portBId + offset);
        // Re-root the other craft so its port is the root of its tree, then hang it from our port.
        Reroot(newParts, pbNew);
        pbNew.Parent = pa.Id;
        pbNew.Attach = pa.Attach == AttachKind.Top ? AttachKind.Top : pa.Attach == AttachKind.Bottom ? AttachKind.Bottom : AttachKind.Radial;
        pa.Fired = true; pbNew.Fired = true;
        if (!claw && Math.Abs(MathD.WrapPi(rot)) > 1e-9) { /* parts were rotated in world; their local frames now differ by rot, art keeps polygons upright: acceptable for 180° docks */ }
        a.Parts.AddRange(newParts);
        a.NextPartId = offset + other.NextPartId;
        foreach (var st in other.Stages) a.Stages.Add(st.Select(id => id + offset).ToList());
        a.UpdateMass();
        // Keep the centre of mass where the merged geometry says it is.
        var comWorldBefore = a.Pos;   // a.Pos was the CoM of a alone
        var originWorld = comWorldBefore - a.ComBefore(newParts.Count).Rotated(a.Angle);
        a.Pos = originWorld + a.Com.Rotated(a.Angle);
        a.CoastTimer = 0;
        a.Npc = a.Npc && other.Npc;
        a.TouchedByPlayer = a.TouchedByPlayer || other.TouchedByPlayer;
        other.Destroyed = true;
        other.DockedName = other.Name;
        Crafts.Remove(other);
        Emit(SimEventKind.Docked, a, claw ? $"{a.Name} grabbed {other.Name}" : $"{a.Name} docked with {other.Name}");
        OnDocked(a, other);
        if (other.Id == ActiveCraftId) ActiveCraftId = a.Id;
        if (a.TargetCraft == other.Id) a.TargetCraft = -1;
        foreach (var c in Crafts) if (c.TargetCraft == other.Id) c.TargetCraft = a.Id;
        a.DockedNames[pbNew.Id] = other.Name;
    }

    /// <summary>Reverse parent links so `newRoot` becomes the root of the given tree.</summary>
    static void Reroot(List<Part> parts, Part newRoot)
    {
        var byId = parts.ToDictionary(p => p.Id);
        var path = new List<Part>();
        for (var p = newRoot; p != null; p = p.Parent >= 0 && byId.TryGetValue(p.Parent, out var q) ? q : null) path.Add(p);
        // path: newRoot → ... → oldRoot. Flip each link.
        for (int i = 0; i + 1 < path.Count; i++)
        {
            var child = path[i]; var parent = path[i + 1];
            var kind = child.Attach;
            parent.Parent = child.Id;
            parent.Attach = kind == AttachKind.Top ? AttachKind.Bottom : kind == AttachKind.Bottom ? AttachKind.Top : AttachKind.Radial;
        }
        newRoot.Parent = -1; newRoot.Attach = AttachKind.Root;
    }

    /// <summary>Undock at a docked port (or release a claw): the far side becomes its own craft again with a gentle push apart.</summary>
    public Craft? Undock(Craft a, int portId)
    {
        var pa = a.Find(portId);
        if (pa == null || !pa.Fired || !pa.Def.Has(PartFlags.DockingPort | PartFlags.Claw)) return null;
        // The joint is between pa and the part attached to it that is also a docked port (or, for a claw, its child).
        var child = a.Parts.FirstOrDefault(p => p.Parent == pa.Id && (p.Fired && p.Def.Has(PartFlags.DockingPort) || pa.Def.Has(PartFlags.Claw)));
        Part? joint = child;
        Part? mine = pa;
        if (joint == null && pa.Parent >= 0)
        {
            // We are the far side: our parent is the other port.
            var parent = a.Find(pa.Parent);
            if (parent != null && parent.Fired && parent.Def.Has(PartFlags.DockingPort | PartFlags.Claw)) { joint = pa; mine = parent; }
        }
        if (joint == null || mine == null) return null;
        var sub = a.Subtree(joint);
        string name = a.DockedNames.TryGetValue(joint.Id, out var n) ? n : a.DockedNames.TryGetValue(mine.Id, out var n2) ? n2 : $"{a.Name} section";
        a.DockedNames.Remove(joint.Id); a.DockedNames.Remove(mine.Id);
        mine.Fired = false; joint.Fired = false;
        var nc = Split(a, sub, name);
        if (!nc.HasCore) { nc.Debris = true; nc.TouchedByPlayer = true; }
        var (_, dir) = Docking.Face(a, mine);
        nc.Vel += dir * Docking.UndockPush; a.Vel -= dir * Docking.UndockPush * (nc.Mass / Math.Max(a.Mass, 1e-6));
        nc.CoastTimer = 0; a.CoastTimer = 0;
        Emit(SimEventKind.Undocked, a, $"{name} undocked from {a.Name}");
        return nc;
    }

    // ---------------------------------------------------------------- pumping

    /// <summary>Start moving a resource between two parts of the same (docked) craft. Rate = the slowest docking port on the path between them, else 2 t/s inside one craft.</summary>
    public PumpJob? StartPump(Craft c, int srcId, int dstId, Resource res)
    {
        var src = c.Find(srcId); var dst = c.Find(dstId);
        if (src == null || dst == null || src == dst || !src.CanHold(res) || !dst.CanHold(res)) return null;
        double rate = Docking.InternalRate;
        foreach (var p in PathBetween(c, src, dst)) if (p.Def.Has(PartFlags.DockingPort) && p.Fired) rate = Math.Min(rate, Docking.PortRate(p));
        if (PathBetween(c, src, dst).Any(p => p.Def.Has(PartFlags.Claw))) return null;   // claws can't pump (GDD §7)
        c.Pumps.RemoveAll(j => j.Source == srcId && j.Destination == dstId && j.Resource == res);
        var job = new PumpJob { Source = srcId, Destination = dstId, Resource = res, Rate = rate };
        c.Pumps.Add(job);
        return job;
    }

    public void StopPumps(Craft c) => c.Pumps.Clear();

    /// <summary>Parts on the attachment path from a to b (inclusive), or empty when disconnected.</summary>
    public static List<Part> PathBetween(Craft c, Part a, Part b)
    {
        var prev = new Dictionary<int, int> { [a.Id] = -1 };
        var q = new Queue<Part>(); q.Enqueue(a);
        while (q.Count > 0)
        {
            var p = q.Dequeue();
            if (p == b) break;
            foreach (var n in c.Neighbours(p)) if (!prev.ContainsKey(n.Id)) { prev[n.Id] = p.Id; q.Enqueue(n); }
        }
        if (!prev.ContainsKey(b.Id)) return new List<Part>();
        var path = new List<Part>();
        for (int id = b.Id; id >= 0; id = prev[id]) path.Add(c.Find(id)!);
        path.Reverse();
        return path;
    }

    void PumpStep(Craft c, double dt)
    {
        if (c.Pumps.Count == 0) return;
        for (int i = c.Pumps.Count - 1; i >= 0; i--)
        {
            var j = c.Pumps[i];
            var src = c.Find(j.Source); var dst = c.Find(j.Destination);
            if (src == null || dst == null || src.Destroyed || dst.Destroyed) { c.Pumps.RemoveAt(i); continue; }
            double room = dst.Room(j.Resource);
            double take = Math.Min(j.Rate * dt, Math.Min(src.Get(j.Resource), room));
            if (take <= 1e-9) { c.Pumps.RemoveAt(i); Emit(SimEventKind.Info, c, $"Pump finished: {j.Moved:0.00} t of {j.Resource}"); continue; }
            src.Set(j.Resource, src.Get(j.Resource) - take);
            dst.Set(j.Resource, dst.Get(j.Resource) + take);
            j.Moved += take;
            c.PumpedTotal += take;
        }
        c.UpdateMass();
    }
}
