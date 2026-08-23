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

    private sealed class RigState
    {
        public string[] Desired = [];
        public bool Registered;
        public bool Applied;
        public bool Failed;
    }

    private static readonly Dictionary<VRRig, RigState> States = [];
    private static bool _loggedDeferred;

    public static void Apply(VRRig rig, string[] slotNames)
    {
        if (!rig)
            return;

        var state = GetOrCreate(rig);
        state.Desired = NormalizeSlots(slotNames ?? []);
        state.Applied = false;
        state.Failed = false;

        TryApply(rig, state);
    }

    public static void Tick()
    {
        if (States.Count == 0)
            return;

        List<VRRig> stale = null;
        foreach (var (rig, state) in States)
        {
            if (!rig)
            {
                stale ??= [];
                stale.Add(rig);
                continue;
            }

            if (!state.Applied && !state.Failed)
                TryApply(rig, state);
        }

        if (stale == null)
            return;

        foreach (var rig in stale)
            States.Remove(rig);
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
            rig.cosmeticSet = EmptySet(controller);
            rig.SetCosmeticsActive(playfx: false);
        }
        catch (Exception e)
        {
            ModLog.Warn($"[cos] failed to clear cosmetics on '{rig.name}': {e.Message}");
        }
    }

    public static void Reset()
    {
        States.Clear();
        _loggedDeferred = false;
    }

    private static RigState GetOrCreate(VRRig rig)
    {
        if (!States.TryGetValue(rig, out var state))
        {
            state = new RigState();
            States[rig] = state;
        }

        return state;
    }

    private static void TryApply(VRRig rig, RigState state)
    {
        if (state.Failed)
            return;

        var controller = CosmeticsController.instance;
        if (!controller)
            return;

        if (!state.Registered && !EnsureRegistered(rig, controller, state))
            return;

        try
        {
            var targetItems = new CosmeticsController.CosmeticItem[SlotCount];
            for (var i = 0; i < SlotCount; i++)
                targetItems[i] = controller.GetItemFromDict(state.Desired[i]);

            var targetSet = new CosmeticsController.CosmeticSet { items = targetItems };
            foreach (var item in targetSet.items)
            {
                if (!item.isNullItem && !string.IsNullOrEmpty(item.itemName))
                    rig.AddCosmetic(item.itemName);
            }

            rig.cosmeticSet = targetSet;
            rig.SetCosmeticsActive(playfx: false);

            state.Applied = true;
        }
        catch (Exception e)
        {
            state.Failed = true;
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

    private static CosmeticsController.CosmeticSet EmptySet(CosmeticsController controller)
    {
        var slots = new string[SlotCount];
        for (var i = 0; i < SlotCount; i++)
            slots[i] = NothingSlot;

        return new CosmeticsController.CosmeticSet(slots, controller);
    }

    private static bool EnsureRegistered(VRRig rig, CosmeticsController controller, RigState state)
    {
        if (state.Registered)
            return true;

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
                state.Failed = true;
                return false;
            }

            if (CosmeticsV2Spawner_Dirty._gVRRigDatasIndexByRig.ContainsKey(rig))
            {
                state.Registered = true;
                return true;
            }

            if (!GTHardCodedBones.TryGetBoneXforms(rig, out var boneXforms, out var boneError))
            {
                ModLog.Warn($"[cos] cannot resolve bones for '{rig.name}': {boneError}");
                state.Failed = true;
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

            state.Registered = true;
            ModLog.Info($"[cos] registered '{rig.name}' as cosmetics rig index {rigIndex} ({counter} load ops)");
            return true;
        }
        catch (Exception e)
        {
            state.Failed = true;
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
