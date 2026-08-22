using System.Collections.Generic;
using ReplayMod.Models;
using UnityEngine;

namespace ReplayMod.Playback;

public class GhostPlayer
{
    public int ActorNumber;
    public List<ReplayEvent> Events;
    public double[] AbsoluteTimes;
    public VRRig Rig;
    public AudioSource VoiceSource;
    public AudioClip VoiceClip;
    public bool VoiceClipScheduled;

    public int NextEventIndex;
    public double PlaybackClock;

    public FrameData CurrentFrame;
    public double CurrentFrameTime;
    public int FramesSeen;

    public bool HasLeft;
}