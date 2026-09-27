namespace Perigee.Sim;

/// <summary>A booster frozen at its separation state, waiting for its flashback (GDD §4, decision #37).</summary>
public sealed class HeldBooster
{
    public int CraftId;
    public double SepTime;
    public string Name = "";
}

/// <summary>Booster flashback, drone ship and refunds (GDD §4 booster flashback, §6 recovery).</summary>
public sealed partial class World
{
    public List<HeldBooster> HeldBoosters = new();
    public bool InFlashback;
    public double FlashbackReturnTime;
    int _flashbackPrevActive = -1;
    public int FlashbackCraftId = -1;
    /// <summary>Drone ships at sea (body id, body-fixed angle). Landing within reach of one refunds 85%.</summary>
    public List<(int bodyId, double localAngle)> DroneShips = new();
    public const double DroneShipReach = 40;      // m from its centre
    public const double LandingZoneRadius = 5000; // m around a pad counts as "your pad or landing zone"
    public long LastRefund;

    /// <summary>The GDD §6 refund share for where a craft sits now (0 when it cannot be recovered).</summary>
    public double RefundRate(Craft c)
    {
        if (c.Mode != CraftMode.Landed || c.Landed is not { } L) return 0;
        var body = Sys[c.BodyId];
        if (c.BodyId != Sys.HomeId) return PadNear(c) != null ? 0.90 : 0;   // GDD §6: one of your off-world pads
        if (L.OnDroneShip) return 0.85;
        if (L.Water) return 0;
        double arc = Math.Abs(MathD.WrapPi(L.LocalAngle - body.LaunchSiteAngle)) * body.Radius;
        return arc <= LandingZoneRadius ? 0.90 : 0.50;
    }

    public static long PropellantPrice(Resource r) => r switch { Resource.Methalox => 100_000, Resource.Rcs => 300_000, Resource.Xenon => 4_000_000, _ => 0 };   // cents per t at home (GDD §10)

    /// <summary>Cash value of recovering the craft now: parts × rate + leftover propellant at the local price.</summary>
    public long RecoveryValue(Craft c)
    {
        double rate = RefundRate(c);
        if (rate <= 0) return 0;
        if (c.BodyId != Sys.HomeId) return (long)(c.PartsCost * Pads.CashShare * rate);   // off-world: the cash share only; metal and propellant go back into the outpost's stores
        long parts = (long)(c.PartsCost * rate);
        long fuel = 0;
        foreach (var r in new[] { Resource.Methalox, Resource.Rcs, Resource.Xenon }) fuel += (long)(c.Resource(r) * PropellantPrice(r));
        return parts + fuel;
    }

    /// <summary>Recover a landed craft: cash in, craft gone (GDD §6: no used-parts inventory).</summary>
    public bool Recover(Craft c)
    {
        long value = RecoveryValue(c);
        if (value <= 0 && RefundRate(c) <= 0) return false;
        Cash += value;
        LastRefund = value;
        if (PadNear(c) is { } pad)
        {
            // Metal share of the intact parts and the leftover propellant return to the outpost (what does not fit is lost).
            double metal = c.Parts.Where(p => !p.Destroyed).Sum(p => p.Def.DryMass) * Pads.MetalPerTonne * RefundRate(c);
            PutInto(pad.Outpost, Resource.Metal, metal, except: c);
            foreach (var r in new[] { Resource.Methalox, Resource.Rcs, Resource.Xenon }) PutInto(pad.Outpost, r, c.Resource(r), except: c);
            foreach (var m in pad.Outpost.Members) m.UpdateMass();
        }
        OnRecovered(c, RefundRate(c));
        if (c.WasBooster) { if (RefundRate(c) >= 0.85) { if (c.DesignName == LastBoosterDesign) Count("booster_streak"); else { LastBoosterDesign = c.DesignName; CareerStats["booster_streak"] = 1; } } else CareerStats["booster_streak"] = 0; }
        c.Destroyed = true;
        Crafts.Remove(c);
        HeldBoosters.RemoveAll(h => h.CraftId == c.Id);
        if (ActiveCraftId == c.Id) ActiveCraftId = Crafts.FirstOrDefault(x => !x.Destroyed && !x.Debris)?.Id ?? -1;
        Emit(SimEventKind.Recovered, c, $"{c.Name} recovered for {Units.FormatMoney(value)} ({RefundRate(c) * 100:0}% of parts + propellant)");
        return true;
    }

