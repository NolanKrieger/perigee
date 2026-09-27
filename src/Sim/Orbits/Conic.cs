namespace Perigee.Sim;

/// <summary>
/// A 2D Keplerian conic around one body, in that body's inertial frame. Covers ellipse, parabola and hyperbola; both directions
/// (Dir = +1 counter-clockwise/prograde, -1 retrograde). Exact propagation: a coasting craft's state at any time is a function
/// of these six numbers, so time warp cannot drift.
///
/// Conventions: position angle θ = Omega + Dir·ν where ν is the true anomaly; r = P / (1 + E cos ν).
/// </summary>
public readonly struct Conic
{
    public const double ParabolicEps = 1e-9;

    /// <summary>Gravitational parameter GM of the central body (m³/s²).</summary>
    public readonly double Mu;
    /// <summary>Semi-latus rectum (m). Always positive and finite, even for parabolas.</summary>
    public readonly double P;
    /// <summary>Eccentricity.</summary>
    public readonly double E;
    /// <summary>Direction of periapsis (rad, counter-clockwise from +X).</summary>
    public readonly double Omega;
    /// <summary>A time of periapsis passage (s).</summary>
    public readonly double Tp;
    /// <summary>+1 prograde (counter-clockwise), -1 retrograde.</summary>
    public readonly int Dir;

    public Conic(double mu, double p, double e, double omega, double tp, int dir)
    {
        Mu = mu; P = p; E = e; Omega = omega; Tp = tp; Dir = dir >= 0 ? 1 : -1;
    }

    public bool IsEllipse => E < 1 - ParabolicEps;
    public bool IsHyperbola => E > 1 + ParabolicEps;
    public bool IsParabola => !IsEllipse && !IsHyperbola;

    /// <summary>Semi-major axis; negative for hyperbolas, +∞ for parabolas.</summary>
    public double A => IsParabola ? double.PositiveInfinity : P / (1 - E * E);
    public double Periapsis => P / (1 + E);
    public double Apoapsis => IsEllipse ? P / (1 - E) : double.PositiveInfinity;
    public double MeanMotion => IsEllipse ? Math.Sqrt(Mu / (A * A * A)) : IsHyperbola ? Math.Sqrt(Mu / (-A * A * A)) : 2 * Math.Sqrt(Mu / (P * P * P));
    public double Period => IsEllipse ? MathD.TwoPi / MeanMotion : double.PositiveInfinity;
    /// <summary>Specific orbital energy (J/kg): -μ/2a.</summary>
    public double SpecificEnergy => -Mu * (1 - E * E) / (2 * P);
    /// <summary>Signed specific angular momentum (m²/s).</summary>
    public double AngularMomentum => Dir * Math.Sqrt(Mu * P);
    /// <summary>Largest reachable true anomaly (hyperbola asymptote); π for closed orbits.</summary>
    public double MaxTrueAnomaly => IsHyperbola ? Math.Acos(-1 / E) : Math.PI;

    /// <summary>Elements from a state vector at time t. A (near) radial state gets a tiny tangential component so it stays representable.</summary>
    public static Conic FromState(double mu, Vec2d r, Vec2d v, double t)
    {
        double rl = r.Length;
        double h = Vec2d.Cross(r, v);
        double hmin = 1e-7 * Math.Sqrt(mu * rl);
        if (Math.Abs(h) < hmin) h = h >= 0 ? hmin : -hmin;
        int dir = h >= 0 ? 1 : -1;
        double p = h * h / mu;
        var rhat = r / rl;
        var evec = new Vec2d(v.Y * h / mu - rhat.X, -v.X * h / mu - rhat.Y);
        double e = evec.Length;
        double theta = r.Angle;
        double omega = e > 1e-13 ? evec.Angle : theta;
        double nu = MathD.WrapPi(dir * (theta - omega));
        var c = new Conic(mu, p, e, omega, 0, dir);
        double tp = t - c.MeanAnomalyFromTrue(nu) / c.MeanMotion;
        return new Conic(mu, p, e, omega, tp, dir);
    }

    public double RadiusAt(double nu) => P / (1 + E * Math.Cos(nu));

    /// <summary>The outbound (positive) true anomaly at which the orbit crosses radius r, or NaN if it never does.</summary>
    public double TrueAnomalyAtRadius(double r)
    {
        if (E < 1e-15) return Math.Abs(r - P) < 1e-9 * P ? 0 : double.NaN;
        double c = (P / r - 1) / E;
        if (c > 1 || c < -1) return double.NaN;
        return Math.Acos(c);
    }

    public double MeanAnomalyFromTrue(double nu)
    {
        if (IsEllipse)
        {
            double ea = 2 * Math.Atan2(Math.Sqrt(1 - E) * Math.Sin(nu / 2), Math.Sqrt(1 + E) * Math.Cos(nu / 2));
            return ea - E * Math.Sin(ea);
        }
        if (IsHyperbola)
        {
            double k = Math.Sqrt((E - 1) / (E + 1)) * Math.Tan(nu / 2);
            k = MathD.Clamp(k, -1 + 1e-16, 1 - 1e-16);
            double ha = 2 * Math.Atanh(k);
            return E * Math.Sinh(ha) - ha;
        }
        double d = Math.Tan(nu / 2);
        return d + d * d * d / 3;
    }

    public double TrueAnomalyFromMean(double m)
    {
        if (IsEllipse)
        {
            double ea = Kepler.SolveEllipse(m, E);
            return 2 * Math.Atan2(Math.Sqrt(1 + E) * Math.Sin(ea / 2), Math.Sqrt(1 - E) * Math.Cos(ea / 2));
        }
        if (IsHyperbola)
        {
            double ha = Kepler.SolveHyperbola(m, E);
            return 2 * Math.Atan(Math.Sqrt((E + 1) / (E - 1)) * Math.Tanh(ha / 2));
        }
        double d = Kepler.SolveParabola(m);
        return 2 * Math.Atan(d);
    }

    public double TrueAnomalyAt(double t) => TrueAnomalyFromMean(MeanMotion * (t - Tp));

    /// <summary>First time ≥ after at which the craft is at true anomaly ν. Open orbits reach each ν once (the result may be before `after`).</summary>
    public double TimeAtTrueAnomaly(double nu, double after)
    {
        double t = Tp + MeanAnomalyFromTrue(nu) / MeanMotion;
        if (IsEllipse && t < after)
        {
            double per = Period;
            t += Math.Ceiling((after - t) / per) * per;
            if (t < after) t += per;
        }
        return t;
    }

    public (Vec2d r, Vec2d v) StateAtTrueAnomaly(double nu)
    {
        double cn = Math.Cos(nu), sn = Math.Sin(nu);
        double r = P / (1 + E * cn);
        double theta = Omega + Dir * nu;
        var rhat = Vec2d.FromAngle(theta);
        var that = rhat.Perp;
        double k = Math.Sqrt(Mu / P);
        var pos = rhat * r;
        var vel = rhat * (k * E * sn) + that * (Dir * k * (1 + E * cn));
        return (pos, vel);
    }

    public (Vec2d r, Vec2d v) StateAt(double t) => StateAtTrueAnomaly(TrueAnomalyAt(t));
    public Vec2d PositionAt(double t) => StateAtTrueAnomaly(TrueAnomalyAt(t)).r;

    /// <summary>Position angle (about the central body) of a true anomaly.</summary>
    public double ThetaOf(double nu) => Omega + Dir * nu;

    public override string ToString() => $"conic(p={P:0}, e={E:0.######}, ω={MathD.Deg(Omega):0.##}°, dir={Dir}, peri={Periapsis:0}, apo={(IsEllipse ? Apoapsis.ToString("0") : "∞")})";
}

