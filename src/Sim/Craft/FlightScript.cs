using System.Globalization;
using System.Text;

namespace Perigee.Sim;

/// <summary>What a script drives: held inputs, taps, launches and ticks. The headless host writes the World directly; the Godot host goes through the real input path.</summary>
public interface IScriptHost
{
    World World { get; }
    void Launch(string design, string name);
    /// <summary>Launch from the operational pad on `bodyId` (M10).</summary>
    void LaunchAt(string design, string name, int bodyId) { var pad = World.PadAt(bodyId) ?? throw new InvalidOperationException($"no pad on {World.Sys[bodyId].Name}"); if (World.TryLaunchAtPad(SimHost.DesignByName(design), name, pad) == null) throw new InvalidOperationException("pad launch refused"); }
    /// <summary>Test setup: put a design on rails in a circular orbit `alt` m above the active craft's body (or home), `phaseDeg` ahead of the active craft, tanks at `fuel` fraction.</summary>
    void Spawn(string design, string name, double alt, double phaseDeg, double fuel);
    void SetHeld(string action, bool down);
    void Tap(string action);
    void SetThrottle(double v);
    void Tick();
    /// <summary>Capture a screenshot named `name` (no-op headless).</summary>
    void Shot(string name) { }
    /// <summary>View hint for captures, e.g. ("zoom", 3) = 3 px/m (no-op headless).</summary>
    void View(string what, double value) { }
    /// <summary>Toggle a UI panel for captures ("transfer", "board"); no-op headless.</summary>
    void Ui(string what) { }
}

public sealed class ScriptResult
{
    public bool Passed => Failures.Count == 0 && !Aborted;
    public bool Aborted;
    public bool Done;
    public List<string> Failures = new();
    public List<string> Log = new();
    public double EndTime;
    public int Ticks;
}

/// <summary>
/// Scripted flights (test tool only, never an autopilot): a timeline of presses, waits, conditions and assertions run through
/// a host. Lines: `launch &lt;design&gt; [name]`, `press/release/tap &lt;action&gt;`, `hold &lt;action&gt; &lt;s&gt;`, `wait &lt;s&gt;`,
/// `until &lt;expr&gt; [timeout &lt;s&gt;]`, `throttle &lt;0..1&gt;`, `warp &lt;n&gt;`, `attitude pitch &lt;deg&gt;|off`, `expect &lt;expr&gt;`, `log &lt;text&gt;`, `end`.
/// </summary>
public sealed class FlightScript
{
    public readonly List<(int line, string[] words)> Lines = new();
    public string Name = "";