    // ---------------------------------------------------------------- flashback

    /// <summary>A booster is a separated stage with engines and fuel but no probe core, dropped by the player's craft.</summary>
    static bool IsBooster(Craft c) => !c.HasControl && c.Parts.Any(p => !p.Destroyed && p.IsEngine) && c.Fuel > 0.05;

    void HoldBooster(Craft booster)
    {
        booster.Mode = CraftMode.Held;
        booster.HeldTime = T;
        booster.Debris = false;
        HeldBoosters.Add(new HeldBooster { CraftId = booster.Id, SepTime = T, Name = booster.Name });
        Emit(SimEventKind.Info, booster, $"{booster.Name} held for flashback");
    }

    /// <summary>The "Fly booster" prompt is offered once the upper stage is coasting on rails (or done).</summary>
    public bool FlashbackOffered => !InFlashback && HeldBoosters.Count > 0 && (Active == null || Active.Destroyed || Active.Mode is CraftMode.Rails or CraftMode.Landed);
    public HeldBooster? NextHeld => HeldBoosters.Count > 0 ? HeldBoosters[0] : null;

    /// <summary>Rewind to the booster's separation and hand it to the player. Everything else is frozen until it lands or is lost.</summary>
    public bool StartFlashback()
    {
        var h = NextHeld;
        if (h == null || InFlashback) return false;
        var b = Find(h.CraftId);
        if (b == null || b.Destroyed) { HeldBoosters.RemoveAt(0); return false; }
        InFlashback = true;
        FlashbackReturnTime = T;
        _flashbackPrevActive = ActiveCraftId;
        FlashbackCraftId = b.Id;
        T = h.SepTime;
        b.Mode = CraftMode.Active;
        b.FlashbackControl = true;
        b.WasBooster = true;
        b.CoastTimer = 0;
        ActiveCraftId = b.Id;
        WarpIndex = 0; PhysicsWarp = 1;
        Emit(SimEventKind.Info, b, $"Flying {b.Name} back from its separation");
        return true;
    }

    public void SkipBooster()
    {
        var h = NextHeld;
        if (h == null) return;
        HeldBoosters.RemoveAt(0);
        var b = Find(h.CraftId);
        if (b != null) { b.Destroyed = true; Crafts.Remove(b); Emit(SimEventKind.Crashed, b, $"{b.Name} was lost"); }
    }

    /// <summary>
    /// The booster has landed or is lost: the clock returns to the upper stage's time. If the booster flight took longer than the
    /// interval it replayed, everyone else (all coasting on rails, which is why the prompt waited) is advanced exactly to the landing time.
    /// </summary>
    void EndFlashback(string reason)
    {
        var b = Find(FlashbackCraftId);
        double tEnd = T;
        InFlashback = false;
        HeldBoosters.RemoveAll(x => x.CraftId == FlashbackCraftId);
        if (b != null && !b.Destroyed) b.FlashbackControl = false;
        FlashbackCraftId = -1;
        if (_flashbackPrevActive >= 0 && Find(_flashbackPrevActive) is { Destroyed: false }) ActiveCraftId = _flashbackPrevActive;
        else ActiveCraftId = Crafts.FirstOrDefault(x => !x.Destroyed && !x.Debris)?.Id ?? -1;
        T = FlashbackReturnTime;
        WarpIndex = 0; PhysicsWarp = 1;
        if (tEnd > T) AdvanceRailsTo(tEnd);   // rails are exact: catching everyone up to the landing time is safe
        Emit(SimEventKind.Info, b ?? new Craft { Id = -1, Name = "booster" }, $"Flashback over: {reason}");
    }

    /// <summary>Called every tick during a flashback: end it when the booster lands or dies.</summary>
    void FlashbackStep()
    {
        if (!InFlashback) return;
        var b = Find(FlashbackCraftId);
        if (b == null || b.Destroyed) { EndFlashback("booster lost"); return; }
        if (b.Mode == CraftMode.Landed) { EndFlashback(b.Landed?.Water == true && !b.Landed.Value.OnDroneShip ? "booster splashed down" : "booster landed"); return; }
        if (b.Mode == CraftMode.Rails) { EndFlashback("booster reached orbit"); return; }
    }

    /// <summary>Crafts to simulate/draw right now: during a flashback only the booster exists.</summary>
    public bool IsPresent(Craft c) => !InFlashback ? c.Mode != CraftMode.Held : c.Id == FlashbackCraftId;
}
