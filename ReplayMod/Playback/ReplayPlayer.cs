using System;
using System.Collections.Generic;
using UnityEngine;
using ReplayMod.IO;
using ReplayMod.Models;

namespace ReplayMod.Playback;

public class ReplayPlayer : MonoBehaviour
{
    private readonly List<GhostPlayer> _ghosts = [];
    private readonly float _playbackSpeed = 1f;

    private static readonly float[] VoiceSampleBuffer = new float[256];

    public bool IsPlaying { get; private set; }
    public float PlaybackTime { get; private set; }
    public float Duration { get; private set; }

    public void Load(string path, Func<int, VRRig> spawnGhostRig)
    {
        Stop();

        Logging.ModLog.Info($"Loading replay from {path}");
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

        Duration = ComputeDuration();
        PlaybackTime = 0;
        IsPlaying = true;
        StartVoices();

        Logging.ModLog.Info($"Playback started with {_ghosts.Count} ghosts, duration={Duration:F3}s");
    }

    private float ComputeDuration()
    {
        var max = 0f;
        foreach (var ghost in _ghosts)
        {
            if (ghost.Events.Count > 0)
                max = Mathf.Max(max, (float)ghost.AbsoluteTimes[^1]);
            if (ghost.VoiceClip)
                max = Mathf.Max(max, ghost.VoiceClip.length);
        }

        return max;
    }

    private void AddGhost(
        int actorNumber,
        List<ReplayEvent> events,
        List<VoiceChunk> voiceChunks,
        Func<int, VRRig> spawnGhostRig)
    {
        var rig = spawnGhostRig(actorNumber);
        if (rig == null)
            return;

        var voiceSource = rig.GetComponent<AudioSource>();
        if (!voiceSource)
        {
            voiceSource = rig.gameObject.AddComponent<AudioSource>();
            voiceSource.playOnAwake = false;
            voiceSource.spatialBlend = 1f;
        }

        rig.remoteUseReplacementVoice = true;
        rig.IsMicEnabled = true;

        var ghost = new GhostPlayer
        {
            ActorNumber = actorNumber,
            Events = events,
            AbsoluteTimes = BuildAbsoluteTimes(events),
            VoiceClip = BuildVoiceClip(actorNumber, voiceChunks),
            Rig = rig,
            VoiceSource = voiceSource
        };

        _ghosts.Add(ghost);
        Logging.ModLog.Info($"Spawned ghost actor={actorNumber} events={events.Count} voice={voiceChunks.Count}");
    }

    public void Stop()
    {
        Logging.ModLog.Info($"Stopping playback ({_ghosts.Count} ghosts)");
        foreach (var ghost in _ghosts)
        {
            ghost.VoiceSource?.Stop();
            if (ghost.VoiceClip)
                Destroy(ghost.VoiceClip);
            GhostRigFactory.Release(ghost.Rig);
        }

        _ghosts.Clear();
        GhostCosmetics.Reset();

        IsPlaying = false;
        PlaybackTime = 0;
        Duration = 0;
    }

    public void Play()
    {
        if (Duration <= 0)
            return;

        if (PlaybackTime >= Duration)
        {
            PlaybackTime = 0;
            foreach (var ghost in _ghosts)
                RecomputeGhost(ghost);
        }

        IsPlaying = true;
        StartVoices();
    }

    public void Pause()
    {
        if (!IsPlaying)
            return;

        IsPlaying = false;
        StopVoices();
    }

    public void TogglePlayback()
    {
        if (IsPlaying)
            Pause();
        else
            Play();
    }

    public void Seek(float t)
    {
        if (Duration <= 0 || _ghosts.Count == 0)
            return;

        PlaybackTime = Mathf.Clamp(t, 0f, Duration);
        foreach (var ghost in _ghosts)
            RecomputeGhost(ghost, replayAudioEvents: false);

        if (IsPlaying)
            StartVoices();
    }

    private void Update()
    {
        if (!IsPlaying)
            return;

        GhostCosmetics.Tick();

        PlaybackTime += Time.deltaTime * _playbackSpeed;
        if (PlaybackTime >= Duration)
        {
            PlaybackTime = Duration;
            IsPlaying = false;
            StopVoices();
        }

        foreach (var ghost in _ghosts)
            AdvanceGhost(ghost);
    }

    private void AdvanceGhost(GhostPlayer ghost)
    {
        ghost.PlaybackClock = PlaybackTime;
        UpdateRigVisibility(ghost);
        UpdateSpeakingLoudness(ghost);

        while (ghost.NextEventIndex < ghost.Events.Count &&
               ghost.AbsoluteTimes[ghost.NextEventIndex] <= ghost.PlaybackClock)
        {
            var e = ghost.Events[ghost.NextEventIndex];
            ApplyEvent(ghost, e, ghost.AbsoluteTimes[ghost.NextEventIndex]);
            ghost.NextEventIndex++;
        }

        ApplyInterpolation(ghost);
        UpdateRigVisibility(ghost);
    }