    public static FlightScript Parse(string text, string name = "")
    {
        var s = new FlightScript { Name = name };
        var lines = text.Replace("\r", "").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string l = lines[i];
            int hash = l.IndexOf('#'); if (hash >= 0) l = l[..hash];
            l = l.Trim();
            if (l.Length == 0) continue;
            s.Lines.Add((i + 1, l.Split(' ', StringSplitOptions.RemoveEmptyEntries)));
        }
        return s;
    }

    public ScriptResult Run(IScriptHost host, Action<string>? log = null, double maxGameTime = 40 * Units.Day)
    {
        ScriptResult res = new();
        foreach (var r in RunSteps(host, log, maxGameTime)) res = r;
        return res;
    }

    /// <summary>Incremental form: yields the (shared, still-running) result after every host tick; the last yield has Done = true.</summary>
    public IEnumerable<ScriptResult> RunSteps(IScriptHost host, Action<string>? log = null, double maxGameTime = 40 * Units.Day)
    {
        var res = new ScriptResult();
        var w = host.World;
        double t0 = w.T;
        void Log(string m) { res.Log.Add(m); log?.Invoke(m); }
        string State()
        {
            var c = w.Active;
            if (c == null) return $"t={w.T - t0:0.0}";
            var body = w.Sys[c.BodyId];
            var (r, v) = c.StateAt(w.T);
            var conic = c.Mode == CraftMode.Rails ? c.Rails.Conic : Conic.FromState(body.Gm, r, v, w.T);
            string apo = conic.IsEllipse ? $"{(conic.Apoapsis - body.Radius) / 1000:0}" : "esc";
            string tgt = "";
            if (c.TargetCraft >= 0 && w.Find(c.TargetCraft) is { Destroyed: false } tc)
            {
                var port = tc.Parts.FirstOrDefault(p => !p.Destroyed && p.Def.Has(PartFlags.DockingPort) && !p.Fired);
                var myPort = c.Parts.FirstOrDefault(p => !p.Destroyed && p.Def.Has(PartFlags.DockingPort) && !p.Fired);
                var mine = myPort != null ? Docking.Face(c, myPort).pos : c.Pos;
                if (port != null) { var (posB, dirB) = Docking.Face(tc, port); double along = Vec2d.Dot(mine - posB, dirB); double lateral = ((mine - posB) - dirB * along).Length; tgt = $" tgt={tc.Mode} d={(w.AbsolutePosition(tc) - w.AbsolutePosition(c)).Length:0.0}m along={along:0.0} lat={lateral:0.0} vrel={(tc.StateAt(w.T).v - c.Vel).Length:0.00} tspin={MathD.Deg(tc.AngVel):0.0}°/s"; }
                else tgt = $" tgt={tc.Mode} d={(w.AbsolutePosition(tc) - w.AbsolutePosition(c)).Length:0.0}m";
            }
            return $"t={w.T - t0:0.0} {c.Name} {c.Mode} alt={r.Length - body.Radius:0} spd={(v - Flight.SurfaceVelocity(body, r)).Length:0} pitch={ScriptVars.Pitch(c):0} thr={c.Throttle:0.00} stage={c.NextStage} fuel={c.Fuel:0.00}{tgt} apo={apo}km peri={(conic.Periapsis - body.Radius) / 1000:0}km";
        }
        double? holdPitch = null; bool holdingLeft = false, holdingRight = false, holdTarget = false, approach = false;
        string holdMode = ""; double? descent = null;
        var transHeld = new Dictionary<string, bool> { ["trans_fore"] = false, ["trans_aft"] = false, ["trans_left"] = false, ["trans_right"] = false };
        void SetTrans(string action, bool down) { if (transHeld[action] != down) { host.SetHeld(action, down); transHeld[action] = down; } }
        void StopApproach() { foreach (var k in transHeld.Keys.ToList()) SetTrans(k, false); }
        void Tick()
        {
            if (holdPitch is { } target && w.Active is { Mode: CraftMode.Active or CraftMode.Rails } ac)
            {
                if (holdMode.Length > 0)
                {
                    var body = w.Sys[ac.BodyId];
                    var vs = ac.Vel - Flight.SurfaceVelocity(body, ac.Pos);
                    double up = ac.Pos.Angle;
                    if (holdMode == "up" || vs.Length < 2) target = 0;
                    else
                    {
                        var vRef = holdMode.EndsWith("_orbital") ? ac.Vel : vs;
                        var dir = holdMode.StartsWith("retrograde") ? -vRef : vRef;
                        target = -MathD.Deg(MathD.WrapPi(dir.Angle - up));
                    }
                }
                if (holdTarget)
                {
                    // Point the nose at the target's docking port (or its centre).
                    var tc = ac.TargetCraft >= 0 ? w.Find(ac.TargetCraft) : null;
                    if (tc != null && !tc.Destroyed && tc.BodyId == ac.BodyId)
                    {
                        var port = tc.Parts.FirstOrDefault(p => !p.Destroyed && p.Def.Has(PartFlags.DockingPort) && !p.Fired);
                        Vec2d dir;
                        if (port != null)
                        {
                            // Far away: point at the port. Near the docking axis gate: line the nose up with the port's axis so the faces meet square.
                            var (posB, dirB) = Docking.Face(tc, port);
                            var gate = posB + dirB * 2.5;
                            dir = (gate - ac.Pos).Length < 20 ? -dirB : gate - ac.Pos;
                        }
                        else { var (tr, _) = tc.StateAt(w.T); dir = tr - ac.Pos; }   // a target still on rails has a stale Pos
                        double up = ac.Pos.Angle;
                        target = -MathD.Deg(MathD.WrapPi(dir.Angle - up));
                    }
                }
                // Bang-bang attitude hold with a 1° deadband, expressed as ordinary rotate presses (test helper; not a player aid).
                // It leads the target by the angle the craft still needs to stop (ω²/2α), so a heavy craft with a small core does not overshoot.
                double wheelTorque = ac.Parts.Where(p => !p.Destroyed).Sum(p => p.Def.Torque);
                double alpha = 3 * wheelTorque / Math.Max(1e-6, ac.Moi * 1000);   // release damping: 3× wheel torque, Moi in t·m²; gimbal/fin-steered craft (no wheels) get no lead
                double stopDeg = wheelTorque > 0 ? MathD.Clamp(MathD.Deg(ac.AngVel * Math.Abs(ac.AngVel) / (2 * alpha)), -90, 90) : 0;
                double err = target - (ScriptVars.Pitch(ac) - stopDeg);
                bool wantRight = err > 1.0, wantLeft = err < -1.0;
                if (wantRight != holdingRight) { host.SetHeld("rotate_right", wantRight); holdingRight = wantRight; }
                if (wantLeft != holdingLeft) { host.SetHeld("rotate_left", wantLeft); holdingLeft = wantLeft; }
            }
            if (descent is { } rate && w.Active is { Mode: CraftMode.Active } dc)
            {
                // Vertical-speed hold by bang-bang throttle (test helper): full throttle when sinking faster than asked, off when slower.
                var body = w.Sys[dc.BodyId];
                var vs = dc.Vel - Flight.SurfaceVelocity(body, dc.Pos);
                double vsr = Vec2d.Dot(vs, dc.Pos.Normalized());
                host.SetThrottle(vsr < rate - 0.3 ? 1 : vsr > rate + 0.3 ? 0 : dc.Throttle);
            }
            if (approach && w.Active is { } apd && (apd.TargetCraft < 0 || w.Find(apd.TargetCraft) is null or { Destroyed: true })) { approach = false; StopApproach(); }
            if (approach && w.Active is { Mode: CraftMode.Rails }) w.WakeActive();   // the approach is a control input: it takes the chaser off the rails
            if (approach && w.Active is { Mode: CraftMode.Active } ap && ap.TargetCraft >= 0 && w.Find(ap.TargetCraft) is { Destroyed: false } tg && tg.BodyId == ap.BodyId)
            {
                // Docking approach helper (test tool): null the relative velocity and close on the target port at a distance-scaled speed.
                var port = tg.Parts.FirstOrDefault(p => !p.Destroyed && p.Def.Has(PartFlags.DockingPort) && !p.Fired);
                var myPort = ap.Parts.FirstOrDefault(p => !p.Destroyed && p.Def.Has(PartFlags.DockingPort) && !p.Fired);
                var mine = myPort != null ? Docking.Face(ap, myPort).pos : ap.Pos;
                Vec2d aim;
                if (port != null)
                {
                    // Fly to a gate 2.5 m out along the port's axis first, then close the last stretch along that axis.
                    var (posB, dirB) = Docking.Face(tg, port);
                    var gate = posB + dirB * 2.5;
                    double along = Vec2d.Dot(mine - posB, dirB);
                    double lateral = ((mine - posB) - dirB * along).Length;
                    bool atGate = (mine - gate).Length < 0.35 || (along < 2.5 && along > 0 && lateral < 0.15);
                    aim = atGate ? posB : gate;
                }
                else aim = tg.StateAt(w.T).r;
                var rel = aim - mine;
                double dist = rel.Length;
                var want = dist > 0.05 ? rel / dist * Math.Min(0.25 + dist * 0.04, 6.0) : Vec2d.Zero;
                var vRel = tg.StateAt(w.T).v - ap.Vel;
                var errV = want + vRel;   // desired own-velocity change relative to the target
                double fore = Vec2d.Dot(errV, ap.NoseDir), side = Vec2d.Dot(errV, ap.RightDir);
                SetTrans("trans_fore", fore > 0.04); SetTrans("trans_aft", fore < -0.04);
                SetTrans("trans_right", side > 0.04); SetTrans("trans_left", side < -0.04);
            }
            host.Tick(); res.Ticks++;
        }
        static Part? FindPart(Craft c, string spec)
        {
            var parts = spec.Split(':');   // defId[:n] — '#' would start a comment
            int n = parts.Length > 1 ? int.Parse(parts[1]) : 1;
            int k = 0;
            foreach (var p in c.Parts) if (!p.Destroyed && p.Def.Id == parts[0] && ++k == n) return p;
            return null;
        }
        void StopHold()
        {
            holdPitch = null;
            if (holdingRight) { host.SetHeld("rotate_right", false); holdingRight = false; }
            if (holdingLeft) { host.SetHeld("rotate_left", false); holdingLeft = false; }
        }
        // Conditions never throw out of the iterator: a bad expression fails the statement instead.
        bool Cond(string[] words, int from, int to, int line, out bool error)
        {
            error = false;
            string expr = string.Join(' ', words, from, to - from);
            try { return ScriptExpr.Eval(expr, w, t0) != 0; }
            catch (Exception e) { error = true; res.Failures.Add($"line {line}: {e.Message}"); return false; }
        }
        double Num(string s) => double.Parse(s, CultureInfo.InvariantCulture);

        bool finished = false;
        foreach (var (line, words) in Lines)
        {
            if (w.T - t0 > maxGameTime) { res.Failures.Add($"line {line}: script exceeded {maxGameTime} s of game time"); res.Aborted = true; break; }
            string op = words[0].ToLowerInvariant();
            string err = "";
            switch (op)
            {
                case "launch":
                {
                    // launch <design> [name...] [pad <Body>]
                    int padAt = Array.IndexOf(words, "pad");
                    string lname = padAt > 2 ? string.Join(' ', words.Skip(2).Take(padAt - 2)) : padAt == 2 ? words[1] : words.Length > 2 ? string.Join(' ', words.Skip(2)) : words[1];
                    try
                    {
                        if (padAt > 0)
                        {
                            var pb = w.Sys.Bodies.FirstOrDefault(b => b.Name.Equals(words[padAt + 1], StringComparison.OrdinalIgnoreCase)) ?? throw new ArgumentException($"no body {words[padAt + 1]}");
                            host.LaunchAt(words[1], lname, pb.Id);
                        }
                        else host.Launch(words[1], lname);
                        Log($"launch {words[1]}: {State()}");
                    }
                    catch (Exception e) { err = e.Message; }
                    break;
                }
                case "spawn":
                {
                    // spawn <design> <name> orbit <alt|match> [phase <deg>] [fuel <fraction>] [cargo <res> <t>]...
                    double alt = 0, phase = 0, fuel = 1;
                    var cargo = new List<(Resource r, double t)>();
                    for (int i = 3; i + 1 < words.Length; i += 2)
                    {
                        if (words[i] == "orbit") alt = words[i + 1] == "match" ? -1 : Num(words[i + 1]);
                        else if (words[i] == "phase") phase = Num(words[i + 1]);
                        else if (words[i] == "fuel") fuel = Num(words[i + 1]);
                        else if (words[i] == "cargo" && i + 2 < words.Length) { cargo.Add((Enum.Parse<Resource>(words[i + 1], true), Num(words[i + 2]))); i++; }
                    }
                    try
                    {
                        host.Spawn(words[1], words[2], alt, phase, fuel);
                        var sc = w.Crafts.First(x => x.Name.Equals(words[2], StringComparison.OrdinalIgnoreCase) && !x.Destroyed);
                        foreach (var (cr, ct) in cargo) { double left = ct; foreach (var p in sc.Parts) { if (left <= 0) break; double room = p.Room(cr); if (room <= 0) continue; double put = Math.Min(room, left); p.Set(cr, p.Get(cr) + put); left -= put; } }
                        sc.UpdateMass();
                        Log($"spawn {words[2]}: {State()}");
                    }
                    catch (Exception e) { err = e.Message; }
                    break;
                }
                case "active":
                {
                    string want = string.Join(' ', words.Skip(1));
                    var ac2 = w.Crafts.FirstOrDefault(x => x.Name.Equals(want, StringComparison.OrdinalIgnoreCase) && !x.Destroyed);
                    if (ac2 == null) err = $"no craft named {want}"; else w.ActiveCraftId = ac2.Id;
                    break;
                }
                case "target":
                {
                    string want = string.Join(' ', words.Skip(1));
                    var tc = w.Crafts.FirstOrDefault(x => x.Name.Equals(want, StringComparison.OrdinalIgnoreCase) && !x.Destroyed);
                    var tb = w.Sys.Bodies.FirstOrDefault(x => x.Name.Equals(want, StringComparison.OrdinalIgnoreCase));
                    if (w.Active == null) err = "no active craft";
                    else if (tc != null) { w.Active.TargetCraft = tc.Id; w.Active.TargetBody = -1; }
                    else if (tb != null) { w.Active.TargetBody = tb.Id; w.Active.TargetCraft = -1; }
                    else err = $"no craft or body named {want}";
                    break;
                }
                case "accept":
                {
                    var k = w.FindContract(words[1]);
                    if (k == null) err = $"no contract '{words[1]}' (offers: {string.Join(", ", w.Offers.Select(o => o.Title))})";
                    else if (!w.Accept(k, words.Length > 2 && words[2] == "advance")) err = $"accept refused for {k.Title} ({k.State}, {w.AcceptedContracts.Count()} accepted)";
                    else Log($"accepted {k.Title}: {State()}");
                    break;
                }
                case "tutorial": { w.TutorialStep = int.Parse(words[1]); w.RefreshBoard(); break; }   // test set-up: jump to lesson n
                case "storm":
                {
                    // storm now [warnDays] [durationDays] [sure]: schedule a storm (test set-up); "sure" makes every exposed standard core fry.
                    double warn = words.Length > 2 ? Num(words[2]) : 1, dur = words.Length > 3 ? Num(words[3]) : 1;
                    if (words.Contains("sure")) w.StormFryChance = 1.0;
                    w.ScheduleStorm(warn, dur);
                    Log($"storm scheduled: {State()}");
                    break;
                }
                case "contract":
                {
                    // contract debris <n>: offer a debris-cleanup contract for the active craft's body (test set-up).
                    if (words[1] == "debris" && w.Active != null)
                    {
                        var k = new Contract { Id = 9000 + w.Contracts.Count, Type = ContractType.DebrisCleanup, Count = int.Parse(words[2]), TargetBody = w.Active.BodyId, Client = "Orbital Assurance Co.", Title = $"Clean up {words[2]} debris objects at {w.Sys[w.Active.BodyId].Name}", Objective = "Deorbit them.", Reward = 6_000_000, Advance = 1_200_000, Penalty = 600_000, DurationDays = 30, OfferedAt = w.T };
                        w.Contracts.Add(k);
                        Log($"offered {k.Title} (#{k.Id})");
                    }
                    else err = "contract: unknown kind";
                    break;
                }
                case "decline": { var k = w.FindContract(words[1]); if (k == null) err = $"no contract '{words[1]}'"; else w.Decline(k); break; }
                case "sell":
                {
                    if (w.Active == null) { err = "no active craft"; break; }
                    long rev = w.SellFrom(w.Active, Enum.Parse<Resource>(words[1], true), Num(words[2]));
                    if (rev <= 0) err = "sale refused (no market node here, or a low-orbit node without a Depot Controller)";
                    else Log($"sold {words[2]} t of {words[1]} for {Units.FormatMoney(rev)}: {State()}");
                    break;
                }
                case "periapsis":
                {
                    // periapsis <m> [tolerance m] [timeout s]: prograde/retrograde burns (retrograde lowers it, prograde raises it — on an approach the
                    // relative velocity is mostly tangential, so this is the cheap correction) until the periapsis is within tolerance. Test helper.
                    double want = Num(words[1]), tol = words.Length > 2 ? Num(words[2]) : 2000, tmo = words.Length > 3 ? Num(words[3]) : 600;
                    double deadline = w.T + tmo; bool okp = false; string mode = "";
                    while (w.T < deadline)
                    {
                        var ac = w.Active; if (ac == null || ac.Destroyed) break;
                        double peri = ScriptVars.Get("peri", w, t0);
                        double perr0 = peri - want;
                        if (Math.Abs(perr0) <= tol) { okp = true; break; }
                        string need = perr0 > 0 ? "retrograde" : "prograde";
                        if (need != mode) { mode = need; holdTarget = false; holdMode = need + "_orbital"; holdPitch = 0; host.SetThrottle(0); }
                        // Aim along the body-centred inertial velocity (far out, the surface-relative one is all rotation); throttle only when lined up,
                        // and gently for the last stretch (a small Δv moves the periapsis a long way from far out).
                        var (rr, vv) = ac.StateAt(w.T);
                        var wantDir = mode == "retrograde" ? -vv : vv;
                        double perr = Math.Abs(MathD.WrapPi(wantDir.Angle - ac.NoseDir.Angle));
                        double thr = perr > MathD.Rad(6) ? 0 : Math.Abs(perr0) > 40 * tol ? 1 : 0.2;
                        host.SetThrottle(thr);
                        Tick(); yield return res;
                    }
                    host.SetThrottle(0); StopHold();
                    if (!okp) res.Failures.Add($"line {line}: periapsis {want} not reached ({State()})");
                    Log($"periapsis {want} → {(okp ? "ok" : "FAIL")}: {State()}");
                    break;
                }
                case "undock":
                {
                    var ac = w.Active;
                    var port = ac?.Parts.FirstOrDefault(p => !p.Destroyed && p.Fired && p.Def.Has(PartFlags.DockingPort | PartFlags.Claw));
                    if (ac == null || port == null) err = "nothing docked";
                    else w.Undock(ac, port.Id);
                    break;
                }
                case "pump":
                {
                    // pump <defId[:n]> <defId[:n]> <resource>
                    var ac = w.Active;
                    if (ac == null) { err = "no active craft"; break; }
                    var src = FindPart(ac, words[1]); var dst = FindPart(ac, words[2]);
                    if (src == null || dst == null) { err = $"pump: part not found ({words[1]} / {words[2]})"; break; }
                    var resource = Enum.Parse<Resource>(words[3], true);
                    if (w.StartPump(ac, src.Id, dst.Id, resource) == null) err = "pump refused";
                    break;
                }
                case "approach": approach = words[1] != "off"; if (!approach) StopApproach(); break;
                case "despawn":   // test set-up: remove a craft from the world (the career model's "flown home off-screen")
                {
                    var dc = w.Crafts.FirstOrDefault(x => x.Name == words[1] && !x.Destroyed);
                    if (dc == null) err = $"no craft named {words[1]}"; else { dc.Destroyed = true; if (w.ActiveCraftId == dc.Id) w.ActiveCraftId = w.Crafts.FirstOrDefault(x => !x.Destroyed && !x.IsDebris)?.Id ?? -1; }
                    break;
                }
                case "press": try { host.SetHeld(words[1], true); } catch (Exception e) { err = e.Message; } break;
                case "release": try { host.SetHeld(words[1], false); } catch (Exception e) { err = e.Message; } break;
                case "tap": try { host.Tap(words[1]); } catch (Exception e) { err = e.Message; } break;
                case "stage": host.Tap("stage"); break;
                case "throttle": host.SetThrottle(Num(words[1])); break;
                case "warp":
                {
                    int n = int.Parse(words[1]);
                    host.Tap("warp_reset");
                    for (int i = 0; i < n; i++) host.Tap("warp_up");
                    break;
                }
                case "hold":
                {
                    double secs = Num(words[2]);
                    try { host.SetHeld(words[1], true); } catch (Exception e) { err = e.Message; break; }
                    double end = w.T + secs;
                    while (w.T < end - 1e-9) { Tick(); yield return res; }
                    host.SetHeld(words[1], false);
                    break;
                }
                case "wait":
                {
                    double end = w.T + Num(words[1]);
                    int guard = 0;
                    while (w.T < end - 1e-9 && guard++ < 10_000_000) { Tick(); yield return res; }
                    break;
                }
                case "until":
                {
                    int to = words.Length; double timeout = 600;
                    for (int i = 1; i < words.Length; i++) if (words[i] == "timeout") { timeout = Num(words[i + 1]); to = i; break; }
                    double deadline = w.T + timeout;
                    bool ok = false, bad = false;
                    int guard = 0;
                    while (w.T < deadline && guard++ < 50_000_000)
                    {
                        if (Cond(words, 1, to, line, out bad)) { ok = true; break; }
                        if (bad) break;
                        Tick(); yield return res;
                        if (w.Active == null || w.Active.Destroyed) break;
                    }
                    if (!ok && !bad) res.Failures.Add($"line {line}: 'until {string.Join(' ', words, 1, to - 1)}' timed out after {timeout} s ({State()})");
                    Log($"until {string.Join(' ', words, 1, to - 1)} → {(ok ? "ok" : "TIMEOUT")}: {State()}");
                    break;
                }
                case "expect":
                {
                    bool ok = Cond(words, 1, words.Length, line, out bool bad);
                    if (!ok && !bad) res.Failures.Add($"line {line}: expect {string.Join(' ', words, 1, words.Length - 1)} failed ({State()})");
                    Log($"expect {string.Join(' ', words, 1, words.Length - 1)} → {(ok ? "PASS" : "FAIL")}: {State()}");
                    break;
                }
                case "attitude":
                    holdTarget = false; holdMode = "";
                    if (words[1] == "off") StopHold();
                    else if (words[1] == "target") { holdTarget = true; holdPitch = 0; }
                    else if (words[1] is "retrograde" or "prograde" or "up") { holdMode = words[1]; holdPitch = 0; }
                    else holdPitch = Num(words[2]);
                    break;
                case "descent":
                    if (words[1] == "off") { descent = null; host.SetThrottle(0); }
                    else descent = Num(words[1]);
                    break;
                case "flyback": if (!w.StartFlashback()) err = "no booster to fly back"; break;
                case "skip_booster": w.SkipBooster(); break;
                case "recover": if (w.Active == null || !w.Recover(w.Active)) err = "cannot recover here"; break;
                case "droneship":
                {
                    // droneship <downrange m>: park a drone ship that far along the rotation from the pad, at sea (nearest ocean sample).
                    var home = w.Sys.Home;
                    double ang = home.LaunchSiteAngle + (home.RotationPeriod < 0 ? -1 : 1) * Num(words[1]) / home.Radius;
                    int guard = 0;
                    while (!home.IsOceanAt(ang) && guard++ < 100000) ang += (home.RotationPeriod < 0 ? -1 : 1) * 100.0 / home.Radius;
                    w.DroneShips.Add((home.Id, MathD.WrapAngle(ang)));
                    Log($"droneship at {(ScriptVars.PadDistanceOfAngle(w, ang)):0} m: {State()}");
                    break;
                }
                case "log": Log($"{string.Join(' ', words, 1, words.Length - 1)}: {State()}"); break;
                case "view": host.View(words[1], Num(words[2])); break;
                case "ui": host.Ui(words[1]); yield return res; break;
                case "shot": host.Shot(words.Length > 1 ? words[1] : $"line{line}"); Log($"shot {(words.Length > 1 ? words[1] : "")}: {State()}"); yield return res; break;   // yield so a rendering host captures before the next command
                case "end": finished = true; break;
                default: err = $"unknown command '{op}'"; break;
            }
            if (err.Length > 0) { res.Failures.Add($"line {line}: {err}"); res.Aborted = true; finished = true; }
            if (finished) break;
        }
        StopHold(); StopApproach();
        res.EndTime = w.T - t0;
        res.Done = true;
        Log($"end: {State()} ticks={res.Ticks} {(res.Passed ? "PASS" : "FAIL")}");
        yield return res;
    }
}

