using Godot;
using Perigee.Sim;

namespace Perigee;

/// <summary>HQ hub (GDD §16): Contracts · Market · R&D · Finances · Logistics · Score. Opens with H; the sim pauses while it is up.</summary>
public partial class HqScreen : Control
{
    public Main Game = null!;
    TabContainer _tabs = null!;
    Control _contractsTab = null!, _marketTab = null!, _rdTab = null!, _financeTab = null!, _logisticsTab = null!, _scoreTab = null!;
    double _refresh;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        var bg = new ColorRect { Color = Pal.Space with { A = 0.94f } }; bg.SetAnchorsPreset(LayoutPreset.FullRect); AddChild(bg);
        var panel = Ui.Panel(1840, 1000, 40, 40); AddChild(panel);
        var root = new VBoxContainer(); panel.AddChild(root);
        var head = new HBoxContainer(); root.AddChild(head);
        head.AddChild(Ui.Title("HEADQUARTERS"));
        var spacer = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill }; head.AddChild(spacer);
        var close = Ui.Button("Back to flight (H / Esc)", 15, 240); close.Pressed += () => Game.CloseHq(); head.AddChild(close);
        _tabs = new TabContainer { SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(1790, 900) };
        _tabs.AddThemeFontOverride("font", Pal.Regular); _tabs.AddThemeFontSizeOverride("font_size", 17);
        root.AddChild(_tabs);
        _contractsTab = Tab("Contracts"); _marketTab = Tab("Market"); _rdTab = Tab("R&D"); _financeTab = Tab("Finances"); _logisticsTab = Tab("Logistics"); _scoreTab = Tab("Score");
        _tabs.TabChanged += _ => Rebuild();
        Visible = false;
    }

    Control Tab(string name)
    {
        var scroll = new ScrollContainer { Name = name, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(1760, 0) }; box.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(box);
        _tabs.AddChild(scroll);
        return box;
    }

    public int CurrentTab { get => _tabs.CurrentTab; set => _tabs.CurrentTab = value; }
    public string TabName(int i) => _tabs.GetTabTitle(i);
    public int TabCount => _tabs.GetTabCount();

    public override void _Process(double delta)
    {
        if (!Visible) return;
        _refresh += delta;
        if (_refresh > 2) { _refresh = 0; Rebuild(); }
    }

    public new void Show() { Visible = true; Game.Hud.Visible = false; Rebuild(); }
    public new void Hide() { Visible = false; Game.Hud.Visible = true; if (Game.Board.GetParent() == _contractsTab) { _contractsTab.RemoveChild(Game.Board); Game.BoardHome.AddChild(Game.Board); Game.Board.HostedInHq = false; Game.Board.Hide(); } }

    public void Rebuild()
    {
        var w = Game.World; if (w == null) return;
        switch (_tabs.CurrentTab)
        {
            case 0: BuildContracts(); break;
            case 1: BuildMarket(w); break;
            case 2: BuildRd(w); break;
            case 3: BuildFinances(w); break;
            case 4: BuildLogistics(w); break;
            case 5: BuildScore(w); break;
        }
    }

    // The contract board is the same node the C key shows; it moves into this tab while HQ is open.
    void BuildContracts()
    {
        var board = Game.Board;
        if (board.GetParent() != _contractsTab) { board.GetParent()?.RemoveChild(board); _contractsTab.AddChild(board); }
        board.Position = Vector2.Zero; board.Size = new Vector2(1760, 860);
        board.HostedInHq = true;
        board.Visible = true; board.Rebuild();
    }

    void BuildMarket(World w)
    {
        Ui.Clear(_marketTab);
        _marketTab.AddChild(Ui.Heading("MARKET LEDGER  ·  price per tonne now, and the 30-day trend"));
        _marketTab.AddChild(Ui.Label("Home buys and sells at fixed prices. Every other node only buys: low orbits need a Depot Controller, off-world surfaces need your pad. Selling floods a node; prices recover with a 10-day half-life.", Pal.Regular, 14, Pal.TextDim, 1700));
        var res = new[] { Perigee.Sim.Resource.Methalox, Perigee.Sim.Resource.Rcs, Perigee.Sim.Resource.Xenon, Perigee.Sim.Resource.Water, Perigee.Sim.Resource.Metal };
        var grid = new GridContainer { Columns = 1 + res.Length };
        grid.AddThemeConstantOverride("h_separation", 18); grid.AddThemeConstantOverride("v_separation", 6);
        grid.AddChild(Ui.Mono("node", 14, Pal.TextDim));
        foreach (var r in res) grid.AddChild(Ui.Mono(Contracts.Name(r), 14, Pal.TextDim));
        int day = (int)Math.Floor(w.T / Units.Day);
        for (int i = 0; i < w.Market.Nodes.Count; i++)
        {
            var n = w.Market.Nodes[i];
            grid.AddChild(Ui.Mono($"{n.Name}  ×{n.Multiplier:0.0}", 14, Pal.Text));
            foreach (var r in res)
            {
                var cell = new HBoxContainer(); cell.AddThemeConstantOverride("separation", 6);
                cell.AddChild(Ui.Mono($"{Units.FormatMoney(w.Market.Price(n, r, w.T)),9}", 14, Pal.Text));
                var hist = w.Market.History.Where(h => h.node == i && h.res == r && h.day >= day - 30).OrderBy(h => h.day).Select(h => (double)h.price).ToList();
                cell.AddChild(new Sparkline { Values = hist, CustomMinimumSize = new Vector2(120, 22) });
                grid.AddChild(cell);
            }
        }
        _marketTab.AddChild(grid);
    }

    void BuildRd(World w)
    {
        Ui.Clear(_rdTab);
        _rdTab.AddChild(Ui.Heading("R&D  ·  every part is for sale from day one; the price is the only gate"));
        _rdTab.AddChild(Ui.Mono($"Cash {Units.FormatMoney(w.Cash)}", 15, Pal.TextDim));
        foreach (PartCategory cat in Enum.GetValues<PartCategory>())
        {
            var parts = Parts.All.Where(p => p.Cat == cat && !p.Has(PartFlags.ClientPayload)).ToList();
            if (parts.Count == 0) continue;
            _rdTab.AddChild(Ui.Label(cat.ToString().ToUpperInvariant(), Pal.Bold, 15, Pal.Accent));
            var grid = new GridContainer { Columns = 2 }; grid.AddThemeConstantOverride("h_separation", 24); grid.AddThemeConstantOverride("v_separation", 4);
            foreach (var p in parts)
            {
                bool unlocked = w.IsUnlocked(p.Id);
                var row = new HBoxContainer { CustomMinimumSize = new Vector2(840, 34) }; row.AddThemeConstantOverride("separation", 8);
                row.AddChild(Ui.Label($"{(unlocked ? "✓ " : "")}{p.Name}", Pal.Regular, 14, unlocked ? Pal.Text : Pal.TextDim, 250));
                row.AddChild(Ui.Mono(unlocked ? $"{Units.FormatMoney(w.PartPrice(p))}/unit" : $"R&D {Units.FormatMoney(w.RdPrice(p))}", 13, Pal.TextDim, 150));
                if (!unlocked)
                {
                    var b = Ui.Button("Buy", 13, 70); var pp = p;
                    b.Disabled = w.Cash < w.RdPrice(p);
                    b.Pressed += () => Game.ConfirmThen($"Buy the R&D for {pp.Name} for {Units.FormatMoney(w.RdPrice(pp))}? This is permanent.", () => { if (w.BuyRd(pp.Id)) Game.Hud.Say($"Unlocked {pp.Name}"); Rebuild(); });
                    row.AddChild(b);
                }
                var tip = Ui.Label(p.Description, Pal.Regular, 12, Pal.TextDim with { A = 0.85f }, 400);
                row.AddChild(tip);
                grid.AddChild(row);
            }
            _rdTab.AddChild(grid);
        }
    }

    void BuildFinances(World w)
    {
        Ui.Clear(_financeTab);
        _financeTab.AddChild(Ui.Heading("FINANCES"));
        long upkeep = w.DailyUpkeep();
        string runway = double.IsPositiveInfinity(w.Runway) ? "no upkeep" : $"{w.Runway:0} days";
        var lines = new List<string>
        {
            $"Cash            {Units.FormatMoney(w.Cash)}",
            $"Daily upkeep    {Units.FormatMoney(upkeep)}   runway {runway}",
            $"Earned / spent  {Units.FormatMoney(w.TotalEarned)} / {Units.FormatMoney(w.TotalSpent)}",
            $"Net worth       {Units.FormatMoney(w.NetWorth())}   (cash + infrastructure at 50% + stored resources at local prices)",
        };
        if (!double.IsNaN(w.InsolventSince)) lines.Add($"INSOLVENT       day {w.InsolventDay} of {w.Preset.GraceDays}: the career ends unless cash is back above zero");
        if (!double.IsNaN(w.StuckSince)) lines.Add($"STUCK           day {w.StuckDay} of {w.Preset.GraceDays}: cash cannot fund any launch and nothing can earn — take a contract advance or sell stock");
        _financeTab.AddChild(Ui.Mono(string.Join("\n", lines), 16, Pal.Text, 1700));
        _financeTab.AddChild(Ui.Label("UPKEEP BREAKDOWN (per day)", Pal.Bold, 15, Pal.Accent));
        var parts = new List<string>();
        if (w.ExtraHomePads > 0) parts.Add($"extra home pads {w.ExtraHomePads} × $4k");
        if (w.DroneShips.Count > 0) parts.Add($"drone ships {w.DroneShips.Count} × $2k");
        int depots = 0, outposts = 0, pads = 0;
        foreach (var c in w.Crafts) { if (c.Destroyed) continue; depots += c.Parts.Count(p => !p.Destroyed && p.Def.Has(PartFlags.DepotController)); outposts += c.Parts.Count(p => !p.Destroyed && p.Def.Has(PartFlags.OutpostCore)); pads += c.Parts.Count(p => !p.Destroyed && p.Def.Has(PartFlags.PadKit)); }
        if (depots > 0) parts.Add($"depots {depots} × $1k"); if (outposts > 0) parts.Add($"outposts {outposts} × $3k"); if (pads > 0) parts.Add($"off-world pads {pads} × $8k");
        _financeTab.AddChild(Ui.Mono(parts.Count == 0 ? "nothing: probes and ordinary craft are free" : string.Join("   ·   ", parts) + (w.Preset.Upkeep != 1 ? $"   (×{w.Preset.Upkeep:0.00} preset)" : ""), 14, Pal.TextDim, 1700));
        _financeTab.AddChild(Ui.Label("CASH · LAST 60 DAYS", Pal.Bold, 15, Pal.Accent));
        int day = (int)Math.Floor(w.T / Units.Day);
        var hist = w.CashHistory.Where(h => h.day >= day - 60).OrderBy(h => h.day).Select(h => (double)h.cash / 100).ToList();
        hist.Add(w.Cash / 100.0);
        _financeTab.AddChild(new Graph { Values = hist, CustomMinimumSize = new Vector2(1700, 300), Unit = "$" });
    }

    void BuildLogistics(World w)
    {
        Ui.Clear(_logisticsTab);
        _logisticsTab.AddChild(Ui.Heading("LOGISTICS  ·  every depot, outpost and pad"));
        var rows = new List<string>();
        foreach (var c in w.Crafts)
        {
            if (c.Destroyed || c.Npc) continue;
            bool depot = c.Parts.Any(p => !p.Destroyed && p.Def.Has(PartFlags.DepotController));
            bool outpost = c.Parts.Any(p => !p.Destroyed && p.Def.Has(PartFlags.OutpostCore));
            if (!depot && !outpost) continue;
            var body = w.Sys[c.BodyId];
            string kind = outpost ? (c.Parts.Any(p => !p.Destroyed && p.Def.Has(PartFlags.PadKit)) ? "OUTPOST + PAD" : "OUTPOST") : "DEPOT";
            string where = c.Mode == CraftMode.Landed ? $"landed on {body.Name}" : c.Mode == CraftMode.Rails && c.Rails.Conic.IsEllipse ? $"{body.Name} {(c.Rails.Conic.Periapsis - body.Radius) / 1000:0}×{(c.Rails.Conic.Apoapsis - body.Radius) / 1000:0} km" : $"near {body.Name}";
            var stock = string.Join("  ", Enum.GetValues<Perigee.Sim.Resource>().Select(r => (r, t: c.Resource(r))).Where(x => x.t > 0.005).Select(x => $"{Contracts.Name(x.r)} {x.t:0.0} t"));
            string status = c.Parts.Any(p => !p.Destroyed && p.Offline && (p.Def.Has(PartFlags.DepotController) || p.Def.Has(PartFlags.OutpostCore))) ? "OFFLINE (storm)" : "online";
            string prod = "";
            if (outpost)
            {
                var o = w.Outposts().FirstOrDefault(x => x.Members.Contains(c));
                if (o != null) prod = $"   power {o.PowerGen:0.0}/{o.PowerNeed:0.0} kW   " + (o.Rates.Count == 0 ? "idle" : string.Join(" ", o.Rates.Where(kv => kv.Value > 1e-6).Select(kv => $"{Contracts.Name(kv.Key)} +{kv.Value:0.0} t/d")));
            }
            long upkeep = (depot ? 100_000 : 0) + (outpost ? 300_000 : 0) + c.Parts.Count(p => !p.Destroyed && p.Def.Has(PartFlags.PadKit)) * 800_000;
            rows.Add($"{kind,-14} {c.Name,-18} {where,-28} {status,-15} upkeep {Units.FormatMoney((long)(upkeep * w.Preset.Upkeep)),8}/d{prod}\n{"",-14} stock: {(stock.Length == 0 ? "empty" : stock)}");
        }
        _logisticsTab.AddChild(Ui.Mono(rows.Count == 0 ? "No infrastructure yet. A Depot Controller in orbit, an Outpost Core on the ground or a Pad Kit + Fabricator next to it will show here." : string.Join("\n", rows), 14, Pal.Text, 1700));
    }

    void BuildScore(World w)
    {
        Ui.Clear(_scoreTab);
        _scoreTab.AddChild(Ui.Heading("SCORE  ·  net worth + exploration"));
        long nw = w.NetWorth(); double pts = w.ExplorationPoints(); long score = w.Score();
        var lines = new List<string>
        {
            $"Net worth          {Units.FormatMoney(nw)}",
            $"Exploration        {pts:0} points × $10k = {Units.FormatMoney((long)(pts * 1_000_000))}",
            $"SCORE              {Units.FormatMoney(score)}   peak {Units.FormatMoney(Math.Max(score, w.PeakScore))}",
        };
        if (Profile.Data.Bests.TryGetValue(w.Preset.Name, out var best)) lines.Add($"Personal best      {Units.FormatMoney(best.PeakScore)}  ({best.Agency}, {best.Days} day{(best.Days == 1 ? "" : "s")}, {best.When})   on {w.Preset.Name}");
        _scoreTab.AddChild(Ui.Mono(string.Join("\n", lines), 16, Pal.Text, 1700));
        _scoreTab.AddChild(Ui.Label("EXPLORATION LEDGER", Pal.Bold, 15, Pal.Accent));
        var ex = new List<string>();
        foreach (var b in w.Sys.Bodies)
        {
            if (b.Type == BodyType.Star) continue;
            var e = w.Exploration.TryGetValue(b.Id, out var ee) ? ee : Explored.None;
            if (e == Explored.None && b.Id != w.Sys.HomeId) continue;
            ex.Add($"{b.Name,-16} {(e.HasFlag(Explored.Flyby) ? "flyby " : "      ")} {(e.HasFlag(Explored.Orbit) ? "orbit " : "      ")} {(e.HasFlag(Explored.Landed) ? "landed" : "")}   ×{w.BodyMultiplier(b.Id):0.0}");
        }
        _scoreTab.AddChild(Ui.Mono(ex.Count == 0 ? "nothing reached yet" : string.Join("\n", ex), 14, Pal.TextDim, 1700));
        _scoreTab.AddChild(Ui.Label($"Career day {(int)(w.T / Units.Day) + 1}  ·  played {TimeSpan.FromSeconds(w.PlayedSeconds):h\\:mm}  ·  launches {w.CareerStats.GetValueOrDefault("launches"):0}  ·  contracts {w.CareerStats.GetValueOrDefault("contracts_completed"):0}", Pal.Regular, 14, Pal.TextDim, 1700));
    }
}

