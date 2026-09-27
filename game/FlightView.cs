using Godot;
using Perigee.Sim;

namespace Perigee;

/// <summary>
/// Draws the world around a floating origin: every position is computed in doubles relative to <see cref="Focus"/> and only the
/// screen-space result is handed to Godot as float. One continuous zoom from part scale (~200 px/m) to the whole system (~1e-8 px/m):
/// the flight view becomes the map with no cut (GDD §13, decision #41).
/// </summary>
public partial class FlightView : Node2D
{
    public const float W = 1920, H = 1080;
    public static readonly Vector2 Centre = new(W / 2, H / 2);
    public const double MinScale = 1.5e-8, MaxScale = 400;

    public World? World;
    /// <summary>Absolute (star-frame) position drawn at the screen centre.</summary>
    public Vec2d Focus;
    /// <summary>Pixels per metre.</summary>
    public double Scale = 0.6;
    public int FocusCraft = -1;
    public int FocusBody = -1;
    /// <summary>Body-fixed anchor (local angle, radius) used when no craft is focused: the view rides the surface as the body rotates and orbits.</summary>
    public double FocusLocalAngle = double.NaN, FocusRadius;
    public int HoverBody = -1;
    public Vector2 MousePx;
    public bool ShowOrbits = true;
    /// <summary>Screen-space stars (parallax-free; drawn as tiny points).</summary>
    readonly List<(Vector2 p, float s, float a)> _stars = new();
    ColorRect _atmoRect = null!;
    ShaderMaterial _atmoMat = null!;
    SceneLayer _scene = null!;
    readonly List<(Vector2 pos, string text, Font font, int size, Color col)> _labels = new();

    public bool IsMapScale => Scale < 0.02;
    /// <summary>Screen rotation (rad): at flight scale the view turns so local "up" (away from the focus body) points up the screen; the map keeps +Y up.</summary>
    public double ViewRotation { get; private set; }
    float _time;

    public override void _Ready()
    {
        var rng = new Random(9);
        for (int i = 0; i < 420; i++)
            _stars.Add((new Vector2((float)rng.NextDouble() * W, (float)rng.NextDouble() * H), 0.6f + (float)rng.NextDouble() * 1.4f, 0.25f + (float)rng.NextDouble() * 0.6f));
        _atmoMat = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/atmo.gdshader") };
        // Draw order: this node's own _Draw (background + stars) → atmosphere glow rect → scene layer (bodies, orbits, craft, labels).
        _atmoRect = new ColorRect { Position = Vector2.Zero, Size = new Vector2(W, H), Material = _atmoMat, MouseFilter = Control.MouseFilterEnum.Ignore };
        AddChild(_atmoRect);
        _scene = new SceneLayer { View = this };
        AddChild(_scene);
    }

    // ---------------------------------------------------------------- transforms

    public Vector2 ToScreen(Vec2d abs)
    {
        var d = ((abs - Focus) * Scale).Rotated(-ViewRotation);
        return new Vector2((float)d.X + Centre.X, Centre.Y - (float)d.Y);
    }

    public Vec2d ToWorld(Vector2 px) => Focus + new Vec2d((px.X - Centre.X) / Scale, (Centre.Y - px.Y) / Scale).Rotated(ViewRotation);

    /// <summary>Angle (rad) of a world direction as it appears on screen (counter-clockwise, y up).</summary>
    public double ScreenAngle(double worldAngle) => worldAngle - ViewRotation;

    void UpdateRotation()
    {
        if (World == null || FocusBody < 0) { ViewRotation = 0; return; }
        // Blend from "up is radial" (Scale ≥ 0.02) to "north up" (Scale ≤ 0.002).
        double k = MathD.Clamp((Math.Log10(Scale) - Math.Log10(0.002)) / (Math.Log10(0.02) - Math.Log10(0.002)), 0, 1);
        var up = Focus - World.Sys.Position(FocusBody, World.T);
        double target = up.LengthSq > 0 ? up.Angle - Math.PI / 2 : 0;
        ViewRotation = MathD.WrapPi(target) * k;
    }

    public void ZoomBy(double factor) => Scale = MathD.Clamp(Scale * factor, MinScale, MaxScale);

