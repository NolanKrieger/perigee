using Godot;
using Perigee.Sim;

namespace Perigee;

/// <summary>`godot --headless --path . -- --selftest --save=/tmp/x.json`: drives the real game through its input and tick paths. Exit 0 only if everything passed.</summary>
public partial class Main
{
    readonly List<string> _fails = new();
    int _checks;

    void Check(bool ok, string what)
    {
        _checks++;
        GD.Print((ok ? "PASS " : "FAIL ") + what);
        if (!ok) _fails.Add(what);
    }

    void Press(string action, bool down = true)
    {
        var ev = new InputEventAction { Action = action, Pressed = down };
        Input.ParseInputEvent(ev);
        Input.FlushBufferedEvents();
    }

    void Tap(string action) => _UnhandledInput(new InputEventAction { Action = action, Pressed = true });

    async void RunSelfTest()
    {
        if (!Args.ContainsKey("save")) { GD.PrintErr("selftest needs --save=<temp path> so it never touches the real save"); GetTree().Quit(2); return; }
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check(RenderingServer.GetCurrentRenderingMethod() == "mobile", $"renderer is mobile ({RenderingServer.GetCurrentRenderingMethod()})");
        Check(Pal.Regular != null && Pal.Mono != null, "fonts load");

        // M1: the test system and two rails craft.
        StartDemo();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var w = World!;
        Check(w.Sys.Bodies.Count == 7 && w.Crafts.Count == 2, $"demo world: {w.Sys.Bodies.Count} bodies, {w.Crafts.Count} craft");
        Check(w.Sys.Home.Soi > 5e7 && w.Sys.Home.Soi < 8e7, $"home SOI {w.Sys.Home.Soi / 1e6:0.0} Mm");

        // Warp keys through the real input path: 7 presses → 100k×.
        for (int i = 0; i < 7; i++) Tap(Controls.WarpUp);
        Check(w.WarpIndex == 7, $"warp up ×7 → index {w.WarpIndex}");
        Tap(Controls.WarpDown);
        Check(w.WarpIndex == 6, "warp down");
        Tap(Controls.WarpReset);
        Check(w.WarpIndex == 0, "warp reset");

        // Warp until the tanker enters the moon's SOI; the clock must stop exactly on the analytic event time.
        var tanker = w.Find(1)!;
        var expected = Patcher.NextEvent(w.Sys, tanker.Rails, w.T, w.T + 10 * Units.Day);
        Check(expected.End == PatchEnd.EnterSoi, $"tanker predicted to enter SOI of {w.Sys[expected.NextBodyId].Name} at t={expected.TEnd:0.0}");
        for (int i = 0; i < 7; i++) Tap(Controls.WarpUp);
        int ticks = 0;
        while (tanker.BodyId == 1 && ticks++ < 100000) TickOnce();
        Check(tanker.BodyId == 2, $"tanker is now in the moon's SOI after {ticks} ticks");
        Check(Math.Abs(w.T - expected.TEnd) < 1e-5, $"clock stopped exactly at the transition (Δ={w.T - expected.TEnd:e2} s)");
        Check(w.WarpIndex == 0, "warp dropped to 1× at the SOI change");
        var (rm, _) = tanker.StateAt(w.T);
        Check(Math.Abs(rm.Length - w.Sys[2].Soi) < 1, $"tanker sits on the moon's SOI boundary ({rm.Length / 1e3:0.000} km vs {w.Sys[2].Soi / 1e3:0.000})");

        // Seamless zoom: every band renders without errors (draw is exercised by the process frames).
        double[] bands = { 200, 10, 0.5, 0.02, 1e-3, 5e-5, 2e-6, 2e-8 };
        foreach (var s in bands)
        {
            View.Scale = s;
            View.FollowFocus();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(View.Scale == s, $"zoom band {s} px/m rendered");
        }
        View.Scale = 0.5;
        Tap(Controls.Map);
        Check(View.IsMapScale, $"M toggles map scale ({View.Scale:e2} px/m)");
        Tap(Controls.Map);
        Check(!View.IsMapScale, "M again returns to the flight scale");
        Tap(Controls.CycleCraft);
        Check(w.ActiveCraftId == 0, "Tab cycles the active craft");
        var patches = w.Predict(w.Find(0)!);
        Check(patches.Count == 1 && patches[0].End == PatchEnd.Horizon, "satellite prediction: one closed leg");

        // M2: launch a rocket from the pad and fly it through the real key path.
        LaunchDesign("sounding", "Hopper");
        var hop = w.Active!;
        Check(hop.Mode == CraftMode.Landed && hop.Parts.Count == 8, $"sounding rocket on the pad ({hop.Parts.Count} parts, {hop.Mode})");
        double alt0 = hop.Pos.Length - w.Sys.Home.Radius;
        Tap(Controls.ThrottleFull);
        Tap(Controls.Stage);
        Check(hop.Throttle == 1 && hop.Parts.Any(p => p.IsEngine && p.Running), "Z + Space: throttle full, engine running");
        for (int i = 0; i < 100; i++) TickOnce();
        double alt1 = hop.Pos.Length - w.Sys.Home.Radius;
        Check(hop.Mode == CraftMode.Active && alt1 > alt0 + 20, $"lifted off: +{alt1 - alt0:0} m in 2 s");
        Press(Controls.RotateRight, true);
        for (int i = 0; i < 40; i++) TickOnce();
        Press(Controls.RotateRight, false);
        double pitch = ScriptVars.Pitch(hop);
        Check(pitch > 3, $"D pitches the nose prograde ({pitch:0.0}°)");
        for (int i = 0; i < 40; i++) TickOnce();
        Check(Math.Abs(hop.AngVel) < 0.1, $"rotation damps on release (ω={hop.AngVel:0.000} rad/s, turn rate was 0.9)");
        Press(Controls.ThrottleDown, true);
        for (int i = 0; i < 50; i++) TickOnce();
        Press(Controls.ThrottleDown, false);
        Check(hop.Throttle < 0.6 && hop.Throttle > 0.3, $"Ctrl ramps the throttle down ({hop.Throttle:0.00})");
        Tap(Controls.ThrottleCut);
        Check(hop.Throttle == 0, "X cuts the throttle");
        var report = hop.StageReport(w.Sys.Home.SurfaceGravity);
        Check(report.Count >= 1 && report[0].DeltaVVac > 500, $"Δv readout {report[0].DeltaVVac:0} m/s");
        Tap(Controls.Chutes);
        Check(hop.Parts.Any(p => p.Def.Has(PartFlags.Parachute) && p.Deployed), "P deploys the parachute");
        // Scripted flight through the Godot host: the sounding hop, fast, no rendering needed.
        var script = FlightScript.Parse(Godot.FileAccess.GetFileAsString("res://scripts/sounding-hop.flight"), "sounding-hop");
        World = new World(TestSystems.Basic(), 7); View.World = World;
        var host = new GodotHost(this);
        var result = script.Run(host);
        Check(result.Passed, $"sounding-hop.flight passes through the real input path ({result.Ticks} ticks{(result.Passed ? "" : ": " + string.Join("; ", result.Failures))})");

        // M3: the builder screen through its real handlers.
        string designsDir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Args["save"])!, "perigee-selftest-designs");
        if (System.IO.Directory.Exists(designsDir)) System.IO.Directory.Delete(designsDir, true);
        Builder.DesignsDir = designsDir;
        StartDemo();   // a fresh career: starter parts unlocked, $800k
        OpenBuilder();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check(InBuilder && Builder.Bp.Count == 1, "builder opens with a bare core");
        var tank = Parts.Get("tank-s-short");
        Builder.Hold(tank);
        var below = Builder.Bp.Snap(tank, new Vec2d(0, -0.9))!;
        Builder.CanvasClick(Builder.ToCanvas(below.Centre), false);
        Check(Builder.Bp.Count == 2 && Builder.Bp.Design.Parts[1].DefId == "tank-s-short", "clicking a snapped node places the held part");
        Builder.Hold(Parts.Get("eng-kestrel"));
        var eng = Builder.Bp.Snap(Parts.Get("eng-kestrel"), new Vec2d(0, -1.9))!;
        Builder.CanvasClick(Builder.ToCanvas(eng.Centre), false);
        Builder.Hold(Parts.Get("fin"));
        var fin = Builder.Bp.Snap(Parts.Get("fin"), new Vec2d(0.55, eng.Centre.Y))!;
        Builder.CanvasClick(Builder.ToCanvas(fin.Centre), false);
        Check(Builder.Bp.Count == 5, $"engine + mirrored fins placed ({Builder.Bp.Count} parts)");
        Check(Builder.Bp.Design.Stages.Count == 1 && Builder.Bp.Design.Stages[0].Contains(2), "auto-staging put the engine in stage 1");
        Builder.CanvasClick(Builder.ToCanvas(new Vec2d(0.6, eng.Centre.Y - 0.2)), false);
        Check(Builder.Selected == 3 || Builder.Selected == 4, $"clicking a fin selects it ({Builder.Selected})");
        Builder.DeleteSelected();
        Check(Builder.Bp.Count == 3, $"Delete removes the fin and its mirror ({Builder.Bp.Count} parts)");
        Builder.Hold(Parts.Get("chute-nose"));
        var top = Builder.Bp.Snap(Parts.Get("chute-nose"), new Vec2d(0, 0.6))!;
        Builder.CanvasClick(Builder.ToCanvas(top.Centre), false);
        Check(Builder.Bp.Design.Stages.Count == 2, "a chute adds its own last stage");
        string saved = Builder.SaveDesign();
        Check(System.IO.File.Exists(saved), $"save writes {saved}");
        Check(DesignLibrary.Load(designsDir).Count == 1, "library lists the saved design");
        var stats = Builder.Bp.Stats(w.Sys.Home.SurfaceGravity);
        Check(stats.Issues.Count == 0 && stats.TwrSurface > 1, $"design is launchable (TWR {stats.TwrSurface:0.0}, Δv {stats.TotalDeltaVVac:0})");
        Builder.Launch();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var launched = World!.Active;
        Check(!InBuilder && launched is { Mode: CraftMode.Landed } && launched.Parts.Count == 4, $"LAUNCH puts the design on the pad and returns to flight ({launched?.Mode}, {launched?.Parts.Count} parts, builder {InBuilder})");
        System.IO.Directory.Delete(designsDir, true);

        // M8: the contract board through its real handlers (C opens, Accept button, Esc closes) and the tutorial hint on the HUD.
        w = World!;
        Check(w.Offers.Any(k => k.Tutorial == 1) && w.Offers.Count() >= 3, $"career start offers T1 + {w.Offers.Count()} open offers");
        _UnhandledInput(new InputEventKey { Keycode = Key.C, Pressed = true });
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check(Board.Visible && Board.OfferCards == w.Offers.Count(), $"C opens the board with {Board.OfferCards} offer cards");
        Check(Board.PressOfferButton("Accept"), "Accept button pressed on the first offer (T1)");
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var lesson = w.AcceptedContracts.FirstOrDefault();
        Check(lesson is { Tutorial: 1 } && Board.ActiveCards == 1, $"T1 is active ({lesson?.Title}) and listed under ACTIVE");
        Check(lesson != null && lesson.Hint.Length > 0 && lesson.Deadline > w.T, "the lesson carries a hint and a deadline");
        _UnhandledInput(new InputEventKey { Keycode = Key.Escape, Pressed = true });
        Check(!Board.Visible, "Esc closes the board");
        Tap("throttle_full"); Tap("stage");
        for (int i = 0; i < 50; i++) TickOnce();
        Check(w.AcceptedContracts.Count() == 1 && lesson!.State == ContractState.Accepted, "the sim runs with the contract active (no premature completion)");

        // M12: the career wrapper through its real screens. Saves go to a temp careers dir (--careers) and a temp profile (--profile).
        string careersDir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Args["save"])!, "perigee-selftest-careers");
        if (System.IO.Directory.Exists(careersDir)) System.IO.Directory.Delete(careersDir, true);
        Args["careers"] = careersDir;
        ShowTitle();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check(Title.Visible && World == null && !Hud.Visible, "title screen shows with no world loaded");
        ShowNewCareer();
        Check(NewCareerUi.Visible && !Title.Visible, "New Career opens the setup screen");
        Args["seed"] = "4242";
        NewCareer("Selftest Co", Preset.Relaxed);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Args.Remove("seed");
        var cw = World!;
        Check(InCareer && cw.AgencyName == "Selftest Co" && cw.Preset.Name == "Relaxed" && cw.SystemKind == "generated" && cw.Sys.Bodies.Count >= 7, $"career started: {cw.AgencyName} on {cw.Preset.Name}, {cw.Sys.Bodies.Count} bodies, cash {Units.FormatMoney(cw.Cash)}");
        string savePath = Careers.PathFor("Selftest Co");
        Check(System.IO.File.Exists(savePath), $"new career autosaved to {savePath}");
        Check(cw.Offers.Any(k => k.Tutorial == 1), "T1 is offered in the new career");
        // HQ: every tab builds.
        _UnhandledInput(new InputEventKey { Keycode = Key.H, Pressed = true });
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check(Hq.Visible && Hq.TabCount == 6, $"H opens HQ with {Hq.TabCount} tabs");
        for (int t = 0; t < Hq.TabCount; t++) { Hq.CurrentTab = t; Hq.Rebuild(); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        Check(Hq.TabName(0) == "Contracts" && Hq.TabName(5) == "Score" && Board.GetParent() != BoardHome, "all six HQ tabs built; the contract board lives inside the Contracts tab while HQ is open");
        _UnhandledInput(new InputEventKey { Keycode = Key.Escape, Pressed = true });
        Check(!Hq.Visible && Board.GetParent() == BoardHome && Hud.Visible, "Esc closes HQ and returns the board to the flight layer");
        // Pause menu.
        _UnhandledInput(new InputEventAction { Action = Controls.Pause, Pressed = true });
        Check(Pause.Visible && Paused, "Esc opens the pause menu and freezes the sim");
        double tBefore = cw.T; TickOnce();
        Check(cw.T == tBefore, "no ticks while paused");
        TogglePause();
        Check(!Pause.Visible && !Paused, "resume");
        // Autosave on a sim request, then continue from disk.
        cw.Cash += 1234;
        cw.SaveRequested = true; _lastTriggerSave = -1000; CareerTick(0.1);
        var reloaded = Careers.Load(savePath);
        Check(reloaded.Cash == cw.Cash && reloaded.StateHash() == cw.StateHash(), $"autosave triggered by the sim and the file round-trips (hash {cw.StateHash():x8})");
        ShowTitle();
        Check(Title.Visible && World == null, "quit to title unloads the world");
        ContinueCareer(savePath);
        Check(InCareer && World!.AgencyName == "Selftest Co" && World.StateHash() == reloaded.StateHash(), "Continue reloads the same state");
        // Settings: rebind a key through the profile and reset it.
        Profile.Rebind(Controls.Stage, Key.N);
        Check(Profile.KeyLabel(Controls.Stage) == "N" && InputMap.ActionGetEvents(Controls.Stage).OfType<InputEventKey>().Any(e => e.PhysicalKeycode == Key.N), "rebinding Stage to N updates the live input map");
        Profile.ResetBindings();
        Check(Profile.KeyLabel(Controls.Stage) == "Space", "reset restores the default keys");
        ShowSettings(fromTitle: false);
        Check(Settings != null && Settings.Visible, "settings screen opens");
        CloseSettings(fromTitle: false);
        Check(!Settings!.Visible && Pause.Visible, "closing settings from the pause menu returns to it");
        TogglePause();
        // Logbook + personal best on career over.
        World.EndCareer("selftest");
        CareerTick(0.1);
        Check(Logbook.Visible && Profile.Data.Bests.ContainsKey("Relaxed"), "career over shows the logbook and records a personal best");
        ShowTitle();
        System.IO.Directory.Delete(careersDir, true);
        if (System.IO.File.Exists(Profile.Path)) System.IO.File.Delete(Profile.Path);

        // M13: audio. The bank loads, and the mix follows the state (pure function, so no listening needed).
        Check(Audio != null && Audio.Loaded == Audio.Music.Length + Audio.Loops.Length + Audio.Shots.Length, $"audio bank loaded {Audio?.Loaded} streams");
        var mTitle = Audio.Mix(null, false);
        Check(mTitle["music_pad"] > 0 && mTitle["engine_air"] == 0 && mTitle["music_storm"] == 0, "title mix: pad only");
        var aw = SimHost.NewScriptWorld(); aw.SystemKind = "test";
        var ahost = new SimHost(aw);
        ahost.Launch("orbital", "Ship"); ahost.Tap("throttle_full"); ahost.Tap("stage"); for (int i = 0; i < 50; i++) ahost.Tick();
        var mLaunch = Audio.Mix(aw, false);
        Check(mLaunch["engine_air"] > 0.5f && mLaunch["engine_vac"] < 0.2f && mLaunch["music_swell"] == 1f, $"launch mix: engine in air {mLaunch["engine_air"]:0.00}, vacuum {mLaunch["engine_vac"]:0.00}, swell {mLaunch["music_swell"]}");
        var vw = SimHost.NewScriptWorld(); vw.SystemKind = "test";
        var vhost = new SimHost(vw);
        SimHost.SpawnInto(vw, "lander", "Probe", 120_000, 0, 1); vhost.Tap("stage"); vhost.Tap("throttle_full"); for (int i = 0; i < 50; i++) vhost.Tick();
        var mVac = Audio.Mix(vw, false);
        Check(mVac["engine_vac"] > 0.5f && mVac["engine_air"] == 0f, $"vacuum mix: rumble {mVac["engine_vac"]:0.00}, no roar");
        SimHost.SpawnInto(vw, "station", "Target", 120_000, 0.02, 0.5); vw.ActiveCraftId = vw.Crafts.First(c => c.Name == "Probe").Id; vw.Active!.TargetCraft = vw.Crafts.First(c => c.Name == "Target").Id;
        Check(Audio.Mix(vw, false)["music_pulse"] == 1f, "docking pulse with a target inside the loaded bubble");
        vw.ScheduleStorm(1, 1);
        Check(Audio.Mix(vw, false)["music_storm"] == 1f, "storm motif during a storm warning");
        vw.Cash = 0; vw.InsolventSince = vw.T;
        Check(Audio.Mix(vw, false).Values.All(v => v == 0f), "silence when insolvent");
        Check(Audio.Mix(vw, true)["engine_vac"] == 0f, "engine loops stop while paused");
        // M13: achievements — conditions read world state; unlocks land in the profile and go to the platform backend.
        Check(Achievements.All.Length == 20 && Achievements.All.Select(a => a.Id).Distinct().Count() == 20, "20 achievements with distinct ids (GDD §19)");
        Check(Platform != null && Platform.Available, $"platform backend: {Platform?.Name}");
        var sat = Achievements.Satisfied(aw).ToList();
        Check(sat.Contains("LIFTOFF") && !sat.Contains("HANDSHAKE"), $"a launched world satisfies Liftoff only ({string.Join(",", sat)})");
        aw.CareerStats["dockings"] = 1; aw.CareerStats["booster_streak"] = 3; aw.CareerStats["depot_sales"] = 1;
        sat = Achievements.Satisfied(aw).ToList();
        Check(sat.Contains("HANDSHAKE") && sat.Contains("HAT_TRICK") && sat.Contains("GAS_STATION"), "docking, a 3-flight booster streak and a depot sale unlock their achievements");
        var fresh = UnlockAchievements(aw);
        Check(fresh.Contains("HAT_TRICK") && Profile.Data.Achievements.Contains("LIFTOFF") && Profile.Data.Achievements.Contains("HAT_TRICK") && UnlockAchievements(aw).Count == 0, $"unlocks are recorded once in the profile ({Profile.Data.Achievements.Count} total, {fresh.Count} new here)");
        GD.Print($"selftest: {_checks - _fails.Count}/{_checks} passed");
        GetTree().Quit(_fails.Count == 0 ? 0 : 1);
    }
}
