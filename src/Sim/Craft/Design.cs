using System.Text.Json;
using System.Text.Json.Serialization;

namespace Perigee.Sim;

public enum AttachKind { Root, Top, Bottom, Radial }

/// <summary>One placed part in a design. Positions are the part centre in craft metres (x right, y up = nose).</summary>
public sealed class DesignPart
{
    public string DefId { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public int Parent { get; set; } = -1;
    public AttachKind Attach { get; set; }
    /// <summary>Radial part mounted on the left side (art mirrored).</summary>
    public bool Flip { get; set; }
}

/// <summary>A rocket design (GDD §5): parts snapped to stack nodes and radial points, plus an editable staging list.</summary>
public sealed class Design
{
    public string Name { get; set; } = "Untitled";
    public List<DesignPart> Parts { get; set; } = new();
    /// <summary>Stages[0] fires first. Entries are indices into Parts (decouplers, engines, chutes, fairings).</summary>
    public List<List<int>> Stages { get; set; } = new();

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    public string ToJson() => JsonSerializer.Serialize(this, Json);
    public static Design FromJson(string s) => JsonSerializer.Deserialize<Design>(s, Json) ?? new Design();
    public Design Clone() => FromJson(ToJson());

    public PartDef Def(int i) => Perigee.Sim.Parts.Get(Parts[i].DefId);

    // ---------------------------------------------------------------- builder helpers (also used by tests and the tutorial designs)

    /// <summary>Place the root part at the origin.</summary>
    public int Root(string defId) { Parts.Clear(); Stages.Clear(); Parts.Add(new DesignPart { DefId = defId, Attach = AttachKind.Root }); return 0; }

    /// <summary>Stack a part under (below=true) or above the parent, snapped node to node.</summary>
    public int Stack(int parent, string defId, bool below = true)
    {
        var pd = Parts[parent]; var pdef = Perigee.Sim.Parts.Get(pd.DefId); var def = Perigee.Sim.Parts.Get(defId);
        double y = below ? pd.Y - pdef.Height / 2 - def.Height / 2 : pd.Y + pdef.Height / 2 + def.Height / 2;
        Parts.Add(new DesignPart { DefId = defId, X = pd.X, Y = y, Parent = parent, Attach = below ? AttachKind.Bottom : AttachKind.Top });
        return Parts.Count - 1;
    }

    /// <summary>Attach a radial part to the side of the parent at height dy from the parent's centre. side = +1 right, -1 left.</summary>
    public int Radial(int parent, string defId, int side, double dy = 0)
    {
        var pd = Parts[parent]; var pdef = Perigee.Sim.Parts.Get(pd.DefId); var def = Perigee.Sim.Parts.Get(defId);
        double x = pd.X + side * (pdef.Width / 2 + (def.RadialMount && (def.Flags & (PartFlags.Fin | PartFlags.GridFin | PartFlags.Legs)) != 0 ? 0 : def.Width / 2));
        Parts.Add(new DesignPart { DefId = defId, X = x, Y = pd.Y + dy, Parent = parent, Attach = AttachKind.Radial, Flip = side < 0 });
        return Parts.Count - 1;
    }

    /// <summary>Mirror-symmetric pair of radial parts; returns both indices.</summary>
    public (int right, int left) Pair(int parent, string defId, double dy = 0) => (Radial(parent, defId, 1, dy), Radial(parent, defId, -1, dy));

    public void Stage(params int[] parts) => Stages.Add(parts.ToList());

