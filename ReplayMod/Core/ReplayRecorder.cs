using System.Collections.Generic;
using ReplayMod.Models;

namespace ReplayMod.Core;

public static class ReplayRecorder
{
    public static readonly Dictionary<int, List<PackedReplayFrame>> Buffers = new();
    private static readonly Dictionary<int, double> LastTimestamps = new();

    public static void RecordFrame(int actorNumber, VRRig rig, double timestamp)
    {
        if (!Buffers.TryGetValue(actorNumber, out var buffer))
        {
            buffer = new List<PackedReplayFrame>();
            Buffers[actorNumber] = buffer;
            LastTimestamps[actorNumber] = timestamp;
        }

        var deltaTime = (float)(timestamp - LastTimestamps[actorNumber]);
        LastTimestamps[actorNumber] = timestamp;

        buffer.Add(new PackedReplayFrame
        {
            DeltaTime = deltaTime,
            BodyPos = BitPackUtils.PackWorldPosForNetwork(rig.transform.position),
            BodyRot = BitPackUtils.PackQuaternionForNetwork(rig.transform.rotation),
            HeadRot = BitPackUtils.PackQuaternionForNetwork(rig.head.rigTarget.localRotation),
            LeftHandLong = BitPackUtils.PackHandPosRotForNetwork(
                rig.leftHand.rigTarget.localPosition, rig.leftHand.rigTarget.localRotation),
            RightHandLong = BitPackUtils.PackHandPosRotForNetwork(
                rig.rightHand.rigTarget.localPosition, rig.rightHand.rigTarget.localRotation),
            HandSync = rig.handSync
        });
    }

    public static void Reset()
    {
        Buffers.Clear();
        LastTimestamps.Clear();
    }
}