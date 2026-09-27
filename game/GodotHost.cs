using Godot;
using Perigee.Sim;

namespace Perigee;

/// <summary>Script host that goes through the real input path: held actions via the Input singleton, taps via _UnhandledInput, one game tick per step.</summary>
public sealed class GodotHost : IScriptHost
{
    readonly Main _main;
    public string ShotDir = "";
    public World World => _main.World!;
    public GodotHost(Main main) { _main = main; }

    public void Launch(string design, string name) => _main.LaunchDesign(design, name);
    public void LaunchAt(string design, string name, int bodyId) => _main.LaunchDesignAt(design, name, bodyId);
    public void Spawn(string design, string name, double alt, double phaseDeg, double fuel) => SimHost.SpawnInto(World, design, name, alt, phaseDeg, fuel);

    public void SetHeld(string action, bool down)
    {
        var ev = new InputEventAction { Action = action, Pressed = down };
        Input.ParseInputEvent(ev);
        Input.FlushBufferedEvents();
    }

    public void Tap(string action) => _main._UnhandledInput(new InputEventAction { Action = action, Pressed = true });
    public void SetThrottle(double v) => World.SetThrottle(v);
    public void Tick() => _main.TickOnce();
    public void Shot(string name) { if (ShotDir.Length > 0) _main.QueueShot(System.IO.Path.Combine(ShotDir, name + ".png")); }
    public void View(string what, double value) { if (what == "zoom") { _main.View.Scale = value; _main.View.FollowFocus(); } }
    public void Ui(string what)
    {
        if (what == "transfer") _main.Transfer.Toggle();
        else if (what == "board") _main.Board.Toggle();
        else if (what is "builder" or "builder-pad") { if (_main.InBuilder) _main.CloseBuilder(); else { _main.OpenBuilder("hopper2"); if (what == "builder-pad") _main.Builder.SelectSite(_main.Builder.Sites.Count - 1); } }
    }
}