    public void FollowFocus()
    {
        if (World == null) return;
        if (FocusCraft >= 0 && World.Find(FocusCraft) is { } c && !c.Destroyed) { Focus = World.AbsolutePosition(c); FocusBody = c.BodyId; }
        else if (FocusBody >= 0)
        {
            var b = World.Sys[FocusBody];
            Focus = World.Sys.Position(FocusBody, World.T);
            if (!double.IsNaN(FocusLocalAngle)) Focus += Vec2d.FromPolar(FocusRadius, FocusLocalAngle + b.SurfaceAngle(World.T));
        }
    }

    public override void _Process(double delta) { _time += (float)delta; FollowFocus(); UpdateRotation(); QueueRedraw(); _scene.QueueRedraw(); }

    // ---------------------------------------------------------------- drawing

    public override void _Draw()
    {
        ulong tb = Main.BenchTiming ? Time.GetTicksUsec() : 0;
        DrawAll();
        if (Main.BenchTiming) Main.DrawUs += Time.GetTicksUsec() - tb;
    }

    void DrawAll()
    {
        DrawRect(new Rect2(0, 0, W, H), Pal.Space);
        foreach (var (p, s, a) in _stars) DrawRect(new Rect2(p, new Vector2(s, s)), Pal.Text with { A = a * (IsMapScale ? 0.5f : 1f) });
        if (World == null) { _atmoMat.SetShaderParameter("count", 0); return; }
        UpdateAtmospheres(World.Sys, World.T);
    }

    /// <summary>Everything above the background, drawn by the scene layer.</summary>
    public void DrawScene(CanvasItem ci)
    {
        if (World == null) return;
        var sys = World.Sys; double t = World.T;
        _labels.Clear();
        if (ShowOrbits) foreach (var b in sys.Bodies) if (b.Orbit is { } o) DrawConic(ci, o, sys.Position(b.Parent, t), BodyColor(b) with { A = 0.35f }, 1f);
        if (World.Storm != StormPhase.None) foreach (var b in sys.Bodies) DrawShadowCone(ci, b, t);
        foreach (var b in sys.Bodies) DrawBody(ci, b, t);
        DrawDroneShips(ci, t);
        _dots.Clear();
        foreach (var c in World.Crafts) if (!c.Destroyed && World.IsPresent(c)) DrawCraft(ci, c, t);
        if (_dots.Count > 0) DrawUtil.Dots(ci, _dots, 2.5f, Pal.TextDim with { A = 0.85f });
        if (World.Active is { } ac && !ac.Destroyed && ac.Mode != CraftMode.Landed) { DrawPrediction(ci, ac); DrawTarget(ci, ac); }
        if (HoverBody >= 0) DrawSoi(ci, sys[HoverBody], t);
        FlushLabels(ci);
    }

    /// <summary>Queue a label; overlapping labels are pushed down so names never pile on top of each other.</summary>
    void Label(Vector2 pos, string text, Font font, int size, Color col) => _labels.Add((pos, text, font, size, col));

    void FlushLabels(CanvasItem ci)
    {
        var placed = new List<Rect2>();
        foreach (var (pos, text, font, size, col) in _labels.OrderBy(l => l.pos.Y).ThenBy(l => l.pos.X))
        {
            var sz = font.GetStringSize(text, HorizontalAlignment.Left, -1, size);
            var r = new Rect2(pos + new Vector2(0, -sz.Y * 0.8f), sz);
            int guard = 0;
            while (placed.Any(p => p.Intersects(r)) && guard++ < 12) r.Position += new Vector2(0, sz.Y * 0.95f);
            placed.Add(r);
            var at = r.Position + new Vector2(0, sz.Y * 0.8f);
            ci.DrawString(font, at + new Vector2(1, 1), text, HorizontalAlignment.Left, -1, size, Colors.Black with { A = 0.6f });
            ci.DrawString(font, at, text, HorizontalAlignment.Left, -1, size, col);
        }
    }

    void UpdateAtmospheres(StarSystem sys, double t)
    {
        var list = new List<(Body b, Vector2 c, float r)>();
        foreach (var b in sys.Bodies)
        {
            if (b.Atmo == null) continue;
            float rpx = (float)(b.Radius * Scale);
            var c = ToScreen(sys.Position(b.Id, t));
            float hpx = (float)(b.Atmo.Height * Scale);
            // Skip when the glow would be invisible (off-screen by more than its height) or sub-pixel.
            float margin = Math.Max(hpx, 3) + rpx;
            if (c.X < -margin || c.X > W + margin || c.Y < -margin || c.Y > H + margin) continue;
            if (rpx < 1.5f) continue;
            list.Add((b, c, rpx));
            if (list.Count == 8) break;
        }
        var bodies = new Vector4[8]; var colors = new Color[8];
        for (int i = 0; i < list.Count; i++)
        {
            var (b, c, r) = list[i];
            float hpx = Math.Max((float)(b.Atmo!.ScaleHeight * Scale), 2.5f);
            bodies[i] = new Vector4(c.X, c.Y, r, hpx);
            var col = AtmoColor(b);
            colors[i] = col;
        }
        _atmoMat.SetShaderParameter("count", list.Count);
        _atmoMat.SetShaderParameter("bodies", bodies);
        _atmoMat.SetShaderParameter("colors", colors);
        _atmoMat.SetShaderParameter("screen", new Vector2(W, H));
    }

