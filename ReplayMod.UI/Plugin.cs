using System.IO;
using System.Linq;
using BepInEx;
using ReplayMod.IO;
using ReplayMod.Playback;
using UnityEngine;

namespace ReplayMod.UI;

[BepInPlugin(PluginInfo.Guid, PluginInfo.Name, PluginInfo.Version)]
[BepInDependency(ReplayMod.PluginInfo.Guid)]
public class Plugin : BaseUnityPlugin
{
    public void OnGUI()
    {
        var replaySystem = ReplayMod.Plugin.ReplaySystem;
        var replayPlayer = ReplayMod.Plugin.ReplayPlayer;

        var isRecording = replaySystem is { IsRecording: true };

        GUI.enabled = !isRecording;
        if (GUI.Button(new Rect(10, 10, 120, 20), "Start Recording"))
            replaySystem?.StartRecording();

        GUI.enabled = isRecording;
        if (GUI.Button(new Rect(10, 40, 120, 20), "Stop Recording"))
            replaySystem?.StopRecording();

        GUI.enabled = true;
        if (GUI.Button(new Rect(10, 70, 120, 20), "Play Latest"))
            PlayLatestReplay();

        if (GUI.Button(new Rect(10, 100, 120, 20), "Stop Playback"))
            replayPlayer?.Stop();
    }

    private static void PlayLatestReplay()
    {
        var replayPlayer = ReplayMod.Plugin.ReplayPlayer;
        if (!replayPlayer)
            return;

        if (!Directory.Exists(ReplayWriter.ReplayFolder))
            return;

        var latest = Directory.GetFiles(ReplayWriter.ReplayFolder, "*.replay")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

        if (latest == null)
            return;

        replayPlayer.Load(latest, SpawnGhostRig);
    }

    private static VRRig SpawnGhostRig(int actorNumber)
    {
        return GhostRigFactory.Spawn(actorNumber);
    }
}

public static class PluginInfo
{
    public const string Guid = "xyz.pl2w.replaymod.ui";
    public const string Name = "ReplayMod.UI";
    public const string Version = "1.0.0";
}