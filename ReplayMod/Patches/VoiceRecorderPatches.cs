using Photon.Voice;
using Photon.Voice.Unity;

namespace ReplayMod.Core;

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
        private static void Postfix(Speaker __instance, int frequency, int channels)
        {
            VoiceRecorder.SetSpeakerFormat(__instance, frequency, channels);
        }
    }
}
