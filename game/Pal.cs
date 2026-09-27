using Godot;

namespace Perigee;

/// <summary>GDD §17 palette: dark navy space, off-white orbit lines, one saturated accent per body type, white/grey parts with orange/black accents.</summary>
public static class Pal
{
    public static readonly Color Space = new(0.043f, 0.055f, 0.094f);
    public static readonly Color SpaceDeep = new(0.02f, 0.025f, 0.05f);
    public static readonly Color Orbit = new(0.92f, 0.9f, 0.84f, 0.55f);
    public static readonly Color OrbitDim = new(0.92f, 0.9f, 0.84f, 0.22f);
    public static readonly Color Text = new(0.94f, 0.93f, 0.9f);
    public static readonly Color TextDim = new(0.62f, 0.64f, 0.7f);
    public static readonly Color Accent = new(1.0f, 0.55f, 0.15f);     // orange (parts accent, warnings)
    public static readonly Color Danger = new(0.95f, 0.25f, 0.2f);
    public static readonly Color Ok = new(0.35f, 0.85f, 0.55f);
    public static readonly Color Info = new(0.35f, 0.7f, 1.0f);
    public static readonly Color PartWhite = new(0.9f, 0.9f, 0.9f);
    public static readonly Color PartGrey = new(0.55f, 0.57f, 0.6f);
    public static readonly Color PartDark = new(0.16f, 0.17f, 0.2f);
    public static readonly Color Panel = new(0.06f, 0.075f, 0.12f, 0.9f);
    public static readonly Color PanelBorder = new(0.25f, 0.29f, 0.38f);
    public static readonly Color Star = new(1.0f, 0.93f, 0.7f);

    // Body accents by type (one saturated accent per body type, §17).
    public static readonly Color HomeLand = new(0.36f, 0.62f, 0.32f);
    public static readonly Color HomeSea = new(0.16f, 0.36f, 0.68f);
    public static readonly Color RockGrey = new(0.58f, 0.56f, 0.52f);
    public static readonly Color RockRed = new(0.78f, 0.42f, 0.28f);
    public static readonly Color Ice = new(0.78f, 0.88f, 0.95f);
    public static readonly Color GasGiant = new(0.85f, 0.68f, 0.42f);
    public static readonly Color IceGiant = new(0.45f, 0.68f, 0.85f);
    public static readonly Color Comet = new(0.75f, 0.85f, 0.9f);
    public static readonly Color Asteroid = new(0.5f, 0.46f, 0.42f);

    static Font? _regular, _bold, _mono, _monoBold;
    public static Font Regular => _regular ??= GD.Load<Font>("res://assets/fonts/NotoSans-Regular.ttf");
    public static Font Bold => _bold ??= GD.Load<Font>("res://assets/fonts/NotoSans-Bold.ttf");
    public static Font Mono => _mono ??= GD.Load<Font>("res://assets/fonts/NotoSansMono-Regular.ttf");
    public static Font MonoBold => _monoBold ??= GD.Load<Font>("res://assets/fonts/NotoSansMono-Bold.ttf");
}
