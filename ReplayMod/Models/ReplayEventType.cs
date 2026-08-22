namespace ReplayMod.Models;

public enum ReplayEventType : byte
{
    Frame = 0,
    ColorChanged = 1,
    MaterialChanged = 2,
    NameChanged = 3,
    PlayerLeft = 4,
}