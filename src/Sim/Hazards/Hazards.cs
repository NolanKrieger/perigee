namespace Perigee.Sim;

public enum StormPhase { None, Warning, Active }

/// <summary>Orbital debris and solar storms (GDD §14). Debris: coreless objects on rails decay below 1.2× the atmosphere height, threaten
/// infrastructure off-screen with a daily hit chance (cascades capped), and collide for real inside the loaded bubble. Storms: ~1 per 40 days,
/// 1–3 days of warning, 0.5–2 days long; exposed standard cores are fried (25%) for 3–5 days, depot/outpost cores go offline 5 days, hardened parts are immune;
/// shelter = landed, inside a body's shadow, or within home's magnetosphere.</summary>
public static class Hazards
{
    public const double DecayBand = 1.2;           // × atmosphere height: below this periapsis debris sinks
    public const double DecayPerDay = 0.004;       // fraction of orbital speed lost per day at the top of the band (scaled up deeper)
    public const double HitRatePerDay = 0.006;     // per overlapping debris object, Standard preset (proposal, BALANCE.md)
    public const int ObjectCap = 2000;             // GDD §14 performance cap on tracked objects
    public const double CollisionSpeed = 3;        // m/s: slower contacts are bumps, not hits
    public const double StormMeanDays = 40;        // GDD §14 Normal rate
    public const double FryChance = 0.25;          // standard cores
    public const double FryDaysMin = 3, FryDaysMax = 5, InfraOfflineDays = 5;
    public const double WarnDaysMin = 1, WarnDaysMax = 3, DurationDaysMin = 0.5, DurationDaysMax = 2;
}

public sealed partial class World
{
    // ---------------------------------------------------------------- storms
    public StormPhase Storm = StormPhase.None;
    public double StormStart = double.NaN, StormEnd = double.NaN;
    public int StormsSoFar;
    public double StormFryChance = Hazards.FryChance;   // tests/scripts can force 1.0
    readonly HashSet<int> _struckThisStorm = new();
    int _lastStormStrikeDay = -1;

    /// <summary>Daily roll (GDD §14): Normal averages one storm per 40 days, scaled by the preset and the star's luminosity.</summary>
    void RollStorm()
    {
        if (Storm != StormPhase.None) return;
        double p = Preset.StormRate * Sys.Star.Luminosity / Hazards.StormMeanDays;
        if (!Rng.Chance(p)) return;
        ScheduleStorm(Rng.Range(Hazards.WarnDaysMin, Hazards.WarnDaysMax), Rng.Range(Hazards.DurationDaysMin, Hazards.DurationDaysMax));
    }

    /// <summary>Announce a storm `warnDays` ahead lasting `durationDays`. Warp stops and the HUD/map show it.</summary>
    public void ScheduleStorm(double warnDays, double durationDays)
    {
        StormStart = T + warnDays * Units.Day;
        StormEnd = StormStart + durationDays * Units.Day;
        Storm = warnDays <= 0 ? StormPhase.Active : StormPhase.Warning;
        _struckThisStorm.Clear();
        _lastStormStrikeDay = -1;
        StormsSoFar++;
        if (Storm == StormPhase.Warning) StopWarpFor($"SOLAR STORM in {warnDays:0.0} days (lasts {durationDays:0.0} d): land, hide behind a body, or stay within {Sys.Home.Magnetosphere / Sys.Home.Radius:0}× home radius");
        else { Emit(SimEventKind.Info, Active ?? new Craft { Id = -1 }, "SOLAR STORM has begun"); StormStrike(); }
    }

    void StormStep()
    {
        if (Storm == StormPhase.Warning && T >= StormStart)
        {
            Storm = StormPhase.Active;
            StopWarpFor($"SOLAR STORM has begun (lasts {(StormEnd - T) / Units.Day:0.0} d)");
            StormStrike();
        }
        if (Storm == StormPhase.Active)
        {
            int day = (int)Math.Floor(T / Units.Day);
            if (day != _lastStormStrikeDay) StormStrike();   // craft that come out of shelter later in the storm roll too
            if (T >= StormEnd) { Storm = StormPhase.None; if (_struckThisStorm.Count == 0) Count("storms_clean"); Count("storms_survived"); Emit(SimEventKind.Info, Active ?? new Craft { Id = -1 }, "Solar storm is over"); }
        }
        // Reboots.
        foreach (var c in Crafts)
        {
            if (c.Destroyed) continue;
            foreach (var p in c.Parts)
                if (p.Offline && !double.IsNaN(p.OfflineUntil) && T >= p.OfflineUntil) { p.Offline = false; p.OfflineUntil = double.NaN; Emit(SimEventKind.Info, c, $"{c.Name}: {p.Def.Name} is back online"); }
        }
    }