/// <summary>Readouts a script can test: the same numbers the HUD shows.</summary>
public static class ScriptVars
{
    static readonly HashSet<string> Known = new()
    {
        "t", "time", "warp", "crafts", "alt", "agl", "speed", "ospeed", "vspeed", "hspeed", "apo", "peri", "ecc", "period", "throttle", "stage", "stages",
        "fuel", "rcsfuel", "mass", "pitch", "angvel", "landed", "water", "rails", "active", "alive", "body", "parts", "temp", "rho", "q", "chutes", "engines",
        "dvspent", "dv", "cash", "target_dist", "target_speed", "docked", "rcs", "pumped", "capturing", "target_ca_dist", "target_ca_time",
        "target_port_free", "held", "flashback", "flashback_offered", "impact_dist", "pad_dist", "refund", "on_ship", "legs", "fuelfrac",
        "tutorial_step", "contracts_active", "contracts_done", "contracts_failed", "offers", "progress", "t_peri", "t_apo", "local_angle", "in_ice", "ice_dist", "target_phase", "next_body", "explored",
        "store_methalox", "store_rcs", "store_xenon", "store_water", "store_ice", "store_co2", "store_ore", "store_metal", "scanned", "power_gen", "power_need", "outposts", "charge", "scooped",
        "pads", "pool_metal", "pool_methalox", "pool_water", "market",
        "storm", "storm_eta", "fried", "safe", "in_shadow", "debris", "objects", "docked_debris",
    };

