using HarmonyLib;
using UnityEngine;

namespace ReplayMod.Patches;

internal static class HandTapPatches
{
    private const double MaxClockSkewSeconds = 1.0;

    [HarmonyPatch(typeof(VRRigSerializer), "OnHandTapRPCShared")]
    private static class OnHandTapRPCSharedPatch
    {
        [HarmonyPostfix]
        private static void Postfix(
            VRRigSerializer __instance,
            int audioClipIndex,
            bool isLeftHand,
            float handTapSpeed,
            PhotonMessageInfoWrapped info)
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
                TapTimestamp(info));
        }
    }

    [HarmonyPatch(typeof(VRRig), nameof(VRRig.SetHandEffectData))]
    private static class LocalHandTapPatch
    {
        [HarmonyPostfix]
        private static void Postfix(VRRig __instance, int audioClipIndex, bool isLeftHand, float handTapSpeed)
        {
            if (Plugin.ReplaySystem is not { IsRecording: true } system)
                return;

            var offlineRig = GorillaTagger.Instance != null ? GorillaTagger.Instance.offlineVRRig : null;
            if (!__instance || __instance != offlineRig)
                return;

            var volume = Mathf.Clamp(handTapSpeed, 0f, GorillaTagger.Instance.DefaultHandTapVolume)
                         * __instance.handSpeedToVolumeModifier;

            system.RecordHandTap(
                NetworkSystem.Instance.LocalPlayer.ActorNumber,
                audioClipIndex,
                volume,
                isLeftHand,
                NetworkSystem.Instance.SimTime);
        }
    }

    private static double TapTimestamp(PhotonMessageInfoWrapped info)
    {
        var sentServerTime = info.SentServerTime;
        var simTime = NetworkSystem.Instance.SimTime;

        if (sentServerTime > 0 && sentServerTime <= simTime + MaxClockSkewSeconds)
            return sentServerTime;

        return simTime;
    }
}
