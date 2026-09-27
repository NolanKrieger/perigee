using Godot;

namespace Perigee;

/// <summary>Default key bindings (GDD §4 controls table), created in code so they exist even with an empty project Input Map. Rebinding (M12) edits the same actions.</summary>
public static class Controls
{
    public const string RotateLeft = "rotate_left", RotateRight = "rotate_right",
        ThrottleUp = "throttle_up", ThrottleDown = "throttle_down", ThrottleFull = "throttle_full", ThrottleCut = "throttle_cut",
        Stage = "stage", Rcs = "rcs", TransFore = "trans_fore", TransAft = "trans_aft", TransLeft = "trans_left", TransRight = "trans_right",
        Legs = "legs", Map = "map", WarpDown = "warp_down", WarpUp = "warp_up", WarpReset = "warp_reset",
        CycleCraft = "cycle_craft", Target = "set_target", Pause = "pause", ZoomIn = "zoom_in", ZoomOut = "zoom_out",
        Select = "select", Confirm = "confirm", Chutes = "chutes";

    public static readonly (string action, string label)[] All =
    {
        (RotateLeft, "Rotate left"), (RotateRight, "Rotate right"), (ThrottleUp, "Throttle up"), (ThrottleDown, "Throttle down"),
        (ThrottleFull, "Full throttle"), (ThrottleCut, "Cut throttle"), (Stage, "Next stage"), (Rcs, "RCS on/off"),
        (TransFore, "RCS forward"), (TransAft, "RCS back"), (TransLeft, "RCS left"), (TransRight, "RCS right"), (Legs, "Landing legs"),
        (Chutes, "Parachutes"), (Map, "Map view"), (WarpDown, "Warp down"), (WarpUp, "Warp up"), (WarpReset, "Warp 1×"),
        (CycleCraft, "Next craft"), (Pause, "Pause"),
    };

    public static void Ensure()
    {
        Add(RotateLeft, Key.A, Key.Left);
        Add(RotateRight, Key.D, Key.Right);
        Add(ThrottleUp, Key.Shift);
        Add(ThrottleDown, Key.Ctrl);
        Add(ThrottleFull, Key.Z);
        Add(ThrottleCut, Key.X);
        Add(Stage, Key.Space);
        Add(Rcs, Key.R);
        Add(TransFore, Key.I);
        Add(TransAft, Key.K);
        Add(TransLeft, Key.J);
        Add(TransRight, Key.L);
        Add(Legs, Key.G);
        Add(Chutes, Key.P);
        Add(Map, Key.M);
        Add(WarpDown, Key.Comma);
        Add(WarpUp, Key.Period);
        Add(WarpReset, Key.Slash);
        Add(CycleCraft, Key.Tab);
        Add(Pause, Key.Escape);
        Add(Confirm, Key.Enter, Key.KpEnter);
        AddMouse(Target, MouseButton.Right);
        AddMouse(Select, MouseButton.Left);
        AddMouse(ZoomIn, MouseButton.WheelUp);
        AddMouse(ZoomOut, MouseButton.WheelDown);
    }

    static void Add(string action, params Key[] keys)
    {
        if (!InputMap.HasAction(action)) InputMap.AddAction(action);
        if (InputMap.ActionGetEvents(action).Count > 0) return;   // rebinding erased the events: restore the defaults
        foreach (var k in keys) InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = k });
    }

    static void AddMouse(string action, MouseButton b)
    {
        if (InputMap.HasAction(action)) return;
        InputMap.AddAction(action);
        InputMap.ActionAddEvent(action, new InputEventMouseButton { ButtonIndex = b });
    }
}