    public static double PadDistanceOfAngle(World w, double localAngle)
    {
        var body = w.Sys.Home;
        double d = MathD.WrapPi(localAngle - body.LaunchSiteAngle) * body.Radius;
        return body.RotationPeriod < 0 ? -d : d;
    }

    /// <summary>Signed surface distance (m) from the point below the craft to the home launch site, positive downrange (with the rotation).</summary>
    public static double PadDistance(World w, Craft c, Vec2d r)
    {
        var body = w.Sys[c.BodyId];
        if (double.IsNaN(body.LaunchSiteAngle)) return double.NaN;
        double local = r.Angle - body.SurfaceAngle(w.T);
        double d = MathD.WrapPi(local - body.LaunchSiteAngle) * body.Radius;
        return body.RotationPeriod < 0 ? -d : d;   // clockwise bodies: downrange is at smaller angles
    }

    /// <summary>Where the current ballistic arc (no drag) meets the surface, as a signed surface distance from the pad, positive downrange.</summary>
    public static double ImpactDistance(World w, Craft c, Vec2d r, Vec2d v)
    {
        var body = w.Sys[c.BodyId];
        if (double.IsNaN(body.LaunchSiteAngle)) return double.NaN;
        var conic = Conic.FromState(body.Gm, r, v, w.T);
        double ground = body.Radius + Math.Max(body.TerrainHeightLocal(body.LaunchSiteAngle), 0);
        double nuZ = conic.TrueAnomalyAtRadius(ground);
        if (double.IsNaN(nuZ)) return double.PositiveInfinity;
        double t = conic.TimeAtTrueAnomaly(-nuZ, w.T);
        if (conic.IsEllipse && t > w.T + conic.Period) t -= conic.Period;
        var hit = conic.PositionAt(t);
        double local = hit.Angle - body.SurfaceAngle(t);
        double d = MathD.WrapPi(local - body.LaunchSiteAngle) * body.Radius;
        return body.RotationPeriod < 0 ? -d : d;
    }

