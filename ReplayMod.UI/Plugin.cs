using ImGuiNET;
using System.IO;
using System.Linq;
using BepInEx;
using DearImGuiInjection.BepInEx;
using ReplayMod.IO;
using ReplayMod.Playback;

namespace ReplayMod.UI;

[BepInPlugin(PluginInfo.Guid, PluginInfo.Name, PluginInfo.Version)]
[BepInDependency(ReplayMod.PluginInfo.Guid)]
public class Plugin : BaseUnityPlugin
{
    public void Awake()
    {
        DearImGuiInjection.DearImGuiInjection.Render += OnRender;
    }

    public void OnDestroy()
    {
        DearImGuiInjection.DearImGuiInjection.Render -= OnRender;
    }

    private static void OnRender()
    {
        if (!DearImGuiInjection.DearImGuiInjection.IsCursorVisible)
            return;

        var open = true;
        if (ImGui.Begin(PluginInfo.Name, ref open, (int)ImGuiWindowFlags.None))
        {
            var replaySystem = ReplayMod.Plugin.ReplaySystem;
            var replayPlayer = ReplayMod.Plugin.ReplayPlayer;

            var isRecording = replaySystem is { IsRecording: true };

            ImGui.BeginDisabled(isRecording);
            if (ImGui.Button("Start Recording"))
            {
                UnityMainThreadDispatcher.Enqueue(() =>
                {
                    replaySystem?.StartRecording();
                });
            }
            ImGui.EndDisabled();

            ImGui.BeginDisabled(!isRecording);
            if (ImGui.Button("Stop Recording"))
            {
                UnityMainThreadDispatcher.Enqueue(() =>
                {
                    replaySystem?.StopRecording();
                });
            }
            ImGui.EndDisabled();

            if (ImGui.Button("Play Latest"))
            {
                UnityMainThreadDispatcher.Enqueue(PlayLatestReplay);
            }

            if (ImGui.Button("Stop Playback"))
            {
                UnityMainThreadDispatcher.Enqueue(() =>
                {
                    replayPlayer?.Stop();
                });
            }
        }
        ImGui.End();
    }

    private static void PlayLatestReplay()
    {
        var replayPlayer = ReplayMod.Plugin.ReplayPlayer;
        if (replayPlayer == null)
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