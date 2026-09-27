using Godot;
using Perigee.Sim;

namespace Perigee;

/// <summary>Pump panel (GDD §7): pick a source and a destination tank on the (docked) craft, pump for game time; undock buttons per joint.</summary>
public partial class TransferPanel : PanelContainer
{
    public Main Game = null!;
    VBoxContainer _rows = null!;
    Label _status = null!;
    int _src = -1, _dst = -1;
    Perigee.Sim.Resource _res = Perigee.Sim.Resource.Methalox;
    OptionButton _resPick = null!;
    VBoxContainer _sellBox = null!;

    public override void _Ready()
    {
        Position = new Vector2(1180, 120);
        Size = new Vector2(700, 640);
        AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = Pal.Panel with { A = 0.96f }, BorderColor = Pal.PanelBorder, BorderWidthBottom = 1, BorderWidthTop = 1, BorderWidthLeft = 1, BorderWidthRight = 1, ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 8, ContentMarginBottom = 8 });
        var box = new VBoxContainer();
        AddChild(box);
        var title = new Label { Text = "TRANSFER & MARKET  (F closes)" };
        title.AddThemeFontOverride("font", Pal.Bold); title.AddThemeFontSizeOverride("font_size", 18); title.AddThemeColorOverride("font_color", Pal.Accent);
        box.AddChild(title);
        var top = new HBoxContainer();
        box.AddChild(top);
        _resPick = new OptionButton();
        foreach (var r in new[] { Perigee.Sim.Resource.Methalox, Perigee.Sim.Resource.Rcs, Perigee.Sim.Resource.Xenon, Perigee.Sim.Resource.Water, Perigee.Sim.Resource.Metal, Perigee.Sim.Resource.Ice, Perigee.Sim.Resource.Co2, Perigee.Sim.Resource.Ore }) _resPick.AddItem(r.ToString());
        _resPick.ItemSelected += i => { _res = (Perigee.Sim.Resource)Enum.Parse(typeof(Perigee.Sim.Resource), _resPick.GetItemText((int)i)); _src = _dst = -1; Rebuild(); };
        top.AddChild(_resPick);
        var pump = Btn("Pump"); pump.Pressed += () => { if (Game.World?.Active is { } c && _src >= 0 && _dst >= 0) { if (Game.World.StartPump(c, _src, _dst, _res) == null) Game.Hud.Say("Pump refused (claw joint or no room)"); } };
        var stop = Btn("Stop"); stop.Pressed += () => { if (Game.World?.Active is { } c) Game.World.StopPumps(c); };
        top.AddChild(pump); top.AddChild(stop);
        _sellBox = new VBoxContainer();
        box.AddChild(_sellBox);
        _status = new Label();
        _status.AddThemeFontOverride("font", Pal.Mono); _status.AddThemeFontSizeOverride("font_size", 15); _status.AddThemeColorOverride("font_color", Pal.TextDim);
        box.AddChild(_status);
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(680, 480), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        box.AddChild(scroll);
        _rows = new VBoxContainer { CustomMinimumSize = new Vector2(660, 0) };
        scroll.AddChild(_rows);
        Visible = false;
    }

    static Button Btn(string t)
    {
        var b = new Button { Text = t, CustomMinimumSize = new Vector2(70, 30) };
        b.AddThemeFontOverride("font", Pal.Regular); b.AddThemeFontSizeOverride("font_size", 15);
        return b;
    }

    public void Toggle() { Visible = !Visible; if (Visible) Rebuild(); }

    public override void _Process(double delta)
    {
        if (!Visible || Game.World?.Active is not { } c) return;
        var jobs = c.Pumps;
        _status.Text = jobs.Count == 0 ? $"Source: {(_src >= 0 ? c.Find(_src)?.Def.Name : "—")}   Destination: {(_dst >= 0 ? c.Find(_dst)?.Def.Name : "—")}" : string.Join("\n", jobs.Select(j => $"Pumping {j.Resource} {c.Find(j.Source)?.Def.Name} → {c.Find(j.Destination)?.Def.Name} at {j.Rate:0.0} t/s · moved {j.Moved:0.00} t"));
        if (_frames++ % 15 == 0) RefreshAmounts();
    }
    int _frames;
    readonly List<(Part part, Label amount)> _amountLabels = new();

    void RefreshAmounts() { foreach (var (p, l) in _amountLabels) l.Text = p.Def.StorageT > 0 ? $"{p.Get(_res):0.00} t ({p.Room(_res):0.0} free of {p.Def.StorageT:0} shared)" : $"{p.Get(_res):0.00} / {p.Capacity(_res):0.0} t"; }

    /// <summary>Market rows: the node the craft can sell at, one line per sellable resource with the quote (price impact included) and sell buttons.</summary>
    void RebuildMarket(Craft c)
    {
        foreach (var ch in _sellBox.GetChildren()) { _sellBox.RemoveChild(ch); ch.QueueFree(); }
        var w = Game.World!;
        var node = w.NodeFor(c);
        if (node == null) { var none = new Label { Text = "No market here (sell in low orbit with a Depot Controller, at home, or from an off-world pad)." }; none.AddThemeFontOverride("font", Pal.Regular); none.AddThemeFontSizeOverride("font_size", 14); none.AddThemeColorOverride("font_color", Pal.TextDim); _sellBox.AddChild(none); return; }
        bool depot = c.Parts.Any(p => !p.Destroyed && p.Def.Has(PartFlags.DepotController));
        bool canSell = node.Kind != NodeKind.LowOrbit || depot;
        var head = new Label { Text = $"MARKET · {node.Name} · ×{node.Multiplier:0.0}{(canSell ? "" : " · selling here needs a Depot Controller")}", AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(660, 0) };
        head.AddThemeFontOverride("font", Pal.Bold); head.AddThemeFontSizeOverride("font_size", 16); head.AddThemeColorOverride("font_color", Pal.Info);
        _sellBox.AddChild(head);
        foreach (var r in new[] { Perigee.Sim.Resource.Methalox, Perigee.Sim.Resource.Rcs, Perigee.Sim.Resource.Xenon, Perigee.Sim.Resource.Water, Perigee.Sim.Resource.Metal })
        {
            double have = c.Resource(r);
            if (have <= 0.001) continue;
            var row = new HBoxContainer();
            long unit = w.Market.Price(node, r, w.T);
            var l = new Label { Text = $"{r}: {have:0.00} t · {Units.FormatMoney(unit)}/t now", CustomMinimumSize = new Vector2(330, 0) };
            l.AddThemeFontOverride("font", Pal.Mono); l.AddThemeFontSizeOverride("font_size", 14);
            row.AddChild(l);
            foreach (var frac in new[] { 0.25, 0.5, 1.0 })
            {
                double q = have * frac;
                long quote = w.Market.Quote(node, r, q, w.T);
                var b = Btn($"sell {q:0.00} t → {Units.FormatMoney(quote)}");
                b.CustomMinimumSize = new Vector2(100, 30);
                b.Disabled = !canSell;
                var rr = r; double qq = q;
                b.Pressed += () => { w.SellFrom(c, rr, qq); Rebuild(); };
                row.AddChild(b);
            }
            _sellBox.AddChild(row);
        }
    }

    /// <summary>Outpost rows (GDD §9): power, per-resource rates, refinery recipe pickers and pooled storage.</summary>
    void RebuildOutpost(Craft c)
    {
        var w = Game.World!;
        var o = c.Mode == CraftMode.Landed ? w.Outposts().FirstOrDefault(x => x.Members.Contains(c)) : null;
        if (o == null) return;
        var head = new Label { Text = $"OUTPOST · {o.Name} on {o.Body.Name} · {o.Members.Count} module{(o.Members.Count == 1 ? "" : "s")}{(o.Offline ? " · OFFLINE" : "")}" };
        head.AddThemeFontOverride("font", Pal.Bold); head.AddThemeFontSizeOverride("font_size", 16); head.AddThemeColorOverride("font_color", o.Offline ? Pal.Danger : Pal.Ok);
        _sellBox.AddChild(head);
        var power = new Label { Text = $"Power {o.PowerGen:0.0} kW available / {o.PowerNeed:0.0} kW needed{(o.PowerNeed > o.PowerGen + 1e-9 ? $"  → running at {(o.PowerNeed <= 0 ? 100 : 100 * o.PowerGen / o.PowerNeed):0}%" : "")}   (solar averages day and night; RTGs are constant)", AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(660, 0) };
        power.AddThemeFontOverride("font", Pal.Mono); power.AddThemeFontSizeOverride("font_size", 14); power.AddThemeColorOverride("font_color", o.PowerNeed > o.PowerGen + 1e-9 ? Pal.Accent : Pal.TextDim);
        _sellBox.AddChild(power);
        var under = o.Body.Deposits.Where(d => d.Contains(o.Core.Landed!.Value.LocalAngle)).ToList();
        var dep = new Label { Text = under.Count == 0 ? "No deposit under the core: drills have nothing to dig." : "Under the core: " + string.Join(", ", under.Select(d => $"{Contracts.Name(d.Kind)} {(d.Revealed ? $"{d.Richness:0.0} t/day per drill" : "(land a drill to read the richness)")}")), AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(660, 0) };
        dep.AddThemeFontOverride("font", Pal.Regular); dep.AddThemeFontSizeOverride("font_size", 14); dep.AddThemeColorOverride("font_color", Pal.TextDim);
        _sellBox.AddChild(dep);
        if (o.Rates.Count > 0)
        {
            var rates = new Label { Text = "Rates: " + string.Join("   ", o.Rates.Where(kv => Math.Abs(kv.Value) > 1e-6).OrderByDescending(kv => kv.Value).Select(kv => $"{Contracts.Name(kv.Key)} {(kv.Value >= 0 ? "+" : "")}{kv.Value:0.00} t/day")), AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(660, 0) };
            rates.AddThemeFontOverride("font", Pal.Mono); rates.AddThemeFontSizeOverride("font_size", 14); rates.AddThemeColorOverride("font_color", Pal.Text);
            _sellBox.AddChild(rates);
        }
        foreach (var r in o.Parts.Where(p => p.Def.Has(PartFlags.Refinery)))
        {
            var row = new HBoxContainer();
            var name = new Label { Text = $"{r.Def.Name} ({r.Def.RefineRate:0} t/day, {r.Def.PowerUseKw:0} kW)   recipe:", CustomMinimumSize = new Vector2(330, 0) };
            name.AddThemeFontOverride("font", Pal.Regular); name.AddThemeFontSizeOverride("font_size", 14);
            row.AddChild(name);
            var pick = new OptionButton();
            foreach (var rc in Enum.GetValues<Recipe>()) pick.AddItem(rc switch { Recipe.Auto => "Auto (methalox › water › metal)", Recipe.Water => "Ice → water", Recipe.Methalox => "Water + CO₂ → methalox", Recipe.Rcs => "Water → RCS propellant", Recipe.Metal => "Ore → metal", _ => rc.ToString() });
            pick.Selected = (int)r.Recipe;
            var rr = r;
            pick.ItemSelected += i => { rr.Recipe = (Recipe)(int)i; Rebuild(); };
            row.AddChild(pick);
            _sellBox.AddChild(row);
        }
        var store = new Label { Text = "Storage: " + string.Join("   ", Enum.GetValues<Perigee.Sim.Resource>().Select(res => (res, have: o.Stored(res), room: o.Room(res))).Where(x => x.have > 1e-6 || x.room > 1e-6).Select(x => $"{Contracts.Name(x.res)} {x.have:0.0} t (+{x.room:0.0} free)")) };
        store.AddThemeFontOverride("font", Pal.Mono); store.AddThemeFontSizeOverride("font_size", 13); store.AddThemeColorOverride("font_color", Pal.TextDim);
        store.AutowrapMode = TextServer.AutowrapMode.WordSmart; store.CustomMinimumSize = new Vector2(660, 0);
        _sellBox.AddChild(store);
    }

    public void Rebuild()
    {
        foreach (var ch in _rows.GetChildren()) { _rows.RemoveChild(ch); ch.QueueFree(); }
        _amountLabels.Clear();
        if (Game.World?.Active is not { } c) return;
        RebuildMarket(c);
        RebuildOutpost(c);
        foreach (var p in c.Parts.Where(p => !p.Destroyed && p.CanHold(_res)))
        {
            var row = new HBoxContainer();
            var name = new Label { Text = $"{p.Def.Name} (#{p.Id})", CustomMinimumSize = new Vector2(230, 0) };
            name.AddThemeFontOverride("font", Pal.Regular); name.AddThemeFontSizeOverride("font_size", 15);
            var amount = new Label { CustomMinimumSize = new Vector2(150, 0) };
            amount.AddThemeFontOverride("font", Pal.Mono); amount.AddThemeFontSizeOverride("font_size", 15);
            _amountLabels.Add((p, amount));
            var from = Btn(_src == p.Id ? "● from" : "from"); var to = Btn(_dst == p.Id ? "● to" : "to");
            int id = p.Id;
            from.Pressed += () => { _src = id; Rebuild(); };
            to.Pressed += () => { _dst = id; Rebuild(); };
            row.AddChild(name); row.AddChild(amount); row.AddChild(from); row.AddChild(to);
            _rows.AddChild(row);
        }
        foreach (var p in c.Parts.Where(p => !p.Destroyed && p.Fired && p.Def.Has(PartFlags.DockingPort | PartFlags.Claw)))
        {
            var row = new HBoxContainer();
            var name = new Label { Text = $"{p.Def.Name} (#{p.Id}) — {(c.DockedNames.TryGetValue(p.Id, out var n) ? n : "joint")}", CustomMinimumSize = new Vector2(400, 0) };
            name.AddThemeFontOverride("font", Pal.Regular); name.AddThemeFontSizeOverride("font_size", 15);
            var undock = Btn(p.Def.Has(PartFlags.Claw) ? "Release" : "Undock");
            int id = p.Id;
            undock.Pressed += () => { Game.World!.Undock(c, id); Rebuild(); };
            row.AddChild(name); row.AddChild(undock);
            _rows.AddChild(row);
        }
        RefreshAmounts();
    }
}