    /// <summary>Is this craft sheltered from the storm right now (GDD §14)?</summary>
    public bool StormSafe(Craft c)
    {
        if (c.Mode == CraftMode.Landed) return true;
        var home = Sys.Home;
        double mag = home.Magnetosphere > 0 ? home.Magnetosphere : 3 * home.Radius;
        if ((AbsolutePosition(c) - Sys.Position(home.Id, T)).Length <= mag) return true;
        return InShadow(c);
    }

    void StormStrike()
    {
        _lastStormStrikeDay = (int)Math.Floor(T / Units.Day);
        foreach (var c in Crafts)
        {
            if (c.Destroyed || c.Debris || _struckThisStorm.Contains(c.Id) || StormSafe(c)) continue;
            _struckThisStorm.Add(c.Id);
            bool hardenedCore = c.Parts.Any(p => !p.Destroyed && p.Def.Has(PartFlags.ProbeCore) && p.Def.Has(PartFlags.Hardened));
            bool fried = false;
            foreach (var p in c.Parts)
            {
                if (p.Destroyed || p.Def.Has(PartFlags.Hardened)) continue;
                if (p.Def.Has(PartFlags.DepotController) || p.Def.Has(PartFlags.OutpostCore)) { Knock(p, Hazards.InfraOfflineDays); fried = true; }
                else if (p.Def.Has(PartFlags.ProbeCore) && !hardenedCore) { if (!fried && !Rng.Chance(StormFryChance)) { fried = false; break; } Knock(p, Rng.Range(Hazards.FryDaysMin, Hazards.FryDaysMax)); fried = true; }
            }
            if (fried) Emit(SimEventKind.Info, c, $"{c.Name}: storm knocked its core out — dark until it reboots");
        }
    }

    void Knock(Part p, double days) { p.Offline = true; p.OfflineUntil = Math.Max(double.IsNaN(p.OfflineUntil) ? 0 : p.OfflineUntil, T + days * Units.Day); }

    // ---------------------------------------------------------------- debris
    public int TrackedObjects => Crafts.Count(c => !c.Destroyed);
    public IEnumerable<Craft> DebrisAt(int bodyId) => Crafts.Where(c => !c.Destroyed && c.IsDebris && c.BodyId == bodyId && c.Mode != CraftMode.Landed);

    /// <summary>Daily debris step (GDD §14): decay of low debris, off-screen hit rolls on craft sharing an altitude band.</summary>
    void DebrisDay()
    {
        var active = Active;
        foreach (var d in Crafts.ToList())
        {
            if (d.Destroyed || !d.IsDebris || d.Mode != CraftMode.Rails) continue;
            var body = Sys[d.BodyId];
            if (body.Atmo == null) continue;
            var conic = d.Rails.Conic;
            if (!conic.IsEllipse) continue;
            double periAlt = conic.Periapsis - body.Radius;
            double band = Hazards.DecayBand * body.Atmo.Height;
            if (periAlt >= band) continue;
            if (active != null && active.BodyId == d.BodyId && Distance(active, d) <= LoadedBubble) continue;   // loaded debris feels real drag
            double depth = MathD.Clamp((band - periAlt) / (band - body.Atmo.Height), 0.2, 3);
            var (r, v) = d.StateAt(T);
            d.Pos = r; d.Vel = v * (1 - Hazards.DecayPerDay * depth); d.Mode = CraftMode.Active;
            d.PutOnRails(Sys, T);
            d.Next = null;
        }
        // Hits.
        foreach (var t in Crafts.ToList())
        {
            if (t.Destroyed || t.IsDebris || t.Npc || t.Mode != CraftMode.Rails || !t.Rails.Conic.IsEllipse) continue;
            double tLo = t.Rails.Conic.Periapsis, tHi = t.Rails.Conic.Apoapsis;
            double vT = Math.Sqrt(Sys[t.BodyId].Gm / (0.5 * (tLo + tHi)));
            double rate = 0;
            foreach (var d in DebrisAt(t.BodyId))
            {
                if (d.Mode != CraftMode.Rails || !d.Rails.Conic.IsEllipse) continue;
                double dLo = d.Rails.Conic.Periapsis, dHi = d.Rails.Conic.Apoapsis;
                if (dHi < tLo || dLo > tHi) continue;
                double vD = Math.Sqrt(Sys[d.BodyId].Gm / (0.5 * (dLo + dHi)));
                double rel = Math.Abs(vT - vD) + 0.5 * (d.Rails.Conic.E + t.Rails.Conic.E) * vT;   // crossing orbits meet faster
                rate += Hazards.HitRatePerDay * Preset.DebrisRate * MathD.Clamp(0.3 + rel / 300, 0.3, 2.0);
            }
            if (rate <= 0) continue;
            if (Rng.Chance(1 - Math.Exp(-rate))) DebrisHit(t);
        }
    }

