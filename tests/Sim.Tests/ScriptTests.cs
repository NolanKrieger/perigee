using Perigee.Sim;

namespace Perigee.Tests;

public class ScriptTests
{
    public static string ScriptsDir
    {
        get
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d != null && !File.Exists(Path.Combine(d.FullName, "project.godot"))) d = d.Parent;
            return Path.Combine(d!.FullName, "scripts");
        }
    }

    public static ScriptResult RunScript(string name, out List<string> log)
    {
        var text = File.ReadAllText(Path.Combine(ScriptsDir, name));
        var script = FlightScript.Parse(text, name);
        var host = new SimHost(SimHost.NewScriptWorld());
        var res = script.Run(host);
        log = res.Log;
        return res;
    }

    [Fact]
    public void ExpressionsEvaluate()
    {
        var w = SimHost.NewTestWorld();
        Assert.Equal(7, ScriptExpr.Eval("1 + 2 * 3", w, 0));
        Assert.Equal(1, ScriptExpr.Eval("(1 + 2) * 3 == 9 and not (2 > 3)", w, 0));
        Assert.Equal(1, ScriptExpr.Eval("t >= 0 or 1 < 0", w, 0));
        Assert.Equal(0, ScriptExpr.Eval("-2 > 1", w, 0));
        Assert.Throws<ArgumentException>(() => ScriptExpr.Eval("bogus > 1", w, 0));
    }

    [Fact]
    public void SoundingRocketScriptFliesAndLands()
    {
        var res = RunScript("sounding-hop.flight", out var log);
        Assert.True(res.Passed, string.Join("\n", res.Failures.Concat(log)));
        Assert.Contains(log, l => l.StartsWith("apex") && double.Parse(l.Split("alt=")[1].Split(' ')[0]) > 20000);
    }

    [Fact]
    public void LaunchOrbitDeorbitLandingRegressionFlight()
    {
        var res = RunScript("launch-orbit-land.flight", out var log);
        Assert.True(res.Passed, string.Join("\n", res.Failures.Concat(log)));
    }

    /// <summary>M9 gate flight: scan, land an outpost on the ice arc, produce in the background.</summary>
    [Fact]
    public void OutpostSetupFlight()
    {
        var res = RunScript("outpost-setup.flight", out var log);
        Assert.True(res.Passed, string.Join("\n", res.Failures.Concat(log.TakeLast(30))));
    }

    /// <summary>M10 gate flight: pad base, sale from the pad, hopper built from stored metal and flown to orbit.</summary>
    [Fact]
    public void OffWorldPadFlight()
    {
        var res = RunScript("offworld-pad.flight", out var log);
        Assert.True(res.Passed, string.Join("\n", res.Failures.Concat(log.TakeLast(30))));
    }

    /// <summary>M11 gate flights: shelter from a storm in a shadow cone; grab, deorbit and let debris decay.</summary>
    [Fact]
    public void StormShelterFlight()
    {
        var res = RunScript("storm-shelter.flight", out var log);
        Assert.True(res.Passed, string.Join("\n", res.Failures.Concat(log.TakeLast(30))));
    }

    [Fact]
    public void DebrisCleanupFlight()
    {
        var res = RunScript("debris-cleanup.flight", out var log);
        Assert.True(res.Passed, string.Join("\n", res.Failures.Concat(log.TakeLast(30))));
    }

    /// <summary>M8 gate: the whole tutorial chain, each lesson accepted from the board and completed by flying (GDD §11, §21).</summary>
    [Fact]
    public void TutorialChainT1ToT10Passes()
    {
        var res = RunScript("tutorial-t1-t10.flight", out var log);
        Assert.True(res.Passed, string.Join("\n", res.Failures.Concat(log.TakeLast(40))));
        Assert.Contains(log, l => l.StartsWith("T10 done"));
    }
}
