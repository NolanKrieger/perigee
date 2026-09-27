using Godot;
using Perigee.Sim;

namespace Perigee;

/// <summary>The contract board (GDD §11, §16 "Contracts"): offers to accept or decline, active contracts with progress and deadlines, and the history.
/// Opens with C; the sim pauses while it is up. Becomes the Contracts tab of the HQ hub in M12.</summary>
public partial class ContractsScreen : PanelContainer
{
    public Main Game = null!;
    VBoxContainer _offers = null!, _active = null!, _history = null!;
    Label _summary = null!;
    Button _close = null!;
    double _refreshTimer;
    public bool HostedInHq { set => _close.Visible = !value; }

    public override void _Ready()
    {
        Position = new Vector2(40, 40);
        Size = new Vector2(1840, 1000);
        AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = Pal.Panel with { A = 0.97f }, BorderColor = Pal.PanelBorder, BorderWidthBottom = 1, BorderWidthTop = 1, BorderWidthLeft = 1, BorderWidthRight = 1, ContentMarginLeft = 16, ContentMarginRight = 16, ContentMarginTop = 10, ContentMarginBottom = 10 });
        var root = new VBoxContainer();
        AddChild(root);
        var head = new HBoxContainer();
        root.AddChild(head);
        var title = Lbl("CONTRACTS", Pal.Bold, 24, Pal.Accent);
        head.AddChild(title);
        _summary = Lbl("", Pal.Mono, 16, Pal.TextDim);
        _summary.SizeFlagsHorizontal = SizeFlags.ExpandFill; _summary.HorizontalAlignment = HorizontalAlignment.Right;
        head.AddChild(_summary);
        _close = Btn("Close (C / Esc)"); _close.Pressed += Hide;
        head.AddChild(_close);
        var cols = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        cols.AddThemeConstantOverride("separation", 16);
        root.AddChild(cols);
        _offers = Column(cols, "OFFERS  ·  new ones daily, 3–6 open", 620);
        _active = Column(cols, "ACTIVE  ·  up to 5 at a time", 620);
        _history = Column(cols, "HISTORY", 500);
        Visible = false;
    }

    VBoxContainer Column(HBoxContainer parent, string heading, float width)
    {
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(width, 0), SizeFlagsVertical = SizeFlags.ExpandFill };
        parent.AddChild(box);
        box.AddChild(Lbl(heading, Pal.Bold, 17, Pal.Accent));
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(width, 880) };
        box.AddChild(scroll);
        var rows = new VBoxContainer { CustomMinimumSize = new Vector2(width - 20, 0), SizeFlagsHorizontal = SizeFlags.Fill };
        rows.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(rows);
        return rows;
    }

    public void Toggle() { if (Visible) Hide(); else Show(); }
    public new void Show() { Visible = true; Game.Hud.Visible = false; Rebuild(); }
    public new void Hide() { Visible = false; Game.Hud.Visible = true; }

    public override void _Process(double delta)
    {
        if (!Visible) return;
        _refreshTimer += delta;
        if (_refreshTimer > 1.0) { _refreshTimer = 0; Rebuild(); }
    }

    static void Clear(Container c) { foreach (var ch in c.GetChildren()) { c.RemoveChild(ch); ch.QueueFree(); } }

    public void Rebuild()
    {
        var w = Game.World; if (w == null) return;
        Clear(_offers); Clear(_active); Clear(_history);
        int accepted = w.AcceptedContracts.Count();
        _summary.Text = $"{w.Offers.Count()} offers · {accepted}/{Contracts.MaxAccepted} active · {w.Contracts.Count(c => c.State == ContractState.Completed)} completed · {w.Contracts.Count(c => c.State == ContractState.Failed)} failed";
        foreach (var c in w.Offers.OrderBy(c => c.Tutorial == 0).ThenBy(c => c.Id)) _offers.AddChild(OfferCard(w, c, accepted < Contracts.MaxAccepted));
        if (_offers.GetChildCount() == 0) _offers.AddChild(Lbl("No open offers. New offers arrive at the start of each day.", Pal.Regular, 15, Pal.TextDim));
        foreach (var c in w.AcceptedContracts.OrderBy(c => c.Deadline)) _active.AddChild(ActiveCard(w, c));
        if (_active.GetChildCount() == 0) _active.AddChild(Lbl("Nothing accepted yet. Accept an offer to start.", Pal.Regular, 15, Pal.TextDim));
        foreach (var c in w.Contracts.Where(c => c.State is ContractState.Completed or ContractState.Failed or ContractState.Expired).OrderByDescending(c => double.IsNaN(c.ResolvedAt) ? c.OfferedAt : c.ResolvedAt).Take(30)) _history.AddChild(HistoryRow(c));
        if (_history.GetChildCount() == 0) _history.AddChild(Lbl("No completed or failed contracts yet.", Pal.Regular, 15, Pal.TextDim));
    }

    Control OfferCard(World w, Contract c, bool canAccept)
    {
        var card = Card(c.Tutorial > 0 ? Pal.Accent with { A = 0.6f } : Pal.PanelBorder);
        var box = new VBoxContainer(); card.AddChild(box);
        box.AddChild(Para(c.Title, Pal.Bold, 17, Pal.Text, 580));
        box.AddChild(Para($"{c.Client}  ·  {w.Sys[c.TargetBody].Name}", Pal.Regular, 14, Pal.TextDim, 580));
        box.AddChild(Para(c.Objective, 15, Pal.Text, 580));
        box.AddChild(Para($"Reward {Units.FormatMoney(c.Reward)}   ·   advance {Units.FormatMoney(c.Advance)} (repaid on failure)   ·   penalty {Units.FormatMoney(c.Penalty)}", Pal.Mono, 13, Pal.TextDim, 580));
        box.AddChild(Para($"{c.DurationDays:0} days from acceptance{(c.Tutorial == 0 ? $"   ·   offer expires in {Math.Max(0, c.TimeLeft(w.T) / Units.Day):0.0} d" : "")}", Pal.Mono, 13, Pal.TextDim, 580));
        var buttons = new HBoxContainer(); box.AddChild(buttons);
        var accept = Btn("Accept"); accept.Disabled = !canAccept; accept.Pressed += () => { if (w.Accept(c, false)) Game.Hud.Say($"Accepted: {c.Title}", 4); Rebuild(); };
        var advance = Btn("Accept with 20% advance"); advance.Disabled = !canAccept; advance.Pressed += () => Game.ConfirmThen($"Take a {Units.FormatMoney(c.Advance)} advance on \"{c.Title}\"? If the contract fails you repay it plus a {Units.FormatMoney(c.Penalty)} penalty.", () => { if (w.Accept(c, true)) Game.Hud.Say($"Accepted with advance: {c.Title} (+{Units.FormatMoney(c.Advance)})", 4); Rebuild(); });
        var decline = Btn("Decline"); decline.Disabled = c.Tutorial > 0; decline.Pressed += () => { w.Decline(c); Rebuild(); };
        buttons.AddChild(accept); buttons.AddChild(advance); buttons.AddChild(decline);
        if (!canAccept) box.AddChild(Lbl("Five contracts are already active.", Pal.Regular, 13, Pal.Danger));
        return card;
    }

    Control ActiveCard(World w, Contract c)
    {
        var card = Card(c.Tutorial > 0 ? Pal.Accent with { A = 0.6f } : Pal.PanelBorder);
        var box = new VBoxContainer(); card.AddChild(box);
        box.AddChild(Para(c.Title, Pal.Bold, 17, Pal.Text, 580));
        box.AddChild(Para(c.Objective, 15, Pal.Text, 580));
        double left = c.Deadline - w.T;
        var bar = new ProgressBar { MinValue = 0, MaxValue = 1, Value = c.Progress, ShowPercentage = false, CustomMinimumSize = new Vector2(600, 10) };
        bar.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = Pal.Ok });
        bar.AddThemeStyleboxOverride("background", new StyleBoxFlat { BgColor = Pal.PanelBorder with { A = 0.4f } });
        box.AddChild(bar);
        box.AddChild(Para($"{c.Progress * 100:0}%   ·   due in {left / Units.Day:0.0} d   ·   pays {Units.FormatMoney(c.Reward - (c.TookAdvance ? c.Advance : 0))} on completion", Pal.Mono, 13, left < Units.Day ? Pal.Danger : Pal.TextDim, 580));
        if (c.Tutorial > 0 && c.Hint.Length > 0) box.AddChild(Para("Hint: " + c.Hint, 14, Pal.Info, 580));
        return card;
    }

    Control HistoryRow(Contract c)
    {
        var card = Card(Pal.PanelBorder with { A = 0.5f });
        var box = new VBoxContainer(); card.AddChild(box);
        var col = c.State == ContractState.Completed ? Pal.Ok : c.State == ContractState.Failed ? Pal.Danger : Pal.TextDim;
        box.AddChild(Para($"{c.State.ToString().ToUpperInvariant()}  {c.Title}", Pal.Bold, 14, col, 460));
        string money = c.State == ContractState.Completed ? $"+{Units.FormatMoney(c.Reward)}" : c.State == ContractState.Failed ? $"−{Units.FormatMoney(c.Penalty + (c.TookAdvance ? c.Advance : 0))}" : "no cost";
        box.AddChild(Para($"{c.Client}  ·  {money}", Pal.Regular, 13, Pal.TextDim, 460));
        return card;
    }

    /// <summary>Test hook: press the first button with this text inside the offers column (self-test drives the real handlers).</summary>
    public bool PressOfferButton(string text)
    {
        foreach (var b in Walk(_offers)) if (b is Button btn && btn.Text == text && !btn.Disabled) { btn.EmitSignal(Button.SignalName.Pressed); return true; }
        return false;
    }
    public int OfferCards => _offers.GetChildren().Count(c => c is PanelContainer);
    public int ActiveCards => _active.GetChildren().Count(c => c is PanelContainer);
    static IEnumerable<Node> Walk(Node n) { foreach (var c in n.GetChildren()) { yield return c; foreach (var g in Walk(c)) yield return g; } }

    static PanelContainer Card(Color border)
    {
        var p = new PanelContainer();
        p.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = Pal.PanelBorder with { A = 0.18f }, BorderColor = border, BorderWidthBottom = 1, BorderWidthTop = 1, BorderWidthLeft = 1, BorderWidthRight = 1, ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 6, ContentMarginBottom = 6 });
        return p;
    }

    static Label Lbl(string text, Font f, int size, Color c)
    {
        var l = new Label { Text = text };
        l.AddThemeFontOverride("font", f); l.AddThemeFontSizeOverride("font_size", size); l.AddThemeColorOverride("font_color", c);
        return l;
    }

    static Label Para(string text, int size, Color c, float width) => Para(text, Pal.Regular, size, c, width);
    static Label Para(string text, Font f, int size, Color c, float width)
    {
        var l = Lbl(text, f, size, c);
        l.AutowrapMode = TextServer.AutowrapMode.WordSmart; l.CustomMinimumSize = new Vector2(width, 0);
        l.SizeFlagsHorizontal = SizeFlags.Fill;
        return l;
    }

    static Button Btn(string t)
    {
        var b = new Button { Text = t, CustomMinimumSize = new Vector2(90, 30) };
        b.AddThemeFontOverride("font", Pal.Regular); b.AddThemeFontSizeOverride("font_size", 15);
        return b;
    }
}