/// <summary>Kepler's equation solvers. Newton with safe starts and a bisection fallback; tolerances at the double-precision floor.</summary>
public static class Kepler
{
    /// <summary>Eccentric anomaly E from mean anomaly M (any value) for 0 ≤ e &lt; 1.</summary>
    public static double SolveEllipse(double m, double e)
    {
        double twoPi = MathD.TwoPi;
        double m0 = m % twoPi; if (m0 < 0) m0 += twoPi;   // [0, 2π)
        double mm = m0 > Math.PI ? m0 - twoPi : m0;       // (-π, π]
        double turns = m - mm;                             // whole turns to add back to E
        double ea = e < 0.8 ? mm + e * Math.Sin(mm) : (mm >= 0 ? Math.PI : -Math.PI);
        if (e < 1e-12) return m;
        for (int i = 0; i < 60; i++)
        {
            double f = ea - e * Math.Sin(ea) - mm;
            double fp = 1 - e * Math.Cos(ea);
            double d = f / fp;
            ea -= d;
            if (Math.Abs(d) < 1e-15) break;
            if (i == 59 || double.IsNaN(ea))
            {
                // Bisection fallback on (-π, π]: g(E) = E - e sin E - M is monotonic.
                double lo = -Math.PI, hi = Math.PI;
                for (int j = 0; j < 200; j++)
                {
                    double mid = 0.5 * (lo + hi);
                    if (mid - e * Math.Sin(mid) - mm < 0) lo = mid; else hi = mid;
                }
                ea = 0.5 * (lo + hi);
            }
        }
        return ea + turns;
    }

    /// <summary>Hyperbolic anomaly H from mean anomaly M for e &gt; 1.</summary>
    public static double SolveHyperbola(double m, double e)
    {
        double ha = m >= 0 ? Math.Log(2 * m / e + 1.8) : -Math.Log(-2 * m / e + 1.8);
        if (double.IsNaN(ha) || double.IsInfinity(ha)) ha = Math.Asinh(m / e);
        for (int i = 0; i < 100; i++)
        {
            double f = e * Math.Sinh(ha) - ha - m;
            double fp = e * Math.Cosh(ha) - 1;
            double d = f / fp;
            // Damp huge steps so the start guess cannot overshoot into overflow.
            if (d > 2) d = 2; else if (d < -2) d = -2;
            ha -= d;
            if (Math.Abs(d) < 1e-15 * Math.Max(1, Math.Abs(ha))) break;
        }
        return ha;
    }

    /// <summary>Barker's equation: D = tan(ν/2) from mean anomaly M = D + D³/3 (closed form).</summary>
    public static double SolveParabola(double m)
    {
        double a = 1.5 * m;
        double b = Math.Cbrt(a + Math.Sqrt(a * a + 1));
        return b - 1 / b;
    }
}
