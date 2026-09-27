namespace Perigee.Sim;

/// <summary>
/// Held controls sampled once per physics tick (GDD §4 table). Edge actions (stage, RCS toggle, legs, chutes, warp) are
/// separate commands so a key tap is never missed between ticks.
/// </summary>
public struct InputState
{
    public bool RotateLeft, RotateRight;
    public bool ThrottleUp, ThrottleDown;
    public bool TransFore, TransAft, TransLeft, TransRight;
    public static InputState None => default;
    public bool Any => RotateLeft || RotateRight || ThrottleUp || ThrottleDown || TransFore || TransAft || TransLeft || TransRight;
    public bool AnyTranslate => TransFore || TransAft || TransLeft || TransRight;
}

public enum Command { Stage, ThrottleFull, ThrottleCut, ToggleRcs, ToggleLegs, DeployChutes, WarpUp, WarpDown, WarpReset }
