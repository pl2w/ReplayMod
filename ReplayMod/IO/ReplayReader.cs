using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using ReplayMod.Models;

namespace ReplayMod.IO;

public static class ReplayReader
{
    public static ReplayData Load(string path)
    {
        using var fileStream = File.OpenRead(path);
        using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
        using var reader = new BinaryReader(gzipStream);

        var magic = reader.ReadInt32();
        if (magic != ReplayFormat.MagicNumber)
            throw new InvalidDataException($"Not a valid replay file: {path}");

        var version = reader.ReadInt32();
        if (version is < 1 or > ReplayFormat.Version)
            throw new InvalidDataException($"Unsupported replay version {version} (expected 1-{ReplayFormat.Version})");

        var recordedAt = DateTime.FromBinary(reader.ReadInt64());
        var replay = new ReplayData();

        var playerCount = reader.ReadInt32();
        Logging.ModLog.Info($"Loading replay {path}: version={version} recorded={recordedAt:u} poseStreams={playerCount}");

        for (var p = 0; p < playerCount; p++)
        {
            var actorNumber = reader.ReadInt32();
            var eventCount = reader.ReadInt32();
            var events = new List<ReplayEvent>(eventCount);

            for (var i = 0; i < eventCount; i++)
                events.Add(ReadEvent(reader));

            replay.PoseStreams[actorNumber] = events;
            Logging.ModLog.Debug($"  actor={actorNumber}: {eventCount} pose events");
        }

        var voiceStreamCount = reader.ReadInt32();
        for (var p = 0; p < voiceStreamCount; p++)
        {
            var actorNumber = reader.ReadInt32();
            var chunkCount = reader.ReadInt32();
            var chunks = new List<VoiceChunk>(chunkCount);

            for (var i = 0; i < chunkCount; i++)
                chunks.Add(ReadVoiceChunk(reader));

            replay.VoiceStreams[actorNumber] = chunks;
            Logging.ModLog.Debug($"  actor={actorNumber}: {chunkCount} voice chunks");
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
                e.Payload = new FrameData
                {
                    BodyPos = reader.ReadInt64(),
                    BodyRot = reader.ReadInt32(),
                    HeadRot = reader.ReadInt32(),
                    LeftHandLong = reader.ReadInt64(),
                    RightHandLong = reader.ReadInt64(),
                    HandSync = reader.ReadInt32()
                };
                break;
            case ReplayEventType.ColorChanged:
                e.Payload = new ColorChangedData { Color = reader.ReadInt16() };
                break;
            case ReplayEventType.MaterialChanged:
                e.Payload = new MaterialChangedData { MaterialIndex = reader.ReadSByte() };
                break;
            case ReplayEventType.NameChanged:
                e.Payload = new NameChangedData { Name = reader.ReadString() };
                break;
            case ReplayEventType.SoundEffect:
                e.Payload = new SoundEffectData
                {
                    SoundIndex = reader.ReadInt32(),
                    Volume = reader.ReadSingle(),
                    StopCurrentAudio = reader.ReadBoolean()
                };
                break;
            case ReplayEventType.HandTap:
                e.Payload = new HandTapData
                {
                    SoundIndex = reader.ReadInt32(),
                    Volume = reader.ReadSingle(),
                    IsLeftHand = reader.ReadBoolean()
                };
                break;
            case ReplayEventType.CosmeticsChanged:
                var count = reader.ReadInt32();
                var items = new string[count];
                for (var i = 0; i < count; i++)
                    items[i] = reader.ReadString();
                e.Payload = new CosmeticsData { Cosmetics = items };
                break;
            case ReplayEventType.PlayerLeft:
                break;
            default:
                throw new InvalidDataException($"Unknown replay event type {type} at player index, file may be corrupt or from an incompatible version");
        }

        return e;
    }
}