    public static Color BodyColor(Body b) => b.Type switch
    {
        BodyType.Star => Pal.Star, BodyType.Home => Pal.HomeLand, BodyType.Rocky => Pal.RockGrey, BodyType.Desert => Pal.RockRed,
        BodyType.GasGiant => Pal.GasGiant, BodyType.IceGiant => Pal.IceGiant, BodyType.IceDwarf => Pal.Ice, BodyType.Moon => Pal.RockGrey,
        BodyType.IceMoon => Pal.Ice, BodyType.Asteroid => Pal.Asteroid, BodyType.Comet => Pal.Comet, _ => Pal.RockGrey
    };

    static Color AtmoColor(Body b) => b.Type switch
    {
        BodyType.Home => new Color(0.45f, 0.65f, 1.0f, 0.75f),
        BodyType.Desert => new Color(0.95f, 0.65f, 0.4f, 0.45f),
        BodyType.GasGiant => new Color(0.95f, 0.8f, 0.55f, 0.5f),
        BodyType.IceGiant => new Color(0.55f, 0.8f, 1.0f, 0.5f),
        _ => new Color(0.8f, 0.8f, 0.9f, 0.4f)
    };

    static Color DepositColor(Perigee.Sim.Resource r) => r switch { Perigee.Sim.Resource.Ice => Pal.Ice, Perigee.Sim.Resource.Co2 => new Color(0.85f, 0.95f, 0.75f), Perigee.Sim.Resource.Ore => Pal.Accent, _ => Pal.TextDim };

    /// <summary>Deposit arcs (GDD §9): drawn only once the body is scanned, or for a deposit a drill has landed on. A band just outside the terrain.</summary>
    void DrawDeposits(CanvasItem ci, Body b, Vector2 centre, double rot)
    {
        if (b.Deposits.Count == 0) return;
        double rpx = b.Radius * Scale;
        double lift = Math.Max(b.TerrainMax, 0) + 3.0 / Scale;            // just above the highest terrain, at least 3 px
        float width = (float)MathD.Clamp(rpx * 0.012, 2.5, 8);
        int layer = 0;
        foreach (var d in b.Deposits)
        {
            if (!b.Scanned && !d.Revealed) continue;
            double len = MathD.WrapAngle(d.ArcEnd - d.ArcStart);
            if (len <= 0) len = MathD.TwoPi;
            int n = (int)MathD.Clamp(len * Math.Max(rpx, 60) / 6, 8, 720);
            double radius = b.Radius + lift + layer * (width + 1.5) / Scale;
            var pts = new Vector2[n + 1];
            for (int i = 0; i <= n; i++) pts[i] = centre + PolarPx(radius * Scale, d.ArcStart + len * i / n + rot);
            if (pts.All(p => p.X < -50 || p.X > W + 50 || p.Y < -50 || p.Y > H + 50)) { layer = (layer + 1) % 3; continue; }
            ci.DrawPolyline(pts, DepositColor(d.Kind) with { A = 0.85f }, width, true);
            if (rpx > 300)
            {
                var mid = centre + PolarPx((radius + 12 / Scale) * Scale, d.ArcStart + len / 2 + rot);
                Label(mid, d.Revealed ? $"{Contracts.Name(d.Kind)} {d.Richness:0.0} t/d" : Contracts.Name(d.Kind), Pal.Regular, 14, DepositColor(d.Kind));
            }
            layer = (layer + 1) % 3;
        }
    }

