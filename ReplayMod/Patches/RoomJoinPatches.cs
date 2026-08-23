using HarmonyLib;
using GorillaNetworking;
using ReplayMod.Logging;

namespace ReplayMod.Patches;

internal static class RoomJoinGuard
{
    internal static bool IsBlocked()
    {
        if (Plugin.ReplayPlayer == null || !Plugin.ReplayPlayer.IsLoaded)
            return false;

        ModLog.Info("Blocked room join while a replay is active.");
        return true;
    }
}

[HarmonyPatch(typeof(PhotonNetworkController), nameof(PhotonNetworkController.AttemptToJoinPublicRoom))]
static class JoinPublicRoomPatch
{
    static bool Prefix()
    {
        return !RoomJoinGuard.IsBlocked();
    }
}

[HarmonyPatch(typeof(PhotonNetworkController), nameof(PhotonNetworkController.AttemptToJoinRankedPublicRoom))]
static class JoinRankedPublicRoomPatch
{
    static bool Prefix()
    {
        return !RoomJoinGuard.IsBlocked();
    }
}

[HarmonyPatch(typeof(PhotonNetworkController), nameof(PhotonNetworkController.AttemptToJoinSpecificRoom))]
static class JoinSpecificRoomPatch
{
    static bool Prefix()
    {
        return !RoomJoinGuard.IsBlocked();
    }
}
