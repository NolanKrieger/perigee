namespace Perigee.Sim;

/// <summary>
/// Active-physics step for one craft (GDD §4): one rigid body, engines, reaction wheels with release damping, RCS, drag with
/// shadowing, fin lift, reentry heat, parachutes, terrain contact and landing. Fixed 50 Hz step in doubles.
/// </summary>
public static class Flight
{
    public const double MaxAngVel = 2.5;        // rad/s hard cap
    public const double TurnRate = 0.9;         // rad/s the wheels and gimbal steer towards while a rotate key is held (≈ 50°/s)
    public const double CoastToRails = 1.0;     // s of clean coasting in vacuum before returning to the rails
    public const double RailsMargin = 500;      // m above the active zone before a coasting craft goes on rails
    public const double LiftoffTilt = 0.35;     // rad: landings within this tilt settle upright (leg springs)
    public const double TopplTilt = 0.6;        // rad: beyond this a touchdown tips the craft over

    static double ThrustMagnitude(Craft c) { double t = 0; foreach (var p in c.Parts) t += p.LastThrust; return t; }

    public static Vec2d SurfaceVelocity(Body b, Vec2d pos) => b.RotationPeriod == 0 ? Vec2d.Zero : pos.Perp * (MathD.TwoPi / b.RotationPeriod);

    public static void Step(World w, Craft c, in InputState input, double dt)
    {
        var sys = w.Sys; var body = sys[c.BodyId]; double t = w.T;
        c.UpdateMass();
        if (c.Mass <= 0 || !c.Parts.Any(p => !p.Destroyed)) { c.Destroyed = true; return; }
        foreach (var p in c.Parts) { p.LastThrust = 0; p.ExposedArea = 0; p.HeatFlux = 0; p.Flameout = false; }

        if (c.Mode == CraftMode.Landed)
        {
            var L = c.Landed!.Value; double rot = body.SurfaceAngle(t);
            c.Angle = L.LocalAngle + rot + L.AngleOffset;
            c.Pos = Vec2d.FromPolar(L.Radius, L.LocalAngle + rot);
            c.Vel = SurfaceVelocity(body, c.Pos);
            c.AngVel = 0;
            double pr0 = body.Atmo?.PressureRatio(L.Radius - body.Radius) ?? 0;
            var (F, _) = Propulsion(w, c, input, dt, pr0, consume: true, q: 0);
            var rhat0 = c.Pos.Normalized();
            double weight = c.Mass * 1000 * body.Gm / c.Pos.LengthSq;
            if (Vec2d.Dot(F, rhat0) > weight)
            {
                c.Mode = CraftMode.Active; c.Landed = null;
                w.Emit(SimEventKind.Launch, c, $"{c.Name} lifted off from {body.Name}");
                if (double.IsNaN(c.LaunchTime)) c.LaunchTime = t;
            }
            Heat.Apply(c, 0, 0, dt);
            return;
        }
        if (c.Mode != CraftMode.Active) return;

        var r = c.Pos; double rl = r.Length; double alt = rl - body.Radius; var rhat = r / rl;
        double rho = body.Atmo?.Density(alt) ?? 0;
        double pr = body.Atmo?.PressureRatio(alt) ?? 0;
        var g = -rhat * (body.Gm / (rl * rl));
        double q0 = rho > 0 ? 0.5 * rho * (c.Vel - SurfaceVelocity(body, c.Pos)).LengthSq : 0;

        var (force, torque) = Propulsion(w, c, input, dt, pr, consume: true, q: q0);
        double vAir = 0;
        if (rho > 0) Aero.Apply(w, c, body, rho, dt, ref force, ref torque, out vAir);
        if (rho > 0) w.Scoop(c, body, rho, vAir, dt);
        w.UpdateBattery(c, dt);
        Heat.Apply(c, rho, vAir, dt);
        w.CheckDestroyed(c);

        double m = c.Mass * 1000;
        c.DeltaVSpent += ThrustMagnitude(c) / m * dt;
        var a = force / m + g;
        c.Vel += a * dt;
        c.Pos += c.Vel * dt;
        double alpha = torque / (c.Moi * 1000);
        c.AngVel = MathD.Clamp(c.AngVel + alpha * dt, -MaxAngVel, MaxAngVel);
        c.Angle = MathD.WrapAngle(c.Angle + c.AngVel * dt);

        Contact(w, c, body, dt);
        if (c.Destroyed || c.Mode != CraftMode.Active) return;

        // Back on rails after a second of clean coasting in vacuum above the active zone.
        bool thrusting = c.Throttle > 0 && c.Parts.Any(p => !p.Destroyed && p.Running && p.LastThrust > 0);
        double alt2 = c.Pos.Length - body.Radius;
        bool spinning = c.HasControl && (Math.Abs(c.AngVel) > 0.02 || input.RotateLeft || input.RotateRight);   // a controlled craft settles first; debris may tumble
        if (rho == 0 && !thrusting && !spinning && !(c.RcsOn && input.AnyTranslate) && alt2 > body.ActiveZoneHeight + RailsMargin && !w.HasLoadedNeighbour(c))
        {
            c.CoastTimer += dt;
            if (c.CoastTimer >= CoastToRails) { if (c.HasControl) c.AngVel = 0; c.PutOnRails(sys, t + dt); }
        }
        else c.CoastTimer = 0;
    }

