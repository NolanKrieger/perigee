using System.Text.Json;

namespace Perigee.Sim;

/// <summary>Where a part may go: a stack node of a parent (top/bottom) or a point on a parent's side.</summary>
public sealed record Placement(int Parent, AttachKind Kind, Vec2d Centre, int Side, double Distance);

/// <summary>Live builder stats (GDD §5). RefundIfRecovered = the best case, every part back on your own pad (90%, GDD §6).</summary>
public sealed record BuildStats(double Mass, double DryMass, long Cost, double Height, double Width, List<StageInfo> Stages, double TotalDeltaVVac, double TotalDeltaVSl, double TwrSurface, List<string> Issues)
{
    public long RefundIfRecovered => (long)(Cost * 0.9);
}

/// <summary>
/// Editing operations on a <see cref="Design"/> (GDD §5 builder): snapping to stack nodes and radial points, 2D mirror
/// symmetry, subtree removal, stage editing, validation and live stats. The Godot builder screen is a view over this.
/// </summary>
public sealed class Blueprint
{
    public Design Design { get; private set; }
    public bool Symmetry = true;
    /// <summary>Once the player edits stages by hand, structural edits stop re-running AutoStage.</summary>
    public bool ManualStages;
    public const double SnapRadius = 0.6;   // m, in craft space
    public const double Touch = 0.02;

    public Blueprint(Design d) { Design = d; }
    public static Blueprint New(string rootDefId = "core-s") { var d = new Design { Name = "New Rocket" }; d.Root(rootDefId); d.AutoStage(); return new Blueprint(d); }

    public PartDef Def(int i) => Sim.Parts.Get(Design.Parts[i].DefId);
    public int Count => Design.Parts.Count;

    // ---------------------------------------------------------------- geometry

    /// <summary>Axis-aligned bounds of a part's outline in craft space.</summary>
    public (Vec2d min, Vec2d max) Bounds(int i)
    {
        var p = Design.Parts[i]; var def = Def(i);
        double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
        foreach (var v in def.Outline)
        {
            double x = p.X + (p.Flip ? -v.X : v.X), y = p.Y + v.Y;
            minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
        }
        return (new Vec2d(minX, minY), new Vec2d(maxX, maxY));
    }

    static (Vec2d min, Vec2d max) BoundsOf(PartDef def, Vec2d centre, bool flip)
    {
        double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
        foreach (var v in def.Outline)
        {
            double x = centre.X + (flip ? -v.X : v.X), y = centre.Y + v.Y;
            minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
        }
        return (new Vec2d(minX, minY), new Vec2d(maxX, maxY));
    }

    static bool Overlap((Vec2d min, Vec2d max) a, (Vec2d min, Vec2d max) b) =>
        a.min.X < b.max.X - Touch && a.max.X > b.min.X + Touch && a.min.Y < b.max.Y - Touch && a.max.Y > b.min.Y + Touch;

    /// <summary>Thin edge-mounted parts (fins, legs) may tuck under a wider neighbour; only their clashes with other radial parts count.</summary>
    static bool EdgeMount(PartDef d) => d.Has(PartFlags.Fin | PartFlags.GridFin | PartFlags.Legs);
    static bool Counts(PartDef a, PartDef b) => !(EdgeMount(a) && !b.RadialMount) && !(EdgeMount(b) && !a.RadialMount) && !LegsAndFins(a, b);
    /// <summary>Landing legs are thin struts angled out past a fin's plane: the two may share a mount zone (Falcon-style).</summary>
    static bool LegsAndFins(PartDef a, PartDef b) => (a.Has(PartFlags.Legs) && b.Has(PartFlags.Fin | PartFlags.GridFin)) || (b.Has(PartFlags.Legs) && a.Has(PartFlags.Fin | PartFlags.GridFin));

