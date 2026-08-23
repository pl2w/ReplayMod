using Photon.Voice;
using ReplayMod.Logging;

namespace ReplayMod.Core;

public sealed class LocalVoiceCapture : IProcessor<float>
{
    public int ActorNumber;

    private readonly int _sampleRate;
    private readonly int _channels;

    public LocalVoiceCapture(int actorNumber, int sampleRate, int channels)
    {
        ActorNumber = actorNumber;
        _sampleRate = sampleRate;
        _channels = channels;
        ModLog.Debug($"LocalVoiceCapture created actor={actorNumber} ({sampleRate}Hz/{channels}ch)");
    }

    public float[] Process(float[] buf)
    {
        if (buf == null || buf.Length == 0)
            return buf;

        VoiceRecorder.RecordLocalVoice(ActorNumber, buf, _channels, _sampleRate);
        return buf;
    }

    public void Dispose()
    {
    }
}
