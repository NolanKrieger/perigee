namespace Perigee.Sim;

/// <summary>
/// Procedural polygon art and collision outlines for parts (GDD §17: parts are polygon data, drawn with fills and outlines,
/// crisp at every zoom). Part-local metres, y up (nose), x right. Radial parts point +x away from the rocket; the game flips them.
/// </summary>
public static class PartArt
{
    // §17 palette: parts mostly white and grey with an orange/black accent.
    const float W = 0.9f, G = 0.56f, D = 0.17f, K = 0.08f;
    static readonly (float r, float g, float b) White = (W, W, W), Grey = (G, G + 0.02f, G + 0.04f), Dark = (D, D + 0.01f, D + 0.03f),
        Black = (K, K, K + 0.02f), Orange = (1.0f, 0.55f, 0.15f), Blue = (0.35f, 0.55f, 0.9f), Gold = (0.85f, 0.7f, 0.3f);

    static ArtPoly P((float r, float g, float b) c, params double[] xy)
    {
        var pts = new Vec2d[xy.Length / 2];
        for (int i = 0; i < pts.Length; i++) pts[i] = new Vec2d(xy[2 * i], xy[2 * i + 1]);
        return new ArtPoly(pts, c.r, c.g, c.b);
    }

    static Vec2d[] Rect(double w, double h, double cx = 0, double cy = 0) => new[] { new Vec2d(cx - w / 2, cy - h / 2), new Vec2d(cx + w / 2, cy - h / 2), new Vec2d(cx + w / 2, cy + h / 2), new Vec2d(cx - w / 2, cy + h / 2) };
    static ArtPoly RectArt((float, float, float) c, double w, double h, double cx = 0, double cy = 0) { var r = Rect(w, h, cx, cy); return new ArtPoly(r, c.Item1, c.Item2, c.Item3); }

    public static Vec2d[] Outline(PartDef p)
    {
        double w = p.Width, h = p.Height;
        if (p.Has(PartFlags.NoseCone) && !p.Has(PartFlags.Fairing)) return new[] { new Vec2d(-w / 2, -h / 2), new Vec2d(w / 2, -h / 2), new Vec2d(w * 0.12, h / 2), new Vec2d(-w * 0.12, h / 2) };
        if (p.Has(PartFlags.Fairing)) return new[] { new Vec2d(-w / 2, -h / 2), new Vec2d(w / 2, -h / 2), new Vec2d(w / 2, h * 0.1), new Vec2d(w * 0.1, h / 2), new Vec2d(-w * 0.1, h / 2), new Vec2d(-w / 2, h * 0.1) };
        if (p.Has(PartFlags.Engine)) return new[] { new Vec2d(-w * 0.3, h / 2), new Vec2d(w * 0.3, h / 2), new Vec2d(w * 0.5, -h / 2), new Vec2d(-w * 0.5, -h / 2) };
        if (p.Has(PartFlags.Fin | PartFlags.GridFin)) return new[] { new Vec2d(0, h / 2), new Vec2d(0, -h / 2), new Vec2d(w, -h / 2), new Vec2d(w, -h * 0.1) };
        if (p.Has(PartFlags.Legs)) return new[] { new Vec2d(0, h * 0.5), new Vec2d(w * 0.5, -h * 0.5), new Vec2d(w * 0.5 - 0.15, -h * 0.5), new Vec2d(-0.05, h * 0.2) };
        if (p.Has(PartFlags.HeatShield)) return new[] { new Vec2d(-w / 2, h / 2), new Vec2d(w / 2, h / 2), new Vec2d(w * 0.42, -h / 2), new Vec2d(-w * 0.42, -h / 2) };
        return Rect(w, h);
    }

