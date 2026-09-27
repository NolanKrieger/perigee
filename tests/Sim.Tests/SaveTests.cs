using Perigee.Sim;

namespace Perigee.Tests;

public class SaveTests
{
    /// <summary>A rich world: the whole tutorial chain has run (crafts in every mode, contracts in every state, an outpost, docked names, sales, exploration).</summary>
    static World RichWorld()
    {
        var text = File.ReadAllText(Path.Combine(ScriptTests.ScriptsDir, "tutorial-t1-t10.flight"));
        var script = FlightScript.Parse(text, "chain");
        var w = SimHost.NewScriptWorld(); w.SystemKind = "test";
        var host = new SimHost(w);
        var res = script.Run(host);
        Assert.True(res.Passed, string.Join("\n", res.Failures));
        return w;
    }

    [Fact]
    public void RoundTripKeepsEveryStateAndStaysDeterministic()
    {
        var w = RichWorld();
        double launchesBefore = w.CareerStats.GetValueOrDefault("launches");
        w.Count("launches", 7);
        ulong h0 = w.StateHash();
        var data = w.ToSave();
        string json = SaveStore.Serialize(data);
        var back = SaveStore.Deserialize(json);
        var w2 = World.FromSave(back);
        Assert.Equal(h0, w2.StateHash());
        Assert.Equal(w.Crafts.Count(c => !c.Destroyed), w2.Crafts.Count);
        Assert.Equal(w.Contracts.Count, w2.Contracts.Count);
        Assert.Equal(w.Market.Nodes.Count, w2.Market.Nodes.Count);
        Assert.Equal(launchesBefore + 7, w2.CareerStats["launches"]);
        Assert.Equal(w.Sys.Bodies.Count, w2.Sys.Bodies.Count);
        Assert.Equal(w.Sys[2].Deposits.Count, w2.Sys[2].Deposits.Count);
        // Determinism: both worlds evolve identically for a day of warp, including a storm roll and background production.
        foreach (var x in new[] { w, w2 }) { x.SetWarp(6); for (int i = 0; i < 400; i++) x.Advance(Units.PhysicsDt * x.EffectiveWarp); }
        Assert.Equal(w.StateHash(), w2.StateHash());
        Assert.Equal(w.T, w2.T);
        Assert.Equal(w.Cash, w2.Cash);
    }

    [Fact]
    public void GeneratedSystemsComeBackFromTheSeed()
    {
        var w = new World(SystemGenerator.Generate(11), 11);
        w.StartCareer(Preset.Brutal, "Seed Eleven");
        var sat = SimHost.SpawnInto(w, "station", "Sat", 150_000, 0, 1);
        w.Sys[3].Scanned = true;
        w.Advance(Units.Day * 3);
        var w2 = World.FromSave(SaveStore.Deserialize(SaveStore.Serialize(w.ToSave())));
        Assert.Equal("Seed Eleven", w2.AgencyName);
        Assert.Equal("Brutal", w2.Preset.Name);
        Assert.Equal(w.Sys.Bodies.Count, w2.Sys.Bodies.Count);
        for (int i = 0; i < w.Sys.Bodies.Count; i++)
        {
            Assert.Equal(w.Sys.Bodies[i].Name, w2.Sys.Bodies[i].Name);
            Assert.Equal(w.Sys.Bodies[i].Radius, w2.Sys.Bodies[i].Radius);
            Assert.Equal(w.Sys.Bodies[i].Deposits.Count, w2.Sys.Bodies[i].Deposits.Count);
        }
        Assert.True(w2.Sys[3].Scanned);
        Assert.Equal(w.StateHash(), w2.StateHash());
    }

    [Fact]
    public void FilesAreGzippedAtomicAndBackedUp()
    {
        var dir = Path.Combine(Path.GetTempPath(), "perigee-savetest-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(dir, "career.pfc");
        var w = SimHost.NewScriptWorld(); w.SystemKind = "test";
        SaveStore.Write(path, w.ToSave());
        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + ".tmp"));
        Assert.False(File.Exists(path + ".bak"));
        var bytes = File.ReadAllBytes(path);
        Assert.Equal(0x1f, bytes[0]); Assert.Equal(0x8b, bytes[1]);   // gzip magic
        w.Cash = 12345;
        SaveStore.Write(path, w.ToSave());
        Assert.True(File.Exists(path + ".bak"));
        Assert.Equal(12345, SaveStore.Read(path).Cash);
        Assert.NotEqual(12345, SaveStore.Read(path + ".bak").Cash);
        // Damaged main file → the backup is used.
        File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
        Assert.NotEqual(12345, SaveStore.ReadOrBackup(path).Cash);
        Directory.Delete(dir, true);
    }

    [Fact]
    public void NewerVersionsAreRefusedAndFlashbacksCannotBeSaved()
    {
        var w = SimHost.NewScriptWorld(); w.SystemKind = "test";
        string json = SaveStore.Serialize(w.ToSave()).Replace("\"Version\":1", "\"Version\":99");
        Assert.Throws<InvalidDataException>(() => SaveStore.Deserialize(json));
        var home = w.Sys.Home;
        var c = w.Launch(TestDesigns.Recoverable(), "R", home.Id, home.LaunchSiteAngle);
        w.Apply(Command.ThrottleFull); w.Apply(Command.Stage);
        for (int i = 0; i < 2500; i++) { c.Angle = MathD.WrapAngle(c.Pos.Angle - Math.PI / 2); c.AngVel = 0; w.Advance(Units.PhysicsDt); }
        w.Apply(Command.Stage);
        Assert.True(w.StartFlashback());
        Assert.Throws<InvalidOperationException>(() => w.ToSave());
    }
}
