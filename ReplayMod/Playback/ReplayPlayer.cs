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

        var replay = ReplayReader.Load(path);

        foreach (var (actorNumber, events) in replay.PoseStreams)
        {
            if (events.Count == 0) continue;

            replay.VoiceStreams.TryGetValue(actorNumber, out var voiceChunks);
            voiceChunks ??= [];

            AddGhost(actorNumber, events, voiceChunks, spawnGhostRig);
        }

        foreach (var (actorNumber, voiceChunks) in replay.VoiceStreams)
        {
            if (voiceChunks.Count == 0 || replay.PoseStreams.ContainsKey(actorNumber))
                continue;

            AddGhost(actorNumber, [], voiceChunks, spawnGhostRig);
        }

        _isPlaying = true;
    }

    private void AddGhost(
        int actorNumber,
        List<ReplayEvent> events,
        List<VoiceChunk> voiceChunks,
        System.Func<int, VRRig> spawnGhostRig)
    {
        var rig = spawnGhostRig(actorNumber);
        if (rig == null)
            return;

        var voiceSource = rig.gameObject.AddComponent<AudioSource>();
        voiceSource.playOnAwake = false;
        voiceSource.spatialBlend = 1f;

        var ghost = new GhostPlayer
        {
            ActorNumber = actorNumber,
            Events = events,
            AbsoluteTimes = BuildAbsoluteTimes(events),
            VoiceChunks = voiceChunks,
            VoiceAbsoluteTimes = BuildAbsoluteTimes(voiceChunks),
            Rig = rig,
            VoiceSource = voiceSource,
            NextEventIndex = 0,
            NextVoiceChunkIndex = 0,
            PlaybackClock = 0
        };

        _ghosts.Add(ghost);
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
        AdvanceVoice(ghost);
        
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

    private static void AdvanceVoice(GhostPlayer ghost)
    {
        while (ghost.NextVoiceChunkIndex < ghost.VoiceChunks.Count &&
               ghost.VoiceAbsoluteTimes[ghost.NextVoiceChunkIndex] <= ghost.PlaybackClock)
        {
            var chunk = ghost.VoiceChunks[ghost.NextVoiceChunkIndex];
            var clip = CreateClip(ghost.ActorNumber, ghost.NextVoiceChunkIndex, chunk);
            if (clip != null)
                ghost.VoiceSource.PlayOneShot(clip);

            ghost.NextVoiceChunkIndex++;
        }
    }

    private static AudioClip CreateClip(int actorNumber, int chunkIndex, VoiceChunk chunk)
    {
        if (chunk.PcmData == null || chunk.PcmData.Length == 0 || chunk.Channels <= 0 || chunk.SampleRate <= 0)
            return null;

        var sampleCount = chunk.PcmData.Length / sizeof(short);
        var frameCount = sampleCount / chunk.Channels;
        if (frameCount <= 0)
            return null;

        var samples = new float[sampleCount];
        for (var i = 0; i < sampleCount; i++)
        {
            var offset = i * sizeof(short);
            var value = (short)(chunk.PcmData[offset] | (chunk.PcmData[offset + 1] << 8));
            samples[i] = value / (float)short.MaxValue;
        }

        var clip = AudioClip.Create($"Voice_{actorNumber}_{chunkIndex}", frameCount, chunk.Channels, chunk.SampleRate, false);
        clip.SetData(samples, 0);
        return clip;
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

    private static double[] BuildAbsoluteTimes(List<VoiceChunk> chunks)
    {
        var times = new double[chunks.Count];
        var sum = 0.0;
        for (var i = 0; i < chunks.Count; i++)
        {
            sum += chunks[i].DeltaTime;
            times[i] = sum;
        }
        return times;
    }
}
