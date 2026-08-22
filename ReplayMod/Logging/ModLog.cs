using BepInEx.Logging;

namespace ReplayMod.Logging;

public static class ModLog
{
    public static ManualLogSource Source { get; set; }

    public static void Debug(object data)
    {
        ReplayLog.Write("DEBUG", data);
        Source?.LogDebug(data);
    }

    public static void Info(object data)
    {
        ReplayLog.Write("INFO", data);
        Source?.LogInfo(data);
    }

    public static void Warn(object data)
    {
        ReplayLog.Write("WARN", data);
        Source?.LogWarning(data);
    }

    public static void Error(object data)
    {
        ReplayLog.Write("ERROR", data);
        Source?.LogError(data);
    }
}
