using System.Collections.Generic;
using ReplayMod.Models;

namespace ReplayMod.Playback;

public class GhostPlayer
{
    public int ActorNumber;
    public List<ReplayEvent> Events;
    public double[] AbsoluteTimes;
    public VRRig Rig;

    public int NextEventIndex;
    public double PlaybackClock;

    public ReplayEvent PreviousFrame;
    public ReplayEvent CurrentFrame;
    public double PreviousFrameTime;
    public double CurrentFrameTime;
    public int FramesSeen;
}