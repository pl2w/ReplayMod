using System;
using System.Collections.Generic;
using ReplayMod.Models;
using UnityEngine;

namespace ReplayMod.Core;

public static class ReplayRecorder
{
    public static readonly Dictionary<int, List<ReplayEvent>> Buffers = new();
    private static readonly Dictionary<int, double> LastTimestamps = new();

    private static readonly Dictionary<int, Action<Color>> ColorHandlers = new();
    private static readonly Dictionary<int, Action<int, int>> MaterialHandlers = new();
    private static readonly Dictionary<int, Action> NameHandlers = new();
    
    public static double CurrentTimestamp { get; set; }
    
    
    public static void BeginRecording(int actorNumber, VRRig rig, double timestamp)
    {
        CurrentTimestamp = timestamp;
        
        if (!Buffers.ContainsKey(actorNumber))
        {
            Buffers[actorNumber] = [];
            LastTimestamps[actorNumber] = timestamp;
        }
        
        rig.OnColorInitialized(color => RecordColorChange(actorNumber, color, GetCurrentTimestamp()));
        RecordNameChange(actorNumber, rig.playerNameVisible, timestamp);
        RecordMaterialChange(actorNumber, rig.setMatIndex, timestamp);
        
        Action<Color> colorHandler = color =>
            RecordColorChange(actorNumber, color, GetCurrentTimestamp());
        rig.OnColorChanged += colorHandler;
        ColorHandlers[actorNumber] = colorHandler;

        Action<int, int> materialHandler = (oldIdx, newIdx) =>
            RecordMaterialChange(actorNumber, newIdx, GetCurrentTimestamp());
        rig.OnMaterialIndexChanged += materialHandler;
        MaterialHandlers[actorNumber] = materialHandler;
        
        Action nameHandler = () =>
            RecordNameChange(actorNumber, rig.playerNameVisible, GetCurrentTimestamp());
        rig.OnPlayerNameVisibleChanged += nameHandler;
        NameHandlers[actorNumber] = nameHandler;
    }

    public static void StopRecording(int actorNumber, VRRig rig)
    {
        if (ColorHandlers.TryGetValue(actorNumber, out var colorHandler))
        {
            rig.OnColorChanged -= colorHandler;
            ColorHandlers.Remove(actorNumber);
        }
        if (MaterialHandlers.TryGetValue(actorNumber, out var materialHandler))
        {
            rig.OnMaterialIndexChanged -= materialHandler;
            MaterialHandlers.Remove(actorNumber);
        }
        if (NameHandlers.TryGetValue(actorNumber, out var nameHandler))
        {
            rig.OnPlayerNameVisibleChanged -= nameHandler;
            NameHandlers.Remove(actorNumber);
        }
    }

    public static void RecordFrame(int actorNumber, VRRig rig, double timestamp)
    {
        EnsureBuffer(actorNumber, timestamp);
        var deltaTime = ConsumeDeltaTime(actorNumber, timestamp);

        Buffers[actorNumber].Add(new ReplayEvent
        {
            Type = ReplayEventType.Frame,
            DeltaTime = deltaTime,
            Payload = new FrameData
            {
                BodyPos = BitPackUtils.PackWorldPosForNetwork(rig.transform.position),
                BodyRot = BitPackUtils.PackQuaternionForNetwork(rig.transform.rotation),
                HeadRot = BitPackUtils.PackQuaternionForNetwork(rig.head.rigTarget.localRotation),
                LeftHandLong = BitPackUtils.PackHandPosRotForNetwork(
                    rig.leftHand.rigTarget.localPosition, rig.leftHand.rigTarget.localRotation),
                RightHandLong = BitPackUtils.PackHandPosRotForNetwork(
                    rig.rightHand.rigTarget.localPosition, rig.rightHand.rigTarget.localRotation),
                HandSync = rig.handSync
            }
        });
    }

    private static void RecordColorChange(int actorNumber, Color color, double timestamp)
    {
        EnsureBuffer(actorNumber, timestamp);
        var deltaTime = ConsumeDeltaTime(actorNumber, timestamp);

        Buffers[actorNumber].Add(new ReplayEvent
        {
            Type = ReplayEventType.ColorChanged,
            DeltaTime = deltaTime,
            Payload = new ColorChangedData { Color = BitPackUtils.PackColorForNetwork(color) }
        });
    }
    
    private static void RecordNameChange(int actorNumber, string name, double timestamp)
    {
        EnsureBuffer(actorNumber, timestamp);
        var deltaTime = ConsumeDeltaTime(actorNumber, timestamp);

        Buffers[actorNumber].Add(new ReplayEvent
        {
            Type = ReplayEventType.NameChanged,
            DeltaTime = deltaTime,
            Payload = new NameChangedData { Name = name }
        });
    }

    private static void RecordMaterialChange(int actorNumber, int newMaterialIndex, double timestamp)
    {
        EnsureBuffer(actorNumber, timestamp);
        var deltaTime = ConsumeDeltaTime(actorNumber, timestamp);

        Buffers[actorNumber].Add(new ReplayEvent
        {
            Type = ReplayEventType.MaterialChanged,
            DeltaTime = deltaTime,
            Payload = new MaterialChangedData { MaterialIndex = (sbyte)newMaterialIndex }
        });
    }
    
    public static void RecordPlayerLeft(int actorNumber, double timestamp)
    {
        EnsureBuffer(actorNumber, timestamp);
        var deltaTime = ConsumeDeltaTime(actorNumber, timestamp);

        Buffers[actorNumber].Add(new ReplayEvent
        {
            Type = ReplayEventType.PlayerLeft,
            DeltaTime = deltaTime
        });
    }
    
    public static void RecordSoundEffect(int actorNumber, int soundIndex, float volume, bool stopCurrentAudio, double timestamp)
    {
        EnsureBuffer(actorNumber, timestamp);
        var deltaTime = ConsumeDeltaTime(actorNumber, timestamp);
        Buffers[actorNumber].Add(new ReplayEvent
        {
            Type = ReplayEventType.SoundEffect,
            DeltaTime = deltaTime,
            Payload = new SoundEffectData
            {
                SoundIndex = soundIndex,
                Volume = volume,
                StopCurrentAudio = stopCurrentAudio
            }
        });
    }

    public static void RecordHandTap(int actorNumber, int soundIndex, float volume, bool isLeftHand, double timestamp)
    {
        EnsureBuffer(actorNumber, timestamp);
        var deltaTime = ConsumeDeltaTime(actorNumber, timestamp);
        Buffers[actorNumber].Add(new ReplayEvent
        {
            Type = ReplayEventType.HandTap,
            DeltaTime = deltaTime,
            Payload = new HandTapData
            {
                SoundIndex = soundIndex,
                Volume = volume,
                IsLeftHand = isLeftHand
            }
        });
    }

    private static void EnsureBuffer(int actorNumber, double timestamp)
    {
        if (!Buffers.ContainsKey(actorNumber))
        {
            Buffers[actorNumber] = new List<ReplayEvent>();
            LastTimestamps[actorNumber] = timestamp;
        }
    }

    private static float ConsumeDeltaTime(int actorNumber, double timestamp)
    {
        var delta = (float)(timestamp - LastTimestamps[actorNumber]);
        LastTimestamps[actorNumber] = timestamp;
        return delta;
    }

    private static double GetCurrentTimestamp() => CurrentTimestamp;

    public static void Reset()
    {
        Buffers.Clear();
        LastTimestamps.Clear();
        ColorHandlers.Clear();
        MaterialHandlers.Clear();
        NameHandlers.Clear();
    }
}