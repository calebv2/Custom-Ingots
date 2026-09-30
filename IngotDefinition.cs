using System;
using UnityEngine;

namespace CustomIngots.API;

public sealed class IngotIngredient
{
    public IngotIngredient(uint itemHash, string itemName, int count)
    {
        if (itemHash == 0) throw new ArgumentOutOfRangeException(nameof(itemHash));
        if (string.IsNullOrWhiteSpace(itemName)) throw new ArgumentException("An ingredient needs an item name.", nameof(itemName));
        if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
        ItemHash = itemHash;
        ItemName = itemName;
        Count = count;
    }

    public uint ItemHash { get; }
    public string ItemName { get; }
    public int Count { get; }
}

public sealed class IngotStatScaling
{
    public IngotStatScaling(uint sourceMaterialHash, float damageScale, float durabilityScale)
    {
        if (sourceMaterialHash == 0) throw new ArgumentOutOfRangeException(nameof(sourceMaterialHash));
        if (float.IsNaN(damageScale) || float.IsInfinity(damageScale) || damageScale <= 0f)
            throw new ArgumentOutOfRangeException(nameof(damageScale));
        if (float.IsNaN(durabilityScale) || float.IsInfinity(durabilityScale) || durabilityScale <= 0f)
            throw new ArgumentOutOfRangeException(nameof(durabilityScale));
        SourceMaterialHash = sourceMaterialHash;
        DamageScale = damageScale;
        DurabilityScale = durabilityScale;
    }

    public uint SourceMaterialHash { get; }
    public float DamageScale { get; }
    public float DurabilityScale { get; }
}

// Stable identifiers and appearance are shared by the server and every client.
// Register future ingots in IngotCatalog with unique item, prefab, recipe, and material hashes.
public sealed class IngotDefinition
{
    public IngotDefinition(
        IngotStatScaling statScaling,
        string itemName,
        string sourceItemName,
        uint itemHash,
        uint prefabHash,
        uint recipeHash,
        uint materialHash,
        string materialName,
        IngotIngredient[] ingredients,
        Color tint,
        Color emission,
        params uint[] legacyPrefabHashes)
        : this(itemName, sourceItemName, itemHash, prefabHash, recipeHash, materialHash,
            materialName, ingredients, tint, emission, legacyPrefabHashes)
    {
        StatScaling = statScaling ?? throw new ArgumentNullException(nameof(statScaling));
    }

    public IngotDefinition(
        string itemName,
        string sourceItemName,
        uint itemHash,
        uint prefabHash,
        uint recipeHash,
        uint materialHash,
        string materialName,
        IngotIngredient[] ingredients,
        Color tint,
        Color emission,
        params uint[] legacyPrefabHashes)
    {
        if (string.IsNullOrWhiteSpace(itemName)) throw new ArgumentException("An ingot needs a name.", nameof(itemName));
        if (string.IsNullOrWhiteSpace(sourceItemName)) throw new ArgumentException("An ingot needs a source item.", nameof(sourceItemName));
        if (string.IsNullOrWhiteSpace(materialName)) throw new ArgumentException("An ingot needs a material name.", nameof(materialName));
        if (ingredients == null || ingredients.Length == 0) throw new ArgumentException("An ingot needs ingredients.", nameof(ingredients));
        if (prefabHash == 0 || prefabHash > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(prefabHash), "Network prefab hashes must fit the game's 16-bit wire format.");
        if (itemHash == 0 || recipeHash == 0 || materialHash == 0)
            throw new ArgumentException("Item, recipe, and material hashes must be nonzero.");
        var ingredientHashes = new System.Collections.Generic.HashSet<uint>();
        foreach (var ingredient in ingredients)
        {
            if (ingredient == null || !ingredientHashes.Add(ingredient.ItemHash))
                throw new ArgumentException("Ingredient hashes must be unique and nonnull.", nameof(ingredients));
        }
        if (ingredientHashes.Contains(itemHash))
            throw new ArgumentException("An ingot cannot be an ingredient of its own smelting recipe.", nameof(ingredients));

        ItemName = itemName;
        SourceItemName = sourceItemName;
        ItemHash = itemHash;
        PrefabHash = prefabHash;
        RecipeHash = recipeHash;
        MaterialHash = materialHash;
        MaterialName = materialName;
        Ingredients = Array.AsReadOnly((IngotIngredient[])ingredients.Clone());
        Tint = tint;
        Emission = emission;
        LegacyPrefabHashes = Array.AsReadOnly((uint[])(legacyPrefabHashes ?? Array.Empty<uint>()).Clone());
    }

    public string ItemName { get; }
    public string SourceItemName { get; }
    public uint ItemHash { get; }
    public uint PrefabHash { get; }
    public uint RecipeHash { get; }
    public uint MaterialHash { get; }
    public string MaterialName { get; }
    public System.Collections.Generic.IReadOnlyList<IngotIngredient> Ingredients { get; }
    public Color Tint { get; }
    public Color Emission { get; }
    public IngotStatScaling? StatScaling { get; }
    public System.Collections.Generic.IReadOnlyList<uint> LegacyPrefabHashes { get; }
}
