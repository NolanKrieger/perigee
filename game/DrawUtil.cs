using Godot;
using System;
using System.Collections.Generic;

namespace Perigee;

/// <summary>Polygon drawing that never hands the renderer degenerate data: non-finite points, repeated points and sub-pixel areas are dropped (each call site warns once).</summary>
public static class DrawUtil
{
    static readonly HashSet<string> _warned = new();

    public static void Poly(CanvasItem ci, Vector2[] pts, Color col, string tag)
    {
        if (pts.Length < 3) return;
        var clean = new List<Vector2>(pts.Length);
        foreach (var p in pts)
        {
            if (!float.IsFinite(p.X) || !float.IsFinite(p.Y)) { Warn(tag, "non-finite point"); return; }
            if (clean.Count > 0 && clean[^1].DistanceSquaredTo(p) < 1e-4f) continue;
            clean.Add(p);
        }
        while (clean.Count > 1 && clean[0].DistanceSquaredTo(clean[^1]) < 1e-4f) clean.RemoveAt(clean.Count - 1);
        if (clean.Count < 3) { Warn(tag, "collapsed"); return; }
        double a2 = 0;
        for (int i = 0; i < clean.Count; i++) { var p = clean[i]; var q = clean[(i + 1) % clean.Count]; a2 += (double)p.X * q.Y - (double)q.X * p.Y; }
        if (Math.Abs(a2) < 1.0) return;   // under half a pixel of area: invisible, and a triangulation failure
        // Slivers (thin stripes a couple of pixels tall) defeat the triangulator in float: draw them as a thick line between the short edges.
        double perim = 0; for (int i = 0; i < clean.Count; i++) perim += clean[i].DistanceTo(clean[(i + 1) % clean.Count]);
        double thick = Math.Abs(a2) / Math.Max(1e-6, perim);   // ≈ half the thickness of a long thin rectangle
        if (thick < 1.5)
        {
            if (clean.Count != 4) return;
            int s0 = ShortestEdge(clean, -1), s1 = ShortestEdge(clean, s0);
            var m0 = (clean[s0] + clean[(s0 + 1) % 4]) * 0.5f; var m1 = (clean[s1] + clean[(s1 + 1) % 4]) * 0.5f;
            ci.DrawLine(m0, m1, col, (float)Math.Max(1.0, thick * 2), true);
            return;
        }
        var arr = clean.ToArray();
        if (Geometry2D.TriangulatePolygon(arr).Length == 0) { Warn(tag, "not triangulable"); return; }
        ci.DrawPolygon(arr, new[] { col });
    }

    /// <summary>Many small square dots in one draw call (debris marks at map scale).</summary>
    public static void Dots(CanvasItem ci, List<Vector2> centres, float r, Color col)
    {
        var pts = new Vector2[centres.Count * 4]; var cols = new Color[pts.Length]; var idx = new int[centres.Count * 6];
        for (int i = 0; i < centres.Count; i++)
        {
            var c = centres[i]; int b = i * 4;
            pts[b] = c + new Vector2(-r, -r); pts[b + 1] = c + new Vector2(r, -r); pts[b + 2] = c + new Vector2(r, r); pts[b + 3] = c + new Vector2(-r, r);
            cols[b] = cols[b + 1] = cols[b + 2] = cols[b + 3] = col;
            int t = i * 6; idx[t] = b; idx[t + 1] = b + 1; idx[t + 2] = b + 2; idx[t + 3] = b; idx[t + 4] = b + 2; idx[t + 5] = b + 3;
        }
        RenderingServer.CanvasItemAddTriangleArray(ci.GetCanvasItem(), idx, pts, cols);
    }

    static int ShortestEdge(List<Vector2> p, int except)
    {
        int best = -1; double bestLen = double.MaxValue;
        for (int i = 0; i < p.Count; i++) { if (i == except) continue; double l = p[i].DistanceTo(p[(i + 1) % p.Count]); if (l < bestLen) { bestLen = l; best = i; } }
        return best;
    }

    static void Warn(string tag, string why) { if (_warned.Add(tag + why)) GD.PushWarning($"DrawUtil: {tag} polygon skipped ({why})"); }
}
