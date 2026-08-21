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

        var streams = ReplayReader.Load(path);

        foreach (var (actorNumber, events) in streams)
        {
            if (events.Count == 0) continue;

            var ghost = new GhostPlayer
            {
                ActorNumber = actorNumber,
                Events = events,
                AbsoluteTimes = BuildAbsoluteTimes(events),
                Rig = spawnGhostRig(actorNumber),
                NextEventIndex = 0,
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
        
        while (ghost.NextEventIndex < ghost.Events.Count &&
               ghost.AbsoluteTimes[ghost.NextEventIndex] <= ghost.PlaybackClock)
        {
            var e = ghost.Events[ghost.NextEventIndex];
            var eventTime = ghost.AbsoluteTimes[ghost.NextEventIndex];
    
            switch (e.Type)
            {
                case ReplayEventType.Frame:
                    ghost.PreviousFrame = ghost.CurrentFrame;
                    ghost.CurrentFrame = e;
                    ghost.PreviousFrameTime = ghost.CurrentFrameTime;
                    ghost.CurrentFrameTime = eventTime;
                    ghost.FramesSeen++;
                    break;
                case ReplayEventType.ColorChanged:
                    ghost.Rig.bodyRenderer.UpdateColor(BitPackUtils.UnpackColorFromNetwork(e.Color));
                    break;
                case ReplayEventType.MaterialChanged:
                    ghost.Rig.ChangeMaterialLocal(e.MaterialIndex);
                    break;
                case ReplayEventType.NameChanged:
                    ghost.Rig.SetNameTagText(e.Name);
                    break;
            }
    
            ghost.NextEventIndex++;
        }
    
        if (ghost.FramesSeen < 1)
            return;
        
        var peekIndex = ghost.NextEventIndex;
        while (peekIndex < ghost.Events.Count && ghost.Events[peekIndex].Type != ReplayEventType.Frame)
            peekIndex++;
    
        ReplayEvent nextFrame;
        double nextFrameTime;
        if (peekIndex < ghost.Events.Count)
        {
            nextFrame = ghost.Events[peekIndex];
            nextFrameTime = ghost.AbsoluteTimes[peekIndex];
        }
        else
        {
            nextFrame = ghost.CurrentFrame;
            nextFrameTime = ghost.CurrentFrameTime;
        }
    
        if (ghost.FramesSeen < 2 && peekIndex >= ghost.Events.Count)
            return;
    
        var t = nextFrameTime > ghost.CurrentFrameTime
            ? Mathf.Clamp01((float)((ghost.PlaybackClock - ghost.CurrentFrameTime) / (nextFrameTime - ghost.CurrentFrameTime)))
            : 0f;
    
        FrameUnpacker.Unpack(ghost.CurrentFrame,
            out var bodyPosA, out var bodyRotA, out var headRotA,
            out var lHandPosA, out var lHandRotA,
            out var rHandPosA, out var rHandRotA);
    
        FrameUnpacker.Unpack(nextFrame,
            out var bodyPosB, out var bodyRotB, out var headRotB,
            out var lHandPosB, out var lHandRotB,
            out var rHandPosB, out var rHandRotB);
    
        var rig = ghost.Rig;
        
        if (ghost.ActorNumber == 430)
            Debug.Log($"t={t:F3} clock={ghost.PlaybackClock:F3} curT={ghost.CurrentFrameTime:F3} nextT={nextFrameTime:F3} pos={rig.transform.position}");
    
        rig.transform.position = Vector3.Lerp(bodyPosA, bodyPosB, t);
        rig.transform.rotation = Quaternion.Lerp(bodyRotA, bodyRotB, t);
        rig.head.rigTarget.localRotation = Quaternion.Lerp(headRotA, headRotB, t);
        rig.leftHand.rigTarget.localPosition = Vector3.Lerp(lHandPosA, lHandPosB, t);
        rig.leftHand.rigTarget.localRotation = Quaternion.Lerp(lHandRotA, lHandRotB, t);
        rig.rightHand.rigTarget.localPosition = Vector3.Lerp(rHandPosA, rHandPosB, t);
        rig.rightHand.rigTarget.localRotation = Quaternion.Lerp(rHandRotA, rHandRotB, t);
    
        var handSync = nextFrame.HandSync;
        rig.rightIndex.MapOtherFinger(handSync % 10 / 10f, t);
        rig.rightMiddle.MapOtherFinger(handSync % 100 / 100f, t);
        rig.rightThumb.MapOtherFinger(handSync % 1000 / 1000f, t);
        rig.leftIndex.MapOtherFinger(handSync % 10000 / 10000f, t);
        rig.leftMiddle.MapOtherFinger(handSync % 100000 / 100000f, t);
        rig.leftThumb.MapOtherFinger(handSync % 1000000 / 1000000f, t);
    }

    private static double[] BuildAbsoluteTimes(List<ReplayEvent> events)
    {
        var times = new double[events.Count];
        var sum = 0.0;
        for (var i = 0; i < events.Count; i++)
        {
            sum += events[i].DeltaTime;
            times[i] = sum;
        }
        return times;
    }
}