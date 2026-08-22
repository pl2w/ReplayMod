using ReplayMod.Core;
using UnityEngine;

namespace ReplayMod.Patches;

internal static class HandTapPatches
{
    [HarmonyLib.HarmonyPatch(typeof(VRRig), nameof(VRRig.SetHandEffectData))]
    private static class SetHandEffectDataPatch
    {
        [HarmonyLib.HarmonyPostfix]
        private static void Postfix(VRRig __instance, int audioClipIndex, bool isLeftHand, float handTapVolume)
        {
            if (!IsRecording() || __instance.Creator == null)
                return;

            ReplayRecorder.RecordHandTap(
                __instance.Creator.ActorNumber,
                audioClipIndex,
                handTapVolume * __instance.handSpeedToVolumeModifier,
                isLeftHand,
                NetworkSystem.Instance.SimTime);
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(VRRigSerializer), nameof(VRRigSerializer.OnHandTapRPCShared))]
    private static class OnHandTapRPCSharedPatch
    {
        [HarmonyLib.HarmonyPostfix]
        private static void Postfix(VRRigSerializer __instance, int audioClipIndex, bool isLeftHand, float handTapSpeed)
        {
            if (!IsRecording() || __instance.VRRig?.Creator == null)
                return;

            var volume = Mathf.Clamp(handTapSpeed, 0f, GorillaTagger.Instance.DefaultHandTapVolume)
                         * __instance.VRRig.handSpeedToVolumeModifier;

            ReplayRecorder.RecordHandTap(
                __instance.VRRig.Creator.ActorNumber,
                audioClipIndex,
                volume,
                isLeftHand,
                NetworkSystem.Instance.SimTime);
        }
    }

    private static bool IsRecording()
        => Plugin.ReplaySystem != null && Plugin.ReplaySystem.IsRecording;
}