    private void RecomputeGhost(GhostPlayer ghost, bool replayAudioEvents = true)
    {
        ghost.PlaybackClock = PlaybackTime;
        ghost.NextEventIndex = 0;
        ghost.CurrentFrame = default;
        ghost.CurrentFrameTime = 0;
        ghost.FramesSeen = 0;
        ghost.HasLeft = false;

        while (ghost.NextEventIndex < ghost.Events.Count &&
               ghost.AbsoluteTimes[ghost.NextEventIndex] <= ghost.PlaybackClock)
        {
            var e = ghost.Events[ghost.NextEventIndex];
            ApplyEvent(ghost, e, ghost.AbsoluteTimes[ghost.NextEventIndex], replayAudioEvents);
            ghost.NextEventIndex++;
        }

        UpdateRigVisibility(ghost);
        SeekVoice(ghost);
        ApplyInterpolation(ghost);
        UpdateSpeakingLoudness(ghost);
    }

    private void UpdateRigVisibility(GhostPlayer ghost)
    {
        if (!ghost.Rig)
            return;

        var active = !ghost.HasLeft;

        if (active && ghost.Events.Count > 0)
        {
            if (ghost.PlaybackClock < ghost.AbsoluteTimes[0])
                active = false;
            else if (ghost.NextEventIndex >= ghost.Events.Count)
                active = false;
        }

        if (ghost.Rig.gameObject.activeSelf != active)
            ghost.Rig.gameObject.SetActive(active);
    }

    private static void ApplyEvent(GhostPlayer ghost, ReplayEvent e, double eventTime, bool replayAudioEvents = true)
    {
        switch (e.Type)
        {
            case ReplayEventType.Frame:
                ghost.CurrentFrame = (FrameData)e.Payload;
                ghost.CurrentFrameTime = eventTime;
                ghost.FramesSeen++;
                break;
            case ReplayEventType.ColorChanged:
                ghost.Rig.bodyRenderer.UpdateColor(
                    BitPackUtils.UnpackColorFromNetwork(((ColorChangedData)e.Payload).Color));
                break;
            case ReplayEventType.MaterialChanged:
                var matIndex = ((MaterialChangedData)e.Payload).MaterialIndex;
                Logging.ModLog.Debug($"[mat] play actor={ghost.ActorNumber} apply material={matIndex} at t={eventTime:F3}");
                ghost.Rig.ChangeMaterialLocal(matIndex);
                break;
            case ReplayEventType.NameChanged:
                ghost.Rig.SetNameTagText(((NameChangedData)e.Payload).Name);
                break;
            case ReplayEventType.SoundEffect:
                if (!replayAudioEvents)
                    break;
                var sound = (SoundEffectData)e.Payload;
                ghost.Rig.PlayTagSoundLocal(sound.SoundIndex, sound.Volume, sound.StopCurrentAudio);
                break;
            case ReplayEventType.HandTap:
                if (!replayAudioEvents)
                    break;
                var handTap = (HandTapData)e.Payload;
                Logging.ModLog.Debug(
                    $"[tap] play actor={ghost.ActorNumber} sound={handTap.SoundIndex} vol={handTap.Volume:F2} left={handTap.IsLeftHand} at t={eventTime:F3}");
                ghost.Rig.PlayHandTapLocal(handTap.SoundIndex, handTap.IsLeftHand, handTap.Volume);
                break;
            case ReplayEventType.CosmeticsChanged:
                ApplyCosmetics(ghost, ((CosmeticsData)e.Payload).Cosmetics, eventTime);
                break;
            case ReplayEventType.PlayerLeft:
                ghost.HasLeft = true;
                break;
        }
    }

