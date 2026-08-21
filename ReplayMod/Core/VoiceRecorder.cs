using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
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

    private static readonly Dictionary<int, List<VoiceCaptureFilter>> Filters = new();
    private static readonly Dictionary<int, HashSet<AudioSource>> Sources = new();
    private static readonly Dictionary<int, double> LastTimestamps = new();
    private static readonly HashSet<int> ActiveActors = new();
    private static readonly Dictionary<Speaker, VoiceFormat> SpeakerFormats = new();
    private static readonly MethodInfo FrameInfoGetter = AccessTools.PropertyGetter(typeof(FrameOut<float>), "Info");
    private static readonly MethodInfo RemoteVoiceLinkGetter = AccessTools.PropertyGetter(typeof(Speaker), "RemoteVoiceLink");
    private static double _startDspTime;
    private static bool _isRecording;

    public static void Reset()
    {
        StopAll();
        lock (Buffers) { Buffers.Clear(); }
        LastTimestamps.Clear();
        ActiveActors.Clear();
        SpeakerFormats.Clear();
        _startDspTime = AudioSettings.dspTime;
        _isRecording = true;
    }

    public static void BeginRecording(int actorNumber, VRRig rig)
    {
        BeginRecording(actorNumber);
    }

    public static void BeginRecording(int actorNumber)
    {
        EnsureBuffer(actorNumber, 0);
        ActiveActors.Add(actorNumber);
    }

    public static void RefreshSources(int actorNumber, VRRig rig)
    {
        EnsureBuffer(actorNumber, 0);
        ActiveActors.Add(actorNumber);

        if (!Filters.TryGetValue(actorNumber, out var filters))
        {
            filters = new List<VoiceCaptureFilter>();
            Filters[actorNumber] = filters;
        }

        if (!Sources.TryGetValue(actorNumber, out var sources))
        {
            sources = new HashSet<AudioSource>();
            Sources[actorNumber] = sources;
        }

        foreach (var source in rig.GetComponentsInChildren<AudioSource>(true))
        {
            if (!source || sources.Contains(source))
                continue;

            var filter = source.gameObject.AddComponent<VoiceCaptureFilter>();
            filter.Initialize(actorNumber);
            filters.Add(filter);
            sources.Add(source);
        }
    }

    public static void StopRecording(int actorNumber)
    {
        if (!Filters.TryGetValue(actorNumber, out var filters))
            return;

        foreach (var filter in filters)
        {
            if (filter)
                UnityEngine.Object.Destroy(filter);
        }

        Filters.Remove(actorNumber);
        Sources.Remove(actorNumber);
        ActiveActors.Remove(actorNumber);
    }

    public static void StopAll()
    {
        foreach (var actorNumber in new List<int>(Filters.Keys))
            StopRecording(actorNumber);

        ActiveActors.Clear();
        SpeakerFormats.Clear();
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

    internal static void RecordSamples(int actorNumber, float[] samples, int channels, int sampleRate)
    {
        if (!_isRecording || !ActiveActors.Contains(actorNumber))
            return;

        RecordSamplesAtCurrentTime(actorNumber, samples, channels, sampleRate);
    }

    public static void RecordPhotonFrame(Speaker speaker, FrameOut<float> frame)
    {
        if (!_isRecording || speaker == null || frame == null || frame.EndOfStream)
            return;

        var actorNumber = speaker.Actor?.ActorNumber ?? GetRemoteVoicePlayerId(speaker);
        if (actorNumber <= 0 || !ActiveActors.Contains(actorNumber))
            return;

        var (sampleRate, channels) = GetFrameFormat(speaker, frame);
        RecordSamplesAtCurrentTime(actorNumber, frame.Buf, channels, sampleRate);
    }

    public static void SetSpeakerFormat(Speaker speaker, int sampleRate, int channels)
    {
        if (speaker == null || sampleRate <= 0 || channels <= 0)
            return;

        SpeakerFormats[speaker] = new VoiceFormat(sampleRate, channels);
    }

    private static (int SampleRate, int Channels) GetFrameFormat(Speaker speaker, FrameOut<float> frame)
    {
        if (SpeakerFormats.TryGetValue(speaker, out var format))
            return (format.SampleRate, format.Channels);

        var info = GetFrameInfo(frame);
        var sampleRate = info.SamplingRate > 0 ? info.SamplingRate : 16000;
        var channels = info.Channels > 0 ? info.Channels : 1;
        return (sampleRate, channels);
    }

    private static VoiceInfo GetFrameInfo(FrameOut<float> frame)
    {
        if (FrameInfoGetter?.Invoke(frame, null) is VoiceInfo info)
            return info;

        return default;
    }

    private static int GetRemoteVoicePlayerId(Speaker speaker)
    {
        if (RemoteVoiceLinkGetter?.Invoke(speaker, null) is RemoteVoiceLink link)
            return link.PlayerId;

        return -1;
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
            Buffers[actorNumber].Add(new VoiceChunk
            {
                DeltaTime = ConsumeDeltaTime(actorNumber, timestamp),
                SampleRate = StoredSampleRate,
                Channels = StoredChannels,
                PcmData = pcm
            });
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

public sealed class VoiceCaptureFilter : MonoBehaviour
{
    private int _actorNumber;
    private bool _initialized;

    public void Initialize(int actorNumber)
    {
        _actorNumber = actorNumber;
        _initialized = true;
    }

    private void OnAudioFilterRead(float[] data, int channels)
    {
        if (!_initialized)
            return;

        VoiceRecorder.RecordSamples(_actorNumber, data, channels, AudioSettings.outputSampleRate);
    }
}