    /// <summary>Automatic staging (GDD §5): bottom-up by part height: each decoupler starts a stage that fires it plus the engines and chutes just above it; the lowest engines fire first.</summary>
    public void AutoStage()
    {
        Stages.Clear();
        // Group activatable parts by "layer": the decoupler that separates them from the parts above, ordered bottom-up.
        var decouplers = Parts.Select((p, i) => (p, i)).Where(t => Perigee.Sim.Parts.Get(t.p.DefId).Has(PartFlags.Decoupler | PartFlags.RadialDecoupler)).OrderBy(t => t.p.Y).Select(t => t.i).ToList();
        var used = new HashSet<int>();
        var engines = Parts.Select((p, i) => (p, i)).Where(t => Perigee.Sim.Parts.Get(t.p.DefId).Engine != null).OrderBy(t => t.p.Y).Select(t => t.i).ToList();
        // First stage: lowest engines (those below the first decoupler or all if none).
        double cut = decouplers.Count > 0 ? Parts[decouplers[0]].Y : double.PositiveInfinity;
        var first = engines.Where(i => Parts[i].Y <= cut + 1e-6).ToList();
        if (first.Count == 0 && engines.Count > 0) first.Add(engines[0]);
        if (first.Count > 0) { Stages.Add(first); used.UnionWith(first); }
        for (int d = 0; d < decouplers.Count; d++)
        {
            double top = d + 1 < decouplers.Count ? Parts[decouplers[d + 1]].Y : double.PositiveInfinity;
            var stage = new List<int> { decouplers[d] };
            stage.AddRange(engines.Where(i => !used.Contains(i) && Parts[i].Y > Parts[decouplers[d]].Y && Parts[i].Y <= top + 1e-6));
            used.UnionWith(stage);
            Stages.Add(stage);
        }
        // Chutes and fairings: chutes last, fairings with the last engine stage.
        var chutes = Parts.Select((p, i) => (p, i)).Where(t => Perigee.Sim.Parts.Get(t.p.DefId).Has(PartFlags.Parachute)).Select(t => t.i).ToList();
        var fairings = Parts.Select((p, i) => (p, i)).Where(t => Perigee.Sim.Parts.Get(t.p.DefId).Has(PartFlags.Fairing)).Select(t => t.i).ToList();
        if (fairings.Count > 0) { if (Stages.Count > 0) Stages[^1].AddRange(fairings); else Stages.Add(fairings); }
        if (chutes.Count > 0)
        {
            var drogues = chutes.Where(i => Perigee.Sim.Parts.Get(Parts[i].DefId).Has(PartFlags.Drogue)).ToList();
            var mains = chutes.Except(drogues).ToList();
            if (drogues.Count > 0) Stages.Add(drogues);
            if (mains.Count > 0) Stages.Add(mains);
        }
        var leftover = engines.Where(e => !used.Contains(e)).ToList();
        if (leftover.Count > 0) Stages.Insert(Math.Min(1, Stages.Count), leftover);
    }

    public long Cost => Parts.Sum(p => Perigee.Sim.Parts.Get(p.DefId).Cost);
    public double DryMass => Parts.Sum(p => Perigee.Sim.Parts.Get(p.DefId).DryMass);
}

/// <summary>Hand-built designs for tests, demos and the tutorial (GDD §11). The builder (M3) makes the same shapes.</summary>
public static class TestDesigns
{
    /// <summary>T1 sounding rocket: core, short tank, Kestrel, nose chute, legs, fins. ~1.5 t, TWR ≈ 4.5.</summary>
    public static Design Sounding()
    {
        var d = new Design { Name = "Sounding Rocket" };
        int core = d.Root("core-s");
        int chute = d.Stack(core, "chute-nose", below: false);
        int tank = d.Stack(core, "tank-s-short");
        int eng = d.Stack(tank, "eng-kestrel");
        d.Pair(tank, "legs-s", LegDy(d, tank));
        d.Pair(eng, "fin", 0.1);   // fins must sit behind the centre of mass: on the engine, not the tank
        d.Stage(eng);
        d.Stage(chute);
        return d;
    }

    /// <summary>Legs hang from near the bottom edge of their tank so the feet reach below the engine bell.</summary>
    public static double LegDy(Design d, int tank) => -(d.Def(tank).Height / 2 - 0.2);

