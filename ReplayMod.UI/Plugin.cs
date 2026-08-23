using System.Globalization;
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
    private const int WindowId = 707001;
    private Rect _windowRect = new(100, 100, 330, 180);
    private bool _scrubbing;

    public void OnGUI()
    {
        _windowRect = GUI.Window(WindowId, _windowRect, DrawWindow, "Replay Playback");
    }

    private void DrawWindow(int id)
    {
        GUI.DragWindow(new Rect(0, 0, _windowRect.width, 20));

        var replayPlayer = ReplayMod.Plugin.ReplayPlayer;
        var replaySystem = ReplayMod.Plugin.ReplaySystem;
        var isRecording = replaySystem is { IsRecording: true };

        var duration = replayPlayer ? replayPlayer.Duration : 0f;
        var time = replayPlayer ? replayPlayer.PlaybackTime : 0f;
        var playing = replayPlayer && replayPlayer.IsPlaying;

        const float y = 26;

        if (GUI.Button(new Rect(10, y, 84, 24), "Load..."))
            PlayLatestReplay();

        if (GUI.Button(new Rect(100, y, 84, 24), isRecording ? "Stop Rec" : "Record"))
        {
            if (isRecording)
                replaySystem?.StopRecording();
            else
                replaySystem?.StartRecording();
        }

        if (GUI.Button(new Rect(246, y, 74, 24), "Stop"))
            replayPlayer?.Stop();

        const float y2 = 56;
        GUI.Label(new Rect(10, y2, 86, 20), $"Time: {FmtTime(time)}/{FmtTime(duration)}");

        if (duration > 0f)
        {
            var slider = GUI.HorizontalSlider(new Rect(100, y2, 150, 18), time, 0f, duration);

            if (!Mathf.Approximately(slider, time))
            {
                if (!_scrubbing)
                {
                    _scrubbing = true;
                    replayPlayer?.Pause();
                }
                replayPlayer?.Seek(slider);
            }
            else
            {
                _scrubbing = false;
            }
        }

        GUI.Label(new Rect(256, y2, 58, 20),
            $"{(duration > 0f ? time / duration * 100f : 0f):F1} %");

        const float y3 = 82;
        GUI.Box(new Rect(10, y3, 306, 6), GUIContent.none);
        GUI.Box(new Rect(10, y3, 306 * Mathf.Clamp01(duration > 0f ? time / duration : 0f), 6), GUIContent.none);

        if (replayPlayer && duration > 0f)
        {
            const float y4 = 98;
            if (GUI.Button(new Rect(10, y4, 36, 24), "|<")) replayPlayer.Seek(0f);
            if (GUI.Button(new Rect(48, y4, 36, 24), "<<")) replayPlayer.Seek(time - 10f);
            if (GUI.Button(new Rect(86, y4, 36, 24), "<")) replayPlayer.Seek(time - 1f);

            if (GUI.Button(new Rect(124, y4, 76, 24), playing ? "Pause" : "Play"))
                replayPlayer.TogglePlayback();

            if (GUI.Button(new Rect(202, y4, 36, 24), ">")) replayPlayer.Seek(time + 1f);
            if (GUI.Button(new Rect(240, y4, 36, 24), ">>")) replayPlayer.Seek(time + 10f);
            if (GUI.Button(new Rect(278, y4, 36, 24), ">|")) replayPlayer.Seek(duration);
        }

        const float y5 = 132;
        GUI.Label(new Rect(10, y5, 130, 20), $"Tick: {(int)(time * 30f)} / {(int)(duration * 30f)}");
    }

    private static string FmtTime(float seconds)
    {
        var total = Mathf.Max(0, Mathf.RoundToInt(seconds));
        return $"{total / 60:00}:{total % 60:00}";
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
