namespace Perigee.Sim;

/// <summary>
/// Deterministic seeded generator (SplitMix64 → xoshiro256**). Its full state is four ulongs, so it serializes into a save and
/// every roll (generator, contracts, storms, debris) replays exactly.
/// </summary>
public sealed class Rng
{
    ulong _s0, _s1, _s2, _s3;

    public Rng(ulong seed) { Seed(seed); }
    public Rng(ulong s0, ulong s1, ulong s2, ulong s3) { _s0 = s0; _s1 = s1; _s2 = s2; _s3 = s3; }

    public void Seed(ulong seed)
    {
        ulong x = seed;
        _s0 = SplitMix(ref x); _s1 = SplitMix(ref x); _s2 = SplitMix(ref x); _s3 = SplitMix(ref x);
        if ((_s0 | _s1 | _s2 | _s3) == 0) _s0 = 1;
    }

    static ulong SplitMix(ref ulong x)
    {
        ulong z = x += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    public (ulong, ulong, ulong, ulong) State => (_s0, _s1, _s2, _s3);

    public ulong NextULong()
    {
        ulong result = RotL(_s1 * 5, 7) * 9;
        ulong t = _s1 << 17;
        _s2 ^= _s0; _s3 ^= _s1; _s1 ^= _s2; _s0 ^= _s3;
        _s2 ^= t;
        _s3 = RotL(_s3, 45);
        return result;
    }

    static ulong RotL(ulong x, int k) => (x << k) | (x >> (64 - k));

    /// <summary>Uniform in [0, 1).</summary>
    public double NextDouble() => (NextULong() >> 11) * (1.0 / 9007199254740992.0);
    public double Range(double lo, double hi) => lo + (hi - lo) * NextDouble();
    /// <summary>Integer in [lo, hi).</summary>
    public int Next(int lo, int hi) => lo + (int)(NextDouble() * (hi - lo));
    public int Next(int n) => Next(0, n);
    public bool Chance(double p) => NextDouble() < p;
    public T Pick<T>(IReadOnlyList<T> list) => list[Next(list.Count)];
    /// <summary>Standard normal via Box–Muller.</summary>
    public double Gaussian()
    {
        double u1 = 1.0 - NextDouble(), u2 = NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(MathD.TwoPi * u2);
    }
    public Rng Fork() => new(NextULong() ^ 0xA5A5A5A5A5A5A5A5UL);
}
