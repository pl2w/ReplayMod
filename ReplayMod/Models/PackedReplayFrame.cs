using System.Runtime.InteropServices;

namespace ReplayMod.Models;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct PackedReplayFrame
{
    public float DeltaTime;
    public long BodyPos;
    public int BodyRot;
    public int HeadRot;
    public long LeftHandLong;
    public long RightHandLong;
    public int HandSync;
}