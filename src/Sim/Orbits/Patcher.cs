namespace Perigee.Sim;

public enum PatchEnd { None, ExitSoi, EnterSoi, ActiveZone, Horizon }

/// <summary>One conic leg of a predicted path: around BodyId from TStart to TEnd, ending with an event.</summary>
public sealed class Patch
{
    public int BodyId;
    public Conic Conic;
    public double TStart, TEnd;
    public PatchEnd End;
    public int NextBodyId = -1;
    /// <summary>State (relative to BodyId) at TEnd.</summary>
    public Vec2d EndPos, EndVel;
}

/// <summary>Where a coasting craft is: a conic around one body.</summary>
public struct RailsState
{
    public int BodyId;
    public Conic Conic;
    public RailsState(int bodyId, Conic conic) { BodyId = bodyId; Conic = conic; }
    public (Vec2d r, Vec2d v) StateAt(double t) => Conic.StateAt(t);
}

/// <summary>
/// Patched conics: finds the next sphere-of-influence change (or atmosphere/surface arrival) analytically, converts the state
/// into the new body's frame, and builds multi-leg previews. Encounter search uses a "safe step" (distance / max relative speed),
/// so an encounter can't be stepped over, then bisection to sub-millisecond time.
/// </summary>
public static class Patcher
{
    public const double MinStep = 0.25;
    public const double TimeTol = 1e-7;

    /// <summary>Next event for a craft on rails from t (exclusive of t itself) up to tMax. Returns End = Horizon when nothing happens before tMax.</summary>
    public static Patch NextEvent(StarSystem sys, RailsState s, double t, double tMax, double zoneAfter = double.NegativeInfinity)
    {
        var body = sys[s.BodyId];
        var c = s.Conic;
        var patch = new Patch { BodyId = s.BodyId, Conic = c, TStart = t, TEnd = tMax, End = PatchEnd.Horizon };

        // Leaving the SOI (outbound crossing of r = Soi).
        if (!double.IsPositiveInfinity(body.Soi))
        {
            double nuExit = c.TrueAnomalyAtRadius(body.Soi);
            double nuNow = c.TrueAnomalyAt(t);
            double rNow = c.RadiusAt(nuNow);
            if (!double.IsNaN(nuExit))
            {
                double te;
                if (rNow > body.Soi * (1 + 1e-9) && nuNow > 0) te = t;             // outside and outbound: leave now
                else te = Math.Max(t, c.TimeAtTrueAnomaly(nuExit, t));
                if (te < patch.TEnd) { patch.TEnd = te; patch.End = PatchEnd.ExitSoi; patch.NextBodyId = body.Parent; }
            }
            else if (c.Periapsis > body.Soi)
            {
                // Never inside this SOI at all: leave immediately.
                patch.TEnd = t; patch.End = PatchEnd.ExitSoi; patch.NextBodyId = body.Parent;
            }
        }

        // Falling into the atmosphere / onto the surface (inbound crossing of the active-zone radius).
        double zone = body.Radius + body.ActiveZoneHeight;
        if (c.Periapsis < zone)
        {
            double nuZ = c.TrueAnomalyAtRadius(zone);
            if (!double.IsNaN(nuZ))
            {
                double nuNow = c.TrueAnomalyAt(t);
                double rNow = c.RadiusAt(nuNow);
                double after = Math.Max(t, zoneAfter);
                double tz = rNow < zone && nuNow < 0 && t >= zoneAfter ? t : Math.Max(after, c.TimeAtTrueAnomaly(-nuZ, after));
                if (tz < patch.TEnd) { patch.TEnd = tz; patch.End = PatchEnd.ActiveZone; patch.NextBodyId = s.BodyId; }
            }
        }

        // Entering a child's SOI.
        foreach (int cid in sys.ChildrenOf(s.BodyId))
        {
            var child = sys[cid];
            double te = FindEncounter(sys, c, child, t, patch.TEnd);
            if (!double.IsNaN(te) && te < patch.TEnd) { patch.TEnd = te; patch.End = PatchEnd.EnterSoi; patch.NextBodyId = cid; }
        }

        var (er, ev) = c.StateAt(patch.TEnd);
        patch.EndPos = er; patch.EndVel = ev;
        return patch;
    }

