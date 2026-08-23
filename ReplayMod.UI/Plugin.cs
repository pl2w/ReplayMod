using System;
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
    private Rect _windowRect = new(100, 100, 330, 232);
    private bool _uiVisible;
    private bool _scrubbing;
    private bool _prevLeftPrimary;
    private bool _showReplayList;
    private string[] _replayFiles = [];
    private Vector2 _replayScroll;

    public void Update()
    {
        var poller = ControllerInputPoller.instance;
        if (poller == null)
        {
            _prevLeftPrimary = false;
            return;
        }

        var down = poller.leftControllerPrimaryButton;
        if (down && !_prevLeftPrimary && _uiVisible)
        {
            var replaySystem = ReplayMod.Plugin.ReplaySystem;
            if (replaySystem is { IsRecording: true })
                replaySystem.StopRecording();
            else
                replaySystem?.StartRecording();
        }

        _prevLeftPrimary = down;

        if (UnityInput.Current.GetKeyDown(KeyCode.X))
            _uiVisible = !_uiVisible;
    }

    public void OnGUI()
    {
        if (!_uiVisible)
            return;

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
        var inRoom = NetworkSystem.Instance != null && NetworkSystem.Instance.InRoom;

        const float y = 26;

        if (GUI.Button(new Rect(10, y, 84, 24), "Load..."))
        {
            _showReplayList = true;
            RefreshReplayList();
        }

        if (_showReplayList)
        {
            DrawReplayList(y + 28);
            return;
        }

        GUI.enabled = inRoom;
        if (GUI.Button(new Rect(100, y, 84, 24), isRecording ? "Stop Rec" : "Record"))
        {
            if (isRecording)
                replaySystem?.StopRecording();
            else
                replaySystem?.StartRecording();
        }
        GUI.enabled = true;

        if (GUI.Button(new Rect(246, y, 74, 24), "Stop"))
            replayPlayer?.Stop();

        const float y2 = 56;
        GUI.Label(new Rect(10, y2, 86, 20), $"Time: {FmtTime(time)}/{FmtTime(duration)}");

        if (duration > 0f)
        {
            GUI.changed = false;
            var slider = GUI.HorizontalSlider(new Rect(100, y2, 150, 18), time, 0f, duration);

            if (GUI.changed)
            {
                if (!_scrubbing)
                {
                    _scrubbing = true;
                    replayPlayer?.Pause();
                }
                replayPlayer?.Seek(slider);
            }
            else if (_scrubbing)
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

        if (isRecording && replaySystem != null)
        {
            const float y6 = 158;
            var elapsed = (float)(DateTime.UtcNow - replaySystem.RecordingStartTime).TotalSeconds;
            GUI.Box(new Rect(10, y6, 306, 64), "Recording");
            GUI.Label(new Rect(18, y6 + 18, 290, 20),
                $"Room: {replaySystem.RecordingRoomName}");
            GUI.Label(new Rect(18, y6 + 38, 290, 20),
                $"Time: {FmtTime(elapsed)}   Players: {replaySystem.RecordedActorCount}");
            GUI.Label(new Rect(18, y6 + 56, 290, 20),
                $"Events: {replaySystem.TotalPoseEvents}   Voice: {replaySystem.TotalVoiceChunks}");
        }
    }

    private static string FmtTime(float seconds)
    {
        var total = Mathf.Max(0, Mathf.RoundToInt(seconds));
        return $"{total / 60:00}:{total % 60:00}";
    }

    private void RefreshReplayList()
    {
        if (!Directory.Exists(ReplayWriter.ReplayFolder))
        {
            _replayFiles = [];
            return;
        }

        _replayFiles = Directory.GetFiles(ReplayWriter.ReplayFolder, "*.replay")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToArray();
    }

    private void DrawReplayList(float y)
    {
        var height = _windowRect.height - y - 10;
        if (height < 40)
            height = 40;

        GUI.Label(new Rect(10, y, 270, 24), "Select a replay to play");
        if (GUI.Button(new Rect(270, y, 46, 24), "Back"))
        {
            _showReplayList = false;
            return;
        }

        if (_replayFiles.Length == 0)
        {
            GUI.Label(new Rect(10, y + 30, 306, 20), "No replays found.");
            return;
        }

        var rowH = 20;
        var rows = _replayFiles.Length;
        var viewH = rows * rowH;
        _replayScroll = GUI.BeginScrollView(
            new Rect(10, y + 28, 306, height - 30), _replayScroll,
            new Rect(0, 0, 290, viewH));

        for (var i = 0; i < rows; i++)
        {
            var name = Path.GetFileNameWithoutExtension(_replayFiles[i]);
            var rowRect = new Rect(0, i * rowH, 290, rowH);

            if (GUI.Button(rowRect, name))
            {
                _showReplayList = false;
                LoadReplay(_replayFiles[i]);
                break;
            }
        }

        GUI.EndScrollView();
    }

    private static void LoadReplay(string path)
    {
        var replayPlayer = ReplayMod.Plugin.ReplayPlayer;
        if (!replayPlayer)
            return;

        replayPlayer.Load(path, SpawnGhostRig);
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
