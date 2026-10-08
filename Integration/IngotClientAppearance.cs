using System;
using System.Collections.Generic;
using Alta.Inventory;
using UnityEngine;

using CustomIngots.API;

namespace CustomIngots.Config.Integration;

internal static class IngotClientAppearance
{
    private const float ScanIntervalSeconds = 1f;
    private static readonly HashSet<int> PreparedIngots = new HashSet<int>();
    private static float nextScanTime;

    internal static void Update()
    {
        if (Time.unscaledTime < nextScanTime) return;
        nextScanTime = Time.unscaledTime + ScanIntervalSeconds;

        var live = new HashSet<int>();
        foreach (var pickup in UnityEngine.Object.FindObjectsOfType<Pickup>())
        {
            if (pickup == null || pickup.Item == null) continue;
            var definition = IngotCatalog.FindByItemHash(pickup.Item.Hash);
            if (definition == null) continue;

            var instanceId = pickup.GetInstanceID();
            live.Add(instanceId);
            if (PreparedIngots.Contains(instanceId)) continue;

            try
            {
                var material = CrystalMaterialRegistration.FindRegistered(definition.MaterialHash);
                var materialPart = pickup.PhysicalMaterial;
                if (material == null || materialPart == null)
                {
                    Core.Logger.Warning("Ingot appearance is waiting for material or PhysicalMaterialPart on " + pickup.name + ".");
                    continue;
                }

                materialPart.SetMaterial(material);
                PreparedIngots.Add(instanceId);
                var renderers = pickup.GetComponentsInChildren<Renderer>(true);
                var enabledRenderers = 0;
                foreach (var renderer in renderers)
                {
                    if (renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy) enabledRenderers++;
                }

                Core.Logger.Msg("Ingot client appearance initialized: pickup=" + pickup.name
                    + ", renderers=" + renderers.Length + ", enabledRenderers=" + enabledRenderers
                    + ", material=" + materialPart.PhysicalMaterial?.Hash + ".");
            }
            catch (Exception exception)
            {
                Core.Logger.Error("Ingot client appearance failed for " + pickup.name + ": " + exception);
            }
        }

        PreparedIngots.IntersectWith(live);
    }
}