    /// <summary>Earliest time in (t, tEnd] at which the conic comes within child.Soi of the child, or NaN.</summary>
    static double FindEncounter(StarSystem sys, Conic c, Body child, double t, double tEnd)
    {
        var co = child.Orbit!.Value;
        double maxStep = Math.Min(co.Period / 24, c.IsEllipse ? c.Period / 24 : double.PositiveInfinity);
        if (maxStep < MinStep) maxStep = MinStep;
        double tt = t;
        // If we start touching/inside the child's SOI (just left it), walk out first so the exit isn't reported as an entry.
        int guardIn = 0;
        while (Gap(c, co, child.Soi, tt) <= 0 && tt < tEnd && guardIn++ < 100000) tt += MinStep;
        if (tt >= tEnd) return double.NaN;
        double tPrev = tt;
        int guard = 0;
        while (tt < tEnd && guard++ < 200000)
        {
            var (rc, vc) = c.StateAt(tt);
            var (rC, vC) = co.StateAt(tt);
            double d = (rc - rC).Length - child.Soi;
            if (d <= 0)
            {
                double lo = tPrev, hi = tt;
                for (int i = 0; i < 100 && hi - lo > TimeTol; i++)
                {
                    double mid = 0.5 * (lo + hi);
                    if (Gap(c, co, child.Soi, mid) <= 0) hi = mid; else lo = mid;
                }
                return hi;
            }
            double vmax = vc.Length + vC.Length + 1e-6;
            double step = 0.95 * d / vmax;
            if (step > maxStep) step = maxStep;
            if (step < MinStep) step = MinStep;
            tPrev = tt;
            tt += step;
            if (tt > tEnd) tt = tEnd;
            if (tt == tPrev) break;
        }
        return double.NaN;
    }

    static double Gap(Conic c, Conic co, double soi, double t) => (c.PositionAt(t) - co.PositionAt(t)).Length - soi;

    /// <summary>Convert a rails state at the event time into the next body's frame.</summary>
    public static RailsState Transition(StarSystem sys, Patch p)
    {
        if (p.End == PatchEnd.ExitSoi)
        {
            var (br, bv) = sys.LocalState(p.BodyId, p.TEnd);
            var r = p.EndPos + br; var v = p.EndVel + bv;
            return new RailsState(p.NextBodyId, Conic.FromState(sys[p.NextBodyId].Gm, r, v, p.TEnd));
        }
        if (p.End == PatchEnd.EnterSoi)
        {
            var (br, bv) = sys.LocalState(p.NextBodyId, p.TEnd);
            var r = p.EndPos - br; var v = p.EndVel - bv;
            return new RailsState(p.NextBodyId, Conic.FromState(sys[p.NextBodyId].Gm, r, v, p.TEnd));
        }
        return new RailsState(p.BodyId, p.Conic);
    }

    /// <summary>Preview: up to `maxPatches` legs ahead of time t (the current leg counts). Closed orbits with no event end after one period at Horizon.</summary>
    public static List<Patch> Predict(StarSystem sys, RailsState s, double t, int maxPatches = 3)
    {
        var list = new List<Patch>();
        var cur = s;
        double tt = t;
        for (int i = 0; i < maxPatches; i++)
        {
            double horizon = cur.Conic.IsEllipse ? tt + cur.Conic.Period : tt + 100 * Units.Year;
            var p = NextEvent(sys, cur, tt, horizon);
            list.Add(p);
            if (p.End is PatchEnd.Horizon or PatchEnd.ActiveZone or PatchEnd.None) break;
            cur = Transition(sys, p);
            tt = p.TEnd;
        }
        return list;
    }
}