    /// <summary>A hit destroys one random part; the pieces become debris (a cascade) unless the object cap is reached.</summary>
    public void DebrisHit(Craft t)
    {
        var alive = t.Parts.Where(p => !p.Destroyed).ToList();
        if (alive.Count == 0) return;
        var victim = alive[Rng.Next(alive.Count)];
        DestroyPart(t, victim, $"{victim.Def.Name} was hit by debris");
        Emit(SimEventKind.Info, t, $"DEBRIS HIT: {t.Name} lost its {victim.Def.Name}");
        if (TrackedObjects >= Hazards.ObjectCap) { CheckDestroyedNoSplit(t); return; }
        CheckDestroyed(t);
    }

    /// <summary>Over the object cap: the craft keeps only its largest connected piece, the rest is simply gone.</summary>
    void CheckDestroyedNoSplit(Craft t)
    {
        int before = Crafts.Count;
        CheckDestroyed(t);
        // Anything newly spawned by the split is dropped again.
        for (int i = Crafts.Count - 1; i >= before; i--) { Crafts[i].Destroyed = true; Crafts.RemoveAt(i); }
    }

    /// <summary>Real collisions inside the loaded bubble (GDD §14: near the active craft debris is real physics). A piece of debris whose part
    /// circles overlap a craft's while *closing* faster than a few m/s destroys the two touching parts. Controlled craft never damage each other
    /// (a clumsy docking is gated by the capture speed instead), and a freshly staged booster is separating, not closing.</summary>
    void CollisionStep()
    {
        var loaded = LoadedCrafts().Where(c => c.Mode == CraftMode.Active).ToList();
        for (int i = 0; i < loaded.Count; i++)
            for (int j = i + 1; j < loaded.Count; j++)
            {
                var a = loaded[i]; var b = loaded[j];
                if (a.Capture != null || b.Capture != null) continue;
                if (!a.IsDebris && !b.IsDebris) continue;
                var relPos = b.Pos - a.Pos; var relVel = b.Vel - a.Vel;
                if (relPos.LengthSq < 1e-9) continue;
                double closing = -Vec2d.Dot(relVel, relPos.Normalized());
                if (closing < Hazards.CollisionSpeed) continue;
                double rel = closing;
                if (relPos.Length > a.Height() + b.Height() + 5) continue;
                Part? pa = null, pb = null; double best = double.PositiveInfinity;
                foreach (var x in a.Parts)
                {
                    if (x.Destroyed) continue;
                    var wx = a.PartWorld(x); double rx = 0.5 * Math.Max(x.Def.Width, x.Def.Height);
                    foreach (var y in b.Parts)
                    {
                        if (y.Destroyed) continue;
                        double gap = (wx - b.PartWorld(y)).Length - rx - 0.5 * Math.Max(y.Def.Width, y.Def.Height);
                        if (gap < best) { best = gap; pa = x; pb = y; }
                    }
                }
                if (pa == null || pb == null || best > 0) continue;
                DestroyPart(a, pa, $"{pa.Def.Name} smashed by {b.Name} at {rel:0} m/s");
                DestroyPart(b, pb, $"{pb.Def.Name} smashed by {a.Name} at {rel:0} m/s");
                Emit(SimEventKind.Crashed, a, $"COLLISION: {a.Name} and {b.Name} at {rel:0} m/s");
                // Exchange a little momentum so the wrecks drift apart instead of re-hitting every tick.
                var n = (b.Pos - a.Pos).Normalized();
                double ma = a.Mass, mb = b.Mass; double push = Math.Min(rel, 20) * 0.5;
                a.Vel -= n * push * mb / (ma + mb); b.Vel += n * push * ma / (ma + mb);
                CheckDestroyed(a); CheckDestroyed(b);
            }
    }

    // ---------------------------------------------------------------- hooks
    void HazardsDay() { DebrisDay(); RollStorm(); }
}
