using Godot;
using Perigee.Sim;

namespace Perigee;

/// <summary>Shared building blocks for the full-screen menus (title, new career, settings, pause, logbook, HQ): palette-styled panels, labels and buttons.</summary>
public static class Ui
{
    public static PanelContainer Panel(float w, float h, float x, float y)
    {
        var p = new PanelContainer { Position = new Vector2(x, y), Size = new Vector2(w, h) };
        p.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = Pal.Panel with { A = 0.97f }, BorderColor = Pal.PanelBorder, BorderWidthBottom = 1, BorderWidthTop = 1, BorderWidthLeft = 1, BorderWidthRight = 1, ContentMarginLeft = 24, ContentMarginRight = 24, ContentMarginTop = 18, ContentMarginBottom = 18 });
        return p;
    }
    public static Label Label(string text, Font? f = null, int size = 16, Color? c = null, float width = 0)
    {
        var l = new Label { Text = text };
        l.AddThemeFontOverride("font", f ?? Pal.Regular); l.AddThemeFontSizeOverride("font_size", size); l.AddThemeColorOverride("font_color", c ?? Pal.Text);
        if (width > 0) { l.AutowrapMode = TextServer.AutowrapMode.WordSmart; l.CustomMinimumSize = new Vector2(width, 0); }
        return l;
    }
    public static Label Title(string text) => Label(text, Pal.Bold, 30, Pal.Accent);
    public static Label Heading(string text) => Label(text, Pal.Bold, 18, Pal.Accent);
    public static Label Mono(string text, int size = 15, Color? c = null, float width = 0) => Label(text, Pal.Mono, size, c ?? Pal.Text, width);
    public static Button Button(string text, int size = 17, float minW = 220, bool primary = false)
    {
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(minW, 40) };
        b.Pressed += () => Audio.I?.Click();
        b.AddThemeFontOverride("font", Pal.Regular); b.AddThemeFontSizeOverride("font_size", size); b.AddThemeColorOverride("font_color", Pal.Text);
        b.AddThemeStyleboxOverride("normal", Box(primary ? Pal.Accent with { A = 0.85f } : Pal.PanelBorder with { A = 0.35f }, primary ? Pal.Accent : Pal.PanelBorder));
        b.AddThemeStyleboxOverride("hover", Box(primary ? Pal.Accent : Pal.PanelBorder with { A = 0.7f }, Pal.Text));
        b.AddThemeStyleboxOverride("pressed", Box(Pal.Accent with { A = 0.6f }, Pal.Accent));
        b.AddThemeStyleboxOverride("disabled", Box(Pal.PanelBorder with { A = 0.15f }, Pal.PanelBorder with { A = 0.4f }));
        return b;
    }
    public static StyleBoxFlat Box(Color bg, Color border) => new() { BgColor = bg, BorderColor = border, BorderWidthBottom = 1, BorderWidthTop = 1, BorderWidthLeft = 1, BorderWidthRight = 1, ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 6, ContentMarginBottom = 6 };
    public static HSlider Slider(double value, double min = 0, double max = 1, double step = 0.01)
    {
        var s = new HSlider { MinValue = min, MaxValue = max, Step = step, Value = value, CustomMinimumSize = new Vector2(260, 24) };
        return s;
    }
    public static Control Spacer(float h) => new Control { CustomMinimumSize = new Vector2(0, h) };
    public static void Clear(Node n) { foreach (var c in n.GetChildren()) { n.RemoveChild(c); c.QueueFree(); } }
}

