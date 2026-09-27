namespace Perigee.Sim;

/// <summary>Pronounceable names from syllable tables (GDD §12), unique within a system.</summary>
public sealed class NameGen
{
    static readonly string[] Starts = { "Ka", "Ve", "Lo", "Tor", "Mi", "Sa", "El", "Or", "Na", "Ry", "Ju", "Pel", "Ar", "Ko", "Dra", "Ith", "Oma", "Ul", "Ban", "Cer", "Ny", "Hal", "Zo", "Tem", "Vir", "Ash", "Bel", "Qua", "Ren", "Sol" };
    static readonly string[] Mids = { "ra", "li", "no", "ve", "tu", "sa", "mi", "re", "ko", "da", "phi", "tha", "le", "ru", "ni", "va", "de", "lo", "ma", "ti" };
    static readonly string[] Ends = { "n", "s", "th", "ra", "m", "x", "l", "ne", "ss", "r", "k", "ia", "os", "a", "d", "t", "ir", "us", "on", "e" };
    readonly Rng _rng;
    readonly HashSet<string> _used = new();
    public NameGen(Rng rng) { _rng = rng; }

    public string Next(int minSyllables = 2, int maxSyllables = 3)
    {
        for (int tries = 0; tries < 1000; tries++)
        {
            int n = _rng.Next(minSyllables, maxSyllables + 1);
            string s = _rng.Pick(Starts);
            for (int i = 1; i < n - 1; i++) s += _rng.Pick(Mids);
            s += _rng.Pick(Ends);
            if (s.Length > 10) continue;
            if (_used.Add(s)) return s;
        }
        return "Body" + _used.Count;
    }
}
