using System.Collections.Generic;
using ReplayMod.Models;
using UnityEngine;

namespace ReplayMod.Playback;

public class GhostPlayer
{
    public int ActorNumber;
    public List<ReplayEvent> Events;
    public double[] AbsoluteTimes;
    public List<VoiceChunk> VoiceChunks;
    public double[] VoiceAbsoluteTimes;
    public VRRig Rig;
    public AudioSource VoiceSource;

    public int NextEventIndex;
    public int NextVoiceChunkIndex;
    public double PlaybackClock;

    public ReplayEvent PreviousFrame;
    public ReplayEvent CurrentFrame;
    public double PreviousFrameTime;
    public double CurrentFrameTime;
    public int FramesSeen;
}
