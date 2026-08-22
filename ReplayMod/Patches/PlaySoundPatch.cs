using HarmonyLib;
using ReplayMod.Core;

namespace ReplayMod.Patches;

[HarmonyPatch(typeof(RoomSystem), nameof(RoomSystem.OnPlaySoundEffect))]
static class SoundEffectPatch
{
    static void Postfix(RoomSystem.SoundEffect sound, NetPlayer target)
    {
        if (Plugin.ReplaySystem == null || !Plugin.ReplaySystem.IsRecording) return;
        ReplayRecorder.RecordSoundEffect(
            target?.ActorNumber ?? -1,
            sound.id, sound.volume, sound.stopCurrentAudio,
            NetworkSystem.Instance.SimTime);
    }
}