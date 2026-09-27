namespace Perigee.Sim;

/// <summary>Difficulty presets (GDD §15, decision #31). Values are the GDD proposals.</summary>
public sealed record Preset(string Name, long StartingCash, double PartPrices, double Upkeep, double ContractPay, double StormRate, double DebrisRate, double MarketDepth, int GraceDays)
{
    public static readonly Preset Relaxed = new("Relaxed", 150_000_000, 0.75, 0.5, 1.25, 0.5, 0.5, 1.5, 20);
    public static readonly Preset Standard = new("Standard", 80_000_000, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 10);
    public static readonly Preset Brutal = new("Brutal", 40_000_000, 1.3, 1.5, 0.85, 1.5, 2.0, 0.7, 3);
    public static readonly Preset[] All = { Relaxed, Standard, Brutal };
    public static Preset ByName(string n) => All.FirstOrDefault(p => p.Name.Equals(n, StringComparison.OrdinalIgnoreCase)) ?? Standard;
}