    void DrawBody(CanvasItem ci, Body b, double t)
    {
        var centre = ToScreen(World!.Sys.Position(b.Id, t));
        double rpx = b.Radius * Scale;
        var col = BodyColor(b);
        if (b.Type == BodyType.Star)
        {
            float r = (float)Math.Max(rpx, 4);
            ci.DrawCircle(centre, r * 1.8f, Pal.Star with { A = 0.12f });
            ci.DrawCircle(centre, r, Pal.Star);
            return;
        }
        if (rpx < 2.2)
        {
            if (centre.X < -10 || centre.X > W + 10 || centre.Y < -10 || centre.Y > H + 10) return;
            ci.DrawCircle(centre, 2.2f, col);
            if (IsMapScale || rpx < 0.5) Label(centre + new Vector2(6, -6), b.Name, Pal.Regular, 15, Pal.TextDim);
            return;
        }
        // Cull bodies whose disc (plus terrain) misses the screen entirely.
        double reach = (b.Radius + Math.Max(b.TerrainMax, 0)) * Scale;
        if (centre.X < -reach || centre.X > W + reach || centre.Y < -reach || centre.Y > H + reach) return;

        bool hasSea = !double.IsNegativeInfinity(b.SeaLevel);
        double rot = b.SurfaceAngle(t);
        // Sea bodies: ocean disc, then land drawn as arc runs (continents) wherever the terrain rises above sea level, on a
        // muted interior. Airless bodies: the terrain polygon in the body colour.
        double band = Math.Max(b.TerrainMax * 1.5 + 1000, 36 / Scale);
        double inner = Math.Max(b.Radius - band, 0);
        var crust = CrustColor(b);
        DrawDeposits(ci, b, centre, rot);
        if (rpx < 5000)
        {
            int n = (int)MathD.Clamp(rpx * 0.6, 48, 1440);
            var pts = new Vector2[n];
            var h = new double[n];
            for (int i = 0; i < n; i++)
            {
                double local = i / (double)n * MathD.TwoPi;
                h[i] = b.TerrainHeightLocal(local);
                pts[i] = centre + PolarPx((b.Radius + h[i]) * Scale, local + rot);
            }
            if (!hasSea) { DrawUtil.Poly(ci, pts, LandColor(b), "land"); return; }
            ci.DrawCircle(centre, (float)((b.Radius + b.SeaLevel) * Scale), Pal.HomeSea);
            ci.DrawCircle(centre, (float)(inner * Scale), crust);
            // Continent runs (closed loop: start at a sea sample so no run is cut by the seam).
            int start = Array.FindIndex(h, x => x <= b.SeaLevel); if (start < 0) start = 0;
            var run = new List<Vector2>();
            for (int k = 0; k <= n; k++)
            {
                int i = (start + k) % n;
                bool land = h[i] > b.SeaLevel && k < n;
                if (land) run.Add(pts[i]);
                else if (run.Count > 0)
                {
                    int i0 = (start + k - run.Count) % n, i1 = (start + k - 1) % n;
                    // close the run with two points below the terrain at the same angles (or the crust radius) so it fills the band.
                    var poly = new List<Vector2>(run);
                    poly.Add(centre + PolarPx(inner * Scale, i1 / (double)n * MathD.TwoPi + rot));
                    poly.Add(centre + PolarPx(inner * Scale, i0 / (double)n * MathD.TwoPi + rot));
                    if (poly.Count >= 3) DrawUtil.Poly(ci, poly.ToArray(), LandColor(b), "land-run");
                    run.Clear();
                }
            }
        }
        else
        {
            // Only the visible arc: angular window of the screen as seen from the body centre.
            var (a0, a1) = VisibleAngleWindow(centre, b.Radius * Scale);
            int n = 512;
            double deep = Math.Max(inner - 2400.0 / Scale, 0);
            Vector2[] Band(Func<double, double> outerRadius, double innerRadius, double from, double to)
            {
                var pts = new List<Vector2>(n + 3);
                for (int i = 0; i <= n; i++)
                {
                    double ang = from + (to - from) * i / n;
                    pts.Add(centre + PolarPx(outerRadius(ang) * Scale, ang));
                }
                pts.Add(centre + PolarPx(innerRadius * Scale, to));
                pts.Add(centre + PolarPx(innerRadius * Scale, from));
                return pts.ToArray();
            }
            if (!hasSea) { DrawUtil.Poly(ci, Band(ang => b.Radius + b.TerrainHeightLocal(ang - rot), deep, a0, a1), LandColor(b), "land-band"); return; }
            DrawUtil.Poly(ci, Band(_ => b.Radius + b.SeaLevel, deep, a0, a1), Pal.HomeSea, "sea-band");
            DrawUtil.Poly(ci, Band(_ => inner, deep, a0, a1), crust, "crust-band");
            // Land runs inside the window.
            int m = 512;
            var runPts = new List<Vector2>();
            double runFrom = 0;
            for (int i = 0; i <= m; i++)
            {
                double ang = a0 + (a1 - a0) * i / m;
                double hh = b.TerrainHeightLocal(ang - rot);
                bool land = hh > b.SeaLevel && i < m;
                if (land) { if (runPts.Count == 0) runFrom = ang; runPts.Add(centre + PolarPx((b.Radius + hh) * Scale, ang)); }
                else if (runPts.Count > 0)
                {
                    double runTo = a0 + (a1 - a0) * (i - 1) / m;
                    runPts.Add(centre + PolarPx(inner * Scale, runTo));
                    runPts.Add(centre + PolarPx(inner * Scale, runFrom));
                    if (runPts.Count >= 3) DrawUtil.Poly(ci, runPts.ToArray(), LandColor(b), "terrain-run");
                    runPts.Clear();
                }
            }
        }
    }

