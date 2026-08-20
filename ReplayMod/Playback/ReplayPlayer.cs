using System.Collections.Generic;
using UnityEngine;
using ReplayMod.IO;
using ReplayMod.Models;

namespace ReplayMod.Playback;

public class ReplayPlayer : MonoBehaviour
{
    private readonly List<GhostPlayer> _ghosts = [];
    private readonly float _playbackSpeed = 1f;

    private bool _isPlaying;

    public void Load(string path, System.Func<int, VRRig> spawnGhostRig)
    {
        Stop();

        var buffers = ReplayReader.Load(path);

        foreach (var (actorNumber, frames) in buffers)
        {
            if (frames.Count == 0) continue;

            var ghost = new GhostPlayer
            {
                ActorNumber = actorNumber,
                Frames = frames,
                CumulativeTimes = BuildCumulativeTimes(frames),
                Rig = spawnGhostRig(actorNumber),
                CurrentFrameIndex = 0,
                PlaybackClock = 0
            };

            _ghosts.Add(ghost);
        }

        _isPlaying = true;
    }

    public void Stop()
    {
        foreach (var ghost in _ghosts)
        {
            if (ghost.Rig != null)
                Destroy(ghost.Rig.gameObject);
        }
        _ghosts.Clear();
        _isPlaying = false;
    }

    private void Update()
    {
        if (!_isPlaying) return;

        var dt = Time.deltaTime * _playbackSpeed;

        foreach (var ghost in _ghosts)
            AdvanceGhost(ghost, dt);
    }

    private void AdvanceGhost(GhostPlayer ghost, float dt)
    {
        ghost.PlaybackClock += dt;

        while (ghost.CurrentFrameIndex < ghost.Frames.Count - 1 &&
               ghost.CumulativeTimes[ghost.CurrentFrameIndex + 1] <= ghost.PlaybackClock)
        {
            ghost.CurrentFrameIndex++;
        }

        var i0 = ghost.CurrentFrameIndex;
        var i1 = Mathf.Min(i0 + 1, ghost.Frames.Count - 1);

        var frameA = ghost.Frames[i0];
        var frameB = ghost.Frames[i1];

        var timeA = ghost.CumulativeTimes[i0];
        var timeB = ghost.CumulativeTimes[i1];
        var t = timeB > timeA ? Mathf.Clamp01((float)((ghost.PlaybackClock - timeA) / (timeB - timeA))) : 0f;

        FrameUnpacker.Unpack(frameA,
            out var bodyPosA, out var bodyRotA, out var headRotA,
            out var lHandPosA, out var lHandRotA,
            out var rHandPosA, out var rHandRotA);

        FrameUnpacker.Unpack(frameB,
            out var bodyPosB, out var bodyRotB, out var headRotB,
            out var lHandPosB, out var lHandRotB,
            out var rHandPosB, out var rHandRotB);

        var rig = ghost.Rig;

        rig.transform.position = Vector3.Lerp(bodyPosA, bodyPosB, t);
        rig.transform.rotation = Quaternion.Lerp(bodyRotA, bodyRotB, t);
        rig.head.rigTarget.localRotation = Quaternion.Lerp(headRotA, headRotB, t);
        rig.leftHand.rigTarget.localPosition = Vector3.Lerp(lHandPosA, lHandPosB, t);
        rig.leftHand.rigTarget.localRotation = Quaternion.Lerp(lHandRotA, lHandRotB, t);
        rig.rightHand.rigTarget.localPosition = Vector3.Lerp(rHandPosA, rHandPosB, t);
        rig.rightHand.rigTarget.localRotation = Quaternion.Lerp(rHandRotA, rHandRotB, t);

        var handSync = frameA.HandSync;
        rig.rightIndex.MapOtherFinger(handSync % 10 / 10f, 1f);
        rig.rightMiddle.MapOtherFinger(handSync % 100 / 100f, 1f);
        rig.rightThumb.MapOtherFinger(handSync % 1000 / 1000f, 1f);
        rig.leftIndex.MapOtherFinger(handSync % 10000 / 10000f, 1f);
        rig.leftMiddle.MapOtherFinger(handSync % 100000 / 100000f, 1f);
        rig.leftThumb.MapOtherFinger(handSync % 1000000 / 1000000f, 1f);
    }

    private double[] BuildCumulativeTimes(List<PackedReplayFrame> frames)
    {
        var times = new double[frames.Count];
        var sum = 0.0;
        for (var i = 0; i < frames.Count; i++)
        {
            sum += frames[i].DeltaTime;
            times[i] = sum;
        }
        return times;
    }
}