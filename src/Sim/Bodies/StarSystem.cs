namespace Perigee.Sim;

/// <summary>All bodies of one career. Absolute positions are computed on demand from the conic chain and cached per time stamp.</summary>
public sealed class StarSystem
{
    public List<Body> Bodies = new();
    public ulong Seed;
    public string StarName = "";
    public double StormRateFactor = 1;
    public int HomeId = -1;

    readonly Dictionary<int, List<int>> _children = new();
    double _cacheT = double.NaN;
    Vec2d[] _cachePos = Array.Empty<Vec2d>(), _cacheVel = Array.Empty<Vec2d>();

    public Body this[int id] => Bodies[id];
    public Body Home => Bodies[HomeId];
    public Body Star => Bodies[0];

    public int Add(Body b)
    {
        b.Id = Bodies.Count;
        Bodies.Add(b);
        _children.Clear();
        _cacheT = double.NaN;
        return b.Id;
    }

    public void Build()
    {
        _children.Clear();
        foreach (var b in Bodies)
        {
            if (!_children.ContainsKey(b.Id)) _children[b.Id] = new List<int>();
            if (b.Parent >= 0)
            {
                if (!_children.ContainsKey(b.Parent)) _children[b.Parent] = new List<int>();
                _children[b.Parent].Add(b.Id);
                var p = Bodies[b.Parent];
                // Sphere of influence: a·(m/M)^(2/5) with the mean orbital radius.
                double a = b.Orbit!.Value.IsEllipse ? b.Orbit.Value.A : b.Orbit.Value.Periapsis;
                b.Soi = a * Math.Pow(b.Gm / p.Gm, 0.4);
            }
            else b.Soi = double.PositiveInfinity;
            b.TerrainMax = b.Terrain.Length > 0 ? b.Terrain.Max() : 0;
        }
        _cacheT = double.NaN;
    }

    public IReadOnlyList<int> ChildrenOf(int id) => _children.TryGetValue(id, out var l) ? l : Array.Empty<int>();

    /// <summary>Position and velocity of a body relative to its parent (zero for the star).</summary>
    public (Vec2d r, Vec2d v) LocalState(int id, double t) => Bodies[id].Orbit is { } c ? c.StateAt(t) : (Vec2d.Zero, Vec2d.Zero);

    void Fill(double t)
    {
        if (_cacheT == t && _cachePos.Length == Bodies.Count) return;
        if (_cachePos.Length != Bodies.Count) { _cachePos = new Vec2d[Bodies.Count]; _cacheVel = new Vec2d[Bodies.Count]; }
        // Bodies are added parents-first, so one pass suffices.
        for (int i = 0; i < Bodies.Count; i++)
        {
            var b = Bodies[i];
            var (r, v) = LocalState(i, t);
            if (b.Parent >= 0) { r += _cachePos[b.Parent]; v += _cacheVel[b.Parent]; }
            _cachePos[i] = r; _cacheVel[i] = v;
        }
        _cacheT = t;
    }

    /// <summary>Absolute (star-centred inertial) position at time t.</summary>
    public Vec2d Position(int id, double t) { Fill(t); return _cachePos[id]; }
    public Vec2d Velocity(int id, double t) { Fill(t); return _cacheVel[id]; }

    /// <summary>State of body `id` relative to body `frame` (both absolute in the star frame, subtracted).</summary>
    public (Vec2d r, Vec2d v) RelativeState(int id, int frame, double t)
    {
        Fill(t);
        return (_cachePos[id] - _cachePos[frame], _cacheVel[id] - _cacheVel[frame]);
    }

    public bool IsAncestor(int ancestor, int id)
    {
        for (int b = Bodies[id].Parent; b >= 0; b = Bodies[b].Parent) if (b == ancestor) return true;
        return false;
    }
}