    /// <summary>
    /// Two-stage orbital rocket with a returnable capsule: [chute-nose, core, heat shield] / decoupler / [S short tank, Sparrow]
    /// / decoupler / [S long + short tank, Kestrel, fins]. Stage 3 drops the spent upper stage so the capsule enters shield-first.
    /// </summary>
    public static Design Orbital()
    {
        var d = new Design { Name = "Orbiter" };
        int core = d.Root("core-s");
        int chute = d.Stack(core, "chute-nose", below: false);
        int shield = d.Stack(core, "shield-s");
        int dec2 = d.Stack(shield, "decoupler-s");
        int t2 = d.Stack(dec2, "tank-s-short");
        int e2 = d.Stack(t2, "eng-sparrow");
        int dec = d.Stack(e2, "decoupler-s");
        int t1a = d.Stack(dec, "tank-s-long");
        int t1b = d.Stack(t1a, "tank-s-short");
        int e1 = d.Stack(t1b, "eng-kestrel");
        d.Pair(e1, "fin", 0.1);
        d.Stage(e1);
        d.Stage(dec, e2);
        d.Stage(dec2);
        d.Stage(chute);
        return d;
    }

    /// <summary>T5-style recoverable rocket: S upper stage on an M booster (Condor, grid fins, legs) that flies home in a flashback.</summary>
    public static Design Recoverable()
    {
        var d = new Design { Name = "Recoverable" };
        int core = d.Root("core-s");
        int chute = d.Stack(core, "chute-nose", below: false);
        int t2 = d.Stack(core, "tank-s-short");
        int e2 = d.Stack(t2, "eng-sparrow");
        int dec = d.Stack(e2, "decoupler-s");
        int ad = d.Stack(dec, "adapter-sm");
        int t1 = d.Stack(ad, "tank-m-long");
        int e1 = d.Stack(t1, "eng-condor");
        d.Pair(t1, "grid-fin", 2.4);
        d.Pair(t1, "grid-fin", 1.4);
        d.Pair(t1, "legs-m", LegDy(d, t1));
        d.Stage(e1);
        d.Stage(dec, e2);
        d.Stage(chute);
        return d;
    }

    /// <summary>Client test target for docking: a docking port on its nose, a core and a short tank.</summary>
    public static Design Station()
    {
        var d = new Design { Name = "Station" };
        int core = d.Root("core-s");
        d.Stack(core, "port", below: false);
        int t = d.Stack(core, "tank-s-short");
        d.Pair(t, "solar", 0);
        return d;
    }

    /// <summary>RCS tanker for docking tests: port on the nose, core, RCS pods and blocks, a short tank and a Sparrow.</summary>
    public static Design Tanker()
    {
        var d = new Design { Name = "Tanker" };
        int core = d.Root("core-s");
        d.Stack(core, "port", below: false);
        int t = d.Stack(core, "tank-s-short");
        int e = d.Stack(t, "eng-sparrow");
        d.Pair(t, "rcs-tank-radial", 0.2);
        d.Pair(core, "rcs-block", 0);
        d.Pair(t, "rcs-block", -0.4);
        d.Stage(e);
        return d;
    }

    /// <summary>T4: client relay on top of an S upper stage, on the recoverable M booster.</summary>
    public static Design Comsat()
    {
        var d = new Design { Name = "Comsat" };
        int core = d.Root("core-s");
        d.Stack(core, "payload-s", below: false);
        int t2 = d.Stack(core, "tank-s-long");
        int e2 = d.Stack(t2, "eng-sparrow");
        int dec = d.Stack(e2, "decoupler-s");
        int ad = d.Stack(dec, "adapter-sm");
        int t1 = d.Stack(ad, "tank-m-long");
        int e1 = d.Stack(t1, "eng-condor");
        d.Pair(t1, "grid-fin", 2.4);
        d.Pair(t1, "legs-m", LegDy(d, t1));
        d.Stage(e1);
        d.Stage(dec, e2);
        return d;
    }

    /// <summary>Home relay on starter parts: the Orbiter with the client relay in place of the capsule (short tank + Sparrow upper stage). Reaches a low home orbit, not T4's 200 km.</summary>
    public static Design ComsatS()
    {
        var d = new Design { Name = "Comsat S" };
        int core = d.Root("core-s");
        d.Stack(core, "payload-s", below: false);
        int t2 = d.Stack(core, "tank-s-short");
        int e2 = d.Stack(t2, "eng-sparrow");
        int dec = d.Stack(e2, "decoupler-s");
        int t1a = d.Stack(dec, "tank-s-long");
        int t1b = d.Stack(t1a, "tank-s-long");
        int e1 = d.Stack(t1b, "eng-kestrel");
        d.Pair(e1, "fin", 0.1);
        d.Stage(e1);
        d.Stage(dec, e2);
        return d;
    }