    public static double Pitch(Craft c)
    {
        double up = c.Pos.Angle;
        // Positive = nose leaning clockwise from local up, which is prograde/east on a clockwise-rotating body.
        return -MathD.Deg(MathD.WrapPi(c.Angle + Math.PI / 2 - up));
    }

    public static double Get(string name, World w, double t0)
    {
        var c = w.Active;
        switch (name)
        {
            case "t": return w.T - t0;
            case "time": return w.T;
            case "warp": return w.EffectiveWarp;
            case "crafts": return w.Crafts.Count(x => !x.Destroyed && x.Mode != CraftMode.Held);
            case "held": return w.HeldBoosters.Count;
            case "flashback": return w.InFlashback ? 1 : 0;
            case "flashback_offered": return w.FlashbackOffered ? 1 : 0;
            case "refund": return w.LastRefund / 100.0;
            case "tutorial_step": return w.TutorialStep;
            case "contracts_active": return w.AcceptedContracts.Count();
            case "contracts_done": return w.Contracts.Count(k => k.State == ContractState.Completed);
            case "contracts_failed": return w.Contracts.Count(k => k.State == ContractState.Failed);
            case "offers": return w.Offers.Count();
            case "progress": return w.AcceptedContracts.Select(k => k.Progress).DefaultIfEmpty(0).Max();
        }
        if (!Known.Contains(name)) throw new ArgumentException($"unknown variable '{name}'");
        if (c == null) return double.NaN;
        var body = w.Sys[c.BodyId];
        var (r, v) = c.StateAt(w.T);
        var conic = c.Mode == CraftMode.Rails ? c.Rails.Conic : Conic.FromState(body.Gm, r, v, w.T);
        var vs = v - Flight.SurfaceVelocity(body, r);
        var rhat = r.Normalized();
        switch (name)
        {
            case "alt": return r.Length - body.Radius;
            case "agl": return r.Length - body.SurfaceRadiusLocal(r.Angle - body.SurfaceAngle(w.T));
            case "speed": return vs.Length;
            case "ospeed": return v.Length;
            case "vspeed": return Vec2d.Dot(vs, rhat);
            case "hspeed": return Math.Abs(Vec2d.Cross(rhat, vs));
            case "apo": return conic.IsEllipse ? conic.Apoapsis - body.Radius : double.PositiveInfinity;
            case "peri": return conic.Periapsis - body.Radius;
            case "ecc": return conic.E;
            case "period": return conic.Period;
            case "throttle": return c.Throttle;
            case "stage": return c.NextStage;
            case "stages": return c.Stages.Count;
            case "fuel": return c.Fuel;
            case "rcsfuel": return c.Resource(Resource.Rcs);
            case "mass": return c.Mass;
            case "pitch": return Pitch(c);
            case "angvel": return MathD.Deg(c.AngVel);
            case "landed": return c.Mode == CraftMode.Landed ? 1 : 0;
            case "water": return c.Landed?.Water == true ? 1 : 0;
            case "rails": return c.Mode == CraftMode.Rails ? 1 : 0;
            case "active": return c.Mode == CraftMode.Active ? 1 : 0;
            case "alive": return c.Destroyed ? 0 : 1;
            case "body": return c.BodyId;
            case "parts": return c.Parts.Count(p => !p.Destroyed);
            case "temp": return c.Parts.Where(p => !p.Destroyed).Select(p => p.Temp).DefaultIfEmpty(0).Max();
            case "rho": return body.Atmo?.Density(r.Length - body.Radius) ?? 0;
            case "q": { double rho = body.Atmo?.Density(r.Length - body.Radius) ?? 0; return 0.5 * rho * vs.LengthSq; }
            case "chutes": return c.Parts.Count(p => !p.Destroyed && p.Def.Has(PartFlags.Parachute) && p.Deployed);
            case "engines": return c.Parts.Count(p => !p.Destroyed && p.IsEngine && p.Running && !p.Flameout);
            case "dvspent": return c.DeltaVSpent;
            case "dv": return c.StageReport().Sum(s => s.DeltaVVac);
            case "cash": return w.Cash / 100.0;
            case "target_port_free":
            {
                var tc = c.TargetCraft >= 0 ? w.Find(c.TargetCraft) : null;
                var port = tc?.Parts.FirstOrDefault(p => !p.Destroyed && p.Def.Has(PartFlags.DockingPort));
                return tc != null && port != null && Docking.IsFreePort(tc, port) ? 1 : 0;
            }
            case "target_dist":
            {
                var tc = c.TargetCraft >= 0 ? w.Find(c.TargetCraft) : null;
                if (tc == null || tc.Destroyed) return double.PositiveInfinity;
                return (w.AbsolutePosition(tc) - w.AbsolutePosition(c)).Length;
            }
            case "target_speed":
            {
                var tc = c.TargetCraft >= 0 ? w.Find(c.TargetCraft) : null;
                if (tc == null || tc.Destroyed) return double.PositiveInfinity;
                var (tr, tv) = tc.StateAt(w.T);
                return tc.BodyId == c.BodyId ? (tv - v).Length : double.PositiveInfinity;
            }
            case "docked": return c.Parts.Count(p => !p.Destroyed && p.Def.Has(PartFlags.DockingPort | PartFlags.Claw) && p.Fired) / 2.0;
            case "rcs": return c.RcsOn ? 1 : 0;
            case "pumped": return c.PumpedTotal;
            case "on_ship": return c.Landed?.OnDroneShip == true ? 1 : 0;
            case "legs": return c.LegsDown ? 1 : 0;
            case "fuelfrac": { double cap = 0; foreach (var p in c.Parts) if (!p.Destroyed) cap += p.Capacity(Resource.Methalox); return cap > 0 ? c.Fuel / cap : 0; }
            case "pad_dist": return PadDistance(w, c, r);
            case "impact_dist": return ImpactDistance(w, c, r, v);
            case "capturing": return c.Capture != null ? 1 : 0;
            case "t_peri": { if (!conic.IsEllipse) return conic.Tp - w.T; double P = conic.Period; double tp = (conic.Tp - w.T) % P; if (tp < 0) tp += P; return tp; }
            case "t_apo": { if (!conic.IsEllipse) return double.PositiveInfinity; double P = conic.Period; double tp = (conic.Tp - w.T) % P; if (tp < 0) tp += P; return tp >= P / 2 ? tp - P / 2 : tp + P / 2; }
            case "local_angle": return MathD.Deg(MathD.WrapAngle(r.Angle - body.SurfaceAngle(w.T)));
            case "in_ice": { double la = MathD.WrapAngle(r.Angle - body.SurfaceAngle(w.T)); return body.Deposits.Any(d => d.Kind == Resource.Ice && d.Contains(la)) ? 1 : 0; }
            case "ice_dist":
            {
                // Along-track surface distance (m) to the centre of the nearest ice arc, positive when it lies ahead of the motion.
                double la = MathD.WrapAngle(r.Angle - body.SurfaceAngle(w.T));
                double sign = Vec2d.Cross(r, v) >= 0 ? 1 : -1;
                double best = double.PositiveInfinity;
                foreach (var d in body.Deposits)
                {
                    if (d.Kind != Resource.Ice) continue;
                    double centre = d.ArcStart + 0.5 * MathD.WrapAngle(d.ArcEnd - d.ArcStart);
                    double ahead = MathD.WrapPi(centre - la) * sign * body.Radius;
                    if (Math.Abs(ahead) < Math.Abs(best)) best = ahead;
                }
                return best;
            }
            case "target_phase":
            {
                // Angle (deg, 0..360) from the craft to the target body, measured ahead along the craft's motion, in the current body's frame.
                if (c.TargetBody < 0) return double.NaN;
                var tp = w.Sys.Position(c.TargetBody, w.T) - w.Sys.Position(c.BodyId, w.T);
                double sign = Vec2d.Cross(r, v) >= 0 ? 1 : -1;
                return MathD.Deg(MathD.WrapAngle((tp.Angle - r.Angle) * sign));
            }
            case "next_body": { var pr = w.Predict(c); return pr.Count > 1 ? pr[1].BodyId : -1; }
            case "store_methalox": return c.Resource(Resource.Methalox);
            case "store_rcs": return c.Resource(Resource.Rcs);
            case "store_xenon": return c.Resource(Resource.Xenon);
            case "store_water": return c.Resource(Resource.Water);
            case "store_ice": return c.Resource(Resource.Ice);
            case "store_co2": return c.Resource(Resource.Co2);
            case "store_ore": return c.Resource(Resource.Ore);
            case "store_metal": return c.Resource(Resource.Metal);
            case "scanned": return body.Scanned ? 1 : 0;
            case "power_gen": { var o = w.Outposts().FirstOrDefault(x => x.Members.Contains(c)); return o?.PowerGen ?? w.GenerationKw(c); }
            case "power_need": { var o = w.Outposts().FirstOrDefault(x => x.Members.Contains(c)); return o?.PowerNeed ?? 0; }
            case "outposts": return w.Outposts().Count;
            case "pads": return w.LaunchPads().Count;
            case "pool_metal": return w.Outposts().FirstOrDefault(o => o.Members.Contains(c))?.Stored(Resource.Metal) ?? c.Resource(Resource.Metal);
            case "pool_methalox": return w.Outposts().FirstOrDefault(o => o.Members.Contains(c))?.Stored(Resource.Methalox) ?? c.Resource(Resource.Methalox);
            case "pool_water": return w.Outposts().FirstOrDefault(o => o.Members.Contains(c))?.Stored(Resource.Water) ?? c.Resource(Resource.Water);
            case "market": return w.NodeFor(c) == null ? 0 : (int)w.NodeFor(c)!.Kind + 1;
            case "storm": return (int)w.Storm;
            case "storm_eta": return w.Storm == StormPhase.Warning ? w.StormStart - w.T : w.Storm == StormPhase.Active ? w.StormEnd - w.T : double.PositiveInfinity;
            case "fried": return c.Fried ? 1 : 0;
            case "safe": return w.StormSafe(c) ? 1 : 0;
            case "in_shadow": return w.InShadow(c) ? 1 : 0;
            case "debris": return w.DebrisAt(c.BodyId).Count();
            case "objects": return w.TrackedObjects;
            case "docked_debris": return c.DockedNames.Count;
            case "charge": return c.Charge;
            case "scooped": return c.Scooped;
            case "explored": return (double)(w.Exploration.TryGetValue(c.BodyId, out var ex) ? (int)ex : 0);
            case "target_ca_dist": { var ca = w.ClosestApproach(c); return ca?.dist ?? double.PositiveInfinity; }
            case "target_ca_time": { var ca = w.ClosestApproach(c); return ca?.t ?? double.PositiveInfinity; }
        }
        throw new ArgumentException($"unknown variable '{name}'");
    }
}