    static Color CrustColor(Body b) => b.Type switch
    {
        BodyType.Home => new Color(0.11f, 0.22f, 0.42f),
        BodyType.IceMoon or BodyType.IceDwarf or BodyType.Comet => BodyColor(b).Darkened(0.35f),
        _ => BodyColor(b).Darkened(0.3f)
    };

    static Color LandColor(Body b) => b.Type == BodyType.Home ? Pal.HomeLand : BodyColor(b);

    /// <summary>Screen px offset for a polar coordinate (world angle counter-clockwise, y up), with the view rotation applied.</summary>
    Vector2 PolarPx(double rpx, double angle) { double a = angle - ViewRotation; return new((float)(rpx * Math.Cos(a)), -(float)(rpx * Math.Sin(a))); }

    /// <summary>Angular window (world angles, counter-clockwise) that covers the screen as seen from a centre, padded.</summary>
    (double, double) VisibleAngleWindow(Vector2 centre, double rpx)
    {
        // Angle from the body centre to the screen centre; half-width from the screen diagonal over the distance.
        var d = Centre - centre;
        double dist = d.Length();
        double mid = Math.Atan2(-d.Y, d.X) + ViewRotation;
        double half = Math.Atan2(1200, Math.Max(dist, 1)) + 0.02;
        if (half > Math.PI) half = Math.PI;
        return (mid - half, mid + half);
    }

    void DrawSoi(CanvasItem ci, Body b, double t)
    {
        if (double.IsPositiveInfinity(b.Soi)) return;
        var c = ToScreen(World!.Sys.Position(b.Id, t));
        float r = (float)(b.Soi * Scale);
        if (r < 3 || r > 1e6) return;
        ci.DrawArc(c, r, 0, Mathf.Tau, 128, BodyColor(b) with { A = 0.5f }, 1f);
        Label(c + new Vector2(r * 0.7f, -r * 0.7f), $"{b.Name} SOI", Pal.Regular, 14, BodyColor(b));
    }

    /// <summary>Draw a conic around an absolute centre. Whole orbit for ellipses, the in-SOI arc for open conics; samples only the visible window when the orbit dwarfs the screen.</summary>
    public void DrawConic(CanvasItem ci, Conic c, Vec2d centreAbs, Color col, float width, double? nuFrom = null, double? nuTo = null)
    {
        var centre = ToScreen(centreAbs);
        double rpx = (c.IsEllipse ? c.Apoapsis : c.Periapsis * 4) * Scale;
        if (rpx < 2.5) return;
        double lo, hi;
        if (nuFrom.HasValue && nuTo.HasValue) { lo = nuFrom.Value; hi = nuTo.Value; if (hi < lo) hi += MathD.TwoPi; }
        else if (c.IsEllipse) { lo = -Math.PI; hi = Math.PI; }
        else { double m = c.MaxTrueAnomaly - 1e-3; lo = -m; hi = m; }
        int n;
        if (rpx > 20000 && !c.IsEllipse || rpx > 20000)
        {
            // Restrict to the visible angular window (in position angle θ), converted to ν.
            var (a0, a1) = VisibleAngleWindow(centre, rpx);
            double n0 = c.Dir * (a0 - c.Omega), n1 = c.Dir * (a1 - c.Omega);
            if (n0 > n1) (n0, n1) = (n1, n0);
            // Overlap with [lo, hi] modulo 2π: try shifts.
            double bestLo = double.NaN, bestHi = double.NaN;
            for (int k = -2; k <= 2; k++)
            {
                double s0 = n0 + k * MathD.TwoPi, s1 = n1 + k * MathD.TwoPi;
                double ol = Math.Max(s0, lo), oh = Math.Min(s1, hi);
                if (oh > ol && (double.IsNaN(bestLo) || oh - ol > bestHi - bestLo)) { bestLo = ol; bestHi = oh; }
            }
            if (double.IsNaN(bestLo)) return;
            lo = bestLo; hi = bestHi; n = 512;
        }
        else n = (int)MathD.Clamp(rpx * 0.8, 64, 720);
        var pts = new Vector2[n + 1];
        for (int i = 0; i <= n; i++)
        {
            double nu = lo + (hi - lo) * i / n;
            double r = c.RadiusAt(nu);
            if (r < 0 || double.IsInfinity(r)) r = 1e30;
            pts[i] = centre + PolarPx(r * Scale, c.ThetaOf(nu));
        }
        ci.DrawPolyline(pts, col, width, true);
    }