    public bool WouldOverlap(PartDef def, Vec2d centre, bool flip, int ignore = -1)
    {
        var box = BoundsOf(def, centre, flip);
        for (int i = 0; i < Count; i++) if (i != ignore && Counts(def, Def(i)) && Overlap(box, Bounds(i))) return true;
        return false;
    }

    public bool NodeFree(int part, AttachKind node)
    {
        var p = Design.Parts[part];
        // The node a part hangs from is taken by its own parent.
        if (node == AttachKind.Top && p.Attach == AttachKind.Bottom) return false;   // attached under its parent → its top is the parent's bottom
        if (node == AttachKind.Bottom && p.Attach == AttachKind.Top) return false;
        for (int i = 0; i < Count; i++) if (Design.Parts[i].Parent == part && Design.Parts[i].Attach == node) return false;
        return true;
    }

    /// <summary>Every valid stack placement for a part definition.</summary>
    public List<Placement> StackPlacements(PartDef def)
    {
        var list = new List<Placement>();
        for (int i = 0; i < Count; i++)
        {
            var p = Design.Parts[i]; var pdef = Def(i);
            if (pdef.RadialMount) continue;
            if (pdef.TopNode && def.BottomNode && NodeFree(i, AttachKind.Top))
            {
                var c = new Vec2d(p.X, p.Y + pdef.Height / 2 + def.Height / 2);
                if (!WouldOverlap(def, c, false)) list.Add(new Placement(i, AttachKind.Top, c, 0, 0));
            }
            if (pdef.BottomNode && def.TopNode && NodeFree(i, AttachKind.Bottom))
            {
                var c = new Vec2d(p.X, p.Y - pdef.Height / 2 - def.Height / 2);
                if (!WouldOverlap(def, c, false)) list.Add(new Placement(i, AttachKind.Bottom, c, 0, 0));
            }
        }
        return list;
    }

    /// <summary>Nearest valid placement for a part dragged to `cursor` (craft space), or null when nothing is within the snap radius.</summary>
    public Placement? Snap(PartDef def, Vec2d cursor)
    {
        Placement? best = null;
        if (!def.RadialMount)
        {
            foreach (var pl in StackPlacements(def))
            {
                double d = (pl.Centre - cursor).Length;
                if (d <= SnapRadius && (best == null || d < best.Distance)) best = pl with { Distance = d };
            }
            return best;
        }
        // Radial: slide along a parent's side; the part's inner edge touches the parent's edge.
        bool edgeMount = def.Has(PartFlags.Fin | PartFlags.GridFin | PartFlags.Legs);
        for (int i = 0; i < Count; i++)
        {
            var p = Design.Parts[i]; var pdef = Def(i);
            if (!pdef.SideNodes || pdef.RadialMount) continue;
            double halfH = pdef.Height / 2 - Math.Min(def.Height / 2, pdef.Height / 2);
            double y = MathD.Clamp(cursor.Y, p.Y - halfH, p.Y + halfH);
            foreach (int side in new[] { 1, -1 })
            {
                double x = p.X + side * (pdef.Width / 2 + (edgeMount ? 0 : def.Width / 2));
                var c = new Vec2d(x, y);
                double d = (c - cursor).Length;
                if (d > SnapRadius) continue;
                if (WouldOverlap(def, c, side < 0)) continue;
                if (best == null || d < best.Distance) best = new Placement(i, AttachKind.Radial, c, side, d);
            }
        }
        return best;
    }

    // ---------------------------------------------------------------- edits

    /// <summary>Place a part; with symmetry on, radial parts get a mirrored twin. Returns the new part's index.</summary>
    public int Add(PartDef def, Placement pl)
    {
        int idx = Design.Parts.Count;
        Design.Parts.Add(new DesignPart { DefId = def.Id, X = pl.Centre.X, Y = pl.Centre.Y, Parent = pl.Parent, Attach = pl.Kind, Flip = pl.Kind == AttachKind.Radial && pl.Side < 0 });
        if (pl.Kind == AttachKind.Radial && Symmetry)
        {
            var parent = Design.Parts[pl.Parent];
            var mirror = new Vec2d(2 * parent.X - pl.Centre.X, pl.Centre.Y);
            if (!WouldOverlap(def, mirror, pl.Side > 0))
                Design.Parts.Add(new DesignPart { DefId = def.Id, X = mirror.X, Y = mirror.Y, Parent = pl.Parent, Attach = AttachKind.Radial, Flip = pl.Side > 0 });
        }
        if (!ManualStages) Design.AutoStage();
        return idx;
    }