/// <summary>Tiny expression language for scripts: numbers, variables, + - * /, comparisons, and/or/not, parentheses.</summary>
public static class ScriptExpr
{
    public static double Eval(string expr, World w, double t0)
    {
        var p = new Parser(expr, w, t0);
        double v = p.Or();
        p.Expect(TokKind.End);
        return v;
    }

    enum TokKind { Num, Ident, Op, LParen, RParen, End }
    sealed class Parser
    {
        readonly List<(TokKind k, string s)> _t = new();
        int _i;
        readonly World _w; readonly double _t0;
        public Parser(string s, World w, double t0)
        {
            _w = w; _t0 = t0;
            int i = 0;
            while (i < s.Length)
            {
                char ch = s[i];
                if (char.IsWhiteSpace(ch)) { i++; continue; }
                if (char.IsDigit(ch) || (ch == '.' && i + 1 < s.Length && char.IsDigit(s[i + 1])))
                {
                    int j = i; while (j < s.Length && (char.IsDigit(s[j]) || s[j] == '.' || s[j] == 'e' || s[j] == 'E' || ((s[j] == '-' || s[j] == '+') && (s[j - 1] == 'e' || s[j - 1] == 'E')))) j++;
                    _t.Add((TokKind.Num, s[i..j])); i = j; continue;
                }
                if (char.IsLetter(ch) || ch == '_') { int j = i; while (j < s.Length && (char.IsLetterOrDigit(s[j]) || s[j] == '_')) j++; _t.Add((TokKind.Ident, s[i..j])); i = j; continue; }
                if (ch == '(') { _t.Add((TokKind.LParen, "(")); i++; continue; }
                if (ch == ')') { _t.Add((TokKind.RParen, ")")); i++; continue; }
                string two = i + 1 < s.Length ? s.Substring(i, 2) : "";
                if (two is "<=" or ">=" or "==" or "!=" or "&&" or "||") { _t.Add((TokKind.Op, two)); i += 2; continue; }
                if ("+-*/<>!".IndexOf(ch) >= 0) { _t.Add((TokKind.Op, ch.ToString())); i++; continue; }
                throw new ArgumentException($"bad character '{ch}' in '{s}'");
            }
            _t.Add((TokKind.End, ""));
        }
        (TokKind k, string s) Peek => _t[_i];
        (TokKind k, string s) Next() => _t[_i++];
        public void Expect(TokKind k) { if (Peek.k != k) throw new ArgumentException($"expected {k} at token {_i} ('{Peek.s}')"); _i++; }
        bool IsOp(string s) => Peek.k == TokKind.Op && Peek.s == s;
        bool IsWord(string s) => Peek.k == TokKind.Ident && Peek.s == s;

