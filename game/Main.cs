using Godot;
using Perigee.Sim;

namespace Perigee;

/// <summary>
/// Root node. Parses `--` user args, owns the world, the view and the HUD, samples input once per 50 Hz physics tick, and
/// provides the test modes: `--screenshot=path [--frames=N]`, `--selftest --save=tmp`, `--script=file [--shots=dir] [--fast]`, `--bench`.
/// </summary>
public partial class Main : Node2D
{
    public static readonly Dictionary<string, string> Args = new();
    public World? World { get; private set; }
    public FlightView View { get; private set; } = null!;
    public Hud Hud { get; private set; } = null!;
    public Audio Audio { get; private set; } = null!;
    public BuilderScreen Builder { get; private set; } = null!;
    public TransferPanel Transfer { get; private set; } = null!;
    public ContractsScreen Board { get; private set; } = null!;
    public bool InBuilder => Builder.Visible;
    public bool Paused { get; private set; }
    public string LastLog { get; private set; } = "";
    int _frames;
    bool _shotTaken;
    double _flightScale = 0.6;
    bool _mapMode;
    // Script mode
    IEnumerator<ScriptResult>? _script;
    string _scriptName = "";
    int _scriptStepsPerFrame = 1;
    readonly Queue<string> _shots = new();
    bool _capturing;
    bool _scriptDone;
    ScriptResult? _scriptResult;

    public override void _Ready()
    {
        foreach (var a in OS.GetCmdlineUserArgs())
        {
            var kv = a.TrimStart('-').Split('=', 2);
            Args[kv[0]] = kv.Length > 1 ? kv[1] : "";
        }
        if (Args.ContainsKey("screenshot") || Args.ContainsKey("shots"))
        {
            // A fixed-size window floats instead of being tiled small, so captures come out at full size.
            DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.ResizeDisabled, true);
            var size = new Vector2I(1920, 1080);
            if (Args.TryGetValue("size", out var sz) && sz.Split('x') is { Length: 2 } wh && int.TryParse(wh[0], out int sw) && int.TryParse(wh[1], out int sh)) size = new Vector2I(sw, sh);   // --size=1600x900 for the readability check
            DisplayServer.WindowSetSize(size);
        }
        if (Args.ContainsKey("nomsaa")) GetViewport().Msaa2D = Viewport.Msaa.Disabled;   // comparison capture for the MSAA check
        GD.Print($"Perigee Fuel Co. boot: renderer={RenderingServer.GetCurrentRenderingMethod()} driver={RenderingServer.GetCurrentRenderingDriverName()} msaa2d={GetViewport().Msaa2D} adapter={RenderingServer.GetVideoAdapterName()}");
        Controls.Ensure();

        View = new FlightView();
        AddChild(View);
        var hudLayer = new CanvasLayer { Layer = 10 };
        AddChild(hudLayer);
        Hud = new Hud { Game = this };
        hudLayer.AddChild(Hud);
        var uiLayer = new CanvasLayer { Layer = 20 };
        AddChild(uiLayer);
        Builder = new BuilderScreen { Game = this, Visible = false, DesignsDir = ProjectSettings.GlobalizePath(Args.TryGetValue("designs", out var dd) ? dd : "user://designs") };
        uiLayer.AddChild(Builder);
        Transfer = new TransferPanel { Game = this };
        hudLayer.AddChild(Transfer);
        Board = new ContractsScreen { Game = this };
        uiLayer.AddChild(Board);
        SetupCareerUi(uiLayer);
        Profile.Load();
        Audio = new Audio { Game = this }; AddChild(Audio);
        var steam = new SteamPlatform(); Platform = steam.Available ? steam : new LocalPlatform();
        if (Args.ContainsKey("selftest") || Args.ContainsKey("script") || Args.ContainsKey("screenshot") || Args.ContainsKey("shots") || Args.ContainsKey("bench")) AudioServer.SetBusMute(0, true);   // test runs stay silent

