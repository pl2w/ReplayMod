namespace ReplayMod.Models;

public struct ReplayEvent
{
    public ReplayEventType Type;
    public float DeltaTime;
    public long BodyPos;
    public int BodyRot;
    public int HeadRot;
    public long LeftHandLong;
    public long RightHandLong;
    public int HandSync;
    public short Color;
    public sbyte MaterialIndex;
    public string Name;
}