    /// <summary>Shadow cone (GDD §14): the strip behind a body, away from the star, where a craft is sheltered from a storm. Drawn only while one is announced or running.</summary>
    void DrawShadowCone(CanvasItem ci, Body b, double t)
    {
        if (b.Type == BodyType.Star) return;
        var sys = World!.Sys;
        var toStar = sys.Position(sys.Star.Id, t) - sys.Position(b.Id, t);
        if (toStar.LengthSq < 1) return;
        var dir = -toStar.Normalized();
        double length = double.IsPositiveInfinity(b.Soi) ? b.Radius * 40 : Math.Min(b.Soi, b.Radius * 60);
        if (length * Scale < 6) return;
        var centre = sys.Position(b.Id, t);
        var side = dir.Perp * b.Radius;
        var pts = new[] { ToScreen(centre + side), ToScreen(centre + side + dir * length), ToScreen(centre - side + dir * length), ToScreen(centre - side) };
        if (pts.All(p => p.X < -50 || p.X > W + 50) || pts.All(p => p.Y < -50 || p.Y > H + 50)) return;
        var col = World.Storm == StormPhase.Active ? Pal.Danger with { A = 0.16f } : Pal.Accent with { A = 0.14f };
        DrawUtil.Poly(ci, pts, col, "shadow-cone");
        ci.DrawPolyline(new[] { pts[0], pts[1] }, col with { A = 0.5f }, 1.5f, true);
        ci.DrawPolyline(new[] { pts[3], pts[2] }, col with { A = 0.5f }, 1.5f, true);
        if (b.Radius * Scale > 20) Label(ToScreen(centre + dir * Math.Min(length, b.Radius * 4)) + new Vector2(6, -8), "shadow · storm-safe", Pal.Regular, 13, col with { A = 0.9f });
    }

    readonly List<Vector2> _dots = new();

    void DrawCraft(CanvasItem ci, Craft c, double t)
    {
        var p = ToScreen(World!.AbsolutePosition(c));
        double sizePx = Math.Max(c.Height(), 1) * Scale;
        if (p.X < -sizePx - 400 || p.X > W + sizePx + 400 || p.Y < -sizePx - 400 || p.Y > H + sizePx + 400) return;
        bool active = c.Id == World.ActiveCraftId;
        if (c.IsDebris && !active && sizePx < 14)
        {
            // Debris (GDD §14 overlay): a small grey mark, no label, so a crowded band reads as a hazard rather than a list. Batched: one draw call for all of them.
            _dots.Add(p);
            return;
        }
        var col = active ? Pal.Accent : c.Fried ? Pal.Danger : Pal.Text;
        if (sizePx >= 14 && c.Parts.Count > 0)
        {
            CraftView.Draw(ci, this, World, c, Scale, _time);
            if (!active) Label(p + new Vector2((float)sizePx * 0.6f + 8, -8), c.Name, Pal.Regular, 15, col with { A = 0.9f });
            return;
        }
        float s = active ? 7 : 5;
        var a = (float)ScreenAngle(c.Angle + Math.PI / 2);   // nose direction on screen
        var tip = p + new Vector2(Mathf.Cos(a), -Mathf.Sin(a)) * s * 1.6f;
        var l = p + new Vector2(Mathf.Cos(a + 2.4f), -Mathf.Sin(a + 2.4f)) * s;
        var r = p + new Vector2(Mathf.Cos(a - 2.4f), -Mathf.Sin(a - 2.4f)) * s;
        DrawUtil.Poly(ci, new[] { tip, l, r }, col, "craft-marker");
        if (IsMapScale || !active) Label(p + new Vector2(9, -8), c.Name, Pal.Regular, 15, col with { A = 0.9f });
    }

