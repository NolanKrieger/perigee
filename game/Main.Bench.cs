using Godot;
using Perigee.Sim;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Perigee;

/// <summary>`godot --path . -- --bench [--out=file.json]`: the GDD §20 targets on this machine — 60 fps with a 250-part craft in the atmosphere, 2,000 on-rails objects, and the map drawn. Needs a window (real GPU).</summary>
public partial class Main
{
    async void RunBench()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);   // measure throughput, not the monitor
        BenchTiming = true;
        StartDemo();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var w = World!;
        var results = new List<(string name, int parts, int objects, double avg, double min, double p1)>();

        // A. A 252-part rocket under full throttle in the thick air, close view.
        var d = new Design { Name = "Bench 252" };
        int prev = d.Root("core-s");
        for (int i = 0; i < 40; i++)
        {
            int t = d.Stack(prev, "tank-s-short");
            d.Pair(t, "rcs-tank-radial", 0); d.Pair(t, "solar", 0.2);
            if (i >= 15) d.Pair(t, "fin", 0.1);
            prev = t;
        }
        d.Stage(d.Stack(prev, "eng-kestrel"));
        var big = w.Launch(d, "Bench", w.Sys.HomeId, w.Sys.Home.LaunchSiteAngle);
        w.ActiveCraftId = big.Id;
        var host = new SimHost(w);
        host.Tap("throttle_full"); host.Tap("stage");
        View.FocusCraft = big.Id; View.Scale = 4;
        results.Add(await Measure("A 252-part craft under thrust in the atmosphere", big.Parts.Count, w.Crafts.Count));

        // B. 2,000 rails objects around home, map view (bodies' conics, the predicted path, the debris overlay).
        for (int i = 0; i < 2000; i++) SimHost.SpawnInto(w, "junk", $"J{i}", 80_000 + (i % 500) * 1_200, i * 0.183, 0);
        w.ActiveCraftId = big.Id;
        View.Scale = 0.0009; View.FocusCraft = big.Id;
        results.Add(await Measure("B 2,000 rails objects at map scale", 0, w.Crafts.Count));

        // C. Both at once at a middle zoom (the craft still under thrust, the objects still propagating).
        View.Scale = 0.02;
        results.Add(await Measure("C both, mid zoom", big.Parts.Count, w.Crafts.Count));

        var lines = results.Select(r => $"{r.name}: parts={r.parts} objects={r.objects} avg {r.avg:0.0} fps, 1% low {r.p1:0.0} fps, min {r.min:0.0} fps").ToList();
        lines.AddRange(_benchPhase.Select(kv => $"timing {kv.Key}: sim {kv.Value.sim:0.00} ms/frame, draw {kv.Value.draw:0.00} ms/frame"));
        foreach (var l in lines) GD.Print("bench " + l);
        bool ok = results.All(r => r.avg >= 60);
        GD.Print($"bench: {(ok ? "PASS" : "FAIL")} (target 60 fps average; renderer={RenderingServer.GetCurrentRenderingMethod()} msaa2d={GetViewport().Msaa2D} vsync={DisplayServer.WindowGetVsyncMode()})");
        if (Args.TryGetValue("out", out var path))
        {
            var json = System.Text.Json.JsonSerializer.Serialize(results.Select(r => new { r.name, r.parts, r.objects, avg = Math.Round(r.avg, 1), min = Math.Round(r.min, 1), p1 = Math.Round(r.p1, 1) }).ToList());
            System.IO.File.WriteAllText(path, json);
        }
        GetTree().Quit(ok ? 0 : 1);
    }

    public static bool BenchTiming;
    public static double SimUs, DrawUs;   // accumulated by TickOnce and FlightView._Draw while BenchTiming
    readonly Dictionary<string, (double sim, double draw)> _benchPhase = new();

    async System.Threading.Tasks.Task<(string, int, int, double, double, double)> Measure(string name, int parts, int objects)
    {
        for (int i = 0; i < 30; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);   // settle
        SimUs = 0; DrawUs = 0;
        var deltas = new List<double>();
        ulong t0 = Time.GetTicksUsec();
        for (int i = 0; i < 240; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            ulong t1 = Time.GetTicksUsec(); deltas.Add((t1 - t0) / 1e6); t0 = t1;
        }
        _benchPhase[name] = (SimUs / 1000.0 / deltas.Count, DrawUs / 1000.0 / deltas.Count);
        deltas.Sort();
        double avg = deltas.Count / deltas.Sum();
        double min = 1 / deltas[^1];
        double p1 = 1 / deltas[(int)(deltas.Count * 0.99)];
        return (name, parts, objects, avg, min, p1);
    }
}
