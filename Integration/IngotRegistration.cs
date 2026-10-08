using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Alta;
using Alta.Inventory;
using Alta.Networking;
using UnityEngine;

using CustomIngots.API;

namespace CustomIngots.Config.Integration;

public static class IngotRegistration
{
    private static readonly FieldInfo HashedValueHashField = typeof(HashedGeneralValue)
        .GetField("hash", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(typeof(HashedGeneralValue).FullName, "hash");
    private static readonly FieldInfo IngotMaterialField = typeof(Ingot)
        .GetField("physicalMaterial", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(typeof(Ingot).FullName, "physicalMaterial");
    private static readonly FieldInfo ItemComponentsField = typeof(Item)
        .GetField("components", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(typeof(Item).FullName, "components");
    private static readonly FieldInfo PhysicalPartMaterialField = typeof(PhysicalMaterialPart)
        .GetField("physicalMaterial", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(typeof(PhysicalMaterialPart).FullName, "physicalMaterial");
    private static readonly FieldInfo NetworkPrefabHashField = typeof(NetworkPrefab)
        .GetField("hash", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(typeof(NetworkPrefab).FullName, "hash");

    public static Item CreateAndRegister(IngotDefinition definition, PhysicalMaterial material)
    {
        if (definition == null) throw new ArgumentNullException(nameof(definition));
        if (material == null) throw new ArgumentNullException(nameof(material));
        if (material.Hash != definition.MaterialHash)
            throw new InvalidOperationException("Ingot material hash does not match " + definition.ItemName + ".");
        Item.CheckItems();
        PhysicalMaterial.CheckItems();

        var itemRegistry = GetItemRegistry();
        if (itemRegistry.TryGetValue(definition.ItemHash, out var registeredItem))
        {
            if (registeredItem != null
                && string.Equals(registeredItem.name, definition.ItemName, StringComparison.Ordinal)
                && registeredItem.Prefab != null
                && registeredItem.Prefab.Hash == definition.PrefabHash)
            {
                return registeredItem;
            }

            throw new InvalidOperationException(definition.ItemName + " item hash is already registered by another item.");
        }

        if (PrefabManager.Exists(definition.PrefabHash))
        {
            throw new InvalidOperationException(definition.ItemName + " prefab hash is already registered.");
        }
        foreach (var alias in definition.LegacyPrefabHashes)
        {
            if (PrefabManager.Exists(alias))
                throw new InvalidOperationException(definition.ItemName + " legacy prefab hash " + alias + " is already registered.");
        }

        var source = Item.All.FirstOrDefault(item => string.Equals(item.name, definition.SourceItemName, StringComparison.Ordinal));
        if (source == null || source.Prefab == null)
        {
            throw new InvalidOperationException("Could not resolve source ingot item and prefab " + definition.SourceItemName + ".");
        }

        var item = UnityEngine.Object.Instantiate(source);
        item.name = definition.ItemName;
        AssignHash(item, definition.ItemHash);
        // Item cloning preserves references to its ScriptableObject components.
        // Clone them before changing the ingot material, otherwise plain Iron
        // and every previously registered custom ingot acquire that material.
        var components = new List<ItemComponent>();
        foreach (var sourceComponent in source.Components)
        {
            if (sourceComponent == null) continue;
            var component = UnityEngine.Object.Instantiate(sourceComponent);
            ((IItemComponent)component).Initialize(item);
            components.Add(component);
        }
        ItemComponentsField.SetValue(item, components);
        var ingot = item.Components.OfType<Ingot>().FirstOrDefault();
        if (ingot == null) throw new InvalidOperationException("The cloned source ingot item has no Ingot component.");
        IngotMaterialField.SetValue(ingot, material);

        var prefabObject = UnityEngine.Object.Instantiate(source.Prefab.gameObject);
        prefabObject.name = definition.ItemName;
        var prefab = prefabObject.GetComponent<NetworkPrefab>();
        var pickup = prefabObject.GetComponent<Pickup>();
        if (prefab == null || pickup == null)
        {
            throw new InvalidOperationException("The cloned source ingot prefab has no NetworkPrefab or Pickup component.");
        }

        NetworkPrefabHashField.SetValue(prefab, unchecked((int)definition.PrefabHash));
        pickup.Item = item;
        item.Prefab = prefab;
        var materialPart = pickup.PhysicalMaterial;
        if (materialPart == null)
        {
            throw new InvalidOperationException("The cloned source ingot prefab has no PhysicalMaterialPart.");
        }

        PhysicalPartMaterialField.SetValue(materialPart, material);
        prefab.Initialize();
        if (prefabObject.transform.parent != null) prefabObject.transform.SetParent(null, true);
        UnityEngine.Object.DontDestroyOnLoad(prefabObject);
        prefabObject.SetActive(false);
        AddPrefabToMap(prefab);
        AddLegacyPrefabAliases(prefab, definition.LegacyPrefabHashes);
        itemRegistry.Add(item.Hash, item);

        if (item.Hash != definition.ItemHash
            || item.Prefab == null
            || item.Prefab.Hash != definition.PrefabHash)
        {
            throw new InvalidOperationException(definition.ItemName + " item/prefab registration did not retain its stable hashes.");
        }

        return item;
    }

    private static void AssignHash(HashedGeneralValue value, uint hash)
    {
        HashedValueHashField.SetValue(value, unchecked((int)hash));
        if (value.Hash != hash) throw new InvalidOperationException("Could not assign stable ingot item hash " + hash + ".");
    }

    private static Dictionary<uint, Item> GetItemRegistry()
    {
        var registryField = typeof(HashedGeneralValue<Item>)
            .GetField("items", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(typeof(HashedGeneralValue<Item>).FullName, "items");
        return registryField.GetValue(null) as Dictionary<uint, Item>
            ?? throw new InvalidOperationException("The item registry is unavailable.");
    }

    private static void AddPrefabToMap(NetworkPrefab prefab)
    {
        var method = typeof(PrefabManager).GetMethod("AddToPrefabMap", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(typeof(PrefabManager).FullName, "AddToPrefabMap");
        method.Invoke(null, new object[] { new[] { prefab } });
    }

    private static void AddLegacyPrefabAliases(NetworkPrefab prefab, IReadOnlyList<uint> aliases)
    {
        if (aliases.Count == 0) return;
        var field = typeof(PrefabManager).GetField("prefabMap", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(typeof(PrefabManager).FullName, "prefabMap");
        var map = field.GetValue(null) as Dictionary<uint, NetworkPrefab>
            ?? throw new InvalidOperationException("The NetworkPrefab registry is unavailable.");
        foreach (var alias in aliases) map.Add(alias, prefab);
    }
}
