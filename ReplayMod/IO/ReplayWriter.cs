using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using BepInEx;
using ReplayMod.Models;

namespace ReplayMod.IO;

public static class ReplayWriter
{
    private const int MagicNumber = 0x52504C59; // "RPLY"
    private const int FormatVersion = 1;

    public static readonly string ReplayFolder =
        Path.Combine(Paths.PluginPath, "ReplayMod", "Recordings");

    public static string Save(string fileName, Dictionary<int, List<ReplayEvent>> streams)
    {
        Directory.CreateDirectory(ReplayFolder);

        var path = Path.Combine(ReplayFolder, fileName + ".replay");

        using var fileStream = File.Create(path);
        using var gzipStream = new GZipStream(fileStream, CompressionLevel.Optimal);
        using var writer = new BinaryWriter(gzipStream);

        writer.Write(MagicNumber);
        writer.Write(FormatVersion);
        writer.Write(DateTime.UtcNow.ToBinary());

        writer.Write(streams.Count);

        foreach (var (actorNumber, events) in streams)
        {
            writer.Write(actorNumber);
            writer.Write(events.Count);

            foreach (var e in events)
                WriteEvent(writer, e);
        }

        return path;
    }

    private static void WriteEvent(BinaryWriter writer, ReplayEvent e)
    {
        writer.Write((byte)e.Type);
        writer.Write(e.DeltaTime);

        switch (e.Type)
        {
            case ReplayEventType.Frame:
                writer.Write(e.BodyPos);
                writer.Write(e.BodyRot);
                writer.Write(e.HeadRot);
                writer.Write(e.LeftHandLong);
                writer.Write(e.RightHandLong);
                writer.Write(e.HandSync);
                break;
            case ReplayEventType.ColorChanged:
                writer.Write(e.Color);
                break;
            case ReplayEventType.MaterialChanged:
                writer.Write(e.MaterialIndex);
                break;
            case ReplayEventType.NameChanged:
                writer.Write(e.Name ?? string.Empty);
                break;
        }
    }
}