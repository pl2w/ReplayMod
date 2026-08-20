using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using ReplayMod.Models;

namespace ReplayMod.IO;

public static class ReplayReader
{
    private const int MagicNumber = 0x52504C59;
    private const int FormatVersion = 1;

    public static Dictionary<int, List<PackedReplayFrame>> Load(string path)
    {
        using var fileStream = File.OpenRead(path);
        using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
        using var reader = new BinaryReader(gzipStream);

        var magic = reader.ReadInt32();
        if (magic != MagicNumber)
            throw new InvalidDataException($"Not a valid replay file: {path}");

        var version = reader.ReadInt32();
        if (version != FormatVersion)
            throw new InvalidDataException($"Unsupported replay version {version} (expected {FormatVersion})");

        DateTime recordedAt = DateTime.FromBinary(reader.ReadInt64());

        var playerCount = reader.ReadInt32();
        var buffers = new Dictionary<int, List<PackedReplayFrame>>(playerCount);

        for (var p = 0; p < playerCount; p++)
        {
            var actorNumber = reader.ReadInt32();
            var frameCount = reader.ReadInt32();
            var frames = new List<PackedReplayFrame>(frameCount);

            for (var i = 0; i < frameCount; i++)
            {
                frames.Add(new PackedReplayFrame
                {
                    DeltaTime = reader.ReadSingle(),
                    BodyPos = reader.ReadInt64(),
                    BodyRot = reader.ReadInt32(),
                    HeadRot = reader.ReadInt32(),
                    LeftHandLong = reader.ReadInt64(),
                    RightHandLong = reader.ReadInt64(),
                    HandSync = reader.ReadInt32()
                });
            }

            buffers[actorNumber] = frames;
        }

        return buffers;
    }
}