    public static ArtPoly[] Build(PartDef p)
    {
        double w = p.Width, h = p.Height;
        var list = new List<ArtPoly>();
        switch (p.Cat)
        {
            case PartCategory.Tank when p.Has(PartFlags.Tank) && !p.RadialMount:
                list.Add(RectArt(White, w, h));
                // orange/black bands (checker stripes on long tanks)
                double band = Math.Min(0.22, h * 0.12);
                list.Add(RectArt(Dark, w, band, 0, h / 2 - band / 2));
                list.Add(RectArt(Dark, w, band, 0, -h / 2 + band / 2));
                if (h > 2) list.Add(RectArt(Orange, w * 0.5, h - 2 * band - 0.2, w * 0.25, 0));
                else list.Add(RectArt(Orange, w * 0.5, h * 0.35, w * 0.25, 0));
                break;
            case PartCategory.Tank:   // radial pods
                list.Add(RectArt(Grey, w, h));
                list.Add(RectArt(p.Capacity.ContainsKey(Resource.Xenon) ? Blue : White, w * 0.6, h * 0.7));
                break;
            case PartCategory.Core:
                list.Add(RectArt(Grey, w, h));
                list.Add(RectArt(Dark, w * 0.8, h * 0.5));
                list.Add(RectArt(p.Has(PartFlags.Hardened) ? Gold : Orange, w * 0.5, h * 0.2, 0, h * 0.1));
                if (p.Has(PartFlags.DepotController)) list.Add(RectArt(Blue, w * 0.7, h * 0.12, 0, -h * 0.3));
                break;
            case PartCategory.Cargo when p.Has(PartFlags.Fairing):
                list.Add(new ArtPoly(Outline(p), W, W, W));
                list.Add(RectArt(Dark, w, Math.Min(0.15, h * 0.06), 0, -h / 2 + 0.08));
                break;
            case PartCategory.Cargo:
                list.Add(RectArt(Grey, w, h));
                list.Add(RectArt(Dark, w * 0.85, h * 0.7));
                list.Add(RectArt(Orange, w * 0.85, 0.08, 0, h * 0.2));
                break;
            case PartCategory.Engine:
                list.Add(P(Dark, -w * 0.3, h / 2, w * 0.3, h / 2, w * 0.3, h * 0.15, -w * 0.3, h * 0.15));   // pump block
                list.Add(P(Grey, -w * 0.22, h * 0.15, w * 0.22, h * 0.15, w * 0.5, -h / 2, -w * 0.5, -h / 2));  // bell
                list.Add(P(Black, -w * 0.16, h * 0.1, w * 0.16, h * 0.1, w * 0.42, -h / 2, -w * 0.42, -h / 2));
                list.Add(RectArt(Orange, w * 0.5, 0.06, 0, h * 0.13));
                break;
            case PartCategory.Rcs:
                list.Add(RectArt(Grey, w, h));
                list.Add(RectArt(Black, w * 0.5, h * 0.4));
                break;
            case PartCategory.Structure when p.Has(PartFlags.Decoupler):
                list.Add(RectArt(Grey, w, h));
                list.Add(RectArt(Orange, w, h * 0.4));
                break;
            case PartCategory.Structure when p.Has(PartFlags.RadialDecoupler):
                list.Add(RectArt(Grey, w, h));
                list.Add(RectArt(Orange, w * 0.6, h * 0.6));
                break;
            case PartCategory.Structure when p.Has(PartFlags.NoseCone):
                list.Add(new ArtPoly(Outline(p), W, W, W));
                list.Add(P(Orange, -w * 0.12, h / 2, w * 0.12, h / 2, w * 0.22, h * 0.15, -w * 0.22, h * 0.15));
                break;
            case PartCategory.Structure when p.Has(PartFlags.Adapter):
                list.Add(P(White, -w / 2, -h / 2, w / 2, -h / 2, w * 0.28, h / 2, -w * 0.28, h / 2));
                list.Add(RectArt(Dark, w * 0.5, 0.08, 0, h / 2 - 0.05));
                break;
            case PartCategory.Structure:   // truss
                list.Add(RectArt(Dark, w, h));
                list.Add(RectArt(Grey, w * 0.15, h, -w * 0.4));
                list.Add(RectArt(Grey, w * 0.15, h, w * 0.4));
                list.Add(P(Grey, -w * 0.4, -h / 2, -w * 0.3, -h / 2, w * 0.4, h / 2, w * 0.3, h / 2));
                break;
            case PartCategory.Docking when p.Has(PartFlags.Claw):
                list.Add(RectArt(Grey, w * 0.6, h * 0.6, 0, -h * 0.2));
                list.Add(P(Dark, -w / 2, h / 2, -w * 0.25, h / 2, -w * 0.15, -h * 0.1, -w * 0.4, -h * 0.1));
                list.Add(P(Dark, w / 2, h / 2, w * 0.25, h / 2, w * 0.15, -h * 0.1, w * 0.4, -h * 0.1));
                break;
            case PartCategory.Docking:
                list.Add(RectArt(Grey, w, h));
                list.Add(RectArt(Black, w * 0.55, h * 0.5, 0, h * 0.2));
                list.Add(RectArt(Orange, w * 0.85, 0.06, 0, -h * 0.35));
                break;
            case PartCategory.Aero when p.Has(PartFlags.HeatShield):
                list.Add(new ArtPoly(Outline(p), Dark.r, Dark.g, Dark.b));
                list.Add(P(Orange, -w * 0.42, -h / 2, w * 0.42, -h / 2, w * 0.44, -h / 2 + 0.08, -w * 0.44, -h / 2 + 0.08));
                break;
            case PartCategory.Aero:   // fins
                list.Add(new ArtPoly(Outline(p), W, W, W));
                list.Add(P(Orange, 0.05, h / 2 - 0.1, 0.05, -h / 2 + 0.05, w * 0.3, -h / 2 + 0.05));
                break;
            case PartCategory.Recovery when p.Has(PartFlags.Legs):
                list.Add(new ArtPoly(Outline(p), G, G, G));
                list.Add(P(Dark, w * 0.5 - 0.25, -h * 0.5, w * 0.5 + 0.1, -h * 0.5, w * 0.5 + 0.1, -h * 0.5 + 0.08, w * 0.5 - 0.25, -h * 0.5 + 0.08));
                break;
            case PartCategory.Recovery:   // chutes
                list.Add(RectArt(Grey, w, h));
                list.Add(RectArt(Orange, w * 0.7, h * 0.5, 0, h * 0.1));
                break;
            case PartCategory.Power when p.Has(PartFlags.Solar):
                list.Add(RectArt(Dark, w, h));
                list.Add(RectArt(Blue, w * 0.7, h * 0.9));
                break;
            case PartCategory.Power:
                list.Add(RectArt(Grey, w, h));
                list.Add(RectArt(p.Has(PartFlags.Rtg) ? Gold : Orange, w * 0.6, h * 0.6));
                break;
            case PartCategory.Survey when p.Has(PartFlags.Drill):
                list.Add(RectArt(Grey, w * 0.6, h * 0.5, 0, h * 0.25));
                list.Add(P(Dark, -w * 0.15, 0, w * 0.15, 0, 0, -h / 2));
                break;
            case PartCategory.Survey:
                list.Add(RectArt(Grey, w, h));
                list.Add(RectArt(Dark, w * 0.7, h * 0.6));
                list.Add(RectArt(Blue, w * 0.5, h * 0.12, 0, h * 0.3));
                break;
            case PartCategory.Base:
                list.Add(RectArt(Grey, w, h));
                list.Add(RectArt(Dark, w * 0.85, h * 0.55));
                list.Add(RectArt(p.Has(PartFlags.PadKit) ? Orange : p.Has(PartFlags.Fabricator) ? Gold : Blue, w * 0.85, h * 0.12, 0, h * 0.38));
                break;
            default:
                list.Add(RectArt(White, w, h));
                break;
        }
        return list.ToArray();
    }
}
