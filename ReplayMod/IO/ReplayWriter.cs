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
    private const int FormatVersion = 3;

    public static readonly string ReplayFolder =
        Path.Combine(Paths.PluginPath, "ReplayMod", "Recordings");

    public static string Save(
        string fileName,
        Dictionary<int, List<ReplayEvent>> poseStreams,
        Dictionary<int, List<VoiceChunk>> voiceStreams)
    {
        Directory.CreateDirectory(ReplayFolder);

        var path = Path.Combine(ReplayFolder, fileName + ".replay");

        using var fileStream = File.Create(path);
        using var gzipStream = new GZipStream(fileStream, CompressionLevel.Optimal);
        using var writer = new BinaryWriter(gzipStream);
        writer.Write(MagicNumber);
        writer.Write(FormatVersion);
        writer.Write(DateTime.UtcNow.ToBinary());

        writer.Write(poseStreams.Count);

        foreach (var (actorNumber, events) in poseStreams)
        {
            writer.Write(actorNumber);
            writer.Write(events.Count);

            foreach (var e in events)
                WriteEvent(writer, e);
        }

        var nonEmptyVoiceStreamCount = 0;
        foreach (var (_, chunks) in voiceStreams)
        {
            if (chunks.Count > 0)
                nonEmptyVoiceStreamCount++;
        }

        writer.Write(nonEmptyVoiceStreamCount);

        foreach (var (actorNumber, chunks) in voiceStreams)
        {
            if (chunks.Count == 0)
                continue;

            writer.Write(actorNumber);
            writer.Write(chunks.Count);

            foreach (var chunk in chunks)
                WriteVoiceChunk(writer, chunk);
        }

        Logging.ModLog.Info($"Saved replay to {path}");
        return path;
    }

    private static void WriteVoiceChunk(BinaryWriter writer, VoiceChunk chunk)
    {
        writer.Write(chunk.DeltaTime);
        writer.Write(chunk.SampleRate);
        writer.Write(chunk.Channels);
        writer.Write(chunk.PcmData?.Length ?? 0);

        if (chunk.PcmData is { Length: > 0 })
            writer.Write(chunk.PcmData);
    }

    private static void WriteEvent(BinaryWriter writer, ReplayEvent e)
    {
        writer.Write((byte)e.Type);
        writer.Write(e.DeltaTime);

        switch (e.Type)
        {
            case ReplayEventType.Frame:
                var frame = (FrameData)e.Payload;
                writer.Write(frame.BodyPos);
                writer.Write(frame.BodyRot);
                writer.Write(frame.HeadRot);
                writer.Write(frame.LeftHandLong);
                writer.Write(frame.RightHandLong);
                writer.Write(frame.HandSync);
                break;
            case ReplayEventType.ColorChanged:
                writer.Write(((ColorChangedData)e.Payload).Color);
                break;
            case ReplayEventType.MaterialChanged:
                writer.Write(((MaterialChangedData)e.Payload).MaterialIndex);
                break;
            case ReplayEventType.NameChanged:
                writer.Write(((NameChangedData)e.Payload).Name ?? string.Empty);
                break;
            case ReplayEventType.SoundEffect:
                var sound = (SoundEffectData)e.Payload;
                writer.Write(sound.SoundIndex);
                writer.Write(sound.Volume);
                writer.Write(sound.StopCurrentAudio);
                break;
            case ReplayEventType.HandTap:
                var handTap = (HandTapData)e.Payload;
                writer.Write(handTap.SoundIndex);
                writer.Write(handTap.Volume);
                writer.Write(handTap.IsLeftHand);
                break;
            case ReplayEventType.CosmeticsChanged:
                var cosmetics = (CosmeticsData)e.Payload;
                var items = cosmetics.Cosmetics;
                writer.Write(items?.Length ?? 0);
                if (items != null)
                {
                    foreach (var item in items)
                        writer.Write(item ?? string.Empty);
                }
                break;
        }
    }
}
