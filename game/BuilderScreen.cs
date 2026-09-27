using Godot;
using Perigee.Sim;

namespace Perigee;

/// <summary>
/// The builder (GDD §5, §16): part catalogue by category (left), a snapping canvas with 2D mirror symmetry (centre), and live
/// stats, issues, the staging editor, the design library and the launch-site picker (right). All rules live in <see cref="Blueprint"/>.
/// </summary>
public partial class BuilderScreen : Control
{
    public Main Game = null!;
    public Blueprint Bp { get; private set; } = Blueprint.New();
    public PartDef? Held { get; private set; }
    public int Selected { get; private set; } = -1;
    public int SelectedStage { get; private set; } = -1;
    public double Zoom { get; private set; } = 40;
    public Vec2d Pan;
    public string DesignsDir = "";
    public List<Design> Library { get; private set; } = new();
    public int LaunchSite { get; private set; }
    public readonly List<(string name, int bodyId, double angle, double heightLimit, double massLimit)> Sites = new();

    Canvas _canvas = null!;
    VBoxContainer _catalogue = null!;
    Label _stats = null!, _issues = null!, _tooltip = null!;
    VBoxContainer _stages = null!;
    LineEdit _name = null!;
    CheckButton _symmetry = null!;
    OptionButton _site = null!;
    PanelContainer _libraryPanel = null!;
    VBoxContainer _libraryList = null!;
    PartCategory _category = PartCategory.Core;
    Placement? _snap;
    bool _panning;
    static readonly (PartCategory cat, string label)[] Categories =
    {
        (PartCategory.Core, "Cores"), (PartCategory.Tank, "Tanks"), (PartCategory.Engine, "Engines"), (PartCategory.Structure, "Structure"),
        (PartCategory.Aero, "Aero"), (PartCategory.Recovery, "Recovery"), (PartCategory.Rcs, "RCS"), (PartCategory.Docking, "Docking"),
        (PartCategory.Cargo, "Cargo"), (PartCategory.Power, "Power"), (PartCategory.Survey, "Survey"), (PartCategory.Base, "Base"),
    };

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        var bg = new ColorRect { Color = Pal.Space, MouseFilter = MouseFilterEnum.Ignore };
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        _canvas = new Canvas { Screen = this, Position = new Vector2(380, 0), Size = new Vector2(1120, 1080) };
        AddChild(_canvas);

