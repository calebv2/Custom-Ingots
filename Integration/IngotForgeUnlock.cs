using System;
using System.Linq;
using System.Reflection;
using Alta.Blacksmithing;
using Alta.Inventory;

namespace CustomIngots.Config.Integration;

internal static class IngotForgeUnlock
{
    private static readonly FieldInfo PhysicalMaterialsField = typeof(SmelterUpgrades)
        .GetField("physicalMaterials", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(typeof(SmelterUpgrades).FullName, "physicalMaterials");

    internal static void Register(Item ingotItem, PhysicalMaterial material)
    {
        if (ingotItem == null || material == null) throw new ArgumentNullException(ingotItem == null ? nameof(ingotItem) : nameof(material));
        MouldDefinition.CheckItems();
        SmelterUpgrades.CheckItems();

        var mouldCount = 0;
        var addedToMoulds = 0;
        foreach (var mould in MouldDefinition.All)
        {
            if (mould == null || mould.Product == null || mould.Cost <= 0 || mould.QuantityProduced <= 0) continue;
            mouldCount++;
            var allowed = mould.AllowedMaterials?.Items;
            if (allowed == null)
            {
                Core.Logger.Warning("Mould " + mould.Hash + " has no allowed-material item set; " + ingotItem.name + " cannot be added.");
                continue;
            }

            if (allowed.All(existing => existing == null || existing.Hash != ingotItem.Hash))
            {
                allowed.Add(ingotItem);
                addedToMoulds++;
            }
        }

        var upgradeCount = 0;
        var addedToUpgrades = 0;
        foreach (var upgrades in SmelterUpgrades.All)
        {
            if (upgrades == null) continue;
            upgradeCount++;
            var materials = PhysicalMaterialsField.GetValue(upgrades) as PhysicalMaterial[];
            if (materials == null)
            {
                Core.Logger.Warning("Smelter upgrade " + upgrades.Hash + " has no physical-material list.");
                continue;
            }

            if (materials.All(existing => existing == null || existing.Hash != material.Hash))
            {
                PhysicalMaterialsField.SetValue(upgrades, materials.Concat(new[] { material }).ToArray());
                addedToUpgrades++;
            }
        }

        Core.Logger.Msg("Forge material unlock: " + ingotItem.name + "#" + ingotItem.Hash
            + " allowed on " + mouldCount + " moulds (new=" + addedToMoulds + ") and "
            + upgradeCount + " smelter upgrade sets (new=" + addedToUpgrades + ").");
    }
}
