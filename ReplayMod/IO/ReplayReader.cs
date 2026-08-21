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

    public static Dictionary<int, List<ReplayEvent>> Load(string path)
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

        var recordedAt = DateTime.FromBinary(reader.ReadInt64());

        var playerCount = reader.ReadInt32();
        var streams = new Dictionary<int, List<ReplayEvent>>(playerCount);

        for (var p = 0; p < playerCount; p++)
        {
            var actorNumber = reader.ReadInt32();
            var eventCount = reader.ReadInt32();
            var events = new List<ReplayEvent>(eventCount);

            for (var i = 0; i < eventCount; i++)
                events.Add(ReadEvent(reader));

            streams[actorNumber] = events;
        }

        return streams;
    }

    private static ReplayEvent ReadEvent(BinaryReader reader)
    {
        var type = (ReplayEventType)reader.ReadByte();
        var deltaTime = reader.ReadSingle();
        var e = new ReplayEvent { Type = type, DeltaTime = deltaTime };

        switch (type)
        {
            case ReplayEventType.Frame:
                e.BodyPos = reader.ReadInt64();
                e.BodyRot = reader.ReadInt32();
                e.HeadRot = reader.ReadInt32();
                e.LeftHandLong = reader.ReadInt64();
                e.RightHandLong = reader.ReadInt64();
                e.HandSync = reader.ReadInt32();
                break;
            case ReplayEventType.ColorChanged:
                e.Color = reader.ReadInt16();
                break;
            case ReplayEventType.MaterialChanged:
                e.MaterialIndex = reader.ReadSByte();
                break;
            case ReplayEventType.NameChanged:
                e.Name = reader.ReadString();
                break;
            default:
                throw new InvalidDataException($"Unknown replay event type {type} at player index, file may be corrupt or from an incompatible version");
        }

        return e;
    }
}