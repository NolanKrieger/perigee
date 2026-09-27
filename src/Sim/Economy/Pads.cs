namespace Perigee.Sim;

/// <summary>An off-world launch pad (GDD §9): an outpost whose modules include a Pad Kit and a Fabricator. Rockets are built there from
/// stored metal (1.5 × part mass) plus half the cash price, fuelled from the outpost's tanks, and launched from beside the kit.</summary>
public sealed class LaunchPad
{
    public Outpost Outpost = null!;
    public Body Body = null!;
    public double LocalAngle;             // launch spot (surface angle), a little downrange of the kit
    public string Name => $"{Body.Name} pad · {Outpost.Name}";
    public const double HeightLimit = 30, MassLimit = 250;   // proposal (BALANCE.md)
}

/// <summary>What an off-world launch would take.</summary>
public sealed record LaunchQuote(long Cash, double MetalNeeded, double MetalAvailable, Dictionary<Resource, (double need, double have)> Propellant)
{
    public bool Affordable(long cash) => cash >= Cash && MetalAvailable + 1e-9 >= MetalNeeded;
}

public static class Pads
{
    public const double MetalPerTonne = 1.5;   // t of metal per t of part mass (GDD §9, decided)
    public const double CashShare = 0.5;       // of the part's unit price (GDD §9, decided)
    public const double SpotOffset = 30;       // m along the surface from the Pad Kit
    public const double FlatRadius = 120;      // m of levelled ground around the kit (proposal, BALANCE.md)
}

public sealed partial class World
{
    /// <summary>Operational pads right now: outposts with a working Pad Kit and Fabricator among their modules.</summary>
    public List<LaunchPad> LaunchPads()
    {
        var pads = new List<LaunchPad>();
        foreach (var o in Outposts())
        {
            if (o.Offline) continue;
            var kit = o.Members.SelectMany(c => c.Parts.Select(p => (c, p))).FirstOrDefault(x => !x.p.Destroyed && !x.p.Offline && x.p.Def.Has(PartFlags.PadKit));
            bool fab = o.Parts.Any(p => !p.Offline && p.Def.Has(PartFlags.Fabricator));
            if (kit.p == null || !fab) continue;
            double dir = o.Body.RotationPeriod < 0 ? -1 : 1;   // downrange = with the rotation
            double kitAngle = kit.c.Landed!.Value.LocalAngle;
            o.Body.Flatten(kitAngle, Pads.FlatRadius);          // the kit is a levelled platform
            pads.Add(new LaunchPad { Outpost = o, Body = o.Body, LocalAngle = MathD.WrapAngle(kitAngle + dir * Pads.SpotOffset / o.Body.Radius) });
        }
        return pads;
    }

    public LaunchPad? PadAt(int bodyId) => LaunchPads().FirstOrDefault(p => p.Body.Id == bodyId);

    /// <summary>The market node a craft can sell at: home surface, a low-orbit band, or — landed off-world — the pad of the outpost it belongs to.</summary>
    public MarketNode? NodeFor(Craft c)
    {
        if (c.Mode == CraftMode.Landed && c.BodyId != Sys.HomeId)
        {
            var pad = PadAt(c.BodyId);
            if (pad == null || !pad.Outpost.Members.Contains(c)) return null;
            return Market.AddPadNode(Sys, c.BodyId);
        }
        return Market.NodeFor(Sys, c, T);
    }

    /// <summary>Cost of launching a design from an off-world pad: half the cash price, metal by mass, propellant from the outpost's stores.</summary>
    public LaunchQuote PadQuote(Design d, LaunchPad pad)
    {
        long cash = 0; double metal = 0;
        var prop = new Dictionary<Resource, (double need, double have)>();
        foreach (var dp in d.Parts)
        {
            var def = Sim.Parts.Get(dp.DefId);
            cash += (long)(PartPrice(def) * Pads.CashShare);
            metal += def.DryMass * Pads.MetalPerTonne;
            if (def.StorageT > 0) continue;
            foreach (var (r, cap) in def.Capacity) if (r is Resource.Methalox or Resource.Rcs or Resource.Xenon) prop[r] = (prop.GetValueOrDefault(r).need + cap, 0);
        }
        foreach (var r in prop.Keys.ToList()) prop[r] = (prop[r].need, pad.Outpost.Stored(r));
        return new LaunchQuote(cash, metal, pad.Outpost.Stored(Resource.Metal), prop);
    }

