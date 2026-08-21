using BepInEx.Logging;

namespace ReplayMod.Logging;

public static class ModLog
{
    public static ManualLogSource Source { get; set; }

    public static void Debug(object data) => Source?.LogDebug(data);
    public static void Info(object data) => Source?.LogInfo(data);
    public static void Warn(object data) => Source?.LogWarning(data);
    public static void Error(object data) => Source?.LogError(data);
}
