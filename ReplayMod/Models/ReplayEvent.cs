namespace ReplayMod.Models;

public struct ReplayEvent
{
    public ReplayEventType Type;
    public float DeltaTime;
    public object Payload;
}