    /// <summary>Launch from an off-world pad (GDD §9): pays the cash share, takes the metal from storage, fills tanks from the outpost's stores (partially if short).</summary>
    public Craft? TryLaunchAtPad(Design d, string name, LaunchPad pad)
    {
        var locked = LockedParts(d).ToList();
        if (locked.Count > 0) { Emit(SimEventKind.Info, Active ?? new Craft { Id = -1 }, $"Locked parts: {string.Join(", ", locked.Select(id => Sim.Parts.Get(id).Name))}"); return null; }
        var q = PadQuote(d, pad);
        if (q.MetalAvailable + 1e-9 < q.MetalNeeded) { Emit(SimEventKind.Info, Active ?? new Craft { Id = -1 }, $"Not enough metal at {pad.Name}: need {q.MetalNeeded:0.0} t, have {q.MetalAvailable:0.0} t"); return null; }
        if (!TrySpend(q.Cash, $"launch of {d.Name} at {pad.Name}")) return null;
        TakeFrom(pad.Outpost, Resource.Metal, q.MetalNeeded);
        var c = Launch(d, name, pad.Body.Id, pad.LocalAngle);
        Count("pad_launches");
        // Local fuel: empty the factory-filled tanks, then draw from the outpost's stores.
        foreach (var p in c.Parts) foreach (var r in new[] { Resource.Methalox, Resource.Rcs, Resource.Xenon }) if (p.Capacity(r) > 0) p.Set(r, 0);
        foreach (var r in new[] { Resource.Methalox, Resource.Rcs, Resource.Xenon })
        {
            foreach (var p in c.Parts)
            {
                if (p.Def.StorageT > 0 || p.Capacity(r) <= 0) continue;
                double want = p.Capacity(r), have = pad.Outpost.Stored(r);
                double take = Math.Min(want, have);
                if (take <= 0) continue;
                TakeFrom(pad.Outpost, r, take);
                p.Set(r, take);
            }
        }
        c.UpdateMass();
        c.LaunchCostPaid = q.Cash;
        foreach (var m in pad.Outpost.Members) m.UpdateMass();
        Emit(SimEventKind.Info, c, $"{name} built at {pad.Name}: {Units.FormatMoney(q.Cash)} + {q.MetalNeeded:0.0} t of metal");
        return c;
    }

    static void TakeFrom(Outpost o, Resource r, double amount)
    {
        double left = amount;
        foreach (var p in o.Parts)
        {
            if (left <= 1e-12) break;
            double take = Math.Min(p.Get(r), left);
            p.Set(r, p.Get(r) - take); left -= take;
        }
    }

    static double PutInto(Outpost o, Resource r, double amount, Craft? except = null)
    {
        double left = amount;
        foreach (var p in o.Members.Where(m => m != except).SelectMany(m => m.Parts).Where(p => !p.Destroyed).OrderBy(p => p.Def.StorageT > 0 ? 1 : 0))
        {
            if (left <= 1e-12) break;
            double room = p.Room(r);
            if (room <= 0) continue;
            double put = Math.Min(room, left);
            p.Set(r, p.Get(r) + put); left -= put;
        }
        return amount - left;
    }

    /// <summary>Pad within the landing zone of a landed off-world craft (for the 90% refund, GDD §6).</summary>
    public LaunchPad? PadNear(Craft c)
    {
        if (c.Mode != CraftMode.Landed || c.Landed is not { } L || c.BodyId == Sys.HomeId) return null;
        foreach (var pad in LaunchPads())
            if (pad.Body.Id == c.BodyId && Math.Abs(MathD.WrapPi(L.LocalAngle - pad.LocalAngle)) * pad.Body.Radius <= LandingZoneRadius && pad.Outpost.Core != c) return pad;   // anything but the core itself (scrapping a module is allowed)
        return null;
    }
}
