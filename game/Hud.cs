using Godot;
using Perigee.Sim;

namespace Perigee;

/// <summary>Flight overlay: date/warp/focus, orbital readouts, scale bar and the event log. Drawn directly; grows with each milestone.</summary>
public partial class Hud : Control
{
    public Main Game = null!;
    public readonly List<(string text, double until)> Log = new();
    float _time;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Process(double delta) { _time += (float)delta; QueueRedraw(); }

    public void Say(string text, double seconds = 6) => Log.Add((text, _time + seconds));

    // Tooltips for HUD readouts (brief): each registered rect shows its text while the mouse is over it.
    readonly List<(Rect2 rect, string text)> _tips = new();
    Vector2 _mouse = new(-1, -1);
    public override void _Input(InputEvent ev) { if (ev is InputEventMouseMotion m) _mouse = m.Position; }
    void Tip(Vector2 pos, float w, float h, string text) => _tips.Add((new Rect2(pos.X, pos.Y - h + 4, w, h), text));
    void DrawTooltip()
    {
        foreach (var (rect, text) in _tips)
        {
            if (!rect.HasPoint(_mouse)) continue;
            var f = Pal.Regular; int size = 14; float width = 360;
            var sz = f.GetMultilineStringSize(text, HorizontalAlignment.Left, width, size);
            var at = new Vector2(Math.Min(_mouse.X + 16, FlightView.W - width - 24), Math.Min(_mouse.Y + 18, FlightView.H - sz.Y - 20));
            DrawRect(new Rect2(at - new Vector2(8, 6), new Vector2(width + 16, sz.Y + 12)), Pal.Panel with { A = 0.96f });
            DrawRect(new Rect2(at - new Vector2(8, 6), new Vector2(width + 16, sz.Y + 12)), Pal.PanelBorder, false, 1);
            DrawMultilineString(f, at + new Vector2(0, size), text, HorizontalAlignment.Left, width, size, -1, Pal.Text);
            break;
        }
    }

    /// <summary>Word-wrapped paragraph; returns the height used.</summary>
    float Para(string s, Vector2 pos, int size, Color c, float width)
    {
        var f = Pal.Regular;
        DrawMultilineString(f, pos + new Vector2(1, size + 1), s, HorizontalAlignment.Left, width, size, -1, Colors.Black with { A = 0.6f });
        DrawMultilineString(f, pos + new Vector2(0, size), s, HorizontalAlignment.Left, width, size, -1, c);
        var sz = f.GetMultilineStringSize(s, HorizontalAlignment.Left, width, size);
        return sz.Y + 6;
    }

    void Text(string s, Vector2 pos, Font f, int size, Color c, HorizontalAlignment align = HorizontalAlignment.Left, float width = -1)
    {
        DrawString(f, pos + new Vector2(1, 1), s, align, width, size, Colors.Black with { A = 0.6f });
        DrawString(f, pos, s, align, width, size, c);
    }