    /// <summary>Engines, RCS, reaction wheels and gimbal steering. Returns force and torque (world frame, N and N·m) and burns propellant when `consume`.</summary>
    public static (Vec2d force, double torque) Propulsion(World w, Craft c, in InputState input, double dt, double pr, bool consume, double q = 0)
    {
        Vec2d F = Vec2d.Zero; double tau = 0;
        var nose = c.NoseDir;
        double rot = (input.RotateLeft ? 1 : 0) - (input.RotateRight ? 1 : 0);   // +1 = counter-clockwise
        double gimbalAuthority = 0;   // N·m of steering torque the running engines can add
        // Deployed grid fins steer in air (GDD §5: "deployable, for booster return").
        if (q > 0) foreach (var p in c.Parts) if (!p.Destroyed && p.Def.Has(PartFlags.GridFin) && p.Deployed) gimbalAuthority += q * p.Def.FinArea * 1.2 * Math.Max(0.5, Math.Abs(p.Pos.Y - c.Com.Y));
        foreach (var p in c.Parts)
        {
            if (p.Destroyed || !p.IsEngine || !p.Running) continue;
            var ed = p.Def.Engine!;
            double thr = c.Throttle <= 0 ? 0 : Math.Max(ed.MinThrottle, c.Throttle);
            if (thr <= 0) continue;
            double tmax = ed.Thrust(pr);
            if (tmax <= 0) continue;
            double isp = Math.Max(ed.Isp(pr), 1);
            double mdot = tmax * thr / (isp * Units.G0);          // kg/s
            double need = mdot * dt / 1000;                        // t this tick
            var feed = c.Feed(p, ed.Fuel);
            double avail = 0; foreach (var tk in feed) avail += tk.Res[(int)ed.Fuel];
            double frac = avail <= 1e-12 ? 0 : Math.Min(1, avail / need);
            if (ed.PowerKw > 0 && !w.HasPower(c, ed.PowerKw)) frac = 0;
            if (frac <= 0) { p.Flameout = true; continue; }
            if (consume && avail > 0)
            {
                double take = need * frac;
                foreach (var tk in feed) { double share = tk.Res[(int)ed.Fuel] / avail * take; tk.Res[(int)ed.Fuel] = Math.Max(0, tk.Res[(int)ed.Fuel] - share); }
            }
            double thrust = tmax * thr * frac;
            p.LastThrust = thrust;
            var fe = nose * thrust;
            var arm = (p.Pos - c.Com).Rotated(c.Angle);
            F += fe;
            tau += Vec2d.Cross(arm, fe);
            if (ed.Gimbal > 0) gimbalAuthority += thrust * Math.Sin(ed.Gimbal) * Math.Abs(p.Pos.Y - c.Com.Y);
        }

        // RCS: translation at the centre of mass plus rotation from the blocks' lever arms; burns RCS propellant from any tank.
        if (c.RcsOn && (input.AnyTranslate || rot != 0))
        {
            var blocks = c.Parts.Where(p => !p.Destroyed && p.Def.RcsThrust > 0).ToList();
            double prop = c.Resource(Resource.Rcs);
            if (blocks.Count > 0 && prop > 0)
            {
                double total = blocks.Sum(b => b.Def.RcsThrust);
                double used = 0;
                var dir = new Vec2d((input.TransRight ? 1 : 0) - (input.TransLeft ? 1 : 0), (input.TransFore ? 1 : 0) - (input.TransAft ? 1 : 0));
                if (dir.LengthSq > 0)
                {
                    dir = dir.Normalized();
                    double f = total * 0.5;   // half the blocks point the right way
                    F += (c.RightDir * dir.X + nose * dir.Y) * f;
                    used += f;
                }
                if (rot != 0)
                {
                    double tq = 0;
                    foreach (var b in blocks) tq += b.Def.RcsThrust * 0.5 * (b.Pos - c.Com).Length;
                    tau += rot * tq;
                    used += total * 0.5;
                }
                if (consume && used > 0)
                {
                    double need = used / (160 * Units.G0) * dt / 1000;
                    double take = Math.Min(need, prop);
                    foreach (var p in c.Parts)
                    {
                        if (p.Destroyed || p.Res[(int)Resource.Rcs] <= 0) continue;
                        double share = p.Res[(int)Resource.Rcs] / prop * take;
                        p.Res[(int)Resource.Rcs] = Math.Max(0, p.Res[(int)Resource.Rcs] - share);
                    }
                }
            }
        }

        // Reaction wheels + gimbal: steer towards a fixed turn rate while a key is held; on release, damp rotation to a stop
        // (decision #38: no heading hold). Rate-limited so small craft don't whip around and big ones stay predictable.
        double wheel = 0;
        foreach (var p in c.Parts) if (!p.Destroyed && p.Def.Torque > 0) wheel += p.Def.Torque;
        double inertia = c.Moi * 1000;
        if (rot != 0)
        {
            double need = (rot * TurnRate - c.AngVel) * inertia / dt;
            double avail = wheel + gimbalAuthority;
            tau += MathD.Clamp(need, -avail, avail);
        }
        else if (wheel + gimbalAuthority > 0)
        {
            double need = -c.AngVel * inertia / dt;
            double avail = wheel * 3 + gimbalAuthority;
            tau += MathD.Clamp(need, -avail, avail);
        }
        return (F, tau);
    }

