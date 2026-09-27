using Godot;
using Perigee.Sim;

namespace Perigee;

/// <summary>Career flow (GDD §3, §16, §18): title → new/continue career → flight, with HQ, pause, settings, confirmations, autosave and the end logbook.</summary>
public partial class Main
{
    public TitleScreen Title { get; private set; } = null!;
    public NewCareerScreen NewCareerUi { get; private set; } = null!;
    public SettingsScreen? Settings { get; private set; }
    public PauseMenu Pause { get; private set; } = null!;
    public HqScreen Hq { get; private set; } = null!;
    public LogbookScreen Logbook { get; private set; } = null!;
    public CanvasLayer BoardHome { get; private set; } = null!;   // where the contract board lives when HQ is closed
    CanvasLayer _menuLayer = null!;
    ConfirmationDialog _confirm = null!;
    Action? _confirmAction;
    double _autosaveClock;
    double _lastTriggerSave = -100;
    bool _logbookShown;
    bool _careerMode;
    public bool InCareer => _careerMode && World != null && !Title.Visible && !NewCareerUi.Visible;
    /// <summary>Any full-screen menu that should freeze the sim.</summary>
    public bool MenuOpen => Title.Visible || NewCareerUi.Visible || (Settings?.Visible ?? false) || Pause.Visible || Hq.Visible || Logbook.Visible || _confirm.Visible;

    void SetupCareerUi(CanvasLayer uiLayer)
    {
        BoardHome = uiLayer;
        _menuLayer = new CanvasLayer { Layer = 30 };
        AddChild(_menuLayer);
        Title = new TitleScreen { Game = this, Visible = false }; _menuLayer.AddChild(Title);
        NewCareerUi = new NewCareerScreen { Game = this, Visible = false }; _menuLayer.AddChild(NewCareerUi);
        Hq = new HqScreen { Game = this }; _menuLayer.AddChild(Hq);
        Pause = new PauseMenu { Game = this, Visible = false }; _menuLayer.AddChild(Pause);
        Logbook = new LogbookScreen { Game = this, Visible = false }; _menuLayer.AddChild(Logbook);
        _confirm = new ConfirmationDialog { Title = "Confirm", OkButtonText = "Yes", CancelButtonText = "No", MinSize = new Vector2I(520, 160) };
        _confirm.Confirmed += () => { var a = _confirmAction; _confirmAction = null; a?.Invoke(); };
        _confirm.Canceled += () => _confirmAction = null;
        _menuLayer.AddChild(_confirm);
    }

    /// <summary>Irreversible spend or loss: ask first (brief: confirmations for irreversible spends).</summary>
    public void ConfirmThen(string text, Action action)
    {
        if (Args.ContainsKey("selftest") || Args.ContainsKey("script")) { action(); return; }   // tests drive the real handlers without a modal
        _confirmAction = action;
        _confirm.DialogText = text;
        _confirm.PopupCentered();
    }

    // ---------------------------------------------------------------- screens