/// <summary>A tiny line chart of recent values (market trend).</summary>
public partial class Sparkline : Control
{
    public List<double> Values = new();
    public override void _Draw()
    {
        var sz = Size;
        DrawRect(new Rect2(Vector2.Zero, sz), Pal.PanelBorder with { A = 0.15f });
        if (Values.Count < 2) return;
        double lo = Values.Min(), hi = Values.Max(); if (hi - lo < 1e-9) { lo -= 1; hi += 1; }
        var pts = new Vector2[Values.Count];
        for (int i = 0; i < Values.Count; i++) pts[i] = new Vector2(sz.X * i / (Values.Count - 1), sz.Y - 2 - (float)((Values[i] - lo) / (hi - lo)) * (sz.Y - 4));
        DrawPolyline(pts, Values[^1] >= Values[0] ? Pal.Ok : Pal.Danger, 1.5f, true);
    }
}

/// <summary>A line graph with axis labels (finances).</summary>
public partial class Graph : Control
{
    public List<double> Values = new();
    public string Unit = "";
    public override void _Draw()
    {
        var sz = Size;
        DrawRect(new Rect2(Vector2.Zero, sz), Pal.PanelBorder with { A = 0.12f });
        if (Values.Count < 2) { DrawString(Pal.Mono, new Vector2(10, 24), "not enough history yet", HorizontalAlignment.Left, -1, 14, Pal.TextDim); return; }
        double lo = Math.Min(0, Values.Min()), hi = Values.Max(); if (hi - lo < 1e-9) hi = lo + 1;
        float left = 110, bottom = sz.Y - 24, top = 12;
        var pts = new Vector2[Values.Count];
        for (int i = 0; i < Values.Count; i++) pts[i] = new Vector2(left + (sz.X - left - 10) * i / (Values.Count - 1), bottom - (float)((Values[i] - lo) / (hi - lo)) * (bottom - top));
        if (lo < 0) { float y0 = bottom - (float)((0 - lo) / (hi - lo)) * (bottom - top); DrawLine(new Vector2(left, y0), new Vector2(sz.X - 10, y0), Pal.Danger with { A = 0.5f }, 1); }
        DrawPolyline(pts, Pal.Accent, 2, true);
        DrawString(Pal.Mono, new Vector2(4, top + 12), Units.FormatMoney((long)(hi * 100)), HorizontalAlignment.Left, -1, 13, Pal.TextDim);
        DrawString(Pal.Mono, new Vector2(4, bottom), Units.FormatMoney((long)(lo * 100)), HorizontalAlignment.Left, -1, 13, Pal.TextDim);
        DrawString(Pal.Mono, new Vector2(left, sz.Y - 6), Values.Count - 1 == 1 ? "1 day ago" : $"{Values.Count - 1} days ago", HorizontalAlignment.Left, -1, 12, Pal.TextDim);
        DrawString(Pal.Mono, new Vector2(sz.X - 60, sz.Y - 6), "today", HorizontalAlignment.Left, -1, 12, Pal.TextDim);
    }
}