    /// <summary>T5 on starter parts + grid fins: capsule / S short tank + Sparrow / an S booster (long + short tank, Kestrel) with grid fins and S legs that flies back to the pad.</summary>
    public static Design RecoverableS()
    {
        var d = new Design { Name = "Recoverable S" };
        int core = d.Root("core-s");
        int chute = d.Stack(core, "chute-nose", below: false);
        int t2 = d.Stack(core, "tank-s-short");
        int e2 = d.Stack(t2, "eng-sparrow");
        int dec = d.Stack(e2, "decoupler-s");
        int t1a = d.Stack(dec, "tank-s-long");
        int t1b = d.Stack(t1a, "tank-s-short");
        int e1 = d.Stack(t1b, "eng-kestrel");
        d.Pair(t1a, "grid-fin", 1.2);
        d.Pair(t1b, "legs-s", LegDy(d, t1b));
        d.Stage(e1);
        d.Stage(dec, e2);
        d.Stage(chute);
        return d;
    }

    /// <summary>T7 tanker: 2.1 t of methalox behind a nose port, with RCS.</summary>
    public static Design Tanker2()
    {
        var d = new Design { Name = "Tanker 2" };
        int core = d.Root("core-s");
        d.Stack(core, "port", below: false);
        int t = d.Stack(core, "tank-s-long");
        int t2 = d.Stack(t, "tank-s-short");
        int e = d.Stack(t2, "eng-sparrow");
        d.Pair(t, "rcs-tank-radial", 0.6);
        d.Pair(core, "rcs-block", 0);
        d.Pair(t2, "rcs-block", -0.3);
        d.Stage(e);
        return d;
    }

    /// <summary>T8 tanker: 12 t of methalox in an M tank behind a nose port.</summary>
    public static Design TankerM()
    {
        var d = new Design { Name = "Tanker M" };
        int core = d.Root("core-s");
        d.Stack(core, "port", below: false);
        int ad = d.Stack(core, "adapter-sm");
        int t = d.Stack(ad, "tank-m-long");
        int e = d.Stack(t, "eng-condor");   // the booster engine the player already owns; the Heron (+$900k R&D) can wait for landers
        d.Pair(t, "rcs-tank-radial", 2.4);
        d.Pair(t, "rcs-tank-radial", 1.8);
        d.Pair(core, "rcs-block", 0);
        d.Pair(t, "rcs-block", -2.6);
        d.Pair(t, "rcs-block", 2.6);
        d.Stage(e);
        return d;
    }

    /// <summary>T8 depot: Depot Controller + M tank + port on the nose, solar panels.</summary>
    public static Design Depot()
    {
        var d = new Design { Name = "Depot" };
        int ctrl = d.Root("depot-ctrl");
        int ad = d.Stack(ctrl, "adapter-sm", below: false);
        d.Stack(ad, "port", below: false);
        int t = d.Stack(ctrl, "tank-m-long");
        d.Pair(t, "solar", 0);
        return d;
    }

    /// <summary>T9 lander: core, 2.1 t of methalox, Sparrow, legs.</summary>
    public static Design Lander()
    {
        var d = new Design { Name = "Lander" };
        int core = d.Root("core-s");
        int t = d.Stack(core, "tank-s-long");
        int t2 = d.Stack(t, "tank-s-short");
        int e = d.Stack(t2, "eng-sparrow");
        d.Pair(t2, "legs-s", LegDy(d, t2));
        d.Stage(e);
        return d;
    }

    /// <summary>Scanner probe: core, short tank, Sparrow, a resource scanner and two panels.</summary>
    public static Design ScanProbe()
    {
        var d = new Design { Name = "Scan Probe" };
        int core = d.Root("core-s");
        int t = d.Stack(core, "tank-s-short");
        int e = d.Stack(t, "eng-sparrow");
        d.Radial(core, "scanner", 1, 0);
        d.Radial(core, "solar", -1, 0);
        d.Stage(e);
        return d;
    }