    private static void ApplyInterpolation(GhostPlayer ghost)
    {
        if (ghost.FramesSeen < 1)
            return;

        var peekIndex = ghost.NextEventIndex;
        while (peekIndex < ghost.Events.Count && ghost.Events[peekIndex].Type != ReplayEventType.Frame)
            peekIndex++;

        FrameData nextFrame;
        double nextFrameTime;
        if (peekIndex < ghost.Events.Count)
        {
            nextFrame = (FrameData)ghost.Events[peekIndex].Payload;
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


    private static void ApplyCosmetics(GhostPlayer ghost, string[] cosmetics, double eventTime)
    {
        if (ghost.Rig == null || cosmetics == null)
            return;

        var worn = 0;
        foreach (var name in cosmetics)
        {
            if (!string.IsNullOrEmpty(name) && name != "NOTHING")
                worn++;
        }

        Logging.ModLog.Debug($"[cos] play actor={ghost.ActorNumber} apply {worn} worn cosmetics at t={eventTime:F3}");
        GhostCosmetics.Apply(ghost.Rig, cosmetics);
    }

    private void StartVoices()
    {
        foreach (var ghost in _ghosts)
        {
            if (!ghost.VoiceClip || !ghost.VoiceSource)
                continue;

            ghost.VoiceSource.clip = ghost.VoiceClip;
            ghost.VoiceSource.time = Mathf.Clamp((float)ghost.PlaybackClock, 0f, ghost.VoiceClip.length);

            if (!ghost.VoiceSource.enabled)
                ghost.VoiceSource.enabled = true;

            if (ghost.VoiceSource.gameObject.activeInHierarchy)
                ghost.VoiceSource.Play();
        }
    }

    private void StopVoices()
    {
        foreach (var ghost in _ghosts)
            ghost.VoiceSource?.Pause();
    }

    private static void SeekVoice(GhostPlayer ghost)
    {
        if (!ghost.VoiceClip || !ghost.VoiceSource)
            return;

        ghost.VoiceSource.time = Mathf.Clamp((float)ghost.PlaybackClock, 0f, ghost.VoiceClip.length);
    }

    private static void UpdateSpeakingLoudness(GhostPlayer ghost)
    {
        if (!ghost.Rig)
            return;

        ghost.Rig.SpeakingLoudness = ComputeLoudness(ghost.VoiceSource);
    }

    private static float ComputeLoudness(AudioSource source)
    {
        if (!source || !source.isPlaying)
            return 0f;

        source.GetOutputData(VoiceSampleBuffer, 0);

        var sum = 0f;
        for (var i = 0; i < VoiceSampleBuffer.Length; i++)
            sum += Mathf.Abs(VoiceSampleBuffer[i]);

        return sum / VoiceSampleBuffer.Length;
    }

    private static AudioClip BuildVoiceClip(int actorNumber, List<VoiceChunk> chunks)
    {
        if (chunks == null || chunks.Count == 0)
            return null;

        var sampleRate = 0;
        var channels = 0;
        foreach (var chunk in chunks)
        {
            if (sampleRate == 0)
            {
                sampleRate = chunk.SampleRate;
                channels = chunk.Channels;
            }
        }

        if (sampleRate <= 0 || channels <= 0)
            return null;

        var elapsed = 0.0;
        var totalFrames = 0;
        for (var i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            var chunkFrames = chunk.PcmData.Length / sizeof(short) / chunk.Channels;
            if (chunkFrames <= 0)
            {
                elapsed += chunk.DeltaTime;
                continue;
            }

            elapsed += chunk.DeltaTime;
            var chunkEndFrame = Mathf.RoundToInt((float)elapsed * sampleRate) + chunkFrames;
            if (chunkEndFrame > totalFrames)
                totalFrames = chunkEndFrame;
        }

        if (totalFrames <= 0)
            return null;

        var output = new float[totalFrames * channels];
        var writeFrame = 0;
        elapsed = 0.0;

        for (var i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            var chunkFrames = chunk.PcmData.Length / sizeof(short) / chunk.Channels;
            if (chunkFrames <= 0)
            {
                elapsed += chunk.DeltaTime;
                continue;
            }

            elapsed += chunk.DeltaTime;
            var chunkStartFrame = Mathf.RoundToInt((float)elapsed * sampleRate);
            if (chunkStartFrame > writeFrame)
                writeFrame = chunkStartFrame;

            var endFrame = Mathf.Min(writeFrame + chunkFrames, totalFrames);
            for (var f = writeFrame; f < endFrame && f < totalFrames; f++)
            {
                for (var c = 0; c < chunk.Channels; c++)
                {
                    var pcmIndex = ((f - writeFrame) * chunk.Channels + c) * sizeof(short);
                    if (pcmIndex + 1 >= chunk.PcmData.Length)
                        break;
                    var value = (short)(chunk.PcmData[pcmIndex] | (chunk.PcmData[pcmIndex + 1] << 8));
                    output[f * channels + c] = value / (float)short.MaxValue;
                }
            }

            writeFrame = endFrame;
        }

        var clip = AudioClip.Create($"Voice_{actorNumber}", totalFrames, channels, sampleRate, false);
        clip.SetData(output, 0);
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

        if (times.Length > 0 && times[0] > 60.0)
        {
            Logging.ModLog.Warn(
                $"Stream starts at t={times[0]:F1}s (clock-sync artifact from an older recording); normalizing to 0");
            var offset = times[0];
            for (var i = 0; i < times.Length; i++)
                times[i] -= offset;
        }

        return times;
    }
}