    public void ShowTitle()
    {
        if (World != null && !World.GameOver) SaveNow();
        World = null; View.World = null; _careerMode = false;
        Hq.Visible = false; Pause.Visible = false; Logbook.Visible = false; NewCareerUi.Visible = false; Builder.Visible = false; Board.Hide(); Transfer.Visible = false;
        Hud.Visible = false; View.Visible = false;
        Title.Refresh(); Title.Visible = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    public void ShowNewCareer() { Title.Visible = false; Logbook.Visible = false; NewCareerUi.Visible = true; }

    public void NewCareer(string agency, Preset preset)
    {
        ulong seed = (ulong)Random.Shared.NextInt64() ^ (ulong)DateTime.UtcNow.Ticks;
        if (Args.TryGetValue("seed", out var st) && ulong.TryParse(st, out var s)) seed = s;
        var sys = SystemGenerator.Generate(seed);
        var w = new World(sys, seed) { SystemKind = "generated" };
        w.StartCareer(preset, agency);
        EnterCareer(w);
        Careers.Save(w);
        Hud.Say($"{agency}: a new career on {preset.Name}. Press B to build your first rocket.", 10);
    }

    public void ContinueCareer(string path)
    {
        World w;
        try { w = Careers.Load(path); }
        catch (Exception e) { Hud.Say($"Could not load: {e.Message}", 8); GD.PushWarning(e.ToString()); return; }
        EnterCareer(w);
        Hud.Say($"Welcome back to {w.AgencyName}. Day {(int)(w.T / Units.Day) + 1}.", 8);
    }

    void EnterCareer(World w)
    {
        World = w; View.World = w; _careerMode = w.SystemKind == "generated";
        Title.Visible = false; NewCareerUi.Visible = false; Logbook.Visible = false; Pause.Visible = false; Hq.Visible = false;
        View.Visible = true; Hud.Visible = true;
        _logbookShown = false; _autosaveClock = 0; Paused = false;
        if (w.Active is { } a) { View.FocusCraft = a.Id; View.FocusBody = a.BodyId; View.Scale = a.Mode == CraftMode.Landed ? 12 : 0.002; }
        else Focus("site");
        View.FollowFocus();
        if (w.GameOver) ShowLogbook();
    }

    public void ShowSettings(bool fromTitle)
    {
        if (Settings == null) { Settings = new SettingsScreen { Game = this }; _menuLayer.AddChild(Settings); }
        Settings.FromTitle = fromTitle; Settings.Visible = true;
        Title.Visible = false; Pause.Visible = false;
    }

    public void CloseSettings(bool fromTitle)
    {
        if (Settings != null) Settings.Visible = false;
        if (fromTitle || World == null) { Title.Refresh(); Title.Visible = true; } else Pause.Visible = true;
    }

    public void TogglePause()
    {
        if (World == null || Title.Visible) return;
        Pause.Visible = !Pause.Visible;
        Paused = Pause.Visible;
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    public void ShowHq() { if (World == null) return; Pause.Visible = false; Hq.Show(); }
    public void CloseHq() { Hq.Hide(); }

    public void QuitToTitle() { Pause.Visible = false; ShowTitle(); }

    public void SaveNow()
    {
        if (World == null || World.InFlashback || !_careerMode) return;
        try { Careers.Save(World); _lastTriggerSave = _realTime; }
        catch (Exception e) { Hud.Say($"Autosave failed: {e.Message}", 8); GD.PushWarning(e.ToString()); }
    }

    double _realTime;

    /// <summary>Autosave triggers (GDD §18): sim requests (warp stop, docking, landing, recovery, purchase) throttled to one per 5 s, plus every 3 real minutes.</summary>
    public IPlatform Platform = null!;
    double _achievementClock;
    /// <summary>Unlocks every achievement the world now satisfies; returns the new ids (profile + platform backend, HUD toast).</summary>
    public List<string> UnlockAchievements(World w)
    {
        var fresh = new List<string>();
        foreach (var id in Achievements.Satisfied(w))
        {
            if (!Profile.UnlockAchievement(id)) continue;
            Platform?.Unlock(id);
            fresh.Add(id);
            Hud.Say($"Achievement: {Achievements.Find(id)?.Title ?? id}", 8);
            Audio?.Play("beep");
        }
        return fresh;
    }

    void CareerTick(double delta)
    {
        _realTime += delta;
        if (World == null || !InCareer) return;
        _achievementClock += delta;
        if (_achievementClock >= 2 && !MenuOpen) { _achievementClock = 0; UnlockAchievements(World); }
        if (!MenuOpen) World.PlayedSeconds += delta;
        _autosaveClock += delta;
        bool trigger = World.SaveRequested && _realTime - _lastTriggerSave >= Careers.MinGap;
        if ((trigger || _autosaveClock >= Careers.AutosaveInterval) && !World.InFlashback && _careerMode)
        {
            _autosaveClock = 0;
            SaveNow();
        }
        if (World.GameOver && !_logbookShown) ShowLogbook();
    }

    /// <summary>Dev/screenshot worlds (demo, scripts) never touch the career files.</summary>
    public void MarkDevWorld() { _careerMode = false; }

    void ShowLogbook()
    {
        if (World == null) return;
        _logbookShown = true;
        bool best = _careerMode && Profile.RecordBest(World.Preset.Name, World.PeakScore, (int)(World.T / Units.Day) + 1, World.AgencyName);
        if (_careerMode) SaveNow();
        Hud.Visible = false;
        Logbook.Show(World, best);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest) { if (World != null && InCareer) SaveNow(); Profile.Save(); GetTree().Quit(); }
    }
}
