using System.Collections.Generic;
using Photon.Voice;
using Photon.Voice.Unity;
using ReplayMod.Models;
using UnityEngine;

namespace ReplayMod.Core;

public static class VoiceRecorder
{
    private const int StoredSampleRate = 16000;
    private const int StoredChannels = 1;
    private const float SilenceThreshold = 0.001f;

    private static readonly Dictionary<int, List<VoiceChunk>> Buffers = new();

    private static readonly Dictionary<int, double> LastTimestamps = new();
    private static readonly HashSet<int> ActiveActors = new();
    private static readonly Dictionary<Speaker, VoiceFormat> SpeakerFormats = new();
    private static readonly Dictionary<Speaker, int> SpeakerActorNumbers = new();
    private static readonly object MappingLock = new();
    private static double _startDspTime;
    private static bool _isRecording;

    public static void Reset()
    {
        StopAll();
        lock (Buffers) { Buffers.Clear(); }
        LastTimestamps.Clear();
        lock (MappingLock)
        {
            ActiveActors.Clear();
            SpeakerFormats.Clear();
            SpeakerActorNumbers.Clear();
        }
        _startDspTime = AudioSettings.dspTime;
        _isRecording = true;
    }

    public static void BeginRecording(int actorNumber, VRRig rig)
    {
        BeginRecording(actorNumber);
        RefreshSources(actorNumber, rig);
    }

    public static void BeginRecording(int actorNumber)
    {
        EnsureBuffer(actorNumber, 0);
        lock (MappingLock)
        {
            ActiveActors.Add(actorNumber);
        }
    }

    public static void RefreshSources(int actorNumber, VRRig rig)
    {
        EnsureBuffer(actorNumber, 0);
        lock (MappingLock)
        {
            ActiveActors.Add(actorNumber);
        }

        MapRigSpeakersToActors(actorNumber, rig);
    }

    private static Speaker MapRigSpeakersToActors(int actorNumber, VRRig rig)
    {
        if (rig == null)
            return null;

        var container = rig.rigContainer != null ? rig.rigContainer : rig.GetComponentInParent<RigContainer>();
        if (container == null || container.Voice == null || container.Voice.SpeakerInUse == null)
            return null;

        var speaker = container.Voice.SpeakerInUse;
        lock (MappingLock)
        {
            if (!SpeakerActorNumbers.ContainsKey(speaker))
                Debug.Log($"[ReplayMod] Voice: mapped rig speaker to actor {actorNumber} (speaker={speaker.name})");

            SpeakerActorNumbers[speaker] = actorNumber;
        }

        return speaker;
    }

    public static void StopRecording(int actorNumber)
    {
        lock (MappingLock)
        {
            ActiveActors.Remove(actorNumber);
        }
        RemoveSpeakerForActor(actorNumber);
    }

    private static void RemoveSpeakerForActor(int actorNumber)
    {
        var toRemove = new List<Speaker>();
        lock (MappingLock)
        {
            foreach (var (speaker, mappedActor) in SpeakerActorNumbers)
            {
                if (mappedActor == actorNumber)
                    toRemove.Add(speaker);
            }

            foreach (var speaker in toRemove)
                SpeakerActorNumbers.Remove(speaker);
        }
    }

    public static void StopAll()
    {
        lock (MappingLock)
        {
            ActiveActors.Clear();
            SpeakerFormats.Clear();
            SpeakerActorNumbers.Clear();
        }
        _isRecording = false;
    }

    public static Dictionary<int, List<VoiceChunk>> SnapshotBuffers()
    {
        lock (Buffers)
        {
            var snapshot = new Dictionary<int, List<VoiceChunk>>(Buffers.Count);
            foreach (var (actorNumber, chunks) in Buffers)
                snapshot[actorNumber] = [..chunks];

            return snapshot;
        }
    }

    public static void RecordPhotonFrame(Speaker speaker, FrameOut<float> frame)
    {
        if (!_isRecording || speaker == null || frame == null || frame.EndOfStream)
            return;

        var actorNumber = GetGameActorNumber(speaker);
        if (actorNumber <= 0 || !IsActiveActor(actorNumber))
            return;

        var (sampleRate, channels) = GetFrameFormat(speaker);
        RecordSamplesAtCurrentTime(actorNumber, frame.Buf, channels, sampleRate);
    }

    private static bool IsActiveActor(int actorNumber)
    {
        lock (MappingLock)
        {
            return ActiveActors.Contains(actorNumber);
        }
    }

