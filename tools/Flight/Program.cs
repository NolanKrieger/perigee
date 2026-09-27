using System.Globalization;
using Perigee.Sim;

// Headless flight-script runner: `dotnet run --project tools/Flight -- scripts/x.flight [--trace=out.csv] [--seed=7]`
// Runs the same script the Godot `--script=` mode runs through the real input path, but directly against the sim.
if (args.Length == 0) { Console.Error.WriteLine("usage: Flight <script.flight> [--trace=file.csv] [--quiet]"); return 2; }
string path = args[0];
string? trace = args.FirstOrDefault(a => a.StartsWith("--trace="))?[8..];
bool quiet = args.Contains("--quiet");
bool events = args.Contains("--events");
var script = FlightScript.Parse(File.ReadAllText(path), Path.GetFileName(path));
var world = SimHost.NewScriptWorld();
var host = new SimHost(world);
StreamWriter? tw = trace != null ? new StreamWriter(trace) : null;
tw?.WriteLine("t,mode,alt,speed,vspeed,pitch,throttle,stage,fuel,apo,peri,q,temp,mass,impact,pad,hspeed");
var tracing = tw != null || events ? new TracingHost(host, tw, events) : (IScriptHost)host;
var res = script.Run(tracing, quiet ? null : Console.WriteLine);
tw?.Dispose();
Console.WriteLine($"{script.Name}: {(res.Passed ? "PASS" : "FAIL")} ({res.Ticks} ticks, {Units.FormatDuration(res.EndTime)} game time)");
foreach (var f in res.Failures) Console.WriteLine("  " + f);
return res.Passed ? 0 : 1;

sealed class TracingHost : IScriptHost
{
    readonly IScriptHost _inner; readonly StreamWriter? _w; int _n; readonly bool _events;
    public TracingHost(IScriptHost inner, StreamWriter? w, bool events) { _inner = inner; _w = w; _events = events; }
    public World World => _inner.World;
    public void Launch(string design, string name) => _inner.Launch(design, name);
    public void Spawn(string design, string name, double alt, double phaseDeg, double fuel) => _inner.Spawn(design, name, alt, phaseDeg, fuel);
    public void SetHeld(string action, bool down) => _inner.SetHeld(action, down);
    public void Tap(string action) => _inner.Tap(action);
    public void SetThrottle(double v) => _inner.SetThrottle(v);
    public void Shot(string name) => _inner.Shot(name);
    public void View(string what, double value) => _inner.View(what, value);
    public void Tick()
    {
        _inner.Tick();
        if (_events) foreach (var e in World.Events) if (e.Kind is not (SimEventKind.WarpStopped)) Console.WriteLine($"  [{World.T:0.00}] {e.Kind}: {e.Text}");
        if (_w == null || ++_n % 10 != 0) return;
        var w = World; var c = w.Active; if (c == null) return;
        double t0 = 0;
        string V(string n) => ScriptVars.Get(n, w, t0).ToString("0.###", CultureInfo.InvariantCulture);
        _w.WriteLine($"{w.T:0.00},{c.Mode},{V("alt")},{V("speed")},{V("vspeed")},{V("pitch")},{V("throttle")},{V("stage")},{V("fuel")},{V("apo")},{V("peri")},{V("q")},{V("temp")},{V("mass")},{V("impact_dist")},{V("pad_dist")},{V("hspeed")}");
    }
}
