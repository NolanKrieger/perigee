using Perigee.Sim;

namespace Perigee.Tests;

public class BuilderTests
{
    [Fact]
    public void StackSnappingUsesFreeNodesOnly()
    {
        var bp = Blueprint.New("core-s");
        var tank = Parts.Get("tank-s-short");
        var places = bp.StackPlacements(tank);
        Assert.Equal(2, places.Count);   // top and bottom of the core
        var below = bp.Snap(tank, new Vec2d(0.1, -0.8));
        Assert.NotNull(below);
        Assert.Equal(AttachKind.Bottom, below!.Kind);
        int t = bp.Add(tank, below);
        Assert.Equal(-0.9, bp.Design.Parts[t].Y, 6);   // core 0.6 tall, tank 1.2 tall
        // The core's bottom node is now taken; only its top and the tank's bottom remain.
        var again = bp.StackPlacements(tank);
        Assert.Equal(2, again.Count);
        Assert.DoesNotContain(again, p => p.Parent == 0 && p.Kind == AttachKind.Bottom);
        Assert.Null(bp.Snap(tank, new Vec2d(5, 5)));
    }

    [Fact]
    public void RadialPartsMirrorAndRemoveTogether()
    {
        var bp = Blueprint.New("core-s");
        var tank = Parts.Get("tank-s-long");
        int t = bp.Add(tank, bp.Snap(tank, new Vec2d(0, -2))!);
        var fin = Parts.Get("fin");
        var pl = bp.Snap(fin, new Vec2d(0.6, bp.Design.Parts[t].Y - 1.0));
        Assert.NotNull(pl);
        Assert.Equal(AttachKind.Radial, pl!.Kind);
        Assert.Equal(1, pl.Side);
        int before = bp.Count;
        int f = bp.Add(fin, pl);
        Assert.Equal(before + 2, bp.Count);   // mirrored twin
        Assert.True(bp.Design.Parts[f + 1].Flip);
        Assert.Equal(-bp.Design.Parts[f].X, bp.Design.Parts[f + 1].X, 6);
        bp.Remove(f);
        Assert.Equal(before, bp.Count);
        // Without symmetry only one fin appears.
        bp.Symmetry = false;
        bp.Add(fin, bp.Snap(fin, new Vec2d(0.6, bp.Design.Parts[t].Y - 1.0))!);
        Assert.Equal(before + 1, bp.Count);
    }

    [Fact]
    public void RemovingAPartRemovesItsSubtreeAndRemapsStages()
    {
        var bp = new Blueprint(TestDesigns.Orbital());
        int n = bp.Count;
        int dec = bp.Design.Parts.FindIndex(p => p.DefId == "decoupler-s" && bp.Design.Parts.Count(q => q.DefId == "decoupler-s") > 0);
        // Remove the lower decoupler (the one whose parent is the Sparrow): the whole booster goes with it.
        int lower = bp.Design.Parts.FindIndex(p => p.DefId == "decoupler-s" && bp.Design.Parts[p.Parent].DefId == "eng-sparrow");
        bp.Remove(lower);
        Assert.DoesNotContain(bp.Design.Parts, p => p.DefId == "eng-kestrel" || p.DefId == "fin");
        Assert.True(bp.Count < n);
        foreach (var st in bp.Design.Stages) foreach (int i in st) Assert.InRange(i, 0, bp.Count - 1);
        Assert.Empty(bp.Stats().Issues.Where(i => i.Contains("missing")));
    }

    [Fact]
    public void OverlapIsRejected()
    {
        var bp = Blueprint.New("core-s");
        var tank = Parts.Get("tank-s-short");
        bp.Add(tank, bp.Snap(tank, new Vec2d(0, -0.9))!);
        Assert.True(bp.WouldOverlap(tank, new Vec2d(0, -0.9), false));
        Assert.False(bp.WouldOverlap(tank, new Vec2d(0, -2.1), false));
        var issues = new Blueprint(new Design { Parts = { new DesignPart { DefId = "core-s" }, new DesignPart { DefId = "core-s", X = 0.2, Parent = 0, Attach = AttachKind.Radial } } }).Stats().Issues;
        Assert.Contains(issues, i => i.Contains("overlaps"));
    }

    [Fact]
    public void StatsAndValidationMatchTheDesign()
    {
        var bp = new Blueprint(TestDesigns.Orbital());
        var st = bp.Stats(9.81);
        Assert.Equal(TestDesigns.Orbital().Cost, st.Cost);
        Assert.InRange(st.Height, 7, 12);
        Assert.InRange(st.TotalDeltaVVac, 3500, 5000);
        Assert.True(st.TwrSurface > 1.2);
        Assert.True(st.Issues.Count == 0, string.Join(" | ", st.Issues));
        var tall = bp.Stats(9.81, padHeightLimit: 5);
        Assert.Contains(tall.Issues, i => i.Contains("Too tall"));
        var noCore = Blueprint.New("tank-s-short");
        Assert.Contains(noCore.Stats().Issues, i => i.Contains("No probe core"));
        Assert.Contains(noCore.Stats().Issues, i => i.Contains("No engine"));
    }

    [Fact]
    public void StageEditingIsManualAfterFirstTouch()
    {
        var bp = new Blueprint(TestDesigns.Orbital());
        int chute = bp.Design.Parts.FindIndex(p => p.DefId == "chute-nose");
        bp.MoveToStage(chute, 0);
        Assert.True(bp.ManualStages);
        Assert.Contains(chute, bp.Design.Stages[0]);
        Assert.Equal(3, bp.Design.Stages.Count);   // the old chute-only stage collapsed
        bp.AddStage();
        Assert.Equal(4, bp.Design.Stages.Count);
        bp.RemoveStage(3);
        Assert.Equal(3, bp.Design.Stages.Count);
        bp.ResetStages();
        Assert.False(bp.ManualStages);
        Assert.Equal(4, bp.Design.Stages.Count);
    }

    [Fact]
    public void LibraryRoundTrips()
    {
        string dir = Path.Combine(Path.GetTempPath(), "perigee-lib-" + Guid.NewGuid().ToString("N"));
        try
        {
            var d = TestDesigns.Orbital(); d.Name = "My Orbiter/1";
            DesignLibrary.Save(d, dir);
            var list = DesignLibrary.Load(dir);
            Assert.Single(list);
            Assert.Equal("My Orbiter/1", list[0].Name);
            Assert.Equal(d.Parts.Count, list[0].Parts.Count);
            File.WriteAllText(Path.Combine(dir, "corrupt.json"), "{ not json");
            Assert.Single(DesignLibrary.Load(dir));
            DesignLibrary.Delete(d, dir);
            Assert.Empty(DesignLibrary.Load(dir));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
