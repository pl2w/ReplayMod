using System;
using System.Collections.Generic;
using ReplayMod.IO;
using ReplayMod.Models;

namespace ReplayMod.Core;

public class ReplaySystem : ITickSystemPost
{
    private const double RecordIntervalSeconds = 1.0 / 30.0;
    private readonly Dictionary<int, ReplayRecorder> _recorders = [];
    private readonly HashSet<int> _recordingActors = [];
    private double _nextRecordTime;

    public bool IsRecording { get; private set; }
    public bool PostTickRunning { get; set; }

    public void StartRecording()
    {
        if (IsRecording) StopRecording();

        _recorders.Clear();
        VoiceRecorder.Reset();
        _recordingActors.Clear();
        _nextRecordTime = 0;
        TickSystem<object>.AddPostTickCallback(this);
        IsRecording = true;

        Logging.ModLog.Info($"Recording started. players={NetworkSystem.Instance.AllNetPlayers.Length}");

        foreach (var player in NetworkSystem.Instance.AllNetPlayers)
        {
            VoiceRecorder.BeginRecording(player.ActorNumber);
            TryBeginRecordingForPlayer(player);
        }

        RoomSystem.PlayerJoinedEvent += OnPlayerJoined;
        RoomSystem.PlayerLeftEvent += OnPlayerLeft;
    }

    public void StopRecording()
    {
        if (!IsRecording) return;

        TickSystem<object>.RemovePostTickCallback(this);
        RoomSystem.PlayerJoinedEvent -= OnPlayerJoined;

        foreach (var (_, recorder) in _recorders)
            recorder.Dispose();

        foreach (var player in NetworkSystem.Instance.AllNetPlayers)
            VoiceRecorder.StopRecording(player.ActorNumber);

        IsRecording = false;
        _recordingActors.Clear();

        var voiceBuffers = VoiceRecorder.SnapshotBuffers();
        VoiceRecorder.StopAll();

        if (_recorders.Count == 0 && voiceBuffers.Count == 0)
        {
            Logging.ModLog.Warn("Recording stopped but produced no data; nothing saved.");
            return;
        }

        var fileName = $"replay_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}";
        var path = ReplayWriter.Save(fileName, SnapshotPoseStreams(), voiceBuffers);
        Logging.ModLog.Info(
            $"Recording stopped. players={_recorders.Count} voice={voiceBuffers.Count} -> {path}");
    }

    private Dictionary<int, List<ReplayEvent>> SnapshotPoseStreams()
    {
        var result = new Dictionary<int, List<ReplayEvent>>(_recorders.Count);
        foreach (var (actorNumber, recorder) in _recorders)
        {
            result[actorNumber] = recorder.Events;
            Logging.ModLog.Info($"actor={actorNumber} recorded {recorder.Events.Count} pose events");
        }
        return result;
    }

    private void OnPlayerJoined(NetPlayer player)
    {
        Logging.ModLog.Info($"Player joined actor={player.ActorNumber}");
        VoiceRecorder.BeginRecording(player.ActorNumber);
        TryBeginRecordingForPlayer(player);
    }
    
    private void OnPlayerLeft(NetPlayer player)
    {
        Logging.ModLog.Info($"Player left actor={player.ActorNumber}");
        if (!_recordingActors.Contains(player.ActorNumber))
            return;

        if (_recorders.TryGetValue(player.ActorNumber, out var recorder))
        {
            recorder.RecordPlayerLeft(NetworkSystem.Instance.SimTime);
            recorder.Dispose();
        }

        VoiceRecorder.StopRecording(player.ActorNumber);
        _recordingActors.Remove(player.ActorNumber);
    }

    private void TryBeginRecordingForPlayer(NetPlayer player)
    {
        if (_recordingActors.Contains(player.ActorNumber))
            return;

        if (!VRRigCache.Instance.TryGetVrrig(player, out var container))
        {
            Logging.ModLog.Debug($"actor={player.ActorNumber} has no VRRig yet; deferring");
            return;
        }

        _recorders[player.ActorNumber] =
            new ReplayRecorder(player.ActorNumber, container.Rig, NetworkSystem.Instance.SimTime);
        VoiceRecorder.BeginRecording(player.ActorNumber, container.Rig);

        _recordingActors.Add(player.ActorNumber);
        Logging.ModLog.Info($"Started recording actor={player.ActorNumber}");
    }

    public void RecordHandTap(int actorNumber, int soundIndex, float volume, bool isLeftHand, double timestamp)
    {
        if (_recorders.TryGetValue(actorNumber, out var recorder))
            recorder.RecordHandTap(soundIndex, volume, isLeftHand, timestamp);
    }

    public void RecordSoundEffect(int actorNumber, int soundIndex, float volume, bool stopCurrentAudio, double timestamp)
    {
        if (_recorders.TryGetValue(actorNumber, out var recorder))
            recorder.RecordSoundEffect(soundIndex, volume, stopCurrentAudio, timestamp);
    }

    public void PostTick()
    {
        if (NetworkSystem.Instance == null || !NetworkSystem.Instance.InRoom)
            return;

        var timestamp = NetworkSystem.Instance.SimTime;

        foreach (var recorder in _recorders.Values)
            recorder.CurrentTimestamp = timestamp;

        if (timestamp < _nextRecordTime)
            return;

        _nextRecordTime = timestamp + RecordIntervalSeconds;

        foreach (var player in NetworkSystem.Instance.AllNetPlayers)
        {
            if (!VRRigCache.Instance.TryGetVrrig(player, out var container))
                continue;

            TryBeginRecordingForPlayer(player);

            if (_recorders.TryGetValue(player.ActorNumber, out var recorder))
                recorder.RecordFrame(timestamp);

            VoiceRecorder.RefreshSources(player.ActorNumber, container.Rig);
        }
    }
}
