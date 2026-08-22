using System;
using System.Collections.Generic;
using GorillaNetworking;
using GorillaTag.CosmeticSystem;
using ReplayMod.Logging;

namespace ReplayMod.Playback;

internal static class GhostCosmetics
{
    private const string NothingSlot = "NOTHING";
    private const int SlotCount = 16;

    private static readonly Dictionary<VRRig, string[]> Desired = [];
    private static readonly HashSet<VRRig> RegisteredRigs = [];
    private static readonly HashSet<VRRig> AppliedRigs = [];
    private static readonly HashSet<VRRig> FailedRigs = [];
    private static bool _loggedDeferred;

    public static void Apply(VRRig rig, string[] slotNames)
    {
        if (!rig || slotNames == null)
            return;

        Desired[rig] = NormalizeSlots(slotNames);
        TryApply(rig);
    }

    public static void Tick()
    {
        if (Desired.Count == 0)
            return;

        List<VRRig> stale = null;
        foreach (var rig in Desired.Keys)
        {
            if (!rig)
            {
                stale ??= [];
                stale.Add(rig);
            }
            else if (!AppliedRigs.Contains(rig))
            {
                TryApply(rig);
            }
        }

        if (stale == null)
            return;

        foreach (var rig in stale)
            Forget(rig);
    }

    public static void ClearVisualState(VRRig rig)
    {
        if (!rig)
            return;

        var controller = CosmeticsController.instance;
        if (!controller)
            return;

        try
        {
            rig.prevSet?.ClearSet(controller.nullItem);
            rig.mergedSet?.ClearSet(controller.nullItem);

            var slots = new string[SlotCount];
            for (var i = 0; i < SlotCount; i++)
                slots[i] = NothingSlot;

            rig.cosmeticSet = new CosmeticsController.CosmeticSet(slots, controller);
            rig.SetCosmeticsActive(playfx: false);
        }
        catch (Exception e)
        {
            ModLog.Warn($"[cos] failed to clear cosmetics on '{rig.name}': {e.Message}");
        }
    }

    public static void Reset()
    {
        Desired.Clear();
        _loggedDeferred = false;
    }

    private static void Forget(VRRig rig)
    {
        Desired.Remove(rig);
        RegisteredRigs.Remove(rig);
        AppliedRigs.Remove(rig);
        FailedRigs.Remove(rig);
    }

    private static void TryApply(VRRig rig)
    {
        if (!Desired.TryGetValue(rig, out var slots))
            return;

        var controller = CosmeticsController.instance;
        if (!controller)
            return;

        if (!EnsureRegistered(rig, controller))
            return;

        try
        {
            if (AppliedRigs.Add(rig))
            {
                rig.prevSet?.ClearSet(controller.nullItem);
                rig.mergedSet?.ClearSet(controller.nullItem);
            }

            var worn = new string[slots.Length];
            Array.Copy(slots, worn, slots.Length);

            var targetSet = new CosmeticsController.CosmeticSet(worn, controller);
            foreach (var item in targetSet.items)
            {
                if (!item.isNullItem && !string.IsNullOrEmpty(item.itemName))
                    rig.AddCosmetic(item.itemName);
            }

            rig.cosmeticSet = targetSet;
            rig.SetCosmeticsActive(playfx: false);
        }
        catch (Exception e)
        {
            AppliedRigs.Remove(rig);
            ModLog.Warn($"[cos] failed to apply cosmetics to '{rig.name}': {e.Message}");
        }
    }

    private static string[] NormalizeSlots(string[] slotNames)
    {
        var slots = new string[SlotCount];
        for (var i = 0; i < SlotCount; i++)
        {
            var name = i < slotNames.Length ? slotNames[i] : null;
            slots[i] = string.IsNullOrEmpty(name) ? NothingSlot : name;
        }
        return slots;
    }

