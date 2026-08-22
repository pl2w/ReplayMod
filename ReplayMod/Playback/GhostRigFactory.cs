using System;
using System.Collections.Generic;
using Photon.Pun;
using Photon.Voice.PUN;
using ReplayMod.Logging;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ReplayMod.Playback;

public static class GhostRigFactory
{
    private static GameObject _template;

    internal static bool IsSpawning { get; private set; }
    
    private static readonly HashSet<Type> TypesToDisable =
    [
        typeof(RigContainer),
        typeof(VRRigReliableState),
        typeof(NetworkView),
        typeof(PhotonView),
        typeof(PhotonVoiceView),
        typeof(GRPlayer)
    ];

    private static readonly HashSet<Type> LckCameraComponentTypes =
    [
        typeof(LckBodyCameraSpawner),
        typeof(LckSocialCamera),
        typeof(LCKSocialCameraFollower)
    ];

    public static VRRig Spawn(int actorNumber)
    {
        EnsureTemplate();
        if (!_template)
        {
            ModLog.Error($"No ghost rig template available; cannot spawn actor={actorNumber}");
            return null;
        }

        IsSpawning = true;
        GameObject instance;
        try { instance = Object.Instantiate(_template); }
        finally { IsSpawning = false; }

        instance.name = $"GhostRig_{actorNumber}";

        var rig = instance.GetComponent<VRRig>();
        if (!rig)
        {
            ModLog.Error($"Instantiated ghost rig missing VRRig for actor={actorNumber}; destroying");
            Object.Destroy(instance);
            return null;
        }

        rig.enabled = false;
        DisableComponentsByType(instance);

        instance.SetActive(true);
        
        rig.bodyRenderer.SetDefaults();
        GorillaSkin.ShowActiveSkin(rig);
        rig.mainSkin.enabled = true;
        
        rig.GetComponent<XRaySkeleton>()?.OnBuildInitialize();

        ModLog.Debug($"Spawned ghost rig actor={actorNumber}");
        return rig;
    }

    private static void EnsureTemplate()
    {
        if (_template != null)
            return;

        if (!VRRigCache.Instance || !VRRigCache.Instance.rigTemplate)
        {
            ModLog.Error("VRRigCache not ready yet.");
            return;
        }

        IsSpawning = true;
        try
        {
            _template = Object.Instantiate(VRRigCache.Instance.rigTemplate);
        }
        finally
        {
            IsSpawning = false;
        }

        _template.name = "GhostRig_Template";
        _template.SetActive(false);
        Object.DontDestroyOnLoad(_template);

        DisableComponentsByType(_template);
        ModLog.Info("Ghost rig template created");
    }

    private static void DisableComponentsByType(GameObject root)
    {
        var behaviours = root.GetComponentsInChildren<Behaviour>(true);
        foreach (var b in behaviours)
        {
            if (b && TypesToDisable.Contains(b.GetType()))
                b.enabled = false;
        }

        foreach (var b in behaviours)
        {
            if (!b || !LckCameraComponentTypes.Contains(b.GetType()))
                continue;

            var go = b.gameObject;
            go.SetActive(false);
            Object.Destroy(go);
        }
    }

    public static void Reset()
    {
        if (_template != null)
            Object.Destroy(_template);
        _template = null;
        ModLog.Debug("Ghost rig template reset");
    }
}