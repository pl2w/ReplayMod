using System.Collections.Generic;

namespace ReplayMod.Models;

public sealed class ReplayData
{
    public Dictionary<int, List<ReplayEvent>> PoseStreams { get; } = new();
    public Dictionary<int, List<VoiceChunk>> VoiceStreams { get; } = new();
}