    private static bool EnsureRegistered(VRRig rig, CosmeticsController controller)
    {
        if (RegisteredRigs.Contains(rig))
            return true;

        if (FailedRigs.Contains(rig))
            return false;

        if (!CosmeticsV2Spawner_Dirty.isPrepared ||
            CosmeticsV2Spawner_Dirty._g_loadOpInfosForRigAndCosmeticIDDicts == null)
        {
            if (!_loggedDeferred)
            {
                ModLog.Info("[cos] cosmetics spawner still initializing; deferring ghost registration");
                _loggedDeferred = true;
            }
            return false;
        }

        try
        {
            if (!(controller.v2_allCosmeticsInfoAssetRef.Asset is AllCosmeticsArraySO allCosmetics))
            {
                ModLog.Warn("[cos] cosmetics catalog not ready; ghost cosmetics unavailable");
                return false;
            }

            if (CosmeticsV2Spawner_Dirty._gVRRigDatasIndexByRig.ContainsKey(rig))
            {
                RegisteredRigs.Add(rig);
                return true;
            }

            if (!GTHardCodedBones.TryGetBoneXforms(rig, out var boneXforms, out var boneError))
            {
                ModLog.Warn($"[cos] cannot resolve bones for '{rig.name}': {boneError}");
                FailedRigs.Add(rig);
                return false;
            }

            var data = new CosmeticsV2Spawner_Dirty.VRRigData(rig, boneXforms);
            if ((bool)data.bdPositionsComp)
                data.bdPositionsComp._allObjects = new TransferrableObject[2000];

            CosmeticsV2Spawner_Dirty._gVRRigDatas.Add(data);
            var rigIndex = CosmeticsV2Spawner_Dirty._gVRRigDatas.Count - 1;
            CosmeticsV2Spawner_Dirty._gVRRigDatasIndexByRig[rig] = rigIndex;
            EnsureDictionaryArraySize(rigIndex + 1);

            var counter = 0;
            foreach (var assetRef in allCosmetics.sturdyAssetRefs)
            {
                var cosmeticSo = assetRef.obj;
                if (!cosmeticSo)
                    continue;

                var info = cosmeticSo.info;

                if (info.hasHoldableParts)
                    AddParts(info.holdableParts, info, rigIndex, ref counter);

                if (info.hasFunctionalParts)
                    AddParts(info.functionalParts, info, rigIndex, ref counter);
            }

            RegisteredRigs.Add(rig);
            ModLog.Info($"[cos] registered '{rig.name}' as cosmetics rig index {rigIndex} ({counter} load ops)");
            return true;
        }
        catch (Exception e)
        {
            FailedRigs.Add(rig);
            ModLog.Error($"[cos] failed to register ghost rig for cosmetics: {e}");
            return false;
        }
    }

    private static void AddParts(CosmeticPart[] parts, CosmeticInfoV2 info, int rigIndex, ref int counter)
    {
        for (var i = 0; i < parts.Length; i++)
        {
            if (!parts[i].prefabAssetRef.RuntimeKeyIsValid())
                continue;

            CosmeticsV2Spawner_Dirty.AddEachAttachInfoToLoadOpInfosList(parts[i], i, info, rigIndex, ref counter);
        }
    }

    private static void EnsureDictionaryArraySize(int requiredLength)
    {
        var existing = CosmeticsV2Spawner_Dirty._g_loadOpInfosForRigAndCosmeticIDDicts;
        if (existing != null && existing.Length >= requiredLength)
            return;

        var newSize = existing == null ? requiredLength : Math.Max(requiredLength, existing.Length * 2);
        var grown = new Dictionary<string, List<CosmeticsV2Spawner_Dirty.LoadOpInfo>>[newSize];
        if (existing != null)
            Array.Copy(existing, grown, existing.Length);

        CosmeticsV2Spawner_Dirty._g_loadOpInfosForRigAndCosmeticIDDicts = grown;
    }
}
