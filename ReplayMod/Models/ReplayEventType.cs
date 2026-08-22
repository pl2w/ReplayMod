namespace ReplayMod.Models;

public enum ReplayEventType : byte
{
    Frame = 0,
    ColorChanged = 1,
    MaterialChanged = 2,
    NameChanged = 3,
    PlayerLeft = 4,
    SoundEffect = 5,
    HandTap = 6,
    CosmeticsChanged = 7
}