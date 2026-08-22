using System;
using System.Collections.Generic;
using ReplayMod.Models;
using UnityEngine;

namespace ReplayMod.Core;

public sealed class ReplayRecorder
{
    private readonly int _actorNumber;
    private readonly VRRig _rig;
    private readonly Action<Color> _colorHandler;
    private readonly Action<int, int> _materialHandler;
    private readonly Action _nameHandler;

    private double _lastTimestamp;
    private sbyte _lastSampledMat;
    private string[] _lastRecordedCosmetics;

    public double CurrentTimestamp { get; set; }

    public List<ReplayEvent> Events { get; } = [];

    public ReplayRecorder(int actorNumber, VRRig rig, double timestamp)
    {
        _actorNumber = actorNumber;
        _rig = rig;
        _lastTimestamp = timestamp;
        CurrentTimestamp = timestamp;

        _colorHandler = color =>
            Add(ReplayEventType.ColorChanged, new ColorChangedData
            {
                Color = BitPackUtils.PackColorForNetwork(color)
            }, CurrentTimestamp);

        _materialHandler = (oldIndex, newIndex) =>
        {
            Logging.ModLog.Debug(
                $"[mat] actor={actorNumber} OnMaterialIndexChanged {oldIndex}->{newIndex} at t={CurrentTimestamp:F3} (rig.setMatIndex={rig.setMatIndex})");
            Add(ReplayEventType.MaterialChanged, new MaterialChangedData
            {
                MaterialIndex = (sbyte)newIndex
            }, CurrentTimestamp);
        };

        _nameHandler = () =>
            Add(ReplayEventType.NameChanged, new NameChangedData
            {
                Name = rig.playerNameVisible
            }, CurrentTimestamp);

        rig.OnColorInitialized(color => _colorHandler(color));
        Add(ReplayEventType.NameChanged, new NameChangedData { Name = rig.playerNameVisible }, timestamp);
        var initialMat = (sbyte)rig.setMatIndex;
        Logging.ModLog.Info($"[mat] actor={_actorNumber} initial setMatIndex={initialMat} at t={timestamp:F3}");
        Add(ReplayEventType.MaterialChanged, new MaterialChangedData { MaterialIndex = initialMat }, timestamp);

        rig.OnColorChanged += _colorHandler;
        rig.OnMaterialIndexChanged += _materialHandler;
        rig.OnPlayerNameVisibleChanged += _nameHandler;

        RecordCosmeticsIfChanged(timestamp);
    }

    private void RecordCosmeticsIfChanged(double timestamp)
    {
        var items = _rig.cosmeticSet?.items;
        if (items == null || items.Length == 0)
            return;

        var current = new string[items.Length];
        for (var i = 0; i < items.Length; i++)
            current[i] = items[i].displayName;

        if (_lastRecordedCosmetics != null && SlotsEqual(_lastRecordedCosmetics, current))
            return;

        _lastRecordedCosmetics = current;

        var worn = 0;
        foreach (var name in current)
        {
            if (!string.IsNullOrEmpty(name) && name != "NOTHING")
                worn++;
        }

        Logging.ModLog.Info(
            $"[cos] actor={_actorNumber} {worn} worn cosmetics at t={timestamp:F3}");
        Add(ReplayEventType.CosmeticsChanged, new CosmeticsData { Cosmetics = current }, timestamp);
    }

    private static bool SlotsEqual(string[] a, string[] b)
    {
        if (a.Length != b.Length)
            return false;
        for (var i = 0; i < a.Length; i++)
        {
            if (!string.Equals(a[i], b[i], StringComparison.Ordinal))
                return false;
        }
        return true;
    }

    public void RecordFrame(double timestamp)
    {
        var sampledMat = (sbyte)_rig.setMatIndex;
        if (sampledMat != _lastSampledMat)
        {
            _lastSampledMat = sampledMat;
            Logging.ModLog.Debug($"[mat] actor={_actorNumber} frame sample setMatIndex={sampledMat} at t={timestamp:F3}");
        }

        RecordCosmeticsIfChanged(timestamp);

        Add(ReplayEventType.Frame, new FrameData
        {
            BodyPos = BitPackUtils.PackWorldPosForNetwork(_rig.transform.position),
            BodyRot = BitPackUtils.PackQuaternionForNetwork(_rig.transform.rotation),
            HeadRot = BitPackUtils.PackQuaternionForNetwork(_rig.head.rigTarget.localRotation),
            LeftHandLong = BitPackUtils.PackHandPosRotForNetwork(
                _rig.leftHand.rigTarget.localPosition, _rig.leftHand.rigTarget.localRotation),
            RightHandLong = BitPackUtils.PackHandPosRotForNetwork(
                _rig.rightHand.rigTarget.localPosition, _rig.rightHand.rigTarget.localRotation),
            HandSync = _rig.handSync
        }, timestamp);
    }

    public void RecordPlayerLeft(double timestamp)
    {
        Logging.ModLog.Info($"actor={_actorNumber} PlayerLeft event at t={timestamp:F3}");
        Add(ReplayEventType.PlayerLeft, null, timestamp);
    }

    public void RecordSoundEffect(int soundIndex, float volume, bool stopCurrentAudio, double timestamp)
    {
        Logging.ModLog.Debug(
            $"[sfx] actor={_actorNumber} sound={soundIndex} vol={volume:F2} stop={stopCurrentAudio} at t={timestamp:F3}");
        Add(ReplayEventType.SoundEffect, new SoundEffectData
        {
            SoundIndex = soundIndex,
            Volume = volume,
            StopCurrentAudio = stopCurrentAudio
        }, timestamp);
    }

    public void RecordHandTap(int soundIndex, float volume, bool isLeftHand, double timestamp)
    {
        Logging.ModLog.Debug(
            $"[tap] actor={_actorNumber} sound={soundIndex} vol={volume:F2} left={isLeftHand} at t={timestamp:F3}");
        Add(ReplayEventType.HandTap, new HandTapData
        {
            SoundIndex = soundIndex,
            Volume = volume,
            IsLeftHand = isLeftHand
        }, timestamp);
    }

    public void Dispose()
    {
        Logging.ModLog.Debug($"actor={_actorNumber} recorder disposed ({Events.Count} events)");
        _rig.OnColorChanged -= _colorHandler;
        _rig.OnMaterialIndexChanged -= _materialHandler;
        _rig.OnPlayerNameVisibleChanged -= _nameHandler;
    }

    private void Add(ReplayEventType type, object payload, double timestamp)
    {
        Events.Add(new ReplayEvent
        {
            Type = type,
            DeltaTime = ConsumeDeltaTime(timestamp),
            Payload = payload
        });
    }

    private float ConsumeDeltaTime(double timestamp)
    {
        var delta = (float)(timestamp - _lastTimestamp);
        if (delta < 0f)
            return 0f;
        _lastTimestamp = timestamp;
        return delta;
    }
}