    void DrawPrediction(CanvasItem ci, Craft c)
    {
        var w = World!;
        var patches = w.Predict(c, 3);
        for (int i = 0; i < patches.Count; i++)
        {
            var p = patches[i];
            var body = w.Sys[p.BodyId];
            var centre = w.Sys.Position(p.BodyId, w.T);
            var col = i == 0 ? Pal.Accent with { A = 0.95f } : i == 1 ? Pal.Info with { A = 0.8f } : Pal.Ok with { A = 0.7f };
            double nuStart = p.Conic.TrueAnomalyAt(p.TStart);
            double nuEnd = p.End == PatchEnd.Horizon && p.Conic.IsEllipse ? nuStart + MathD.TwoPi : p.Conic.TrueAnomalyAt(p.TEnd);
            if (nuEnd < nuStart) nuEnd += MathD.TwoPi;
            if (nuEnd - nuStart < 1e-6) nuEnd = nuStart + 1e-6;
            DrawConic(ci, p.Conic, centre, col, i == 0 ? 1.6f : 1.2f, nuStart, nuEnd);
            DrawApsides(ci, p, centre, body, col);
            if (p.End is PatchEnd.EnterSoi or PatchEnd.ExitSoi or PatchEnd.ActiveZone)
            {
                var ep = ToScreen(centre + p.EndPos);
                var cp = ToScreen(World!.AbsolutePosition(c));
                if (ep.X > -50 && ep.X < W + 50 && ep.Y > -50 && ep.Y < H + 50 && (ep - cp).Length() > 24)
                {
                    ci.DrawArc(ep, 5, 0, Mathf.Tau, 24, col, 1.5f);
                    string label = p.End switch
                    {
                        PatchEnd.EnterSoi => $"→ {w.Sys[p.NextBodyId].Name} in {Units.FormatDuration(p.TEnd - w.T)}",
                        PatchEnd.ExitSoi => $"leave {body.Name} in {Units.FormatDuration(p.TEnd - w.T)}",
                        _ => body.Atmo != null ? $"atmosphere in {Units.FormatDuration(p.TEnd - w.T)}" : $"impact in {Units.FormatDuration(p.TEnd - w.T)}",
                    };
                    Label(ep + new Vector2(9, 5), label, Pal.Regular, 14, col);
                }
            }
        }
    }

    /// <summary>Drone ships (GDD §6): a flat deck floating at sea, 60 m long.</summary>
    void DrawDroneShips(CanvasItem ci, double t)
    {
        foreach (var (bodyId, ang) in World!.DroneShips)
        {
            var b = World.Sys[bodyId];
            if (b.Radius * Scale < 500) continue;   // invisible at map scale
            double rot = b.SurfaceAngle(t);
            double r = b.Radius + b.SeaLevel;
            var centre = World.Sys.Position(bodyId, t);
            double a0 = ang + rot;
            var deck = new List<Vector2>();
            double halfArc = 30.0 / b.Radius;
            deck.Add(ToScreen(centre + Vec2d.FromPolar(r + 3, a0 - halfArc)));
            deck.Add(ToScreen(centre + Vec2d.FromPolar(r + 3, a0 + halfArc)));
            deck.Add(ToScreen(centre + Vec2d.FromPolar(r - 1, a0 + halfArc * 0.9)));
            deck.Add(ToScreen(centre + Vec2d.FromPolar(r - 1, a0 - halfArc * 0.9)));
            DrawUtil.Poly(ci, deck.ToArray(), Pal.PartGrey, "drone-deck");
            var top = new[] { ToScreen(centre + Vec2d.FromPolar(r + 3.3, a0 - halfArc * 0.95)), ToScreen(centre + Vec2d.FromPolar(r + 3.3, a0 + halfArc * 0.95)), ToScreen(centre + Vec2d.FromPolar(r + 2.7, a0 + halfArc * 0.95)), ToScreen(centre + Vec2d.FromPolar(r + 2.7, a0 - halfArc * 0.95)) };
            DrawUtil.Poly(ci, top, Pal.Accent, "drone-top");
            if (Scale > 0.5) Label(ToScreen(centre + Vec2d.FromPolar(r + 6, a0)), "drone ship", Pal.Regular, 14, Pal.TextDim);
        }
    }

