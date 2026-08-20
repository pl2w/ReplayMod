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

    public static string Save(string fileName, Dictionary<int, List<PackedReplayFrame>> buffers)
    {
        Directory.CreateDirectory(ReplayFolder);

        var path = Path.Combine(ReplayFolder, fileName + ".replay");

        using var fileStream = File.Create(path);
        using var gzipStream = new GZipStream(fileStream, CompressionLevel.Optimal);
        using var writer = new BinaryWriter(gzipStream);

        writer.Write(MagicNumber);
        writer.Write(FormatVersion);
        writer.Write(DateTime.UtcNow.ToBinary());

        writer.Write(buffers.Count);

        foreach (var (actorNumber, frames) in buffers)
        {
            writer.Write(actorNumber);
            writer.Write(frames.Count);

            foreach (var f in frames)
            {
                writer.Write(f.DeltaTime);
                writer.Write(f.BodyPos);
                writer.Write(f.BodyRot);
                writer.Write(f.HeadRot);
                writer.Write(f.LeftHandLong);
                writer.Write(f.RightHandLong);
                writer.Write(f.HandSync);
            }
        }

        return path;
    }
}