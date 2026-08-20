using System.Collections.Generic;
using ReplayMod.Models;

namespace ReplayMod.Playback;

public class GhostPlayer
{
    public int ActorNumber;
    public List<PackedReplayFrame> Frames;
    public double[] CumulativeTimes;
    public VRRig Rig;
    public int CurrentFrameIndex;
    public double PlaybackClock;
}