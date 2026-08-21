using UnityEngine;
using ReplayMod.Models;

namespace ReplayMod.Playback;

public static class FrameUnpacker
{
    public static void Unpack(
        ReplayEvent frame,
        out Vector3 bodyPos, out Quaternion bodyRot, out Quaternion headRot,
        out Vector3 leftHandPos, out Quaternion leftHandRot,
        out Vector3 rightHandPos, out Quaternion rightHandRot)
    {
        if (frame.Type != ReplayEventType.Frame)
            throw new System.InvalidOperationException($"FrameUnpacker.Unpack called on a non-Frame event ({frame.Type})");

        bodyPos = BitPackUtils.UnpackWorldPosFromNetwork(frame.BodyPos);
        bodyRot = BitPackUtils.UnpackQuaternionFromNetwork(frame.BodyRot);
        headRot = BitPackUtils.UnpackQuaternionFromNetwork(frame.HeadRot);

        BitPackUtils.UnpackHandPosRotFromNetwork(frame.LeftHandLong, out leftHandPos, out leftHandRot);
        BitPackUtils.UnpackHandPosRotFromNetwork(frame.RightHandLong, out rightHandPos, out rightHandRot);
    }
}