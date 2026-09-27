namespace Perigee.Sim;

public enum NodeKind { HomeSurface, LowOrbit, PadSurface }

/// <summary>A market node (GDD §10): home surface buys and sells at fixed prices; low orbits and owned off-world pads only buy.</summary>
public sealed class MarketNode
{
    public NodeKind Kind;
    public int BodyId;
    public double DeltaVFromHome;           // m/s from the home surface to this node (sets the price multiplier)
    /// <summary>Saturation per resource: (glut tonnes, time stamp); decays with a 10-day half-life.</summary>
    public Dictionary<Resource, (double glut, double at)> Glut = new();
    public string Name = "";

    public double Multiplier => Kind == NodeKind.HomeSurface ? 1 : 1 + 5 * (Math.Exp(DeltaVFromHome / 3000) - 1);

    public double GlutAt(Resource r, double t)
    {
        if (!Glut.TryGetValue(r, out var g)) return 0;
        return g.glut * Math.Pow(0.5, (t - g.at) / Market.GlutHalfLife);
    }
}

/// <summary>Prices, saturation and sales (GDD §10). Only products trade: water, methalox, RCS propellant, xenon, metal.</summary>
public sealed class Market
{
    public const double GlutHalfLife = 10 * Units.Day;
    public List<MarketNode> Nodes = new();
    /// <summary>Daily price snapshots for the HQ ledger's 30-day trend: (day, node index, resource, price cents/t).</summary>
    public List<(int day, int node, Resource res, long price)> History = new();
    public double DepthScale = 1;

    public static bool Sellable(Resource r) => r is Resource.Water or Resource.Methalox or Resource.Rcs or Resource.Xenon or Resource.Metal;

    /// <summary>Home surface prices, cents per tonne (GDD §10 anchors; water and metal are proposals).</summary>
    public static long BasePrice(Resource r) => r switch
    {
        Resource.Methalox => 100_000, Resource.Rcs => 300_000, Resource.Xenon => 4_000_000, Resource.Water => 40_000, Resource.Metal => 250_000, _ => 0
    };

    /// <summary>Node depth K in tonnes (GDD example: 50 t of methalox in low home orbit).</summary>
    public static double BaseDepth(Resource r, NodeKind kind) => (r switch { Resource.Methalox => 50.0, Resource.Water => 40.0, Resource.Metal => 20.0, Resource.Rcs => 15.0, Resource.Xenon => 2.0, _ => 10.0 }) * (kind == NodeKind.LowOrbit ? 1 : 0.6);

    public double Depth(Resource r, MarketNode n) => BaseDepth(r, n.Kind) * DepthScale;

    public void Build(StarSystem sys)
    {
        Nodes.Clear();
        Nodes.Add(new MarketNode { Kind = NodeKind.HomeSurface, BodyId = sys.HomeId, DeltaVFromHome = 0, Name = $"{sys.Home.Name} surface" });
        foreach (var b in sys.Bodies)
        {
            if (b.Type == BodyType.Star) continue;
            double dv = DeltaV.HomeSurfaceToLowOrbitOf(sys, b.Id);
            Nodes.Add(new MarketNode { Kind = NodeKind.LowOrbit, BodyId = b.Id, DeltaVFromHome = dv, Name = $"{b.Name} low orbit" });
        }
    }

    public MarketNode? PadNode(int bodyId) => Nodes.FirstOrDefault(n => n.Kind == NodeKind.PadSurface && n.BodyId == bodyId);
    public MarketNode? OrbitNode(int bodyId) => Nodes.FirstOrDefault(n => n.Kind == NodeKind.LowOrbit && n.BodyId == bodyId);
    public MarketNode Home => Nodes[0];

    /// <summary>Add the surface node of a body where the player owns an off-world pad (GDD decision #42).</summary>
    public MarketNode AddPadNode(StarSystem sys, int bodyId)
    {
        var existing = PadNode(bodyId);
        if (existing != null) return existing;
        var b = sys[bodyId];
        var n = new MarketNode { Kind = NodeKind.PadSurface, BodyId = bodyId, DeltaVFromHome = DeltaV.HomeSurfaceToLowOrbitOf(sys, bodyId) + DeltaV.LandingFromLowOrbit(b), Name = $"{b.Name} surface" };
        Nodes.Add(n);
        return n;
    }

    /// <summary>Current unit price (cents/t) for the next tonne sold at a node.</summary>
    public long Price(MarketNode n, Resource r, double t) => (long)(BasePrice(r) * n.Multiplier / (1 + n.GlutAt(r, t) / Depth(r, n)));

    /// <summary>Revenue for selling `tonnes` now, integrating the falling price: base·M·K·ln((K+G+q)/(K+G)). Home surface has unlimited depth.</summary>
    public long Quote(MarketNode n, Resource r, double tonnes, double t)
    {
        if (tonnes <= 0 || !Sellable(r)) return 0;
        if (n.Kind == NodeKind.HomeSurface) return (long)(BasePrice(r) * tonnes);
        double k = Depth(r, n), g = n.GlutAt(r, t);
        return (long)(BasePrice(r) * n.Multiplier * k * Math.Log((k + g + tonnes) / (k + g)));
    }

    /// <summary>Execute a sale: adds to the node's glut. Returns the revenue in cents.</summary>
    public long Sell(MarketNode n, Resource r, double tonnes, double t)
    {
        long rev = Quote(n, r, tonnes, t);
        if (n.Kind != NodeKind.HomeSurface) n.Glut[r] = (n.GlutAt(r, t) + tonnes, t);
        return rev;
    }

    /// <summary>The node a craft can sell at, if any: home surface when landed on home; the orbit band of its body when coasting in it; an owned pad's surface when landed there.</summary>
    public MarketNode? NodeFor(StarSystem sys, Craft c, double t)
    {
        var body = sys[c.BodyId];
        if (c.Mode == CraftMode.Landed)
        {
            if (c.BodyId == sys.HomeId) return Home;
            return PadNode(c.BodyId);
        }
        var (r, v) = c.StateAt(t);
        var conic = c.Mode == CraftMode.Rails ? c.Rails.Conic : Conic.FromState(body.Gm, r, v, t);
        double floor = body.Radius + body.ActiveZoneHeight;
        if (conic.IsEllipse && conic.Periapsis >= floor && conic.Apoapsis <= body.LowOrbitTop) return OrbitNode(c.BodyId);
        return null;
    }

    public void Snapshot(int day)
    {
        History.RemoveAll(h => h.day < day - 60);
        double t = day * Units.Day;
        for (int i = 0; i < Nodes.Count; i++)
            foreach (var r in new[] { Resource.Methalox, Resource.Rcs, Resource.Xenon, Resource.Water, Resource.Metal })
                History.Add((day, i, r, Price(Nodes[i], r, t)));
    }
}
