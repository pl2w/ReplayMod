using System;
using System.IO;
using BepInEx;

namespace ReplayMod.Logging;

public static class ReplayLog
{
    private static readonly object Sync = new();
    private static StreamWriter _writer;
    private static string _path;

    private static StreamWriter Writer
    {
        get
        {
            lock (Sync)
            {
                if (_writer != null)
                    return _writer;

                var dir = Path.Combine(Paths.PluginPath, "ReplayMod", "Logs");
                Directory.CreateDirectory(dir);

                var stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                _path = Path.Combine(dir, $"replay_{stamp}.log");

                _writer = new StreamWriter(_path, append: false)
                {
                    AutoFlush = true
                };

                return _writer;
            }
        }
    }

    public static string LogPath
    {
        get
        {
            _ = Writer;
            return _path;
        }
    }

    public static void Write(string level, object data)
    {
        lock (Sync)
        {
            Writer.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [{level}] {data}");
        }
    }

    public static void Close()
    {
        lock (Sync)
        {
            _writer?.Flush();
            _writer?.Dispose();
            _writer = null;
        }
    }
}