/// <summary>Title screen (GDD §16): New Career · Continue · Settings · Quit, with personal bests per preset.</summary>
public partial class TitleScreen : Control
{
    public Main Game = null!;
    VBoxContainer _careers = null!;
    Label _bests = null!;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        var bg = new ColorRect { Color = Pal.Space }; bg.SetAnchorsPreset(LayoutPreset.FullRect); AddChild(bg);
        var panel = Ui.Panel(720, 760, 600, 140); AddChild(panel);
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 12); panel.AddChild(box);
        box.AddChild(Ui.Label("PERIGEE FUEL CO.", Pal.Bold, 44, Pal.Accent));
        box.AddChild(Ui.Label("A career in rockets, refuelling and getting there cheaper.", Pal.Regular, 17, Pal.TextDim));
        box.AddChild(Ui.Spacer(8));
        var nb = Ui.Button("New Career", 19, 320, primary: true); nb.Pressed += () => Game.ShowNewCareer(); box.AddChild(nb);
        box.AddChild(Ui.Heading("CONTINUE"));
        _careers = new VBoxContainer(); _careers.AddThemeConstantOverride("separation", 6); box.AddChild(_careers);
        box.AddChild(Ui.Spacer(4));
        var sb = Ui.Button("Settings", 17, 320); sb.Pressed += () => Game.ShowSettings(fromTitle: true); box.AddChild(sb);
        var qb = Ui.Button("Quit", 17, 320); qb.Pressed += () => Game.GetTree().Quit(); box.AddChild(qb);
        box.AddChild(Ui.Spacer(10));
        box.AddChild(Ui.Heading("PERSONAL BESTS"));
        _bests = Ui.Mono("", 15, Pal.TextDim, 660); box.AddChild(_bests);
        box.AddChild(Ui.Spacer(6));
        box.AddChild(Ui.Label("Keyboard + mouse. One autosave per career, no reverts. Career ends at $0.", Pal.Regular, 13, Pal.TextDim with { A = 0.8f }, 660));
        Refresh();
    }

    public void Refresh()
    {
        Ui.Clear(_careers);
        var list = Careers.List();
        if (list.Count == 0) _careers.AddChild(Ui.Label("No careers yet.", Pal.Regular, 15, Pal.TextDim));
        foreach (var (path, data) in list.Take(6))
        {
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 8);
            var b = Ui.Button($"{data.Agency}  ·  {data.Preset}  ·  day {(int)(data.T / Units.Day) + 1}  ·  {Units.FormatMoney(data.Cash)}{(data.GameOver ? "  ·  OVER" : "")}", 15, 520);
            b.Alignment = HorizontalAlignment.Left;
            string p = path; b.Pressed += () => Game.ContinueCareer(p);
            row.AddChild(b);
            var del = Ui.Button("Delete", 14, 90); del.Pressed += () => Game.ConfirmThen($"Delete the career \"{data.Agency}\"? This cannot be undone.", () => { Careers.Delete(p); Refresh(); });
            row.AddChild(del);
            _careers.AddChild(row);
        }
        var lines = new List<string>();
        foreach (var preset in Preset.All)
            lines.Add(Profile.Data.Bests.TryGetValue(preset.Name, out var best) ? $"{preset.Name,-9} {Units.FormatMoney(best.PeakScore),12}   {best.Days} day{(best.Days == 1 ? "" : "s")}   {best.Agency}   {best.When}" : $"{preset.Name,-9} —");
        _bests.Text = string.Join("\n", lines);
    }
}