        public double Or() { double v = And(); while (IsWord("or") || IsOp("||")) { Next(); double r = And(); v = (v != 0 || r != 0) ? 1 : 0; } return v; }
        double And() { double v = Not(); while (IsWord("and") || IsOp("&&")) { Next(); double r = Not(); v = (v != 0 && r != 0) ? 1 : 0; } return v; }
        double Not() { if (IsWord("not") || IsOp("!")) { Next(); return Not() != 0 ? 0 : 1; } return Cmp(); }
        double Cmp()
        {
            double v = Add();
            while (Peek.k == TokKind.Op && Peek.s is "<" or "<=" or ">" or ">=" or "==" or "!=")
            {
                string op = Next().s; double r = Add();
                v = op switch { "<" => v < r ? 1 : 0, "<=" => v <= r ? 1 : 0, ">" => v > r ? 1 : 0, ">=" => v >= r ? 1 : 0, "==" => v == r ? 1 : 0, _ => v != r ? 1 : 0 };
            }
            return v;
        }
        double Add() { double v = Mul(); while (IsOp("+") || IsOp("-")) { string op = Next().s; double r = Mul(); v = op == "+" ? v + r : v - r; } return v; }
        double Mul() { double v = Unary(); while (IsOp("*") || IsOp("/")) { string op = Next().s; double r = Unary(); v = op == "*" ? v * r : v / r; } return v; }
        double Unary() { if (IsOp("-")) { Next(); return -Unary(); } if (IsOp("+")) { Next(); return Unary(); } return Primary(); }
        double Primary()
        {
            var t = Next();
            switch (t.k)
            {
                case TokKind.Num: return double.Parse(t.s, CultureInfo.InvariantCulture);
                case TokKind.Ident: return t.s == "inf" ? double.PositiveInfinity : ScriptVars.Get(t.s, _w, _t0);
                case TokKind.LParen: { double v = Or(); Expect(TokKind.RParen); return v; }
                default: throw new ArgumentException($"unexpected token '{t.s}'");
            }
        }
    }
}

/// <summary>Headless script host: drives the World directly (the xUnit gate and `tools/Flight`).</summary>
public sealed class SimHost : IScriptHost
{
    public World World { get; }
    public SimHost(World w) { World = w; }

    public static World NewTestWorld(ulong seed = 7)
    {
        var sys = TestSystems.Basic();
        return new World(sys, seed);
    }

