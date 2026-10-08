using System;
using System.Reflection;
using Alta.Networking;
using HarmonyLib;

using CustomIngots.API;

namespace CustomIngots.Config.Integration;

internal static class IngotSpawnActivation
{
    private static Action<string>? log;

    internal static void Install(HarmonyLib.Harmony harmony, Action<string> logger)
    {
        log = logger;
        var initialize = typeof(NetworkEntity).GetMethod(
            "Initialize",
            BindingFlags.Instance | BindingFlags.Public,
            null,
            new[] { typeof(INetworkScene), typeof(NetworkEntity), typeof(uint), typeof(uint[]), typeof(bool) },
            null)
            ?? throw new MissingMethodException(typeof(NetworkEntity).FullName, "Initialize(INetworkScene, NetworkEntity, UInt32, UInt32[], Boolean)");
        var prefix = AccessTools.Method(typeof(IngotSpawnActivation), nameof(ActivateSpawnedIngot))
            ?? throw new MissingMethodException(typeof(IngotSpawnActivation).FullName, nameof(ActivateSpawnedIngot));

        harmony.Patch(initialize, prefix: new HarmonyMethod(prefix));
    }

    private static void ActivateSpawnedIngot(NetworkEntity __instance)
    {
        if (__instance == null || __instance.gameObject.activeSelf) return;
        var prefab = __instance.GetComponent<NetworkPrefab>();
        if (prefab == null || IngotCatalog.FindByPrefabHash(prefab.Hash) == null) return;

        __instance.gameObject.SetActive(true);
        log?.Invoke("Activated spawned ingot: entity=" + __instance.name + ", prefab=" + prefab.Hash + ".");
    }
}