/// <summary>New career (GDD §3): agency name + preset; the system is generated from a hidden seed.</summary>
public partial class NewCareerScreen : Control
{
    public Main Game = null!;
    LineEdit _name = null!;
    OptionButton _preset = null!;
    Label _desc = null!;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        var bg = new ColorRect { Color = Pal.Space }; bg.SetAnchorsPreset(LayoutPreset.FullRect); AddChild(bg);
        var panel = Ui.Panel(760, 560, 580, 220); AddChild(panel);
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 12); panel.AddChild(box);
        box.AddChild(Ui.Title("NEW CAREER"));
        box.AddChild(Ui.Label("Agency name"));
        _name = new LineEdit { Text = "Perigee Fuel Co.", MaxLength = 32, CustomMinimumSize = new Vector2(500, 36) };
        _name.AddThemeFontOverride("font", Pal.Regular); _name.AddThemeFontSizeOverride("font_size", 17);
        box.AddChild(_name);
        box.AddChild(Ui.Label("Difficulty preset"));
        _preset = new OptionButton { CustomMinimumSize = new Vector2(300, 36) };
        _preset.AddThemeFontOverride("font", Pal.Regular); _preset.AddThemeFontSizeOverride("font_size", 16);
        foreach (var p in Preset.All) _preset.AddItem(p.Name);
        _preset.Selected = 1;
        _preset.ItemSelected += _ => Describe();
        box.AddChild(_preset);
        _desc = Ui.Mono("", 14, Pal.TextDim, 700); box.AddChild(_desc);
        box.AddChild(Ui.Label("A fresh star system is generated for this career: a G or K star, 6–10 planets, an Earth-like home with a launch pad. Ice and ore are always within reach of a small rocket.", Pal.Regular, 14, Pal.TextDim, 700));
        box.AddChild(Ui.Spacer(6));
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 12);
        var start = Ui.Button("Start", 18, 220, primary: true); start.Pressed += Start; row.AddChild(start);
        var back = Ui.Button("Back", 17, 160); back.Pressed += () => Game.ShowTitle(); row.AddChild(back);
        box.AddChild(row);
        Describe();
    }

    void Describe()
    {
        var p = Preset.All[Math.Clamp(_preset.Selected, 0, Preset.All.Length - 1)];
        _desc.Text = $"Start {Units.FormatMoney(p.StartingCash)} · parts ×{p.PartPrices:0.00} · upkeep ×{p.Upkeep:0.00} · contract pay ×{p.ContractPay:0.00}\nstorms ×{p.StormRate:0.0} · debris ×{p.DebrisRate:0.0} · market depth ×{p.MarketDepth:0.0} · insolvency grace {p.GraceDays} days";
    }

    void Start()
    {
        string name = _name.Text.Trim(); if (name.Length == 0) name = "Perigee Fuel Co.";
        var preset = Preset.All[Math.Clamp(_preset.Selected, 0, Preset.All.Length - 1)];
        if (Careers.List().Any(c => Careers.Slug(c.data.Agency) == Careers.Slug(name)))
        {
            Game.ConfirmThen($"A career named \"{name}\" exists. Overwrite it?", () => Game.NewCareer(name, preset));
            return;
        }
        Game.NewCareer(name, preset);
    }
}

