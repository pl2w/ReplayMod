using System;
using ReplayMod.IO;

namespace ReplayMod.Core;

public class ReplaySystem : ITickSystemPost
{
    private const double RecordIntervalSeconds = 1.0 / 30.0;
    private double _nextRecordTime;

    public bool IsRecording { get; private set; }
    public bool PostTickRunning { get; set; }

    public void StartRecording()
    {
        if (IsRecording) StopRecording();

        ReplayRecorder.Reset();
        _nextRecordTime = 0;
        TickSystem<object>.AddPostTickCallback(this);
        IsRecording = true;
    }

    public void StopRecording()
    {
        if (!IsRecording) return;

        TickSystem<object>.RemovePostTickCallback(this);
        IsRecording = false;

        if (ReplayRecorder.Buffers.Count == 0) return;

        var fileName = $"replay_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}";
        ReplayWriter.Save(fileName, ReplayRecorder.Buffers);
    }

    public void PostTick()
    {
        if (NetworkSystem.Instance == null || !NetworkSystem.Instance.InRoom)
            return;

        var timestamp = NetworkSystem.Instance.SimTime;
        if (timestamp < _nextRecordTime)
            return;

        _nextRecordTime = timestamp + RecordIntervalSeconds;

        foreach (var player in NetworkSystem.Instance.AllNetPlayers)
        {
            if (!VRRigCache.Instance.TryGetVrrig(player, out var container))
                continue;

            ReplayRecorder.RecordFrame(player.ActorNumber, container.Rig, timestamp);
        }
    }
}