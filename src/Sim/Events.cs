namespace Perigee.Sim;

public enum SimEventKind { SoiChange, ActiveZone, WarpStopped, Launch, Staging, Landed, Crashed, PartDestroyed, Docked, Undocked, Recovered, Info }

public readonly record struct SimEvent(SimEventKind Kind, int CraftId, string Text, double Time);