/// <summary>Settings (brief): volume per bus, resolution/fullscreen, UI scale, key rebinding.</summary>
public partial class SettingsScreen : Control
{
    public Main Game = null!;
    public bool FromTitle;
    VBoxContainer _keys = null!;
    Label _capture = null!;
    string? _rebinding;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        var bg = new ColorRect { Color = Pal.Space with { A = 0.92f } }; bg.SetAnchorsPreset(LayoutPreset.FullRect); AddChild(bg);
        var panel = Ui.Panel(1400, 920, 260, 80); AddChild(panel);
        var cols = new HBoxContainer(); cols.AddThemeConstantOverride("separation", 40); panel.AddChild(cols);
        var left = new VBoxContainer { CustomMinimumSize = new Vector2(600, 0) }; left.AddThemeConstantOverride("separation", 10); cols.AddChild(left);
        left.AddChild(Ui.Title("SETTINGS"));
        left.AddChild(Ui.Heading("AUDIO"));
        var d = Profile.Data;
        AddSlider(left, "Master", d.MasterVolume, v => { d.MasterVolume = v; Profile.Apply(); });
        AddSlider(left, "Music", d.MusicVolume, v => { d.MusicVolume = v; Profile.Apply(); });
        AddSlider(left, "Sound effects", d.SfxVolume, v => { d.SfxVolume = v; Profile.Apply(); });
        AddSlider(left, "Interface", d.UiVolume, v => { d.UiVolume = v; Profile.Apply(); });
        left.AddChild(Ui.Heading("DISPLAY"));
        var fs = new CheckButton { Text = "Fullscreen", ButtonPressed = d.Fullscreen }; fs.AddThemeFontOverride("font", Pal.Regular); fs.AddThemeFontSizeOverride("font_size", 16);
        fs.Toggled += on => { d.Fullscreen = on; Profile.Apply(); Profile.Save(); };
        left.AddChild(fs);
        var resRow = new HBoxContainer(); resRow.AddChild(Ui.Label("Window size", Pal.Regular, 16, null, 180));
        var res = new OptionButton(); res.AddThemeFontOverride("font", Pal.Regular); res.AddThemeFontSizeOverride("font_size", 15);
        foreach (var (w, h) in Profile.Resolutions) res.AddItem($"{w} × {h}");
        res.Selected = Math.Clamp(d.ResolutionIndex, 0, Profile.Resolutions.Length - 1);
        res.ItemSelected += i => { d.ResolutionIndex = (int)i; Profile.Apply(); Profile.Save(); };
        resRow.AddChild(res); left.AddChild(resRow);
        AddSlider(left, "UI scale", d.UiScale, v => { d.UiScale = v; Profile.Apply(); }, 0.75, 1.5, 0.05);
        left.AddChild(Ui.Spacer(10));
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 12);
        var back = Ui.Button("Back", 17, 180, primary: true); back.Pressed += () => { Profile.Save(); Game.CloseSettings(FromTitle); }; row.AddChild(back);
        var reset = Ui.Button("Reset keys", 15, 160); reset.Pressed += () => { Profile.ResetBindings(); RefreshKeys(); }; row.AddChild(reset);
        left.AddChild(row);
        var right = new VBoxContainer { CustomMinimumSize = new Vector2(640, 0) }; right.AddThemeConstantOverride("separation", 6); cols.AddChild(right);
        right.AddChild(Ui.Heading("KEYS  ·  click a key, then press the new one (Esc cancels)"));
        _capture = Ui.Label("", Pal.Regular, 14, Pal.Accent); right.AddChild(_capture);
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(640, 700), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; right.AddChild(scroll);
        _keys = new VBoxContainer { CustomMinimumSize = new Vector2(620, 0) }; _keys.AddThemeConstantOverride("separation", 4); scroll.AddChild(_keys);
        RefreshKeys();
    }

    void AddSlider(VBoxContainer box, string label, double value, Action<double> set, double min = 0, double max = 1, double step = 0.01)
    {
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 12);
        row.AddChild(Ui.Label(label, Pal.Regular, 16, null, 180));
        var s = Ui.Slider(value, min, max, step);
        var val = Ui.Mono($"{value:0.00}", 14, Pal.TextDim);
        s.ValueChanged += v => { set(v); val.Text = $"{v:0.00}"; };
        s.DragEnded += _ => Profile.Save();
        row.AddChild(s); row.AddChild(val);
        box.AddChild(row);
    }

    void RefreshKeys()
    {
        Ui.Clear(_keys);
        foreach (var (action, label) in Controls.All)
        {
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 10);
            row.AddChild(Ui.Label(label, Pal.Regular, 15, null, 300));
            var b = Ui.Button(Profile.KeyLabel(action), 15, 200);
            string a = action;
            b.Pressed += () => { _rebinding = a; _capture.Text = $"Press a key for “{label}”…"; };
            row.AddChild(b);
            _keys.AddChild(row);
        }
    }

    public override void _UnhandledInput(InputEvent ev)
    {
        if (_rebinding == null) return;
        if (ev is InputEventKey { Pressed: true } k)
        {
            if (k.Keycode == Key.Escape) { _rebinding = null; _capture.Text = ""; GetViewport().SetInputAsHandled(); return; }
            Profile.Rebind(_rebinding, k.PhysicalKeycode);
            _capture.Text = $"Bound to {OS.GetKeycodeString(k.PhysicalKeycode)}.";
            _rebinding = null;
            RefreshKeys();
            GetViewport().SetInputAsHandled();
        }
    }
}