    public override void _Draw()
    {
        var w = Game.World; if (w == null) return;
        var view = Game.View;
        _tips.Clear();
        // Top-left: date, warp
        Text(Units.FormatDate(w.T), new Vector2(24, 40), Pal.MonoBold, 24, Pal.Text);
        Tip(new Vector2(24, 40), 220, 28, "Game date and time. One day is 6 hours of game time; a year is 400 days. Upkeep is charged at each day boundary.");
        Tip(new Vector2(24, 70), 140, 24, "Time warp. , and . step it down and up, / returns to 1×. Warp stops by itself at sphere-of-influence changes, atmosphere entry, contract deadlines and storm warnings. Flying craft warp up to 4×; coasting craft up to 100,000×.");
        Tip(new Vector2(FlightView.W - 24 - 420, 40), 420, 28, "Cash, and the runway: how many days of upkeep it covers. The career ends at $0 unless something in flight can still earn.");
        string warp = w.EffectiveWarp >= 1000 ? $"{w.EffectiveWarp / 1000:0}k×" : $"{w.EffectiveWarp:0}×";
        Text($"WARP {warp}", new Vector2(24, 70), Pal.Mono, 20, w.EffectiveWarp > 1 ? Pal.Accent : Pal.TextDim);
        // Contracts (GDD §11): the current lesson's objective and hint, or the pending offer, at the top left.
        float cy = 104;
        var lesson = w.AcceptedContracts.FirstOrDefault(k => k.Tutorial > 0);
        if (lesson != null)
        {
            Text(lesson.Title, new Vector2(24, cy), Pal.Bold, 18, Pal.Accent); cy += 24;
            cy += Para(lesson.Objective, new Vector2(24, cy), 16, Pal.Text, 560);
            cy += Para(lesson.Hint, new Vector2(24, cy), 15, Pal.Info with { A = 0.9f }, 560);
        }
        else if (w.Offers.FirstOrDefault(k => k.Tutorial > 0) is { } offer)
        {
            Text($"NEW OFFER  {offer.Title}", new Vector2(24, cy), Pal.Bold, 18, Pal.Accent); cy += 24;
            Text("C opens the contract board", new Vector2(24, cy), Pal.Regular, 15, Pal.TextDim); cy += 22;
        }
        int activeN = w.AcceptedContracts.Count();
        if (activeN > 0 && lesson == null) { Text($"CONTRACTS  {activeN} active  ·  C", new Vector2(24, cy), Pal.Mono, 16, Pal.TextDim); cy += 22; }
        foreach (var k in w.AcceptedContracts.Where(k => k.Tutorial == 0).Take(3))
        {
            double left = k.Deadline - w.T;
            Text($"· {k.Title}  {k.Progress * 100:0}%  {left / Units.Day:0.0} d", new Vector2(24, cy), Pal.Mono, 14, left < Units.Day ? Pal.Danger : Pal.TextDim); cy += 20;
        }
        // Cash + runway (GDD §10: the HUD always shows cash runway).
        string runway = double.IsPositiveInfinity(w.Runway) ? "no upkeep" : $"runway {w.Runway:0} d";
        Text($"{Units.FormatMoney(w.Cash)}   {runway}", new Vector2(FlightView.W - 24 - 420, 40), Pal.MonoBold, 22, w.Cash > 0 ? Pal.Text : Pal.Danger, HorizontalAlignment.Right, 420);
        if (w.DailyUpkeep() > 0) Text($"upkeep {Units.FormatMoney(w.DailyUpkeep())}/day", new Vector2(FlightView.W - 24 - 420, 66), Pal.Mono, 16, Pal.TextDim, HorizontalAlignment.Right, 420);
        // Storm alerts (GDD §14): warning and active banners, with the active craft's shelter state.
        if (w.Storm == StormPhase.Warning)
        {
            Text($"SOLAR STORM in {(w.StormStart - w.T) / Units.Day:0.0} d  ·  lasts {(w.StormEnd - w.StormStart) / Units.Day:0.0} d", new Vector2(0, 96), Pal.Bold, 22, Pal.Accent, HorizontalAlignment.Center, FlightView.W);
            Text($"shelter: land, hide in a body's shadow, or stay within {w.Sys.Home.Magnetosphere / w.Sys.Home.Radius:0}× home radius{(w.Active is { } sc ? (w.StormSafe(sc) ? "   ·   this craft: SAFE" : "   ·   this craft: EXPOSED") : "")}", new Vector2(0, 122), Pal.Regular, 15, Pal.Accent with { A = 0.9f }, HorizontalAlignment.Center, FlightView.W);
        }
        else if (w.Storm == StormPhase.Active)
        {
            Text($"SOLAR STORM  ·  {(w.StormEnd - w.T) / Units.Day:0.0} d left{(w.Active is { } sc ? (w.StormSafe(sc) ? "   ·   this craft: SAFE" : "   ·   this craft: EXPOSED") : "")}", new Vector2(0, 96), Pal.Bold, 22, Pal.Danger, HorizontalAlignment.Center, FlightView.W);
        }
        if (w.Active is { Fried: true } fc)
        {
            double back = fc.Parts.Where(p => p.Offline && !double.IsNaN(p.OfflineUntil)).Select(p => p.OfflineUntil).DefaultIfEmpty(w.T).Min();
            Text($"CORE FRIED  ·  no control  ·  reboots in {Math.Max(0, back - w.T) / Units.Day:0.0} d", new Vector2(0, 150), Pal.Bold, 22, Pal.Danger, HorizontalAlignment.Center, FlightView.W);
        }
        if (w.GameOver) Text($"CAREER OVER — {w.GameOverCause}", new Vector2(0, 200), Pal.Bold, 40, Pal.Danger, HorizontalAlignment.Center, FlightView.W);
        else if (!double.IsNaN(w.InsolventSince)) Text($"INSOLVENT — day {w.InsolventDay}/{w.Preset.GraceDays}", new Vector2(0, 200), Pal.Bold, 34, Pal.Danger, HorizontalAlignment.Center, FlightView.W);

        // Top-centre: focus
        var c = w.Active;
        if (c != null)
        {
            var body = w.Sys[c.BodyId];
            var (r, v) = c.StateAt(w.T);
            double alt = r.Length - body.Radius;
            var vs = v - Flight.SurfaceVelocity(body, r);
            var rhat = r.Normalized();
            Text(c.Name, new Vector2(0, 40), Pal.Bold, 26, Pal.Text, HorizontalAlignment.Center, FlightView.W);
            string mode = c.Mode switch { CraftMode.Landed => c.Landed?.Water == true ? "floating" : "landed", CraftMode.Rails => "coasting", CraftMode.Active => "flying", _ => c.Mode.ToString() };
            Text($"{body.Name}  ·  {mode}", new Vector2(0, 66), Pal.Regular, 17, Pal.TextDim, HorizontalAlignment.Center, FlightView.W);
            // Bottom-left: flight readouts (GDD §16 flight HUD)
            var conic = c.Mode == CraftMode.Rails ? c.Rails.Conic : Conic.FromState(body.Gm, r, v, w.T);
            string ap = conic.IsEllipse ? Units.FormatDistance(conic.Apoapsis - body.Radius) : "escape";
            float y = 1040;
            Text($"ALT {Units.FormatDistance(alt)}", new Vector2(24, y - 120), Pal.Mono, 20, Pal.Text);
            Tip(new Vector2(24, y - 120), 260, 24, "Altitude above the body's mean radius (negative in valleys). The air ends at the atmosphere line; landing legs need the ground.");
            Tip(new Vector2(24, y - 96), 320, 24, "SRF: speed over the ground (what drag and landings feel). ORB: speed in the body's frame (what the orbit feels).");
            Tip(new Vector2(24, y - 72), 200, 24, "Vertical speed: positive climbing, negative sinking. Legs survive 14 m/s.");
            Tip(new Vector2(24, y - 48), 220, 24, "Apoapsis: the highest point of the current orbit above the surface. Burn prograde to raise it.");
            Tip(new Vector2(24, y - 24), 220, 24, "Periapsis: the lowest point. It must clear the atmosphere (home: 50 km) to stay in orbit. Negative means the path hits the ground.");
            Tip(new Vector2(24, y), 200, 24, "Dynamic pressure: how hard the air pushes. Max-Q is the worst moment for fins and fairings.");
            Text($"SRF {vs.Length:0} m/s   ORB {v.Length:0} m/s", new Vector2(24, y - 96), Pal.Mono, 20, Pal.Text);
            Text($"VS  {Vec2d.Dot(vs, rhat):+0;-0} m/s", new Vector2(24, y - 72), Pal.Mono, 20, Pal.Text);
            Text($"AP  {ap}", new Vector2(24, y - 48), Pal.Mono, 20, Pal.Text);
            Text($"PE  {Units.FormatDistance(conic.Periapsis - body.Radius)}", new Vector2(24, y - 24), Pal.Mono, 20, Pal.Text);
            double rho = body.Atmo?.Density(alt) ?? 0;
            if (rho > 0) Text($"Q   {0.5 * rho * vs.LengthSq / 1000:0.0} kPa", new Vector2(24, y), Pal.Mono, 20, Pal.TextDim);

            // Target block (GDD §4 target markers): distance, relative speed, closest approach.
            var tc = c.TargetCraft >= 0 ? w.Find(c.TargetCraft) : null;
            float ty = 150;
            if (tc != null && !tc.Destroyed)
            {
                double dist = w.Distance(c, tc);
                var (tr, tv) = tc.StateAt(w.T);
                string rel = tc.BodyId == c.BodyId ? $"{(tv - v).Length:0.0} m/s" : "other SOI";
                Text($"TGT {tc.Name}   {Units.FormatDistance(dist)}   rel {rel}", new Vector2(0, ty), Pal.Mono, 18, Pal.Info, HorizontalAlignment.Center, FlightView.W); ty += 22;
                var ca = w.ClosestApproach(c);
                if (ca is { } a && dist > 500) { Text($"closest approach {Units.FormatDistance(a.dist)} in {Units.FormatDuration(a.t)} at {a.relSpeed:0} m/s", new Vector2(0, ty), Pal.Mono, 16, Pal.Info with { A = 0.85f }, HorizontalAlignment.Center, FlightView.W); ty += 22; }
                if (c.Capture != null) { Text("SOFT CAPTURE", new Vector2(0, ty), Pal.Bold, 22, Pal.Ok, HorizontalAlignment.Center, FlightView.W); ty += 26; }
            }
            else if (c.TargetBody >= 0)
            {
                var tb = w.Sys[c.TargetBody];
                double dist = (w.Sys.Position(tb.Id, w.T) - w.AbsolutePosition(c)).Length;
                Text($"TGT {tb.Name}   {Units.FormatDistance(dist)}", new Vector2(0, ty), Pal.Mono, 18, Pal.Info, HorizontalAlignment.Center, FlightView.W); ty += 22;
            }
            int docked = c.Parts.Count(p => !p.Destroyed && p.Fired && p.Def.Has(PartFlags.DockingPort | PartFlags.Claw));
            if (docked > 0) { Text($"DOCKED · {string.Join(", ", c.DockedNames.Values)}   (F transfer · U undock)", new Vector2(0, ty), Pal.Mono, 17, Pal.Ok, HorizontalAlignment.Center, FlightView.W); ty += 22; }
            // Recovery + booster flashback prompts (GDD §4, §6).
            if (w.InFlashback) { Text($"FLASHBACK · flying {c.Name} home · clock returns to {Units.FormatDate(w.FlashbackReturnTime)}", new Vector2(0, ty), Pal.MonoBold, 18, Pal.Accent, HorizontalAlignment.Center, FlightView.W); ty += 24; }
            else if (w.FlashbackOffered && w.NextHeld is { } hb) { Text($"FLY BOOSTER  {hb.Name}  ·  Enter fly it home  ·  Backspace let it go", new Vector2(0, ty), Pal.MonoBold, 18, Pal.Accent, HorizontalAlignment.Center, FlightView.W); ty += 24; }
            if (c.Mode == CraftMode.Landed && w.RefundRate(c) > 0) { Text($"RECOVER for {Units.FormatMoney(w.RecoveryValue(c))}  ({w.RefundRate(c) * 100:0}% of parts)  ·  Enter", new Vector2(0, ty), Pal.MonoBold, 18, Pal.Ok, HorizontalAlignment.Center, FlightView.W); ty += 24; }
            int debrisHere = w.DebrisAt(c.BodyId).Count();
            if (debrisHere > 0 && view.IsMapScale) { Text($"DEBRIS  {debrisHere} tracked at {body.Name}  ·  {w.TrackedObjects} objects", new Vector2(0, ty), Pal.Mono, 16, Pal.TextDim, HorizontalAlignment.Center, FlightView.W); ty += 22; }
            var node = w.NodeFor(c);
            if (node != null)
            {
                bool depot = c.Parts.Any(p => !p.Destroyed && p.Def.Has(PartFlags.DepotController));
                bool canSell = node.Kind != NodeKind.LowOrbit || depot;
                Text($"MARKET {node.Name}: methalox {Units.FormatMoney(w.Market.Price(node, Perigee.Sim.Resource.Methalox, w.T))}/t{(canSell ? "  ·  F to sell" : "  ·  needs a Depot Controller to sell here")}", new Vector2(0, ty), Pal.Mono, 16, Pal.Info with { A = 0.9f }, HorizontalAlignment.Center, FlightView.W); ty += 22;
            }

            // Outpost (GDD §9): production and power at a glance when the active craft is part of one.
            var outpost = c.Mode == CraftMode.Landed ? w.Outposts().FirstOrDefault(o => o.Members.Contains(c)) : null;
            if (outpost != null)
            {
                string rates = string.Join("  ", outpost.Rates.Where(kv => kv.Value > 1e-6).OrderByDescending(kv => kv.Value).Take(3).Select(kv => $"{Contracts.Name(kv.Key)} +{kv.Value:0.0} t/d"));
                string power = outpost.Offline ? "OFFLINE" : $"power {outpost.PowerGen:0.0}/{outpost.PowerNeed:0.0} kW";
                Text($"OUTPOST {outpost.Name}  ·  {power}{(rates.Length > 0 ? "  ·  " + rates : "  ·  idle")}  ·  F", new Vector2(0, ty), Pal.Mono, 16, outpost.Offline ? Pal.Danger : Pal.Ok with { A = 0.9f }, HorizontalAlignment.Center, FlightView.W); ty += 22;
            }
            // Right column: throttle, stage, fuel/Δv per stage, heat.
            float rx = FlightView.W - 24;          // right edge of the text column
            DrawThrottle(new Vector2(FlightView.W - 24 - 16, 300), c.Throttle);
            float tx = rx - 30;                    // text ends left of the throttle bar
            Text($"THR {c.Throttle * 100:0}%", new Vector2(tx - 300, 320), Pal.Mono, 20, Pal.Text, HorizontalAlignment.Right, 300);
            Tip(new Vector2(tx - 140, 320), 180, 24, "Throttle. Shift/Ctrl ramp it, Z is full, X cuts it. Engines below their minimum throttle run at the minimum.");
            Tip(new Vector2(tx - 140, 348), 180, 22, "Next stage to fire with Space, out of the stages the design has. Staging drops decouplers, ignites engines and opens chutes in their stage.");
            Tip(new Vector2(tx - 140, 372), 180, 22, "Reaction control thrusters: R toggles them, IJKL translate. They burn RCS propellant from any RCS tank.");
            Text($"STAGE {c.NextStage}/{c.Stages.Count}", new Vector2(tx - 300, 348), Pal.Mono, 18, Pal.TextDim, HorizontalAlignment.Right, 300);
            Text(c.RcsOn ? "RCS ON" : "RCS off", new Vector2(tx - 300, 372), Pal.Mono, 16, c.RcsOn ? Pal.Ok : Pal.TextDim, HorizontalAlignment.Right, 300);
            var report = c.StageReport(body.SurfaceGravity);
            float ry = 440;
            foreach (var st in report.Take(4))
            {
                Text($"S{st.Stage + 1}  Δv {st.DeltaVVac:0} m/s  {Units.FormatDuration(st.BurnTime)}  TWR {st.TwrSurface:0.0}", new Vector2(rx - 420, ry), Pal.Mono, 16, Pal.Text, HorizontalAlignment.Right, 420);
                ry += 22;
            }
            double fuelCap = 0, fuel = 0;
            foreach (var p in c.Parts) if (!p.Destroyed) { fuelCap += p.Capacity(Perigee.Sim.Resource.Methalox); fuel += p.Get(Perigee.Sim.Resource.Methalox); }
            Tip(new Vector2(rx - 420, 440), 420, Math.Max(22, ry - 440), "Per stage: vacuum Δv, burn time at full throttle and thrust-to-weight on this body. Δv is what the rocket equation says the stage can change your speed by.");
            if (fuelCap > 0) { DrawBar(new Vector2(rx - 200, ry + 6), 200, (float)(fuel / fuelCap), Pal.Accent); Text($"FUEL {fuel:0.00} t", new Vector2(rx - 420, ry + 18), Pal.Mono, 16, Pal.TextDim, HorizontalAlignment.Right, 215); Tip(new Vector2(rx - 420, ry + 18), 420, 24, "Methalox aboard this craft, all tanks. Engines drain the tanks their feed reaches; decouplers and docking ports block the flow."); ry += 34; }
            double maxT = 0; double maxRatio = 0;
            foreach (var p in c.Parts) if (!p.Destroyed) { maxT = Math.Max(maxT, p.Temp); maxRatio = Math.Max(maxRatio, p.Temp / p.Def.MaxTemp); }
            if (maxRatio > 0.35)
            {
                var hc = maxRatio > 0.85 ? Pal.Danger : maxRatio > 0.6 ? Pal.Accent : Pal.TextDim;
                Text($"HEAT {maxT:0} K", new Vector2(rx - 420, ry), Pal.MonoBold, 18, hc, HorizontalAlignment.Right, 400);
                if (maxRatio > 0.85 && ((int)(_time * 4) % 2 == 0)) Text("OVERHEATING", new Vector2(0, 120), Pal.Bold, 30, Pal.Danger, HorizontalAlignment.Center, FlightView.W);
            }
        }

        // Bottom-right: scale bar
        DrawScaleBar(view);

        // Event log (bottom centre)
        DrawTooltip();
        Log.RemoveAll(e => e.until < _time);
        for (int i = 0; i < Log.Count && i < 4; i++)
        {
            var (text, until) = Log[Log.Count - 1 - i];
            float a = Mathf.Clamp((float)(until - _time), 0, 1);
            Text(text, new Vector2(0, 980 - i * 24), Pal.Regular, 18, Pal.Text with { A = a }, HorizontalAlignment.Center, FlightView.W);
        }
        Text("A/D rotate · Shift/Ctrl throttle · Z/X full/cut · Space stage · P chutes · G legs · R rcs · IJKL translate · right-click target · F transfer · U undock · M map · wheel zoom · , . / warp · Tab craft · B builder · C contracts", new Vector2(0, 1062), Pal.Regular, 15, Pal.TextDim with { A = 0.7f }, HorizontalAlignment.Center, FlightView.W);
        if (Game.LastLog.Length > 0) Text(Game.LastLog, new Vector2(24, 870), Pal.Mono, 13, Pal.Info with { A = 0.8f }, HorizontalAlignment.Left, 1200);   // script mode only: last script line, clear of the lesson block
    }