    /// <summary>Terrain contact: crash parts above their impact tolerance, otherwise land (fix to the surface in the rotating frame).</summary>
    static void Contact(World w, Craft c, Body body, double dt)
    {
        double t = w.T; double rot = body.SurfaceAngle(t);
        double maxPen = 0; var contacts = new List<Part>();
        var vSurf = SurfaceVelocity(body, c.Pos);
        var vRel = c.Vel - vSurf;
        foreach (var p in c.Parts)
        {
            if (p.Destroyed) continue;
            bool hit = false;
            foreach (var lv in p.LocalOutline())
            {
                var v = c.LocalToWorld(lv);
                double rr = v.Length; double th = v.Angle;
                double ground = body.SurfaceRadiusLocal(th - rot);
                double pen = ground - rr;
                if (pen > 0) { hit = true; if (pen > maxPen) maxPen = pen; }
            }
            if (hit) contacts.Add(p);
        }
        if (contacts.Count == 0) return;
        var rhat = c.Pos.Normalized();
        bool water = body.IsOceanAt(c.Pos.Angle - rot);
        bool onShip = false;
        if (water)
            foreach (var (bodyId, ang) in w.DroneShips)
                if (bodyId == body.Id && Math.Abs(MathD.WrapPi(c.Pos.Angle - rot - ang)) * body.Radius <= World.DroneShipReach) { onShip = true; water = false; break; }
        double speed = vRel.Length;
        bool crashed = false;
        foreach (var p in contacts)
        {
            double tol = p.Def.ImpactTolerance * (water ? 1.6 : 1);
            if (p.Def.Has(PartFlags.Legs) && !p.Deployed) tol *= 0.6;
            if (speed > tol) { w.DestroyPart(c, p, $"{p.Def.Name} broke on impact at {speed:0} m/s"); crashed = true; }
        }
        if (crashed)
        {
            c.Pos += rhat * maxPen;
            double vn = Vec2d.Dot(vRel, rhat);
            if (vn < 0) c.Vel -= rhat * vn * 1.25;
            c.AngVel *= 0.5;
            w.Emit(SimEventKind.Crashed, c, $"{c.Name} hit {body.Name} at {speed:0} m/s");
            return;
        }
        // Touchdown. Tilt is the nose angle away from local up.
        double up = c.Pos.Angle;
        double tilt = MathD.WrapPi(c.Angle + Math.PI / 2 - up);
        if (Math.Abs(tilt) > TopplTilt)
        {
            foreach (var p in contacts) w.DestroyPart(c, p, $"{p.Def.Name} crushed: {c.Name} toppled over");
            c.Pos += rhat * maxPen;
            w.Emit(SimEventKind.Crashed, c, $"{c.Name} toppled over on {body.Name}");
            return;
        }
        if (Math.Abs(tilt) < LiftoffTilt) c.Angle = MathD.WrapAngle(up - Math.PI / 2);   // leg springs settle it upright
        c.UpdateMass();
        // Sit the lowest point exactly on the ground.
        double minY = c.MinLocalY();
        double ground0 = body.SurfaceRadiusLocal(up - rot);
        double radius = ground0 + (c.Com.Y - minY) * Math.Cos(Math.Abs(tilt) < LiftoffTilt ? 0 : tilt);
        if (Math.Abs(tilt) >= LiftoffTilt) radius = c.Pos.Length + maxPen;
        c.Pos = rhat * radius;
        c.Vel = SurfaceVelocity(body, c.Pos);
        c.AngVel = 0;
        c.Landed = new LandedState { LocalAngle = up - rot, Radius = radius, AngleOffset = c.Angle - up, Water = water, OnDroneShip = onShip };
        c.Mode = CraftMode.Landed;
        c.CoastTimer = 0;
        foreach (var p in c.Parts) { p.LastThrust = 0; p.HeatFlux = 0; }   // a frozen landed craft keeps no plume from its last burn
        w.Emit(SimEventKind.Landed, c, $"{c.Name} {(onShip ? "landed on the drone ship" : water ? "splashed down" : "landed")} on {body.Name} at {speed:0.0} m/s");
        if (!c.Npc && !c.Debris) w.RecordExploration(body.Id, Explored.Landed);
    }
}