/// <summary>Pause menu: resume, HQ, settings, save & quit to title, quit.</summary>
public partial class PauseMenu : Control
{
    public Main Game = null!;
    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        var bg = new ColorRect { Color = Pal.Space with { A = 0.7f } }; bg.SetAnchorsPreset(LayoutPreset.FullRect); AddChild(bg);
        var panel = Ui.Panel(420, 400, 750, 320); AddChild(panel);
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 10); panel.AddChild(box);
        box.AddChild(Ui.Title("PAUSED"));
        var resume = Ui.Button("Resume (Esc)", 17, 340, primary: true); resume.Pressed += () => Game.TogglePause(); box.AddChild(resume);
        var hq = Ui.Button("Headquarters (H)", 17, 340); hq.Pressed += () => { Game.TogglePause(); Game.ShowHq(); }; box.AddChild(hq);
        var settings = Ui.Button("Settings", 17, 340); settings.Pressed += () => Game.ShowSettings(fromTitle: false); box.AddChild(settings);
        var save = Ui.Button("Save and quit to title", 17, 340); save.Pressed += () => Game.QuitToTitle(); box.AddChild(save);
        var quit = Ui.Button("Save and quit game", 17, 340); quit.Pressed += () => { Game.SaveNow(); Game.GetTree().Quit(); }; box.AddChild(quit);
        box.AddChild(Ui.Spacer(4));
        box.AddChild(Ui.Label("The career autosaves on every landing, docking, recovery and purchase, every 3 minutes, and when you quit.", Pal.Regular, 13, Pal.TextDim, 360));
    }
}

/// <summary>End-of-career logbook (GDD §10): days survived, bodies reached, peak score, cause.</summary>
public partial class LogbookScreen : Control
{
    public Main Game = null!;
    public void Show(World w, bool newBest)
    {
        Ui.Clear(this);
        SetAnchorsPreset(LayoutPreset.FullRect);
        var bg = new ColorRect { Color = Pal.Space with { A = 0.94f } }; bg.SetAnchorsPreset(LayoutPreset.FullRect); AddChild(bg);
        var panel = Ui.Panel(820, 720, 550, 160); AddChild(panel);
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 10); panel.AddChild(box);
        box.AddChild(Ui.Label("CAREER OVER", Pal.Bold, 40, Pal.Danger));
        box.AddChild(Ui.Label(w.GameOverCause, Pal.Regular, 18, Pal.Text, 760));
        box.AddChild(Ui.Spacer(6));
        box.AddChild(Ui.Heading($"LOGBOOK  ·  {w.AgencyName}  ·  {w.Preset.Name}"));
        int days = (int)(w.T / Units.Day) + 1;
        int flyby = w.Exploration.Count(kv => kv.Key >= 0 && kv.Value != Explored.None), orbit = w.Exploration.Count(kv => kv.Key >= 0 && kv.Value.HasFlag(Explored.Orbit)), landed = w.Exploration.Count(kv => kv.Key >= 0 && kv.Value.HasFlag(Explored.Landed));
        string S(string k) => $"{w.CareerStats.GetValueOrDefault(k):0}";
        var lines = new[]
        {
            $"Days survived      {days}",
            $"Peak score         {Units.FormatMoney(w.PeakScore)}{(newBest ? "   ★ new personal best" : "")}",
            $"Bodies reached     {flyby} flown by · {orbit} orbited · {landed} landed on",
            $"Launches           {S("launches")}   recoveries {S("recoveries")}   dockings {S("dockings")}   landings {S("landings")}",
            $"Contracts done     {S("contracts_completed")}   sales {S("sales")} ({w.CareerStats.GetValueOrDefault("tonnes_sold"):0.0} t)   debris cleared {S("debris_deorbited")}",
            $"Money              earned {Units.FormatMoney(w.TotalEarned)} · spent {Units.FormatMoney(w.TotalSpent)}",
            $"Storms weathered   {w.StormsSoFar}",
        };
        box.AddChild(Ui.Mono(string.Join("\n", lines), 16, Pal.Text, 760));
        box.AddChild(Ui.Spacer(10));
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 12);
        var nb = Ui.Button("New Career", 17, 220, primary: true); nb.Pressed += () => { Visible = false; Game.ShowNewCareer(); }; row.AddChild(nb);
        var tb = Ui.Button("Title screen", 17, 200); tb.Pressed += () => { Visible = false; Game.ShowTitle(); }; row.AddChild(tb);
        box.AddChild(row);
        Visible = true;
    }
}