    void DrawThrottle(Vector2 pos, double throttle)
    {
        DrawRect(new Rect2(pos, new Vector2(16, 120)), Pal.Panel);
        DrawRect(new Rect2(pos + new Vector2(0, 120 - 120 * (float)throttle), new Vector2(16, 120 * (float)throttle)), Pal.Accent);
        DrawRect(new Rect2(pos, new Vector2(16, 120)), Pal.PanelBorder, false, 1);
    }

    void DrawBar(Vector2 pos, float width, float frac, Color col)
    {
        DrawRect(new Rect2(pos, new Vector2(width, 8)), Pal.Panel);
        DrawRect(new Rect2(pos, new Vector2(width * Mathf.Clamp(frac, 0, 1), 8)), col);
        DrawRect(new Rect2(pos, new Vector2(width, 8)), Pal.PanelBorder, false, 1);
    }

    void DrawScaleBar(FlightView view)
    {
        // Pick a "nice" length that fits in 100–260 px.
        double[] nice = { 1, 2, 5 };
        double best = 1; 
        for (int e = -1; e < 12; e++) foreach (var n in nice)
        {
            double len = n * Math.Pow(10, e); double px = len * view.Scale;
            if (px >= 100 && px <= 260) { best = len; goto found; }
        }
        found:
        float bar = (float)(best * view.Scale);
        var p0 = new Vector2(FlightView.W - 40 - bar, 1040);
        DrawLine(p0, p0 + new Vector2(bar, 0), Pal.Text, 2);
        DrawLine(p0, p0 + new Vector2(0, -8), Pal.Text, 2);
        DrawLine(p0 + new Vector2(bar, 0), p0 + new Vector2(bar, -8), Pal.Text, 2);
        Text(Units.FormatDistance(best), p0 + new Vector2(0, -14), Pal.Mono, 16, Pal.Text, HorizontalAlignment.Center, bar);
    }
}
