using System.IO;
using System.Linq;
using BepInEx;
using HarmonyLib;
using ReplayMod.Core;
using ReplayMod.IO;
using ReplayMod.Logging;
using ReplayMod.Patches;
using ReplayMod.Playback;
using UnityEngine;

namespace ReplayMod;

[BepInPlugin(PluginInfo.Guid, PluginInfo.Name, PluginInfo.Version)]
public class Plugin : BaseUnityPlugin
{
    private Harmony _harmony;
    private ReplaySystem _replaySystem;
    private ReplayPlayer _replayPlayer;

    public void Awake()
    {
        ModLog.Source = Logger;

        _harmony = new Harmony(PluginInfo.Guid);
        _harmony.PatchAll(typeof(VoiceRecorderPatches).Assembly);

        _replaySystem = new ReplaySystem();
        _replayPlayer = gameObject.AddComponent<ReplayPlayer>();
    }

    public void OnGUI()
    {
        GUI.enabled = !_replaySystem.IsRecording;
        if (GUI.Button(new Rect(10, 10, 120, 20), "Start Recording"))
            _replaySystem.StartRecording();

        GUI.enabled = _replaySystem.IsRecording;
        if (GUI.Button(new Rect(10, 40, 120, 20), "Stop Recording"))
            _replaySystem.StopRecording();

        GUI.enabled = true;
        if (GUI.Button(new Rect(10, 70, 120, 20), "Play Latest"))
            PlayLatestReplay();

        if (GUI.Button(new Rect(10, 100, 120, 20), "Stop Playback"))
            _replayPlayer.Stop();
    }

    private void PlayLatestReplay()
    {
        if (!Directory.Exists(ReplayWriter.ReplayFolder))
            return;

        var latest = Directory.GetFiles(ReplayWriter.ReplayFolder, "*.replay")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

        if (latest == null)
            return;
        
        _replayPlayer.Load(latest, SpawnGhostRig);
    }

    private VRRig SpawnGhostRig(int actorNumber)
    {
        return GhostRigFactory.Spawn(actorNumber);
    }
}

public static class PluginInfo
{
    public const string Guid = "xyz.pl2w.replaymod";
    public const string Name = "ReplayMod";
    public const string Version = "1.0.0";
}
