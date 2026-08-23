using System;
using System.Collections.Generic;
using System.Linq;
using Photon.Voice;
using ReplayMod.IO;
using ReplayMod.Models;

namespace ReplayMod.Core;

public class ReplaySystem : ITickSystemPost
{
    private const double RecordIntervalSeconds = 1.0 / 30.0;
    private const double MinValidSimTimeSeconds = 60.0;
    private readonly Dictionary<int, ReplayRecorder> _recorders = [];
    private readonly HashSet<int> _recordingActors = [];
    private LocalVoiceCapture _localVoiceCapture;
    private LocalVoiceAudioFloat _localVoice;
    private int _localActor;
    private double _nextRecordTime;

    public bool IsRecording { get; private set; }
    public bool PostTickRunning { get; set; }

    public DateTime RecordingStartTime { get; private set; }
    public string RecordingRoomName { get; private set; }
    public int RecordedActorCount => _recorders.Count;
    public int TotalPoseEvents
    {
        get
        {
            var total = 0;
            foreach (var recorder in _recorders.Values)
                total += recorder.Events.Count;
            return total;
        }
    }

    public int TotalVoiceChunks => VoiceRecorder.TotalChunkCount();

    public void StartRecording()
    {
        var netSystem = NetworkSystem.Instance;
        if (netSystem == null || !netSystem.InRoom)
        {
            Logging.ModLog.Warn("Cannot start recording: not in a room.");
            return;
        }

        if (IsRecording) StopRecording();

        _recorders.Clear();
        VoiceRecorder.Reset();
        _recordingActors.Clear();
        _localActor = 0;
        _nextRecordTime = 0;
        TickSystem<object>.AddPostTickCallback(this);
        IsRecording = true;
        RecordingStartTime = DateTime.UtcNow;
        RecordingRoomName = netSystem.RoomName;

        Logging.ModLog.Info($"Recording started. players={netSystem.AllNetPlayers.Length} room={RecordingRoomName}");

        foreach (var player in netSystem.AllNetPlayers)
        {
            VoiceRecorder.BeginRecording(player.ActorNumber);
            TryBeginRecordingForPlayer(player);
        }

        TryBeginRecordingForLocalPlayer();

        RoomSystem.PlayerJoinedEvent += OnPlayerJoined;
        RoomSystem.PlayerLeftEvent += OnPlayerLeft;
        RoomSystem.LeftRoomEvent += OnLeftRoom;
    }

    public void StopRecording()
    {
        if (!IsRecording) return;

        TickSystem<object>.RemovePostTickCallback(this);
        RoomSystem.PlayerJoinedEvent -= OnPlayerJoined;
        RoomSystem.PlayerLeftEvent -= OnPlayerLeft;
        RoomSystem.LeftRoomEvent -= OnLeftRoom;

        foreach (var (_, recorder) in _recorders)
            recorder.Dispose();

        foreach (var player in NetworkSystem.Instance.AllNetPlayers)
            VoiceRecorder.StopRecording(player.ActorNumber);

        IsRecording = false;
        _recordingActors.Clear();
        _localActor = 0;

        var voiceBuffers = VoiceRecorder.SnapshotBuffers();
        VoiceRecorder.StopAll();

        if (_recorders.Count == 0 && voiceBuffers.Count == 0)
        {
            Logging.ModLog.Warn("Recording stopped but produced no data; nothing saved.");
            return;
        }

        var room = string.IsNullOrEmpty(RecordingRoomName)
            ? "unknown"
            : new string(RecordingRoomName.Where(c => char.IsLetterOrDigit(c)).ToArray());
        if (string.IsNullOrEmpty(room))
            room = "unknown";

        var fileName = $"{room}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}";
        var path = ReplayWriter.Save(fileName, SnapshotPoseStreams(), voiceBuffers);
        Logging.ModLog.Info(
            $"Recording stopped. room={RecordingRoomName} players={_recorders.Count} voice={voiceBuffers.Count} -> {path}");
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

    private void OnLeftRoom()
    {
        if (!IsRecording)
            return;

        Logging.ModLog.Info("Left room while recording; stopping recording.");
        StopRecording();
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

        if (NetworkSystem.Instance.SimTime < MinValidSimTimeSeconds)
        {
            Logging.ModLog.Debug($"actor={player.ActorNumber} SimTime not synced yet ({NetworkSystem.Instance.SimTime:F3}); deferring");
            return;
        }

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

    private void TryBeginRecordingForLocalPlayer()
    {
        var netSystem = NetworkSystem.Instance;
        if (!netSystem || !netSystem.InRoom || netSystem.SimTime < MinValidSimTimeSeconds)
            return;

        var netPlayer = netSystem.LocalPlayer;
        if (netPlayer == null)
            return;

        if (_recorders.ContainsKey(netPlayer.ActorNumber))
            return;

        var rig = GorillaTagger.Instance ? GorillaTagger.Instance.offlineVRRig : null;
        if (!rig)
            return;

        _localActor = netPlayer.ActorNumber;
        _recorders[_localActor] = new ReplayRecorder(_localActor, rig, netSystem.SimTime);
        VoiceRecorder.BeginRecording(_localActor);
        Logging.ModLog.Info($"Started recording LOCAL actor={_localActor}");
    }

    private void AttachLocalVoiceCapture()
    {
        if (_localActor == 0)
            return;

        try
        {
            var recorder = NetworkSystem.Instance?.LocalRecorder;
            if (recorder == null)
            {
                Logging.ModLog.Debug("local recorder not available; own voice capture deferred");
                return;
            }

            if (recorder.Voice is not LocalVoiceAudioFloat voice)
            {
                Logging.ModLog.Debug($"local voice type unsupported ({recorder.Voice?.GetType().Name}); own voice will not be recorded");
                _localVoice = null;
                return;
            }

            if (_localVoice == voice && _localVoiceCapture != null)
            {
                _localVoiceCapture.ActorNumber = _localActor;
                return;
            }

            var info = voice.Info;
            var capture = new LocalVoiceCapture(_localActor, info.SamplingRate, info.Channels);
            voice.AddPostProcessor(capture);
            _localVoiceCapture = capture;
            _localVoice = voice;
            Logging.ModLog.Info(
                $"attached local voice capture actor={_localActor} ({info.SamplingRate}Hz/{info.Channels}ch)");
        }
        catch (Exception e)
        {
            Logging.ModLog.Warn($"failed to attach local voice capture: {e}");
        }
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

        TryBeginRecordingForLocalPlayer();
        if (_localActor != 0 && _recorders.TryGetValue(_localActor, out var localRecorder))
            localRecorder.RecordFrame(timestamp);

        AttachLocalVoiceCapture();
    }
}
