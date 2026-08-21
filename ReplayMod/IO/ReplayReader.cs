using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using ReplayMod.Models;

namespace ReplayMod.IO;

public static class ReplayReader
{
    private const int MagicNumber = 0x52504C59;
    private const int FormatVersion = 2;

    public static ReplayData Load(string path)
    {
        using var fileStream = File.OpenRead(path);
        using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
        using var reader = new BinaryReader(gzipStream);

        var magic = reader.ReadInt32();
        if (magic != MagicNumber)
            throw new InvalidDataException($"Not a valid replay file: {path}");

        var version = reader.ReadInt32();
        if (version is < 1 or > FormatVersion)
            throw new InvalidDataException($"Unsupported replay version {version} (expected 1-{FormatVersion})");

        var recordedAt = DateTime.FromBinary(reader.ReadInt64());
        var replay = new ReplayData();

        var playerCount = reader.ReadInt32();

        for (var p = 0; p < playerCount; p++)
        {
            var actorNumber = reader.ReadInt32();
            var eventCount = reader.ReadInt32();
            var events = new List<ReplayEvent>(eventCount);

            for (var i = 0; i < eventCount; i++)
                events.Add(ReadEvent(reader));

            replay.PoseStreams[actorNumber] = events;
        }

        if (version < 2)
            return replay;

        var voiceStreamCount = reader.ReadInt32();
        for (var p = 0; p < voiceStreamCount; p++)
        {
            var actorNumber = reader.ReadInt32();
            var chunkCount = reader.ReadInt32();
            var chunks = new List<VoiceChunk>(chunkCount);

            for (var i = 0; i < chunkCount; i++)
                chunks.Add(ReadVoiceChunk(reader));

            replay.VoiceStreams[actorNumber] = chunks;
        }

        return replay;
    }

    private static VoiceChunk ReadVoiceChunk(BinaryReader reader)
    {
        var deltaTime = reader.ReadSingle();
        var sampleRate = reader.ReadInt32();
        var channels = reader.ReadInt32();
        var byteCount = reader.ReadInt32();

        return new VoiceChunk
        {
            DeltaTime = deltaTime,
            SampleRate = sampleRate,
            Channels = channels,
            PcmData = byteCount > 0 ? reader.ReadBytes(byteCount) : []
        };
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
