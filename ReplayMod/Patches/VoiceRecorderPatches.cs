using Photon.Voice;
using Photon.Voice.Unity;
using ReplayMod.Core;

namespace ReplayMod.Patches;

internal static class VoiceRecorderPatches
{
    [HarmonyLib.HarmonyPatch(typeof(Speaker), "OnAudioFrame")]
    private static class OnAudioFramePatch
    {
        [HarmonyLib.HarmonyPostfix]
        private static void Postfix(Speaker __instance, FrameOut<float> frame)
        {
            VoiceRecorder.RecordPhotonFrame(__instance, frame);
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(Speaker), "AudioOutputStart")]
    private static class AudioOutputStartPatch
    {
        [HarmonyLib.HarmonyPostfix]
        private static void Postfix(Speaker __instance, int frequency, int channels, int frameSamplesPerChannel)
        {
            VoiceRecorder.SetSpeakerFormat(__instance, frequency, channels);
        }
    }
}
