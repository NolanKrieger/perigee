namespace Perigee.Sim;

/// <summary>Game-time constants (GDD §4). One game day is 6 h; one year ≈ 400 days.</summary>
public static class Units
{
    public const double Day = 6 * 3600.0;        // seconds of game time in one day
    public const double Year = 400 * Day;
    public const double Hour = 3600.0;
    public const double PhysicsDt = 1.0 / 50.0;  // fixed active-physics step
    public const double G0 = 9.80665;            // standard gravity for Isp
    public const double Tonne = 1000.0;

    public static double Days(double seconds) => seconds / Day;
    public static string FormatDate(double t)
    {
        int day = (int)Math.Floor(t / Day);
        double rem = t - day * Day;
        int h = (int)(rem / Hour), m = (int)((rem - h * Hour) / 60), s = (int)(rem % 60);
        return $"Day {day + 1}  {h:00}:{m:00}:{s:00}";
    }
    public static string FormatDuration(double seconds)
    {
        if (seconds < 0) seconds = 0;
        if (seconds < 60) return $"{seconds:0}s";
        if (seconds < 3600) return $"{(int)(seconds / 60)}m {(int)(seconds % 60):00}s";
        if (seconds < Day) return $"{(int)(seconds / 3600)}h {(int)(seconds % 3600 / 60):00}m";
        double d = seconds / Day;
        return d < 100 ? $"{d:0.0}d" : $"{d:0}d";
    }
    public static string FormatDistance(double m)
    {
        double a = Math.Abs(m);
        if (a < 1000) return $"{m:0} m";
        if (a < 1e6) return $"{m / 1e3:0.0} km";
        if (a < 1e9) return $"{m / 1e6:0.00} Mm";
        return $"{m / 1e9:0.000} Gm";
    }
    public static string FormatMoney(long cents)
    {
        double d = cents / 100.0;
        string sign = d < 0 ? "-" : "";
        d = Math.Abs(d);
        if (d >= 1e9) return $"{sign}${d / 1e9:0.00}B";
        if (d >= 1e6) return $"{sign}${d / 1e6:0.00}M";
        if (d >= 1e4) return $"{sign}${d / 1e3:0.0}k";
        return $"{sign}${d:0}";
    }
}
