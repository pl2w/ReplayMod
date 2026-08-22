namespace ReplayMod.Models;

public sealed class FrameData
{
    public long BodyPos;
    public int BodyRot;
    public int HeadRot;
    public long LeftHandLong;
    public long RightHandLong;
    public int HandSync;
}

public sealed class ColorChangedData
{
    public short Color;
}

public sealed class MaterialChangedData
{
    public sbyte MaterialIndex;
}

public sealed class NameChangedData
{
    public string Name;
}

public sealed class SoundEffectData
{
    public int SoundIndex;
    public float Volume;
    public bool StopCurrentAudio;
}

public sealed class HandTapData
{
    public int SoundIndex;
    public float Volume;
    public bool IsLeftHand;
}