    private static int GetGameActorNumber(Speaker speaker)
    {
        lock (MappingLock)
        {
            if (SpeakerActorNumbers.TryGetValue(speaker, out var mapped))
                return mapped;
        }

        return -1;
    }

    public static void SetSpeakerFormat(Speaker speaker, int sampleRate, int channels)
    {
        if (speaker == null || sampleRate <= 0 || channels <= 0)
            return;

        lock (MappingLock)
        {
            SpeakerFormats[speaker] = new VoiceFormat(sampleRate, channels);
        }
    }

    private static (int SampleRate, int Channels) GetFrameFormat(Speaker speaker)
    {
        lock (MappingLock)
        {
            if (SpeakerFormats.TryGetValue(speaker, out var format))
                return (format.SampleRate, format.Channels);
        }

        return (16000, 1);
    }

    private static void RecordSamplesAtCurrentTime(int actorNumber, float[] samples, int channels, int sampleRate)
    {
        if (samples == null || samples.Length == 0 || channels <= 0 || sampleRate <= 0)
            return;

        var storedSamples = ResampleToStoredFormat(samples, channels, sampleRate);
        if (storedSamples.Length == 0 || IsSilent(storedSamples))
            return;

        var timestamp = AudioSettings.dspTime - _startDspTime;
        EnsureBuffer(actorNumber, timestamp);

        var pcm = new byte[storedSamples.Length * sizeof(short)];
        for (var i = 0; i < storedSamples.Length; i++)
        {
            var sample = Mathf.Clamp(storedSamples[i], -1f, 1f);
            var value = (short)Mathf.RoundToInt(sample * short.MaxValue);
            var offset = i * sizeof(short);
            pcm[offset] = (byte)value;
            pcm[offset + 1] = (byte)(value >> 8);
        }

        lock (Buffers)
        {
            var chunk = new VoiceChunk
            {
                DeltaTime = ConsumeDeltaTime(actorNumber, timestamp),
                SampleRate = StoredSampleRate,
                Channels = StoredChannels,
                PcmData = pcm
            };
            Buffers[actorNumber].Add(chunk);
        }
    }

    private static float[] ResampleToStoredFormat(float[] samples, int channels, int sampleRate)
    {
        var sourceFrames = samples.Length / channels;
        if (sourceFrames <= 0)
            return [];

        var targetFrames = Mathf.Max(1, Mathf.RoundToInt(sourceFrames * (StoredSampleRate / (float)sampleRate)));
        var output = new float[targetFrames];

        for (var targetFrame = 0; targetFrame < targetFrames; targetFrame++)
        {
            var sourcePosition = targetFrame * (sampleRate / (float)StoredSampleRate);
            var sourceFrameA = Mathf.Clamp((int)sourcePosition, 0, sourceFrames - 1);
            var sourceFrameB = Mathf.Min(sourceFrameA + 1, sourceFrames - 1);
            var t = sourcePosition - sourceFrameA;

            output[targetFrame] = Mathf.Lerp(
                ReadMonoSample(samples, channels, sourceFrameA),
                ReadMonoSample(samples, channels, sourceFrameB),
                t);
        }

        return output;
    }

    private static float ReadMonoSample(float[] samples, int channels, int frame)
    {
        var offset = frame * channels;
        var sum = 0f;
        for (var channel = 0; channel < channels; channel++)
            sum += samples[offset + channel];

        return sum / channels;
    }

    private static bool IsSilent(float[] samples)
    {
        for (var i = 0; i < samples.Length; i++)
        {
            if (Mathf.Abs(samples[i]) > SilenceThreshold)
                return false;
        }

        return true;
    }

    private static void EnsureBuffer(int actorNumber, double timestamp)
    {
        lock (Buffers)
        {
            if (Buffers.ContainsKey(actorNumber))
                return;

            Buffers[actorNumber] = [];
            LastTimestamps[actorNumber] = timestamp;
        }
    }

    private static float ConsumeDeltaTime(int actorNumber, double timestamp)
    {
        var delta = (float)(timestamp - LastTimestamps[actorNumber]);
        LastTimestamps[actorNumber] = timestamp;
        return delta;
    }

    private readonly struct VoiceFormat(int sampleRate, int channels)
    {
        public readonly int SampleRate = sampleRate;
        public readonly int Channels = channels;
    }
}
