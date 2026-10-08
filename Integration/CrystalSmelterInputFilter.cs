using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Alta.Inventory;
using Alta.Networking;
using HarmonyLib;

namespace CustomIngots.Config.Integration;

[HarmonyPatch(typeof(NetworkPrefab), "Initialize")]
internal static class CrystalSmelterInputFilter
{
    private const uint SmelterPrefabHash = 44646u;
    private const uint SmelterOreDockEntityHash = 42990u;
    private static readonly List<Item> AllowedItems = new List<Item>();

    internal static void Allow(Item item)
    {
        if (item == null || AllowedItems.Any(existing => existing.Hash == item.Hash)) return;
        AllowedItems.Add(item);

        try
        {
            ApplyToPrefab(PrefabManager.GetPrefab(SmelterPrefabHash));
        }
        catch (Exception exception)
        {
            Core.Logger.Warning("Crysteel could not update the existing smelter prefab input list: " + exception);
        }
    }

    private static void Postfix(NetworkPrefab __instance)
    {
        ApplyToPrefab(__instance);
    }

    private static void ApplyToPrefab(NetworkPrefab prefab)
    {
        if (prefab == null || prefab.Hash != SmelterPrefabHash || AllowedItems.Count == 0) return;

        try
        {
            var entitiesField = typeof(NetworkEntityParent).GetField("embeddedEntities", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(typeof(NetworkEntityParent).FullName, "embeddedEntities");
            if (entitiesField.GetValue(prefab) is not List<NetworkEntity> entities) return;

            var oreDockEntity = entities.FirstOrDefault(entity => entity != null && entity.Hash == SmelterOreDockEntityHash);
            var oreDock = oreDockEntity == null ? null : oreDockEntity.gameObject.GetComponent<PickupDock>();
            if (oreDock == null)
            {
                Core.Logger.Warning("Crysteel could not find the smelter ore dock on prefab " + prefab.Hash + ".");
                return;
            }

            var includedItems = oreDock.Settings == null ? null : oreDock.Settings.IncludedItems;
            if (includedItems == null)
            {
                Core.Logger.Warning("Crysteel found the smelter ore dock but its included-item settings were unavailable.");
                return;
            }

            foreach (var item in AllowedItems)
            {
                if (includedItems.All(existing => existing == null || existing.Hash != item.Hash)) includedItems.Add(item);
            }
        }
        catch (Exception exception)
        {
            Core.Logger.Warning("Crysteel could not update smelter prefab " + prefab.Hash + " input settings: " + exception);
        }
    }
}
