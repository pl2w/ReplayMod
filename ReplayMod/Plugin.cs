using BepInEx;
using HarmonyLib;
using ReplayMod.Core;
using ReplayMod.Logging;
using ReplayMod.Patches;
using ReplayMod.Playback;

namespace ReplayMod;

[BepInPlugin(PluginInfo.Guid, PluginInfo.Name, PluginInfo.Version)]
public class Plugin : BaseUnityPlugin
{
    public static ReplaySystem ReplaySystem { get; private set; }
    public static ReplayPlayer ReplayPlayer { get; private set; }
    
    private Harmony _harmony;

    public void Awake()
    {
        ModLog.Source = Logger;

        _harmony = new Harmony(PluginInfo.Guid);
        _harmony.PatchAll(typeof(VoiceRecorderPatches).Assembly);

        ReplaySystem = new ReplaySystem();
        ReplayPlayer = gameObject.AddComponent<ReplayPlayer>();
    }
}

public static class PluginInfo
{
    public const string Guid = "xyz.pl2w.replaymod";
    public const string Name = "ReplayMod";
    public const string Version = "1.0.0";
}