    /// <summary>Remove a part and everything hanging from it (and its mirror twin when symmetry is on).</summary>
    public void Remove(int index)
    {
        if (index <= 0 || index >= Count) return;   // the root stays
        var doomed = new HashSet<int>();
        void Mark(int i) { if (!doomed.Add(i)) return; for (int k = 0; k < Count; k++) if (Design.Parts[k].Parent == i) Mark(k); }
        Mark(index);
        var p = Design.Parts[index];
        if (Symmetry && p.Attach == AttachKind.Radial)
        {
            var parent = Design.Parts[p.Parent];
            for (int k = 0; k < Count; k++)
            {
                var q = Design.Parts[k];
                if (k != index && q.Parent == p.Parent && q.DefId == p.DefId && q.Attach == AttachKind.Radial && Math.Abs(q.Y - p.Y) < 1e-6 && Math.Abs((q.X - parent.X) + (p.X - parent.X)) < 1e-6) Mark(k);
            }
        }
        var keep = new List<int>();
        for (int i = 0; i < Count; i++) if (!doomed.Contains(i)) keep.Add(i);
        var remap = new Dictionary<int, int>();
        for (int n = 0; n < keep.Count; n++) remap[keep[n]] = n;
        var parts = keep.Select(i => Design.Parts[i]).ToList();
        foreach (var q in parts) q.Parent = q.Parent >= 0 && remap.TryGetValue(q.Parent, out var np) ? np : (q.Parent >= 0 ? -1 : -1);
        var stages = Design.Stages.Select(st => st.Where(i => remap.ContainsKey(i)).Select(i => remap[i]).ToList()).Where(st => st.Count > 0).ToList();
        Design.Parts = parts;
        Design.Stages = stages;
        if (!ManualStages) Design.AutoStage();
    }

    public void Clear(string rootDefId = "core-s") { Design.Root(rootDefId); Design.AutoStage(); ManualStages = false; }

    // ---------------------------------------------------------------- stages

    public bool IsStageable(int part) { var d = Def(part); return d.Engine != null || d.Has(PartFlags.Decoupler | PartFlags.RadialDecoupler | PartFlags.Parachute | PartFlags.Fairing); }

    public void MoveToStage(int part, int stage)
    {
        ManualStages = true;
        foreach (var st in Design.Stages) st.Remove(part);
        while (Design.Stages.Count <= stage) Design.Stages.Add(new List<int>());
        Design.Stages[stage].Add(part);
        Design.Stages.RemoveAll(st => st.Count == 0);
    }

    public void AddStage(int at = -1) { ManualStages = true; if (at < 0 || at > Design.Stages.Count) Design.Stages.Add(new List<int>()); else Design.Stages.Insert(at, new List<int>()); }
    public void RemoveStage(int stage)
    {
        if (stage < 0 || stage >= Design.Stages.Count) return;
        ManualStages = true;
        var parts = Design.Stages[stage];
        Design.Stages.RemoveAt(stage);
        if (parts.Count > 0)
        {
            int target = Math.Min(stage, Design.Stages.Count - 1);
            if (target < 0) Design.Stages.Add(new List<int>(parts)); else Design.Stages[target].AddRange(parts);
        }
    }
    public void SwapStages(int a, int b)
    {
        if (a < 0 || b < 0 || a >= Design.Stages.Count || b >= Design.Stages.Count) return;
        ManualStages = true;
        (Design.Stages[a], Design.Stages[b]) = (Design.Stages[b], Design.Stages[a]);
    }
    public void ResetStages() { ManualStages = false; Design.AutoStage(); }

