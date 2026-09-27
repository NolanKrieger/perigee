using Godot;
using Perigee.Sim;

namespace Perigee;

/// <summary>Draws craft: part polygons (GDD §17), engine plumes that widen as pressure drops, open parachutes, reentry glow and landing-leg feet.</summary>
public static class CraftView
{
    public static readonly Color Plume = new(1.0f, 0.75f, 0.35f);
    public static readonly Color PlumeCore = new(1.0f, 0.95f, 0.8f);

    /// <summary>Screen position of a craft-frame point.</summary>
    static Vector2 P(FlightView v, Vec2d bodyPos, Craft c, Vec2d local) => v.ToScreen(bodyPos + c.LocalToWorld(local));

    static readonly List<Vector2> _verts = new(); static readonly List<Color> _cols = new(); static readonly List<int> _idx = new(); static readonly Vector2[] _scratch = new Vector2[16];

    public static void Draw(CanvasItem ci, FlightView v, World w, Craft c, double scale, float time)
    {
        var bodyPos = w.Sys.Position(c.BodyId, w.T);
        var body = w.Sys[c.BodyId];
        double alt = c.Pos.Length - body.Radius;
        double pr = body.Atmo?.PressureRatio(alt) ?? 0;
        bool active = c.Id == w.ActiveCraftId;

        // Off-screen craft are skipped: far away their screen coordinates run into float precision and the triangulator fails.
        var screenC = v.ToScreen(bodyPos + c.Pos);
        float reach = (float)(Math.Max(c.Height(), 40.0) * scale) * 3f + 64f;
        var vp = ci.GetViewportRect().Size;
        if (screenC.X < -reach || screenC.Y < -reach || screenC.X > vp.X + reach || screenC.Y > vp.Y + reach) return;

        // Plumes first (behind the parts).
        foreach (var p in c.Parts)
        {
            if (p.Destroyed || p.LastThrust <= 0 || p.Def.Engine == null) continue;
            double frac = p.LastThrust / Math.Max(1, p.Def.Engine.ThrustVac);
            double len = p.Def.Width * (2.0 + 6.0 * (1 - pr)) * (0.6 + 0.4 * frac);
            double wid = p.Def.Width * (0.45 + 1.4 * (1 - pr));
            double flick = 1 + 0.08 * Mathf.Sin(time * 60 + p.Id);
            var top = p.Pos + new Vec2d(0, -p.Def.Height * 0.5);
            var tip = top + new Vec2d(0, -len * flick);
            var poly = new[] { P(v, bodyPos, c, top + new Vec2d(-wid * 0.5, 0)), P(v, bodyPos, c, top + new Vec2d(wid * 0.5, 0)), P(v, bodyPos, c, tip + new Vec2d(wid * 0.25, 0)), P(v, bodyPos, c, tip + new Vec2d(-wid * 0.25, 0)) };
            DrawUtil.Poly(ci, poly, Plume with { A = 0.75f }, "plume");
            var core = new[] { P(v, bodyPos, c, top + new Vec2d(-wid * 0.2, 0)), P(v, bodyPos, c, top + new Vec2d(wid * 0.2, 0)), P(v, bodyPos, c, top + new Vec2d(0, -len * 0.55 * flick)) };
            DrawUtil.Poly(ci, core, PlumeCore with { A = 0.9f }, "plume-core");
        }

        // Parts: every fill of every part goes into one triangle array (a 250-part craft is ~1,000 polygons; one call, not a thousand).
        _verts.Clear(); _cols.Clear(); _idx.Clear();
        foreach (var p in c.Parts)
        {
            if (p.Destroyed) continue;
            float sx = p.Flip ? -1 : 1;
            foreach (var poly in p.Def.Art)
            {
                var pts = poly.Points.Length <= 16 ? _scratch : new Vector2[poly.Points.Length];
                float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
                int n = poly.Points.Length;
                for (int i = 0; i < n; i++)
                {
                    pts[i] = P(v, bodyPos, c, p.Pos + new Vec2d(sx * poly.Points[i].X, poly.Points[i].Y));
                    minX = Math.Min(minX, pts[i].X); maxX = Math.Max(maxX, pts[i].X); minY = Math.Min(minY, pts[i].Y); maxY = Math.Max(maxY, pts[i].Y);
                }
                if (maxX - minX < 1.2f && maxY - minY < 1.2f) continue;   // sub-pixel detail: skip
                float area2 = 0; for (int i = 0, j = n - 1; i < n; j = i++) area2 += pts[j].X * pts[i].Y - pts[i].X * pts[j].Y;
                if (Math.Abs(area2) < 1.0f) continue;   // collinear/sliver at this zoom
                var col = new Color(poly.R, poly.G, poly.B);
                if (p.Temp > 600) col = col.Lerp(new Color(1f, 0.45f, 0.15f), (float)MathD.Clamp((p.Temp - 600) / 900, 0, 0.7));
                int b0 = _verts.Count;
                for (int i = 0; i < n; i++) { _verts.Add(pts[i]); _cols.Add(col); }
                for (int i = 1; i + 1 < n; i++) { _idx.Add(b0); _idx.Add(b0 + i); _idx.Add(b0 + i + 1); }   // fan: PartArt shapes are convex
            }
            // Open chute: canopy above the part.
            if (p.Def.Has(PartFlags.Parachute) && p.Deployed && p.ChuteRamp > 0)
            {
                double r = Math.Sqrt(p.Def.ChuteArea / Math.PI) * 0.7 * p.ChuteRamp;
                double h = 5 + 8 * p.ChuteRamp;
                var apex = p.Pos + new Vec2d(0, h + r);
                var canopy = new List<Vector2>();
                for (int i = 0; i <= 12; i++)
                {
                    double a = Math.PI * i / 12;
                    canopy.Add(P(v, bodyPos, c, apex + new Vec2d(-r * Math.Cos(a), -r * 0.45 * Math.Sin(a) * -1 + 0)));
                }
                var col = p.Def.Has(PartFlags.Drogue) ? Pal.PartGrey : Pal.Accent;
                DrawUtil.Poly(ci, canopy.ToArray(), col with { A = 0.9f }, "canopy");
                ci.DrawLine(P(v, bodyPos, c, p.Pos), P(v, bodyPos, c, apex + new Vec2d(-r, 0)), Pal.TextDim, 1);
                ci.DrawLine(P(v, bodyPos, c, p.Pos), P(v, bodyPos, c, apex + new Vec2d(r, 0)), Pal.TextDim, 1);
            }
        }

        if (_idx.Count > 0) RenderingServer.CanvasItemAddTriangleArray(ci.GetCanvasItem(), _idx.ToArray(), _verts.ToArray(), _cols.ToArray());

        // Reentry glow / plasma: from the heat flux on exposed parts.
        double maxFlux = 0; foreach (var p in c.Parts) if (!p.Destroyed) maxFlux = Math.Max(maxFlux, p.HeatFlux);
        if (maxFlux > 3e5)
        {
            float k = (float)MathD.Clamp((maxFlux - 3e5) / 2.5e6, 0, 1);
            var vAir = c.Vel - Flight.SurfaceVelocity(body, c.Pos);
            if (vAir.LengthSq > 1)
            {
                var dir = vAir.Normalized();
                var centre = v.ToScreen(bodyPos + c.Pos);
                float len = (float)(c.Height() * scale) * (2f + 4f * k);
                var d = new Vector2((float)dir.X, -(float)dir.Y);
                var side = new Vector2(-d.Y, d.X) * (float)(c.Height() * scale * 0.6);
                var glow = new[] { centre + d * (float)(c.Height() * scale * 0.4) + side, centre + d * (float)(c.Height() * scale * 0.4) - side, centre - d * len };
                DrawUtil.Poly(ci, glow, new Color(1f, 0.5f, 0.2f, 0.35f * k + 0.1f), "glow");
                ci.DrawCircle(centre, (float)(c.Height() * scale * 0.7f), new Color(1f, 0.6f, 0.25f, 0.18f * k + 0.05f));
            }
        }

        if (active && scale < 2)
        {
            var centre = v.ToScreen(bodyPos + c.Pos);
            ci.DrawArc(centre, 9, 0, Mathf.Tau, 24, Pal.Accent with { A = 0.8f }, 1.2f);
        }
    }
}