    /// <summary>The world every flight script runs in (headless runner, tests and the Godot `--script` mode alike): a Standard career with unlimited cash and every part unlocked, so scripts test flying and contracts, not budgets.</summary>
    public static World NewScriptWorld(ulong seed = 7)
    {
        var w = NewTestWorld(seed);
        w.StartCareer(Preset.Standard, "Perigee Fuel Co.");
        w.Cash = 100_000_000_000;
        foreach (var p in Parts.All) w.Unlocked.Add(p.Id);
        return w;
    }

    /// <summary>Career model: launches and spawns pay their real launch cost (refused when unaffordable) instead of being free test set-ups.</summary>
    public bool ChargeLaunches;

    public void Launch(string design, string name)
    {
        var d = DesignByName(design);
        var home = World.Sys.Home;
        if (ChargeLaunches) { if (World.TryLaunch(d, name, home.Id, home.LaunchSiteAngle) == null) throw new InvalidOperationException($"cannot afford {d.Name} ({Units.FormatMoney(World.LaunchCost(d))} with {Units.FormatMoney(World.Cash)})"); return; }
        World.Launch(d, name, home.Id, home.LaunchSiteAngle);
    }

    public void Spawn(string design, string name, double alt, double phaseDeg, double fuel)
    {
        if (ChargeLaunches) { var d = DesignByName(design); if (d.Parts.Any(p => Parts.Get(p.DefId).Has(PartFlags.ProbeCore)) && !World.TrySpend(World.LaunchCost(d), $"launch of {d.Name}")) throw new InvalidOperationException($"cannot afford {d.Name} ({Units.FormatMoney(World.LaunchCost(d))} with {Units.FormatMoney(World.Cash)})"); }
        SpawnInto(World, design, name, alt, phaseDeg, fuel);
    }

    public static Design DesignByName(string design) => design.ToLowerInvariant() switch
    {
        "sounding" => TestDesigns.Sounding(), "orbital" => TestDesigns.Orbital(), "probe" => TestDesigns.Probe(),
        "station" => TestDesigns.Station(), "tanker" => TestDesigns.Tanker(), "recoverable" => TestDesigns.Recoverable(),
        "comsat" => TestDesigns.Comsat(), "comsat-s" => TestDesigns.ComsatS(), "recoverable-s" => TestDesigns.RecoverableS(), "tanker2" => TestDesigns.Tanker2(), "tankerm" => TestDesigns.TankerM(), "depot" => TestDesigns.Depot(),
        "lander" => TestDesigns.Lander(), "outpost" => TestDesigns.Outpost(), "outpost-t10" => TestDesigns.OutpostT10(), "scanprobe" => TestDesigns.ScanProbe(),
        "padbase" => TestDesigns.PadBase(), "hopper2" => TestDesigns.Hopper2(), "clawtanker" => TestDesigns.ClawTanker(), "junk" => TestDesigns.Junk(),
        _ => File.Exists(design) ? Design.FromJson(File.ReadAllText(design)) : throw new ArgumentException($"unknown design '{design}'")
    };

    /// <summary>Shared by both hosts: a craft on rails in a circular clockwise orbit, nose pointing retrograde (so a chaser from behind sees its port).</summary>
    public static Craft SpawnInto(World w, string design, string name, double alt, double phaseDeg, double fuel)
    {
        var d = DesignByName(design);
        int bodyId = w.Active?.BodyId ?? w.Sys.HomeId;
        var body = w.Sys[bodyId];
        var c = Craft.FromDesign(d, name);
        foreach (var p in c.Parts) if (p.Def.StorageT <= 0) foreach (var r in new[] { Resource.Methalox, Resource.Rcs, Resource.Xenon }) if (p.Capacity(r) > 0) p.Set(r, p.Capacity(r) * fuel);   // propellant tanks only; surface storage starts empty
        c.BodyId = bodyId;
        Vec2d pos, vel;
        if (alt < 0 && w.Active != null)
        {
            // "orbit match": the active craft's own conic, shifted in phase (a rigid rotation of its state about the body keeps the orbit
            // identical, eccentricity included — a circular orbit at the same radius would drift away from a slightly eccentric target).
            var st = w.Active.StateAt(w.T);
            double rot = -MathD.Rad(phaseDeg);   // clockwise orbits: "ahead" is at a smaller angle
            double cs = Math.Cos(rot), sn = Math.Sin(rot);
            pos = new Vec2d(st.r.X * cs - st.r.Y * sn, st.r.X * sn + st.r.Y * cs);
            vel = new Vec2d(st.v.X * cs - st.v.Y * sn, st.v.X * sn + st.v.Y * cs);
        }
        else
        {
            double r0 = body.Radius + alt;
            double theta = (w.Active != null ? w.Active.StateAt(w.T).r.Angle : 0) - MathD.Rad(phaseDeg);
            pos = Vec2d.FromPolar(r0, theta);
            vel = -pos.Perp.Normalized() * Math.Sqrt(body.Gm / r0);
        }
        c.Pos = pos; c.Vel = vel;
        c.Angle = MathD.WrapAngle((-vel).Angle - Math.PI / 2);   // nose retrograde
        c.Mode = CraftMode.Active;
        c.Debris = !c.HasCore;
        c.UpdateMass();
        w.AddCraft(c);
        c.PutOnRails(w.Sys, w.T);
        if (!c.Debris) w.ActiveCraftId = c.Id;
        return c;
    }

    public void SetHeld(string action, bool down)
    {
        ref var i = ref World.Input;
        switch (action)
        {
            case "rotate_left": i.RotateLeft = down; break;
            case "rotate_right": i.RotateRight = down; break;
            case "throttle_up": i.ThrottleUp = down; break;
            case "throttle_down": i.ThrottleDown = down; break;
            case "trans_fore": i.TransFore = down; break;
            case "trans_aft": i.TransAft = down; break;
            case "trans_left": i.TransLeft = down; break;
            case "trans_right": i.TransRight = down; break;
            default: throw new ArgumentException($"unknown held action '{action}'");
        }
    }

    public void Tap(string action)
    {
        var cmd = action switch
        {
            "stage" => Command.Stage, "throttle_full" => Command.ThrottleFull, "throttle_cut" => Command.ThrottleCut, "rcs" => Command.ToggleRcs,
            "legs" => Command.ToggleLegs, "chutes" => Command.DeployChutes, "warp_up" => Command.WarpUp, "warp_down" => Command.WarpDown, "warp_reset" => Command.WarpReset,
            _ => throw new ArgumentException($"unknown tap action '{action}'")
        };
        World.Apply(cmd);
    }

    public void SetThrottle(double v) => World.SetThrottle(v);
    /// <summary>Observer called before every scripted tick (the career model uses it to account play time).</summary>
    public Action<World>? BeforeTick;
    public void Tick() { BeforeTick?.Invoke(World); World.Advance(Units.PhysicsDt * World.EffectiveWarp); }
}