    // ---------------------------------------------------------------- stats & validation

    public BuildStats Stats(double surfaceGravity = 9.81, double padHeightLimit = double.PositiveInfinity, double padMassLimit = double.PositiveInfinity)
    {
        var craft = Craft.FromDesign(Design, "stats");
        var report = craft.StageReport(surfaceGravity);
        double minY = double.MaxValue, maxY = double.MinValue, minX = double.MaxValue, maxX = double.MinValue;
        for (int i = 0; i < Count; i++) { var (a, b) = Bounds(i); minY = Math.Min(minY, a.Y); maxY = Math.Max(maxY, b.Y); minX = Math.Min(minX, a.X); maxX = Math.Max(maxX, b.X); }
        var issues = new List<string>();
        if (!Design.Parts.Any(p => Sim.Parts.Get(p.DefId).Has(PartFlags.ProbeCore))) issues.Add("No probe core: the craft cannot be controlled.");
        if (!Design.Parts.Any(p => Sim.Parts.Get(p.DefId).Engine != null)) issues.Add("No engine.");
        var firstEngineStage = report.FirstOrDefault();
        if (firstEngineStage != null && firstEngineStage.TwrSurface < 1.0 && firstEngineStage.TwrSurface > 0) issues.Add($"First stage TWR {firstEngineStage.TwrSurface:0.00} < 1: it will not lift off.");
        if (report.Count == 0 && Design.Parts.Any(p => Sim.Parts.Get(p.DefId).Engine != null)) issues.Add("Engines have no fuel: put tanks between the engine and the decoupler.");
        for (int i = 0; i < Count; i++) for (int k = i + 1; k < Count; k++) if (Counts(Def(i), Def(k)) && Overlap(Bounds(i), Bounds(k))) { issues.Add($"{Def(i).Name} overlaps {Def(k).Name}."); break; }
        double height = maxY - minY, width = maxX - minX;
        if (height > padHeightLimit) issues.Add($"Too tall for this pad ({height:0.0} m > {padHeightLimit:0} m).");
        if (craft.Mass > padMassLimit) issues.Add($"Too heavy for this pad ({craft.Mass:0.0} t > {padMassLimit:0} t).");
        foreach (var st in Design.Stages) foreach (int idx in st) if (idx < 0 || idx >= Count) { issues.Add("Stage list references a missing part."); break; }
        return new BuildStats(craft.Mass, craft.DryMass, Design.Cost, height, width, report, report.Sum(s => s.DeltaVVac), report.Sum(s => s.DeltaVSl), firstEngineStage?.TwrSurface ?? 0, issues);
    }
}

/// <summary>Design library: one JSON file per design in a folder (`user://designs/` in the game).</summary>
public static class DesignLibrary
{
    public static string FileName(string name)
    {
        var safe = new string(name.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or ' ' ? ch : '_').ToArray()).Trim();
        if (safe.Length == 0) safe = "design";
        return safe + ".json";
    }

    public static List<Design> Load(string dir)
    {
        var list = new List<Design>();
        if (!Directory.Exists(dir)) return list;
        foreach (var f in Directory.GetFiles(dir, "*.json").OrderBy(f => f))
        {
            try { var d = Design.FromJson(File.ReadAllText(f)); if (d.Parts.Count > 0) list.Add(d); }
            catch (Exception) { /* a corrupt file is skipped, never fatal */ }
        }
        return list;
    }

    public static string Save(Design d, string dir)
    {
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, FileName(d.Name));
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, d.ToJson());
        File.Move(tmp, path, true);
        return path;
    }

    public static void Delete(Design d, string dir)
    {
        string path = Path.Combine(dir, FileName(d.Name));
        if (File.Exists(path)) File.Delete(path);
    }
}