    /// <summary>Target markers (GDD §4): the target's orbit in blue and the closest-approach pair of points with a connecting line.</summary>
    void DrawTarget(CanvasItem ci, Craft c)
    {
        var w = World!;
        var tc = c.TargetCraft >= 0 ? w.Find(c.TargetCraft) : null;
        if (tc == null || tc.Destroyed || tc.BodyId != c.BodyId) return;
        var body = w.Sys[tc.BodyId];
        var centre = w.Sys.Position(tc.BodyId, w.T);
        var (tr, tv) = tc.StateAt(w.T);
        var tconic = Conic.FromState(body.Gm, tr, tv, w.T);
        DrawConic(ci, tconic, centre, Pal.Info with { A = 0.7f }, 1.2f);
        var tp = ToScreen(centre + tr);
        ci.DrawArc(tp, 8, 0, Mathf.Tau, 24, Pal.Info, 1.5f);
        var ca = w.ClosestApproach(c);
        if (ca is { } a && a.dist > 300)
        {
            var (r0, v0) = c.StateAt(w.T);
            var mine = Conic.FromState(body.Gm, r0, v0, w.T);
            var pm = ToScreen(centre + mine.PositionAt(w.T + a.t));
            var pt = ToScreen(centre + tconic.PositionAt(w.T + a.t));
            ci.DrawLine(pm, pt, Pal.Info with { A = 0.8f }, 1f);
            ci.DrawRect(new Rect2(pm - new Vector2(3, 3), new Vector2(6, 6)), Pal.Accent);
            ci.DrawRect(new Rect2(pt - new Vector2(3, 3), new Vector2(6, 6)), Pal.Info);
            Label(pm + new Vector2(8, -6), $"CA {Units.FormatDistance(a.dist)} in {Units.FormatDuration(a.t)}", Pal.Mono, 14, Pal.Info);
        }
    }

    void DrawApsides(CanvasItem ci, Patch p, Vec2d centre, Body body, Color col)
    {
        var c = p.Conic;
        double nuStart = c.TrueAnomalyAt(p.TStart);
        double nuEnd = c.TrueAnomalyAt(p.TEnd); if (nuEnd < nuStart) nuEnd += MathD.TwoPi;
        bool whole = p.End == PatchEnd.Horizon && c.IsEllipse;
        // Periapsis at ν=0 (or 2π), apoapsis at ν=π.
        void Mark(double nu, string name, double r)
        {
            if (!whole)
            {
                double k = nu; while (k < nuStart) k += MathD.TwoPi;
                if (k > nuEnd) return;
            }
            var pos = ToScreen(centre + Vec2d.FromPolar(r, c.ThetaOf(nu)));
            if (pos.X < -30 || pos.X > W + 30 || pos.Y < -30 || pos.Y > H + 30) return;
            if ((ToScreen(centre) - pos).Length() < 12) return;   // orbit too small on screen for apsis marks
            ci.DrawRect(new Rect2(pos - new Vector2(3, 3), new Vector2(6, 6)), col);
            double tt = c.TimeAtTrueAnomaly(nu, World!.T);
            Label(pos + new Vector2(8, -6), $"{name} {Units.FormatDistance(r - body.Radius)}  in {Units.FormatDuration(tt - World.T)}", Pal.Mono, 14, col);
        }
        Mark(0, "Pe", c.Periapsis);
        if (c.IsEllipse) Mark(Math.PI, "Ap", c.Apoapsis);
    }

    /// <summary>Body under the cursor (within 12 px, or inside its disc), for SOI hover and clicks.</summary>
    public int PickBody(Vector2 px)
    {
        if (World == null) return -1;
        int best = -1; double bestD = 14;
        foreach (var b in World.Sys.Bodies)
        {
            var c = ToScreen(World.Sys.Position(b.Id, World.T));
            double d = (c - px).Length() - Math.Max(b.Radius * Scale, 0);
            if (d < bestD) { bestD = d; best = b.Id; }
        }
        return best;
    }

    public int PickCraft(Vector2 px)
    {
        if (World == null) return -1;
        int best = -1; double bestD = 14;
        foreach (var c in World.Crafts)
        {
            if (c.Destroyed) continue;
            var p = ToScreen(World.AbsolutePosition(c));
            double d = (p - px).Length();
            if (d < bestD) { bestD = d; best = c.Id; }
        }
        return best;
    }
}

/// <summary>Child node that draws the world scene above the atmosphere glow (see FlightView draw order).</summary>
public partial class SceneLayer : Node2D
{
    public FlightView View = null!;
    public override void _Draw() => View.DrawScene(this);
}
