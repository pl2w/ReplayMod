namespace ReplayMod.Models;

public sealed class VoiceChunk
{
    public float DeltaTime;
    public int SampleRate;
    public int Channels;
    public byte[] PcmData;
}
