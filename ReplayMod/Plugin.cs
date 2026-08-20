using BepInEx;

namespace ReplayMod;

[BepInPlugin(PluginInfo.Guid, PluginInfo.Name, PluginInfo.Version)]
public class Plugin : BaseUnityPlugin
{
}

public static class PluginInfo
{
    public const string Guid = "xyz.pl2w.replaymod";
    public const string Name = "ReplayMod";
    public const string Version = "1.0.0";
}