    /// <summary>M10 pad base lander: Outpost Core, Pad Kit, Fabricator, surface tank, M tank, Heron, RTGs, legs.</summary>
    public static Design PadBase()
    {
        var d = new Design { Name = "Pad Base" };
        int core = d.Root("outpost-core");
        int kit = d.Stack(core, "pad-kit", below: false);
        d.Stack(kit, "fabricator", below: false);
        int st = d.Stack(core, "surface-tank");
        int t = d.Stack(st, "tank-m-long");
        int e = d.Stack(t, "eng-heron");
        d.Pair(core, "rtg", 0);
        d.Pair(st, "rtg", 0.5);
        d.Pair(t, "legs-m", LegDy(d, t));
        d.Stage(e);
        return d;
    }

    /// <summary>Small hopper built off-world: core, long S tank, Sparrow, legs.</summary>
    public static Design Hopper2()
    {
        var d = new Design { Name = "Hopper" };
        int core = d.Root("core-s");
        int t = d.Stack(core, "tank-s-long");
        int e = d.Stack(t, "eng-sparrow");
        d.Pair(t, "legs-s", LegDy(d, t));
        d.Stage(e);
        return d;
    }

    /// <summary>Debris sweeper: claw on the nose, core, RCS, a short tank and a Sparrow.</summary>
    public static Design ClawTanker()
    {
        var d = new Design { Name = "Sweeper" };
        int core = d.Root("core-s");
        d.Stack(core, "claw", below: false);
        int t = d.Stack(core, "tank-s-short");
        int e = d.Stack(t, "eng-sparrow");
        d.Pair(t, "rcs-tank-radial", 0.2);
        d.Pair(core, "rcs-block", 0);
        d.Pair(t, "rcs-block", -0.4);
        d.Stage(e);
        return d;
    }

    /// <summary>A spent tank: no core, so it is debris the moment it exists.</summary>
    public static Design Junk()
    {
        var d = new Design { Name = "Junk" };
        d.Root("tank-s-short");
        return d;
    }

    /// <summary>T10 outpost lander: Outpost Core, drills, M tank, Heron, legs.</summary>
    public static Design Outpost()
    {
        var d = new Design { Name = "Outpost" };
        int core = d.Root("outpost-core");
        d.Stack(core, "refinery-s", below: false);
        int st = d.Stack(core, "surface-tank");
        int t = d.Stack(st, "tank-m-medium");
        int e = d.Stack(t, "eng-heron");
        d.Pair(core, "drill", 0);
        d.Pair(t, "solar", 1.2);
        d.Pair(t, "solar", 0.6);
        d.Pair(t, "battery", 0);
        d.Pair(t, "legs-m", LegDy(d, t));
        d.Stage(e);
        return d;
    }

    /// <summary>T10 on a budget: Outpost Core + drill + panels + battery on an S landing stage (adapter, long S tank, Sparrow, S legs). Refinery and storage come later.</summary>
    public static Design OutpostT10()
    {
        var d = new Design { Name = "Outpost T10" };
        int core = d.Root("outpost-core");
        int ad = d.Stack(core, "adapter-sm");
        int t = d.Stack(ad, "tank-s-long");
        int e = d.Stack(t, "eng-sparrow");
        d.Pair(core, "drill", 0);
        d.Pair(t, "solar", 0.9);
        d.Pair(t, "battery", 0.2);
        d.Pair(t, "legs-s", LegDy(d, t));
        d.Stage(e);
        return d;
    }

    /// <summary>Reentry probe: core, short tank, Sparrow, heat shield below, nose chute; used for deorbit/landing tests.</summary>
    public static Design Probe()
    {
        var d = new Design { Name = "Probe" };
        int core = d.Root("core-s");
        int chute = d.Stack(core, "chute-nose", below: false);
        int t = d.Stack(core, "tank-s-short");
        int e = d.Stack(t, "eng-sparrow");
        d.Pair(t, "legs-s", LegDy(d, t));
        d.Stage(e);
        d.Stage(chute);
        return d;
    }
}