        // Left: categories + parts
        var left = new PanelContainer { Position = new Vector2(0, 0), Size = new Vector2(380, 1080) };
        left.AddThemeStyleboxOverride("panel", Box(Pal.Panel, Pal.PanelBorder));
        AddChild(left);
        var leftBox = new VBoxContainer();
        left.AddChild(leftBox);
        leftBox.AddChild(Title("PARTS"));
        var tabs = new GridContainer { Columns = 3 };
        leftBox.AddChild(tabs);
        foreach (var (cat, label) in Categories)
        {
            var b = SmallButton(label);
            b.Name = "cat_" + cat;
            b.Pressed += () => { _category = cat; RebuildCatalogue(); };
            tabs.AddChild(b);
        }
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(370, 700), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        leftBox.AddChild(scroll);
        _catalogue = new VBoxContainer { CustomMinimumSize = new Vector2(350, 0) };
        scroll.AddChild(_catalogue);
        _tooltip = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(360, 120) };
        _tooltip.AddThemeFontOverride("font", Pal.Regular); _tooltip.AddThemeFontSizeOverride("font_size", 15); _tooltip.AddThemeColorOverride("font_color", Pal.TextDim);
        leftBox.AddChild(_tooltip);

        // Right: name, symmetry, stats, staging, library, launch
        var right = new PanelContainer { Position = new Vector2(1500, 0), Size = new Vector2(420, 1080) };
        right.AddThemeStyleboxOverride("panel", Box(Pal.Panel, Pal.PanelBorder));
        AddChild(right);
        var rb = new VBoxContainer();
        right.AddChild(rb);
        rb.AddChild(Title("DESIGN"));
        _name = new LineEdit { Text = Bp.Design.Name, PlaceholderText = "Design name", Name = "design_name" };
        _name.TextChanged += t => Bp.Design.Name = t;
        rb.AddChild(_name);
        var row = new HBoxContainer();
        rb.AddChild(row);
        _symmetry = new CheckButton { Text = "Mirror symmetry", ButtonPressed = true, Name = "symmetry" };
        _symmetry.Toggled += on => Bp.Symmetry = on;
        Style(_symmetry);
        row.AddChild(_symmetry);
        var btns = new HBoxContainer();
        rb.AddChild(btns);
        var newB = SmallButton("New"); newB.Name = "btn_new"; newB.Pressed += () => { Bp = Blueprint.New(); Bp.Symmetry = _symmetry.ButtonPressed; _name.Text = Bp.Design.Name; Selected = -1; Refresh(); };
        var saveB = SmallButton("Save"); saveB.Name = "btn_save"; saveB.Pressed += () => SaveDesign();
        var loadB = SmallButton("Load"); loadB.Name = "btn_load"; loadB.Pressed += () => ToggleLibrary(true);
        btns.AddChild(newB); btns.AddChild(saveB); btns.AddChild(loadB);
        rb.AddChild(Title("STATS"));
        _stats = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(400, 0) };
        _stats.AddThemeFontOverride("font", Pal.Mono); _stats.AddThemeFontSizeOverride("font_size", 15); _stats.AddThemeColorOverride("font_color", Pal.Text);
        rb.AddChild(_stats);
        _issues = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(400, 0) };
        _issues.AddThemeFontOverride("font", Pal.Regular); _issues.AddThemeFontSizeOverride("font_size", 15); _issues.AddThemeColorOverride("font_color", Pal.Danger);
        rb.AddChild(_issues);
        rb.AddChild(Title("STAGES  (select a part, then ▲ ▼)"));
        var stageBtns = new HBoxContainer();
        rb.AddChild(stageBtns);
        var up = SmallButton("▲ stage"); up.Name = "btn_stage_up"; up.Pressed += () => MoveSelected(-1);
        var down = SmallButton("▼ stage"); down.Name = "btn_stage_down"; down.Pressed += () => MoveSelected(1);
        var add = SmallButton("+ stage"); add.Name = "btn_stage_add"; add.Pressed += () => { Bp.AddStage(); Refresh(); };
        var rem = SmallButton("− stage"); rem.Name = "btn_stage_remove"; rem.Pressed += () => { if (SelectedStage >= 0) { Bp.RemoveStage(SelectedStage); SelectedStage = -1; Refresh(); } };
        var auto = SmallButton("Auto"); auto.Name = "btn_stage_auto"; auto.Pressed += () => { Bp.ResetStages(); Refresh(); };
        foreach (var b in new[] { up, down, add, rem, auto }) stageBtns.AddChild(b);
        var stageScroll = new ScrollContainer { CustomMinimumSize = new Vector2(400, 220), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        rb.AddChild(stageScroll);
        _stages = new VBoxContainer { CustomMinimumSize = new Vector2(390, 0) };
        stageScroll.AddChild(_stages);
        rb.AddChild(Title("LAUNCH SITE"));
        _site = new OptionButton { Name = "launch_site" };
        Style(_site);
        _site.ItemSelected += i => { LaunchSite = (int)i; Refresh(); };
        rb.AddChild(_site);
        var launch = new Button { Text = "LAUNCH", CustomMinimumSize = new Vector2(400, 56), Name = "btn_launch" };
        launch.AddThemeFontOverride("font", Pal.Bold); launch.AddThemeFontSizeOverride("font_size", 26);
        launch.AddThemeStyleboxOverride("normal", Box(Pal.Accent with { A = 0.85f }, Pal.Accent)); launch.AddThemeStyleboxOverride("hover", Box(Pal.Accent, Colors.White));
        launch.AddThemeStyleboxOverride("pressed", Box(Pal.Accent.Darkened(0.2f), Colors.White));
        launch.AddThemeColorOverride("font_color", Pal.SpaceDeep);
        launch.Pressed += Launch;
        rb.AddChild(launch);
        var back = SmallButton("Back to flight (Esc)"); back.Name = "btn_back"; back.Pressed += () => Game.CloseBuilder();
        rb.AddChild(back);

        // Library popup (over the canvas)
        _libraryPanel = new PanelContainer { Position = new Vector2(700, 200), Size = new Vector2(480, 600), Visible = false };
        _libraryPanel.AddThemeStyleboxOverride("panel", Box(Pal.Panel with { A = 0.98f }, Pal.Accent));
        AddChild(_libraryPanel);
        var lb = new VBoxContainer();
        _libraryPanel.AddChild(lb);
        lb.AddChild(Title("DESIGN LIBRARY"));
        var ls = new ScrollContainer { CustomMinimumSize = new Vector2(460, 480), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        lb.AddChild(ls);
        _libraryList = new VBoxContainer { CustomMinimumSize = new Vector2(440, 0) };
        ls.AddChild(_libraryList);
        var close = SmallButton("Close"); close.Pressed += () => ToggleLibrary(false);
        lb.AddChild(close);

        RebuildCatalogue();
        Refresh();
    }

    // ---------------------------------------------------------------- setup

    public void Open(World w, Design? design = null)
    {
        if (design != null) { Bp = new Blueprint(design.Clone()); Bp.Symmetry = _symmetry.ButtonPressed; }
        _name.Text = Bp.Design.Name;
        Sites.Clear();
        var home = w.Sys.Home;
        Sites.Add(("Home pad", home.Id, home.LaunchSiteAngle, 40, 400));
        foreach (var pad in w.LaunchPads()) Sites.Add((pad.Name, pad.Body.Id, pad.LocalAngle, LaunchPad.HeightLimit, LaunchPad.MassLimit));
        _site.Clear();
        foreach (var s in Sites) _site.AddItem(s.name);
        _site.Selected = Math.Min(LaunchSite, Sites.Count - 1);
        Selected = -1; Held = null;
        Refresh();
    }

    static StyleBoxFlat Box(Color bg, Color border) => new() { BgColor = bg, BorderColor = border, BorderWidthBottom = 1, BorderWidthTop = 1, BorderWidthLeft = 1, BorderWidthRight = 1, ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 6, ContentMarginBottom = 6 };
    static Label Title(string t)
    {
        var l = new Label { Text = t };
        l.AddThemeFontOverride("font", Pal.Bold); l.AddThemeFontSizeOverride("font_size", 18); l.AddThemeColorOverride("font_color", Pal.Accent);
        return l;
    }
    static void Style(Control c) { c.AddThemeFontOverride("font", Pal.Regular); c.AddThemeFontSizeOverride("font_size", 16); c.AddThemeColorOverride("font_color", Pal.Text); }
    static Button SmallButton(string text)
    {
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(0, 32) };
        Style(b);
        b.AddThemeStyleboxOverride("normal", Box(Pal.PanelBorder with { A = 0.35f }, Pal.PanelBorder));
        b.AddThemeStyleboxOverride("hover", Box(Pal.PanelBorder with { A = 0.7f }, Pal.Text));
        b.AddThemeStyleboxOverride("pressed", Box(Pal.Accent with { A = 0.6f }, Pal.Accent));
        return b;
    }

    void RebuildCatalogue()
    {
        foreach (var c in _catalogue.GetChildren()) { _catalogue.RemoveChild(c); c.QueueFree(); }
        var w = Game.World;
        var payloads = w?.AvailablePayloads().ToList() ?? new List<(Contract contract, PartDef part)>();
        foreach (var def in Parts.All.Where(p => p.Cat == _category))
        {
            bool unlocked = w == null || w.IsUnlocked(def.Id);
            if (def.Has(PartFlags.ClientPayload))
            {
                var owner = payloads.FirstOrDefault(x => x.part.Id == def.Id);
                if (owner.contract == null) continue;   // client payloads exist only while a Deploy contract provides them
                var pb = SmallButton($"{def.Name}   {def.DryMass:0.00} t   free · {owner.contract.Title}");
                pb.Alignment = HorizontalAlignment.Left; pb.Name = "part_" + def.Id; pb.CustomMinimumSize = new Vector2(350, 34);
                var pd = def; pb.Pressed += () => Hold(pd);
                pb.MouseEntered += () => _tooltip.Text = Tooltip(pd) + $"\nProvided free for contract: {owner.contract.Title}";
                _catalogue.AddChild(pb);
                continue;
            }
            var b = SmallButton(unlocked ? $"{def.Name}   {def.DryMass:0.00} t   {Units.FormatMoney(w?.PartPrice(def) ?? def.Cost)}" : $"🔒 {def.Name}   R&D {Units.FormatMoney(w!.RdPrice(def))}");
            b.Alignment = HorizontalAlignment.Left;
            b.Name = "part_" + def.Id;
            b.CustomMinimumSize = new Vector2(350, 34);
            var d = def;
            if (unlocked) b.Pressed += () => Hold(d);
            else
            {
                b.AddThemeColorOverride("font_color", Pal.TextDim);
                b.Pressed += () => Game.ConfirmThen($"Buy the R&D for {d.Name} for {Units.FormatMoney(w!.RdPrice(d))}? This is permanent.", () => { if (w!.BuyRd(d.Id)) { Game.Hud.Say($"Unlocked {d.Name}"); RebuildCatalogue(); Refresh(); } else Game.Hud.Say($"Not enough cash for R&D of {d.Name} ({Units.FormatMoney(w.RdPrice(d))})"); });
            }
            b.MouseEntered += () => _tooltip.Text = Tooltip(d) + (unlocked ? "" : $"\nLOCKED — click to buy the R&D for {Units.FormatMoney(w!.RdPrice(d))}");
            _catalogue.AddChild(b);
        }
    }

    public static string Tooltip(PartDef d)
    {
        var s = $"{d.Name}  ·  {d.DryMass:0.00} t  ·  {Units.FormatMoney(d.Cost)} (R&D {Units.FormatMoney(d.RdPrice)})\n{d.Description}";
        if (d.Engine != null) s += $"\nThrust {d.Engine.ThrustSl / 1000:0} / {d.Engine.ThrustVac / 1000:0} kN (sea level / vacuum), Isp {d.Engine.IspSl:0} / {d.Engine.IspVac:0} s, min throttle {d.Engine.MinThrottle * 100:0}%";
        if (d.FuelCapacity > 0) s += $"\nMethalox {d.FuelCapacity} t";
        foreach (var (r, cap) in d.Capacity) if (r != Sim.Resource.Methalox && r != Sim.Resource.Ice && r != Sim.Resource.Ore && r != Sim.Resource.Co2 && r != Sim.Resource.Water && r != Sim.Resource.Metal) s += $"\n{r} {cap} t";
        s += $"\nMax {d.MaxTemp:0} K · impact {d.ImpactTolerance:0} m/s · Cd {d.Cd:0.00}";
        return s;
    }

    public void SelectSite(int index) { LaunchSite = Math.Clamp(index, 0, Math.Max(0, Sites.Count - 1)); _site.Selected = LaunchSite; Refresh(); }

    public void ShowCategory(string name)
    {
        var hit = Categories.FirstOrDefault(c => c.label.Equals(name, StringComparison.OrdinalIgnoreCase) || c.cat.ToString().Equals(name, StringComparison.OrdinalIgnoreCase));
        if (hit.label != null) { _category = hit.cat; RebuildCatalogue(); }
    }

    public void Hold(PartDef? def) { Held = def; Selected = -1; _snap = null; _canvas.QueueRedraw(); }

    // ---------------------------------------------------------------- canvas interaction

    public Vector2 ToCanvas(Vec2d craft) => new((float)((craft.X - Pan.X) * Zoom) + _canvas.Size.X / 2, _canvas.Size.Y / 2 - (float)((craft.Y - Pan.Y) * Zoom));
    public Vec2d ToCraft(Vector2 px) => new((px.X - _canvas.Size.X / 2) / Zoom + Pan.X, (_canvas.Size.Y / 2 - px.Y) / Zoom + Pan.Y);

    /// <summary>Canvas click at a canvas-local pixel: place the held part, or select a part.</summary>
    public void CanvasClick(Vector2 px, bool right)
    {
        if (right) { Hold(null); return; }
        var cursor = ToCraft(px);
        if (Held != null)
        {
            var pl = Bp.Snap(Held, cursor);
            if (pl != null) { Selected = Bp.Add(Held, pl); Held = null; _snap = null; Refresh(); }
            return;
        }
        Selected = PartAt(cursor);
        _canvas.QueueRedraw();
        RefreshStages();
    }

    public void CanvasMove(Vector2 px)
    {
        if (Held == null) { _snap = null; return; }
        _snap = Bp.Snap(Held, ToCraft(px));
        _canvas.QueueRedraw();
    }

    public int PartAt(Vec2d p)
    {
        for (int i = Bp.Count - 1; i >= 0; i--)
        {
            var (min, max) = Bp.Bounds(i);
            if (p.X >= min.X && p.X <= max.X && p.Y >= min.Y && p.Y <= max.Y) return i;
        }
        return -1;
    }

    public void DeleteSelected()
    {
        if (Selected <= 0) return;
        Bp.Remove(Selected);
        Selected = -1;
        Refresh();
    }

    public override void _GuiInput(InputEvent ev)
    {
        if (ev is InputEventKey k && k.Pressed && !k.Echo)
        {
            if (k.Keycode is Key.Delete or Key.Backspace) { DeleteSelected(); AcceptEvent(); }
            else if (k.Keycode == Key.Escape) { if (Held != null) Hold(null); else Game.CloseBuilder(); AcceptEvent(); }
        }
    }

    public override void _UnhandledInput(InputEvent ev)
    {
        if (!Visible) return;
        if (ev is InputEventKey k && k.Pressed && !k.Echo)
        {
            if (k.Keycode is Key.Delete or Key.Backspace) DeleteSelected();
            else if (k.Keycode == Key.Escape) { if (Held != null) Hold(null); else Game.CloseBuilder(); }
        }
    }

    void MoveSelected(int dir)
    {
        if (Selected < 0 || !Bp.IsStageable(Selected)) return;
        int cur = Bp.Design.Stages.FindIndex(st => st.Contains(Selected));
        int target = Math.Clamp((cur < 0 ? 0 : cur) + dir, 0, Math.Max(0, Bp.Design.Stages.Count - 1 + (dir > 0 ? 1 : 0)));
        Bp.MoveToStage(Selected, target);
        Refresh();
    }

    // ---------------------------------------------------------------- library

    void ToggleLibrary(bool on)
    {
        _libraryPanel.Visible = on;
        if (!on) return;
        Library = DesignLibrary.Load(DesignsDir);
        foreach (var c in _libraryList.GetChildren()) { _libraryList.RemoveChild(c); c.QueueFree(); }
        foreach (var d in Library)
        {
            var row = new HBoxContainer();
            var b = SmallButton($"{d.Name}   ({d.Parts.Count} parts, {Units.FormatMoney(d.Cost)})"); b.CustomMinimumSize = new Vector2(360, 32); b.Alignment = HorizontalAlignment.Left;
            var dd = d;
            b.Pressed += () => { Bp = new Blueprint(dd.Clone()); Bp.Symmetry = _symmetry.ButtonPressed; _name.Text = Bp.Design.Name; Selected = -1; ToggleLibrary(false); Refresh(); };
            var del = SmallButton("✕"); del.CustomMinimumSize = new Vector2(40, 32);
            del.Pressed += () => { DesignLibrary.Delete(dd, DesignsDir); ToggleLibrary(true); };
            row.AddChild(b); row.AddChild(del);
            _libraryList.AddChild(row);
        }
        if (Library.Count == 0) _libraryList.AddChild(new Label { Text = "No saved designs yet." });
    }

    public string SaveDesign()
    {
        Bp.Design.Name = _name.Text.Length > 0 ? _name.Text : "Untitled";
        string path = DesignLibrary.Save(Bp.Design, DesignsDir);
        Game.Hud.Say($"Saved {Bp.Design.Name}");
        return path;
    }

    /// <summary>Home: parts + fuel in cash. Off-world pad: the cash share, the metal from the outpost's store and the local propellant (GDD §9).</summary>
    string CostLine(World? w, long launchCost, BuildStats st)
    {
        var site = Sites.Count > 0 ? Sites[Math.Min(LaunchSite, Sites.Count - 1)] : default;
        if (w != null && site.bodyId >= 0 && site.bodyId != w.Sys.HomeId && w.PadAt(site.bodyId) is { } pad)
        {
            var q = w.PadQuote(Bp.Design, pad);
            string fuel = string.Join("  ", q.Propellant.Select(kv => $"{Contracts.Name(kv.Key)} {Math.Min(kv.Value.need, kv.Value.have):0.0}/{kv.Value.need:0.0} t"));
            return $"Build at {pad.Name}: {Units.FormatMoney(q.Cash)} cash + {q.MetalNeeded:0.0} t metal (store {q.MetalAvailable:0.0} t)   local fuel {fuel}   Height {st.Height:0.0} m";
        }
        return $"Launch cost {Units.FormatMoney(launchCost)} (parts + fuel){(w != null ? $"   cash {Units.FormatMoney(w.Cash)}" : "")}   Height {st.Height:0.0} m   Width {st.Width:0.0} m";
    }

    public void Launch()
    {
        var st = Bp.Stats(Game.World!.Sys[Sites[LaunchSite].bodyId].SurfaceGravity, Sites[LaunchSite].heightLimit, Sites[LaunchSite].massLimit);
        if (st.Issues.Count > 0) { Game.Hud.Say("Fix the design first: " + st.Issues[0]); Refresh(); return; }
        if (Sites[LaunchSite].bodyId != Game.World.Sys.HomeId && Game.World.PadAt(Sites[LaunchSite].bodyId) is { } pad)
        {
            var q = Game.World.PadQuote(Bp.Design, pad);
            if (!q.Affordable(Game.World.Cash)) { Game.Hud.Say(q.MetalAvailable < q.MetalNeeded ? $"Not enough metal at {pad.Name}: need {q.MetalNeeded:0.0} t, have {q.MetalAvailable:0.0} t" : "Not enough cash"); Refresh(); return; }
        }
        Bp.Design.Name = _name.Text.Length > 0 ? _name.Text : "Untitled";
        var site = Sites[LaunchSite];
        Game.LaunchFromBuilder(Bp.Design.Clone(), site.bodyId, site.angle);
    }

    // ---------------------------------------------------------------- refresh

    public void Refresh()
    {
        var home = Game.World?.Sys.Home;
        double hLimit = double.PositiveInfinity, mLimit = double.PositiveInfinity;
        if (Sites.Count > 0) { var site = Sites[Math.Min(LaunchSite, Sites.Count - 1)]; hLimit = site.heightLimit; mLimit = site.massLimit; }
        var w = Game.World;
        var siteBody = w != null && Sites.Count > 0 ? w.Sys[Sites[Math.Min(LaunchSite, Sites.Count - 1)].bodyId] : home;
        var st = Bp.Stats(siteBody?.SurfaceGravity ?? 9.81, hLimit, mLimit);
        long launchCost = w?.LaunchCost(Bp.Design) ?? st.Cost;
        var lines = new List<string>
        {
            $"Mass {st.Mass:0.00} t  (dry {st.DryMass:0.00} t)",
            CostLine(w, launchCost, st),
            $"Δv total {st.TotalDeltaVVac:0} m/s vac · {st.TotalDeltaVSl:0} m/s sea level",
            $"TWR at pad {st.TwrSurface:0.00}   Refund if recovered {Units.FormatMoney(st.RefundIfRecovered)}",
        };
        foreach (var s in st.Stages) lines.Add($"S{s.Stage + 1}: Δv {s.DeltaVVac:0}/{s.DeltaVSl:0} m/s  {Units.FormatDuration(s.BurnTime)}  TWR {s.TwrSurface:0.00}  {s.StartMass:0.0}→{s.EndMass:0.0} t");
        _stats.Text = string.Join("\n", lines);
        var issues = new List<string>(st.Issues);
        if (w != null)
        {
            var locked = w.LockedParts(Bp.Design).Select(id => Parts.Get(id).Name).ToList();
            if (locked.Count > 0) issues.Add("Locked parts (buy R&D in the catalogue): " + string.Join(", ", locked));
            if (w.Cash < launchCost) issues.Add($"Not enough cash to launch ({Units.FormatMoney(launchCost)} needed).");
        }
        _issues.Text = issues.Count == 0 ? "" : "⚠ " + string.Join("\n⚠ ", issues);
        RefreshStages();
        _canvas.QueueRedraw();
    }

    void RefreshStages()
    {
        foreach (var c in _stages.GetChildren()) { _stages.RemoveChild(c); c.QueueFree(); }
        for (int s = 0; s < Bp.Design.Stages.Count; s++)
        {
            var names = Bp.Design.Stages[s].Select(i => (i == Selected ? "[" : "") + Bp.Def(i).Name + (i == Selected ? "]" : ""));
            var b = SmallButton($"S{s + 1}: {string.Join(", ", names)}");
            b.Alignment = HorizontalAlignment.Left; b.CustomMinimumSize = new Vector2(380, 30);
            if (s == SelectedStage) b.AddThemeStyleboxOverride("normal", Box(Pal.Accent with { A = 0.4f }, Pal.Accent));
            int idx = s;
            b.Pressed += () => { SelectedStage = idx; RefreshStages(); };
            _stages.AddChild(b);
        }
    }

    /// <summary>The drawing surface. Parts are drawn from the same polygon data the flight view uses.</summary>
    public partial class Canvas : Control
    {
        public BuilderScreen Screen = null!;
        public override void _Ready() { MouseFilter = MouseFilterEnum.Stop; }
        public override void _GuiInput(InputEvent ev)
        {
            switch (ev)
            {
                case InputEventMouseButton mb when mb.Pressed && mb.ButtonIndex == MouseButton.Left: Screen.CanvasClick(mb.Position, false); AcceptEvent(); break;
                case InputEventMouseButton mb when mb.Pressed && mb.ButtonIndex == MouseButton.Right: Screen.CanvasClick(mb.Position, true); AcceptEvent(); break;
                case InputEventMouseButton mb when mb.Pressed && mb.ButtonIndex == MouseButton.WheelUp: Screen.Zoom = Math.Min(160, Screen.Zoom * 1.2); QueueRedraw(); break;
                case InputEventMouseButton mb when mb.Pressed && mb.ButtonIndex == MouseButton.WheelDown: Screen.Zoom = Math.Max(8, Screen.Zoom / 1.2); QueueRedraw(); break;
                case InputEventMouseButton mb when mb.ButtonIndex == MouseButton.Middle: Screen._panning = mb.Pressed; break;
                case InputEventMouseMotion mm:
                    if (Screen._panning) { Screen.Pan -= new Vec2d(mm.Relative.X / Screen.Zoom, -mm.Relative.Y / Screen.Zoom); QueueRedraw(); }
                    Screen.CanvasMove(mm.Position);
                    break;
            }
        }

        public override void _Draw()
        {
            var s = Screen;
            DrawRect(new Rect2(0, 0, Size), Pal.SpaceDeep);
            // Ground line and a metre grid.
            float gy = s.ToCanvas(new Vec2d(0, s.Bp.Count > 0 ? s.Bp.Bounds(0).min.Y - 0.01 : 0)).Y;
            for (double x = -20; x <= 20; x += 1) { var p = s.ToCanvas(new Vec2d(x, 0)); DrawLine(new Vector2(p.X, 0), new Vector2(p.X, Size.Y), Pal.PanelBorder with { A = 0.18f }, 1); }
            for (double y = -20; y <= 40; y += 1) { var p = s.ToCanvas(new Vec2d(0, y)); DrawLine(new Vector2(0, p.Y), new Vector2(Size.X, p.Y), Pal.PanelBorder with { A = 0.18f }, 1); }
            // Parts
            for (int i = 0; i < s.Bp.Count; i++) DrawPart(s.Bp.Def(i), new Vec2d(s.Bp.Design.Parts[i].X, s.Bp.Design.Parts[i].Y), s.Bp.Design.Parts[i].Flip, 1f, i == s.Selected);
            // Free stack nodes
            if (s.Held != null && !s.Held.RadialMount)
                foreach (var pl in s.Bp.StackPlacements(s.Held))
                {
                    var c = s.ToCanvas(new Vec2d(pl.Centre.X, pl.Kind == AttachKind.Top ? pl.Centre.Y - s.Held.Height / 2 : pl.Centre.Y + s.Held.Height / 2));
                    DrawCircle(c, 5, Pal.Ok with { A = 0.8f });
                }
            // Held part ghost
            if (s.Held != null)
            {
                var pos = s._snap?.Centre ?? s.ToCraft(GetLocalMousePosition());
                bool flip = s._snap is { Kind: AttachKind.Radial, Side: < 0 };
                DrawPart(s.Held, pos, flip, s._snap != null ? 0.75f : 0.35f, false, s._snap != null ? null : Pal.Danger);
                if (s._snap is { Kind: AttachKind.Radial } && s.Bp.Symmetry)
                {
                    var parent = s.Bp.Design.Parts[s._snap.Parent];
                    DrawPart(s.Held, new Vec2d(2 * parent.X - pos.X, pos.Y), !flip, 0.4f, false);
                }
            }
            // Centre of mass marker
            var craft = Craft.FromDesign(s.Bp.Design, "com");
            var com = s.ToCanvas(craft.Com);
            DrawArc(com, 7, 0, Mathf.Tau, 24, Pal.Accent, 2);
            DrawLine(com - new Vector2(10, 0), com + new Vector2(10, 0), Pal.Accent, 1); DrawLine(com - new Vector2(0, 10), com + new Vector2(0, 10), Pal.Accent, 1);
            DrawString(Pal.Regular, new Vector2(12, Size.Y - 14), s.Held != null ? $"Placing {s.Held.Name}: click a green node · right-click/Esc to cancel" : "Click a part to select · Delete removes it · wheel zooms · middle-drag pans", HorizontalAlignment.Left, -1, 15, Pal.TextDim);
            DrawString(Pal.Mono, new Vector2(Size.X - 120, 24), $"{s.Zoom:0} px/m", HorizontalAlignment.Left, -1, 14, Pal.TextDim);
        }

        void DrawPart(PartDef def, Vec2d centre, bool flip, float alpha, bool selected, Color? tint = null)
        {
            var s = Screen;
            foreach (var poly in def.Art)
            {
                var pts = new Vector2[poly.Points.Length];
                for (int i = 0; i < pts.Length; i++) pts[i] = s.ToCanvas(centre + new Vec2d(flip ? -poly.Points[i].X : poly.Points[i].X, poly.Points[i].Y));
                if (flip) Array.Reverse(pts);
                var col = tint ?? new Color(poly.R, poly.G, poly.B);
                DrawPolygon(pts, new[] { col with { A = alpha } });
            }
            if (selected)
            {
                var (min, max) = Blueprint_BoundsOf(def, centre, flip);
                var a = s.ToCanvas(new Vec2d(min.X, max.Y)); var b = s.ToCanvas(new Vec2d(max.X, min.Y));
                DrawRect(new Rect2(a, b - a), Pal.Accent, false, 2);
            }
        }

        static (Vec2d min, Vec2d max) Blueprint_BoundsOf(PartDef def, Vec2d centre, bool flip)
        {
            double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
            foreach (var v in def.Outline)
            {
                double x = centre.X + (flip ? -v.X : v.X), y = centre.Y + v.Y;
                minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
            }
            return (new Vec2d(minX, minY), new Vec2d(maxX, maxY));
        }
    }
}
