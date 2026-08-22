using UnityEngine;

namespace ReplayMod.Patches;

internal static class HandTapPatches
{
    [HarmonyLib.HarmonyPatch(typeof(VRRigSerializer), nameof(VRRigSerializer.OnHandTapRPCShared))]
    private static class OnHandTapRPCSharedPatch
    {
        [HarmonyLib.HarmonyPostfix]
        private static void Postfix(VRRigSerializer __instance, int audioClipIndex, bool isLeftHand, float handTapSpeed)
        {
            if (Plugin.ReplaySystem is not { IsRecording: true } system || __instance.VRRig?.Creator == null)
                return;

            var volume = Mathf.Clamp(handTapSpeed, 0f, GorillaTagger.Instance.DefaultHandTapVolume)
                         * __instance.VRRig.handSpeedToVolumeModifier;

            system.RecordHandTap(
                __instance.VRRig.Creator.ActorNumber,
                audioClipIndex,
                volume,
                isLeftHand,
                NetworkSystem.Instance.SimTime);
        }
    }
}