        if (Args.ContainsKey("selftest")) { CallDeferred(MethodName.RunSelfTest); return; }
        if (Args.ContainsKey("bench")) { CallDeferred(MethodName.RunBench); return; }
        if (Args.TryGetValue("script", out var scriptPath)) { StartScript(scriptPath); return; }
        // A plain launch opens the title screen; any dev/screenshot argument keeps the old demo world.
        bool dev = new[] { "demo", "seed", "design", "zoom", "time", "focus", "warp", "tutorial", "accept", "board", "transfer", "builder", "screenshot", "shots", "career" }.Any(Args.ContainsKey);
        if (Args.TryGetValue("career", out var careerPath) && careerPath.Length > 0) { ContinueCareer(ProjectSettings.GlobalizePath(careerPath)); if (Args.ContainsKey("hq")) ShowHq(); return; }
        if (!dev) { ShowTitle(); if (Args.ContainsKey("title-new")) ShowNewCareer(); return; }
        StartDemo();
        if (Args.ContainsKey("title")) { ShowTitle(); if (Args.ContainsKey("title-new")) ShowNewCareer(); if (Args.ContainsKey("settings")) ShowSettings(fromTitle: true); return; }
        if (Args.ContainsKey("hq")) { ShowHq(); if (Args.TryGetValue("hqtab", out var ht) && int.TryParse(ht, out int hti)) { Hq.CurrentTab = hti; Hq.Rebuild(); } }
        if (Args.ContainsKey("pause")) TogglePause();
        if (Args.ContainsKey("settings") && !Args.ContainsKey("title")) ShowSettings(fromTitle: false);
        if (Args.ContainsKey("logbook")) { World!.EndCareer("cash ran out (screenshot)"); ShowLogbook(); }
        if (Args.TryGetValue("design", out var design)) LaunchDesign(design, design);
        if (Args.TryGetValue("zoom", out var z)) View.Scale = double.Parse(z, System.Globalization.CultureInfo.InvariantCulture);
        if (Args.TryGetValue("time", out var tt)) World!.Advance(double.Parse(tt, System.Globalization.CultureInfo.InvariantCulture));
        if (Args.TryGetValue("focus", out var f)) Focus(f);
        if (Args.TryGetValue("warp", out var wi)) World!.SetWarp(int.Parse(wi));
        View.FollowFocus();
        if (Args.TryGetValue("tutorial", out var tut) && int.TryParse(tut, out int tstep)) { World!.TutorialStep = tstep; World.RefreshBoard(); }
        if (Args.TryGetValue("accept", out var acc)) { var k = World!.FindContract(acc); if (k != null) World.Accept(k, false); }
        if (Args.ContainsKey("board")) Board.Show();
        if (Args.ContainsKey("transfer")) Transfer.Toggle();
        if (Args.ContainsKey("builder")) { OpenBuilder(Args.TryGetValue("builder", out var bd) && bd.Length > 0 ? bd : null); if (Args.TryGetValue("tab", out var tab)) Builder.ShowCategory(tab); }
    }

    // ---------------------------------------------------------------- builder

    public void OpenBuilder(string? design = null)
    {
        if (World == null) StartDemo();
        Design? d = design?.ToLowerInvariant() switch { "sounding" => TestDesigns.Sounding(), "orbital" => TestDesigns.Orbital(), "probe" => TestDesigns.Probe(), null or "" => null, _ => System.IO.File.Exists(design) ? Design.FromJson(System.IO.File.ReadAllText(design)) : SimHost.DesignByName(design) };
        Builder.Open(World!, d);
        Builder.Visible = true;
        Hud.Visible = false;
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    public void CloseBuilder() { Builder.Visible = false; Hud.Visible = true; }

    public void LaunchFromBuilder(Design d, int bodyId, double padAngle)
    {
        var c = World!.TryLaunch(d, d.Name, bodyId, padAngle);
        if (c == null) { foreach (var e in World.Events) Hud.Say(e.Text); Hud.Say("Launch refused: " + (World.LockedParts(d).Any() ? "locked parts" : "not enough cash")); return; }
        View.FocusCraft = c.Id; View.FocusBody = bodyId; View.FocusLocalAngle = double.NaN;
        View.Scale = 12;
        View.FollowFocus();
        CloseBuilder();
        Hud.Say($"{d.Name} on the pad");
    }

    /// <summary>Demo world: the hand-made test system (or `--seed=N` for a generated one) with a satellite in low orbit and a tanker on a transfer.</summary>
    public void StartDemo()
    {
        ulong seed = 0;
        bool generated = Args.TryGetValue("seed", out var seedText) && ulong.TryParse(seedText, out seed);
        var sys = generated ? SystemGenerator.Generate(seed) : TestSystems.Basic();
        World = new World(sys, 7) { SystemKind = generated ? "generated" : "test" };
        MarkDevWorld();
        World.StartCareer(Preset.ByName(Args.TryGetValue("preset", out var pn) ? pn : "Standard"), "Perigee Fuel Co.");
        if (generated)
        {
            // Generated systems: one satellite in low home orbit is enough to have an active craft.
            var h = sys.Home; double r0 = h.Radius + 100_000;
            World.AddCraft(new Craft { Name = "Sat-1", BodyId = h.Id, Rails = new RailsState(h.Id, Conic.FromState(h.Gm, new Vec2d(r0, 0), new Vec2d(0, -Math.Sqrt(h.Gm / r0)), 0)) });
            World.ActiveCraftId = 0;
            View.World = World; View.FocusCraft = 0; View.Scale = 0.002; View.FollowFocus();
            GD.Print($"generated system seed={seed}: {sys.StarName}, {sys.Bodies.Count} bodies, home={h.Name}");
            return;
        }
        var home = sys.Home; var moon = sys[2];
        double r1 = home.Radius + 100_000;
        World.AddCraft(new Craft { Name = "Sat-1", BodyId = home.Id, Rails = new RailsState(home.Id, Conic.FromState(home.Gm, new Vec2d(r1, 0), new Vec2d(0, -Math.Sqrt(home.Gm / r1)), 0)) });
        double r2 = moon.Orbit!.Value.A, a = 0.5 * (r1 + r2);
        double tof = Math.PI * Math.Sqrt(a * a * a / home.Gm);
        double arr = moon.Orbit.Value.ThetaOf(moon.Orbit.Value.TrueAnomalyAt(tof));
        var r = Vec2d.FromPolar(r1, arr - Math.PI);
        var v = -Vec2d.FromAngle(arr - Math.PI).Perp * Math.Sqrt(home.Gm * (2 / r1 - 1 / a));
        World.AddCraft(new Craft { Name = "Tanker", BodyId = home.Id, Rails = new RailsState(home.Id, Conic.FromState(home.Gm, r, v, 0)) });
        World.ActiveCraftId = 1;
        View.World = World;
        View.FocusCraft = World.ActiveCraftId;
        View.Scale = 0.002;
        View.FollowFocus();
    }

    /// <summary>Put a design on the home pad and make it the active craft (test designs by name, or a design JSON path).</summary>
    public void LaunchDesign(string design, string name)
    {
        if (World == null) { World = new World(TestSystems.Basic(), 7); View.World = World; }
        var d = design.ToLowerInvariant() switch
        {
            "sounding" => TestDesigns.Sounding(),
            "orbital" => TestDesigns.Orbital(),
            "probe" => TestDesigns.Probe(),
            _ => SimHost.DesignByName(design)
        };
        var home = World.Sys.Home;
        var c = World.Launch(d, name, home.Id, home.LaunchSiteAngle);
        View.FocusCraft = c.Id;
        View.FocusBody = home.Id;
        View.Scale = 12;
        View.FollowFocus();
        Hud.Say($"{name} on the pad");
    }

    /// <summary>Build and launch a design at the operational pad on `bodyId` (M10); throws when there is no pad or the launch is refused.</summary>
    public void LaunchDesignAt(string design, string name, int bodyId)
    {
        var pad = World!.PadAt(bodyId) ?? throw new InvalidOperationException($"no pad on {World.Sys[bodyId].Name}");
        var c = World.TryLaunchAtPad(SimHost.DesignByName(design), name, pad) ?? throw new InvalidOperationException("pad launch refused");
        View.FocusCraft = c.Id; View.FocusBody = bodyId; View.FocusLocalAngle = double.NaN; View.Scale = 12; View.FollowFocus();
        Hud.Say($"{name} on the pad at {pad.Name}");
    }

    void Focus(string name)
    {
        if (World == null) return;
        var c = World.Crafts.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (c != null) { World.ActiveCraftId = c.Id; View.FocusCraft = c.Id; return; }
        var b = World.Sys.Bodies.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (b != null) { View.FocusCraft = -1; View.FocusBody = b.Id; return; }
        if (name == "site")
        {
            var home = World.Sys.Home;
            View.FocusCraft = -1; View.FocusBody = home.Id;
            View.FocusLocalAngle = home.LaunchSiteAngle;
            View.FocusRadius = home.Radius + home.TerrainHeightLocal(home.LaunchSiteAngle) + 300 / View.Scale;
        }
    }

    // ---------------------------------------------------------------- script mode (test tool: replays a timeline through the real input path)

    void StartScript(string path)
    {
        var text = Godot.FileAccess.GetFileAsString(path);
        if (string.IsNullOrEmpty(text)) { GD.PrintErr($"script not found: {path}"); GetTree().Quit(2); return; }
        var script = FlightScript.Parse(text, System.IO.Path.GetFileName(path));
        _scriptName = script.Name;
        World = SimHost.NewScriptWorld();
        View.World = World;
        var host = new GodotHost(this);
        if (Args.TryGetValue("shots", out var dir)) { host.ShotDir = ProjectSettings.GlobalizePath(dir); System.IO.Directory.CreateDirectory(host.ShotDir); }
        _scriptStepsPerFrame = Args.ContainsKey("fast") ? 200 : Args.TryGetValue("steps", out var st) ? int.Parse(st) : 1;
        _script = script.RunSteps(host, m => { GD.Print(m); LastLog = m; }).GetEnumerator();
        Hud.Say($"script {script.Name}");
    }

    public void QueueShot(string path) { SyncFocus(); _shots.Enqueue(path); }

    /// <summary>Point the view at the active craft (scripts switch the active craft without a tick in between).</summary>
    public void SyncFocus()
    {
        if (World?.Active is { } a) { View.FocusCraft = a.Id; View.FocusBody = a.BodyId; View.FocusLocalAngle = double.NaN; View.FollowFocus(); }
    }

    // ---------------------------------------------------------------- input

    public override void _UnhandledInput(InputEvent ev)
    {
        if (ev.IsEcho()) return;
        if (Title.Visible || NewCareerUi.Visible || (Settings?.Visible ?? false) || Logbook.Visible) return;
        if (World == null) return;
        if (Pause.Visible) { if (ev.IsActionPressed(Controls.Pause)) TogglePause(); return; }
        if (Hq.Visible) { if (ev is InputEventKey { Pressed: true, Keycode: Key.H or Key.Escape }) CloseHq(); return; }
        if (InBuilder) return;
        if (Board.Visible) { if (ev is InputEventKey { Pressed: true, Keycode: Key.C or Key.Escape }) Board.Hide(); return; }
        if (ev is InputEventKey { Pressed: true, Keycode: Key.C }) { Board.Toggle(); return; }
        if (ev is InputEventKey { Pressed: true, Keycode: Key.H }) { ShowHq(); return; }
        if (ev is InputEventKey { Pressed: true, Keycode: Key.B }) { OpenBuilder(); return; }
        if (ev is InputEventMouseMotion mm) { View.MousePx = mm.Position; View.HoverBody = View.PickBody(mm.Position); return; }
        if (ev.IsActionPressed(Controls.ZoomIn)) { View.ZoomBy(1.25); return; }
        if (ev.IsActionPressed(Controls.ZoomOut)) { View.ZoomBy(1 / 1.25); return; }
        if (ev.IsActionPressed(Controls.Map)) { ToggleMap(); return; }
        if (ev.IsActionPressed(Controls.WarpUp)) { World.Apply(Command.WarpUp); return; }
        if (ev.IsActionPressed(Controls.WarpDown)) { World.Apply(Command.WarpDown); return; }
        if (ev.IsActionPressed(Controls.WarpReset)) { World.Apply(Command.WarpReset); return; }
        if (ev.IsActionPressed(Controls.Pause)) { TogglePause(); return; }
        if (ev.IsActionPressed(Controls.CycleCraft)) { CycleCraft(); return; }
        if (ev.IsActionPressed(Controls.Stage)) { World.Apply(Command.Stage); return; }
        if (ev.IsActionPressed(Controls.ThrottleFull)) { World.Apply(Command.ThrottleFull); return; }
        if (ev.IsActionPressed(Controls.ThrottleCut)) { World.Apply(Command.ThrottleCut); return; }
        if (ev.IsActionPressed(Controls.Rcs)) { World.Apply(Command.ToggleRcs); return; }
        if (ev.IsActionPressed(Controls.Legs)) { World.Apply(Command.ToggleLegs); return; }
        if (ev.IsActionPressed(Controls.Chutes)) { World.Apply(Command.DeployChutes); return; }
        if (ev is InputEventKey { Pressed: true, Keycode: Key.F }) { Transfer.Toggle(); return; }
        if (ev.IsActionPressed(Controls.Confirm))
        {
            if (World.FlashbackOffered) { World.StartFlashback(); View.FocusCraft = World.ActiveCraftId; View.Scale = 6; }
            else if (World.Active is { } rc && World.RefundRate(rc) > 0) { World.Recover(rc); View.FocusCraft = World.ActiveCraftId; }
            return;
        }
        if (ev is InputEventKey { Pressed: true, Keycode: Key.Backspace } && World.FlashbackOffered) { World.SkipBooster(); return; }
        if (ev is InputEventKey { Pressed: true, Keycode: Key.U }) { UndockFirst(); return; }
        if (ev.IsActionPressed(Controls.Target) && ev is InputEventMouseButton tb)
        {
            if (World.Active is { } ac)
            {
                int tcid = View.PickCraft(tb.Position);
                int tbid = tcid < 0 ? View.PickBody(tb.Position) : -1;
                if (tcid >= 0 && tcid != ac.Id) { ac.TargetCraft = tcid; ac.TargetBody = -1; Hud.Say($"Target: {World.Find(tcid)?.Name}", 3); }
                else if (tbid >= 0) { ac.TargetBody = tbid; ac.TargetCraft = -1; Hud.Say($"Target: {World.Sys[tbid].Name}", 3); }
                else { ac.TargetCraft = -1; ac.TargetBody = -1; Hud.Say("Target cleared", 3); }
            }
            return;
        }
        if (ev.IsActionPressed(Controls.Select) && ev is InputEventMouseButton mb)
        {
            int cid = View.PickCraft(mb.Position);
            if (cid >= 0) { World.ActiveCraftId = cid; View.FocusCraft = cid; return; }
            int bid = View.PickBody(mb.Position);
            if (bid >= 0 && View.IsMapScale) { View.FocusCraft = -1; View.FocusBody = bid; View.FocusLocalAngle = double.NaN; }
        }
    }

    void UndockFirst()
    {
        if (World?.Active is not { } c) return;
        var port = c.Parts.FirstOrDefault(p => !p.Destroyed && p.Fired && p.Def.Has(PartFlags.DockingPort | PartFlags.Claw));
        if (port != null) World.Undock(c, port.Id); else Hud.Say("Nothing docked", 3);
    }

    public void ToggleMap()
    {
        if (World == null) return;
        _mapMode = !_mapMode;
        if (_mapMode)
        {
            _flightScale = View.Scale;
            var body = World.Sys[View.FocusBody >= 0 ? View.FocusBody : World.Sys.HomeId];
            double fit = double.IsPositiveInfinity(body.Soi) ? SystemRadius(World.Sys) : body.Soi;
            View.Scale = 480 / fit;
        }
        else View.Scale = _flightScale;
    }

    static double SystemRadius(StarSystem sys)
    {
        double r = 1e9;
        foreach (var b in sys.Bodies) if (b.Parent == 0 && b.Orbit is { } o) r = Math.Max(r, o.Apoapsis * 1.05);
        return r;
    }

    void CycleCraft()
    {
        if (World == null || World.Crafts.Count == 0) return;
        int idx = World.Crafts.FindIndex(c => c.Id == World.ActiveCraftId);
        for (int k = 1; k <= World.Crafts.Count; k++)
        {
            var c = World.Crafts[(idx + k) % World.Crafts.Count];
            if (!c.Destroyed && !c.Debris) { World.ActiveCraftId = c.Id; View.FocusCraft = c.Id; break; }
        }
    }

    /// <summary>Held keys → sim input, sampled once per tick.</summary>
    InputState Sample() => new()
    {
        RotateLeft = Input.IsActionPressed(Controls.RotateLeft), RotateRight = Input.IsActionPressed(Controls.RotateRight),
        ThrottleUp = Input.IsActionPressed(Controls.ThrottleUp), ThrottleDown = Input.IsActionPressed(Controls.ThrottleDown),
        TransFore = Input.IsActionPressed(Controls.TransFore), TransAft = Input.IsActionPressed(Controls.TransAft),
        TransLeft = Input.IsActionPressed(Controls.TransLeft), TransRight = Input.IsActionPressed(Controls.TransRight),
    };

    // ---------------------------------------------------------------- tick

    public override void _PhysicsProcess(double delta)
    {
        if (_script != null)
        {
            if (_shots.Count > 0 || _capturing) return;   // let the renderer catch up and capture before stepping on
            for (int i = 0; i < _scriptStepsPerFrame && _script != null; i++)
            {
                if (!_script.MoveNext() || _script.Current.Done) { _scriptResult = _script?.Current; _scriptDone = true; _script = null; break; }
                if (_shots.Count > 0) break;
            }
            return;
        }
        if (_scriptDone) { if (_shots.Count == 0 && !_capturing) FinishScript(_scriptResult); return; }
        TickOnce();
    }

    void FinishScript(ScriptResult? res)
    {
        _scriptDone = false;
        if (res == null) { GetTree().Quit(1); return; }
        GD.Print($"{_scriptName}: {(res.Passed ? "PASS" : "FAIL")} ({res.Ticks} ticks, {Units.FormatDuration(res.EndTime)} game time)");
        foreach (var f in res.Failures) GD.Print("  " + f);
        if (Args.ContainsKey("shots") || Args.ContainsKey("quit")) GetTree().CreateTimer(0.5).Timeout += () => GetTree().Quit(res.Passed ? 0 : 1);
    }

    /// <summary>One 50 Hz step: sample held keys, advance the world by the warped time. Public so tests can drive it.</summary>
    public void TickOnce()
    {
        if (World == null || Paused || InBuilder || Board.Visible || MenuOpen) return;
        World.Input = Sample();
        ulong tb = BenchTiming ? Time.GetTicksUsec() : 0;
        World.Advance(Units.PhysicsDt * World.EffectiveWarp);
        if (BenchTiming) SimUs += Time.GetTicksUsec() - tb;
        foreach (var e in World.Events)
        {
            Audio?.OnEvent(e);
            if (e.Kind is SimEventKind.SoiChange or SimEventKind.ActiveZone or SimEventKind.Launch or SimEventKind.Landed or SimEventKind.Crashed or SimEventKind.PartDestroyed or SimEventKind.Staging or SimEventKind.Docked or SimEventKind.Undocked or SimEventKind.Info or SimEventKind.Recovered) Hud.Say(e.Text);
            if (e.Kind is SimEventKind.Docked or SimEventKind.Undocked && Transfer.Visible) Transfer.Rebuild();
            if (e.Kind == SimEventKind.WarpStopped) Hud.Say("Warp stopped", 3);
        }
        if (World.Active is { } a && View.FocusCraft != a.Id) { View.FocusCraft = a.Id; if (View.Scale < 0.02 && !World.InFlashback) { } }
    }

    public override void _Process(double delta)
    {
        _frames++;
        CareerTick(delta);
        Screenshot();
        if (_shots.Count > 0 && !_capturing && _frames > 20)
        {
            string path = _shots.Dequeue();
            _capturing = true;
            GetTree().CreateTimer(0.1).Timeout += () =>
            {
                var img = GetViewport().GetTexture().GetImage();
                img.SavePng(path);
                GD.Print($"shot saved {path} scale={View.Scale:e2} t={World?.T:0.0}");
                _capturing = false;
            };
        }
    }

    void Screenshot()
    {
        if (!Args.TryGetValue("screenshot", out var path) || _shotTaken) return;
        int wanted = Args.TryGetValue("frames", out var fr) ? int.Parse(fr) : 30;
        if (_frames < Math.Max(20, wanted)) return;   // let the window settle at its capture size
        _shotTaken = true;
        GetTree().CreateTimer(0.08).Timeout += () =>
        {
            var img = GetViewport().GetTexture().GetImage();
            img.SavePng(path);
            GD.Print($"screenshot saved {path} ({img.GetWidth()}x{img.GetHeight()}) scale={View.Scale:e3} px/m t={World?.T:0.0}");
            GetTree().Quit();
        };
    }
}
