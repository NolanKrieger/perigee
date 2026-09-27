using System.Runtime.CompilerServices;

namespace Perigee.Sim;

/// <summary>Double-precision 2D vector. The whole sim runs in doubles; Godot's float32 is only used for drawing relative to a floating origin.</summary>
public readonly record struct Vec2d(double X, double Y)
{
    public static readonly Vec2d Zero = new(0, 0);
    public static readonly Vec2d UnitX = new(1, 0);
    public static readonly Vec2d UnitY = new(0, 1);

    public double Length => Math.Sqrt(X * X + Y * Y);
    public double LengthSq => X * X + Y * Y;
    /// <summary>Angle in radians, counter-clockwise from +X.</summary>
    public double Angle => Math.Atan2(Y, X);

    public Vec2d Normalized() { double l = Length; return l > 0 ? new Vec2d(X / l, Y / l) : Zero; }
    /// <summary>Rotated +90° (counter-clockwise).</summary>
    public Vec2d Perp => new(-Y, X);
    public Vec2d Rotated(double a) { double c = Math.Cos(a), s = Math.Sin(a); return new Vec2d(X * c - Y * s, X * s + Y * c); }
    public static Vec2d FromAngle(double a) => new(Math.Cos(a), Math.Sin(a));
    public static Vec2d FromPolar(double r, double a) => new(r * Math.Cos(a), r * Math.Sin(a));

    public static double Dot(Vec2d a, Vec2d b) => a.X * b.X + a.Y * b.Y;
    /// <summary>2D cross product (z component of a × b).</summary>
    public static double Cross(Vec2d a, Vec2d b) => a.X * b.Y - a.Y * b.X;
    public static double Distance(Vec2d a, Vec2d b) => (a - b).Length;
    public static Vec2d Lerp(Vec2d a, Vec2d b, double t) => a + (b - a) * t;

    public static Vec2d operator +(Vec2d a, Vec2d b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2d operator -(Vec2d a, Vec2d b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec2d operator -(Vec2d a) => new(-a.X, -a.Y);
    public static Vec2d operator *(Vec2d a, double s) => new(a.X * s, a.Y * s);
    public static Vec2d operator *(double s, Vec2d a) => new(a.X * s, a.Y * s);
    public static Vec2d operator /(Vec2d a, double s) => new(a.X / s, a.Y / s);

    public override string ToString() => $"({X:0.###}, {Y:0.###})";
}

public static class MathD
{
    public const double TwoPi = 2 * Math.PI;
    public static double WrapAngle(double a) { a %= TwoPi; if (a < 0) a += TwoPi; return a; }
    /// <summary>Wrap into (-π, π].</summary>
    public static double WrapPi(double a) { a = WrapAngle(a); return a > Math.PI ? a - TwoPi : a; }
    public static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;
    public static double Lerp(double a, double b, double t) => a + (b - a) * t;
    public static double Deg(double rad) => rad * 180 / Math.PI;
    public static double Rad(double deg) => deg * Math.PI / 180;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Sq(double x) => x * x;
}
