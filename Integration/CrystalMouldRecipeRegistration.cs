using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Alta;
using Alta.Blacksmithing;
using Alta.Inventory;
using UnityEngine;

using CustomIngots.API;

namespace CustomIngots.Config.Integration;

public static class CrystalMouldRecipeRegistration
{
    public const uint CrystalGemBlueItemHash = 45754u;
    private const uint RecipeHashOffset = 0x43570000u;
    private const uint RecipeHashMultiplier = 0x9E3779B1u;
    private const string RecipeNamePrefix = "Crystal Mould ";

    private static readonly FieldInfo ItemCountItemField = typeof(ItemCount).GetField("item", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(typeof(ItemCount).FullName, "item");
    private static readonly FieldInfo ItemCountCountField = typeof(ItemCount).GetField("count", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(typeof(ItemCount).FullName, "count");

    private static IReadOnlyList<CrystalMouldTarget> targets = Array.Empty<CrystalMouldTarget>();
    private static readonly HashSet<uint> RegisteredIngotHashes = new HashSet<uint>();
    private static readonly Dictionary<ulong, CrystalMouldTarget> TargetsByMouldAndIngot = new Dictionary<ulong, CrystalMouldTarget>();
    private static readonly Dictionary<uint, CrystalMouldTarget> TargetsByRecipeHash = new Dictionary<uint, CrystalMouldTarget>();

    public static IReadOnlyList<CrystalMouldTarget> Register()
    {
        Item.CheckItems();
        var crystalIngot = Item.All.FirstOrDefault(item => item.Hash == IngotCatalog.Crystal.ItemHash);
        if (crystalIngot == null) throw new InvalidOperationException("Crystal Ingot must be registered before its mould recipes.");
        return Register(IngotCatalog.Crystal, crystalIngot);
    }

    public static IReadOnlyList<CrystalMouldTarget> Register(IngotDefinition ingotDefinition, Item crystalIngot)
    {
        if (ingotDefinition == null) throw new ArgumentNullException(nameof(ingotDefinition));
        if (crystalIngot == null || crystalIngot.Hash != ingotDefinition.ItemHash)
            throw new InvalidOperationException("Mould recipe ingot does not match its definition.");
        if (RegisteredIngotHashes.Contains(ingotDefinition.ItemHash))
            return targets.Where(target => target.Ingot.ItemHash == ingotDefinition.ItemHash).ToArray();

        Item.CheckItems();
        SmeltingRecipe.CheckItems();

        var template = IngotSmeltingRecipeRegistration.GetVanillaTemplate();

        var recipeRegistry = GetRecipeRegistry();
        var registeredTargets = new List<CrystalMouldTarget>();
        var allDefinitions = MouldDefinition.All.ToArray();
        foreach (var missingDefinition in allDefinitions.Where(definition => definition == null))
        {
            Core.Logger.Warning("Skipping mould registry entry because its MouldDefinition is missing.");
        }

        var candidates = allDefinitions
            .Where(definition => definition != null)
            .OrderBy(definition => definition.Product == null ? string.Empty : definition.Product.name, StringComparer.Ordinal)
            .ThenBy(definition => definition.Hash)
            .ToArray();

        foreach (var definition in candidates)
        {
            var product = definition.Product;
            var cost = definition.Cost;
            var outputQuantity = definition.QuantityProduced;
            if (product == null)
            {
                Core.Logger.Warning("Skipping mould " + definition.Hash + ": product is missing.");
                continue;
            }
            if (cost <= 0)
            {
                Core.Logger.Warning("Skipping mould " + definition.Hash + " for " + product.name + ": cost " + cost + " is invalid.");
                continue;
            }
            if (outputQuantity <= 0)
            {
                Core.Logger.Warning("Skipping mould " + definition.Hash + " for " + product.name + ": output quantity " + outputQuantity + " is invalid.");
                continue;
            }
            if (product.Prefab == null)
            {
                Core.Logger.Warning("Skipping mould " + definition.Hash + " for " + product.name + ": product prefab is missing.");
                continue;
            }
            if (definition.AllowedMaterials?.Items?.Any(item => item != null && item.Hash == crystalIngot.Hash) != true)
            {
                Core.Logger.Warning("Skipping mould " + definition.Hash + " for " + product.name
                    + ": " + ingotDefinition.ItemName + " is absent from its allowed materials.");
                continue;
            }

            var recipeHash = GetRecipeHash(definition.Hash, ingotDefinition);
            if (recipeRegistry.ContainsKey(recipeHash) || TargetsByRecipeHash.ContainsKey(recipeHash))
            {
                Core.Logger.Warning("Skipping mould " + definition.Hash + " for " + product.name + ": deterministic recipe hash " + recipeHash + " collides with a registered recipe.");
                continue;
            }

            try
            {
                var recipe = CreateRecipe(template, definition, product, cost, outputQuantity, crystalIngot, ingotDefinition, recipeHash);
                recipeRegistry.Add(recipe.Hash, recipe);
                AddRecipeToAllSmelterUpgradeSets(recipe);
                var target = new CrystalMouldTarget(definition, product, ingotDefinition, cost, outputQuantity, recipe);
                TargetsByMouldAndIngot.Add(GetTargetKey(definition.Hash, ingotDefinition.ItemHash), target);
                TargetsByRecipeHash.Add(recipe.Hash, target);
                registeredTargets.Add(target);
                Core.Logger.Msg("Registered Crystal mould product " + product.name + "(" + product.Hash + "): mould="
                    + definition.Hash + ", " + ingotDefinition.ItemName + " cost=" + cost + ", output=" + outputQuantity
                    + ", recipe=" + recipe.Hash + ".");
            }
            catch (Exception exception)
            {
                Core.Logger.Error("Could not register Crystal mould " + definition.Hash + " for " + product.name + ": " + exception);
            }
        }

        if (registeredTargets.Count > 0)
        {
            CrystalSmelterInputFilter.Allow(crystalIngot);
        }

        targets = targets.Concat(registeredTargets).ToArray();
        RegisteredIngotHashes.Add(ingotDefinition.ItemHash);
        Core.Logger.Msg("Crysteel registered " + registeredTargets.Count + " of " + candidates.Length
            + " discovered mould products for " + ingotDefinition.ItemName + ".");
        return registeredTargets;
    }

    public static CrystalMouldTarget? FindTarget(uint mouldHash)
    {
        return FindTargetForIngot(mouldHash, IngotCatalog.Crystal.ItemHash);
    }

    public static CrystalMouldTarget? FindTarget(uint mouldHash, uint productHash)
    {
        return FindTarget(mouldHash, productHash, IngotCatalog.Crystal.ItemHash);
    }

    public static CrystalMouldTarget? FindTargetForIngot(uint mouldHash, uint ingotHash)
    {
        return TargetsByMouldAndIngot.TryGetValue(GetTargetKey(mouldHash, ingotHash), out var target) ? target : null;
    }

    public static CrystalMouldTarget? FindTarget(uint mouldHash, uint productHash, uint ingotHash)
    {
        var target = FindTargetForIngot(mouldHash, ingotHash);
        return target != null && target.Product.Hash == productHash ? target : null;
    }

    public static SmeltingRecipe? FindRecipe(uint recipeHash)
    {
        return TargetsByRecipeHash.TryGetValue(recipeHash, out var target) ? target.Recipe : null;
    }

    public static CrystalMouldTarget? FindTargetForRecipe(uint recipeHash)
    {
        return TargetsByRecipeHash.TryGetValue(recipeHash, out var target) ? target : null;
    }

    private static SmeltingRecipe CreateRecipe(
        SmeltingRecipe template,
        MouldDefinition definition,
        Item product,
        int cost,
        int outputQuantity,
        Item crystalIngot,
        IngotDefinition ingotDefinition,
        uint recipeHash)
    {
        var recipe = UnityEngine.Object.Instantiate(template);
        recipe.name = (ingotDefinition.ItemHash == IngotCatalog.Crystal.ItemHash
            ? RecipeNamePrefix : ingotDefinition.ItemName + " Mould ") + product.name + " " + definition.Hash;
        AssignStableHash(recipe, recipeHash, recipe.name);

        var inputs = EnsureOneInput(recipe);
        var outputs = ReadItemCounts(recipe, "output");
        SetItem(inputs[0], crystalIngot);
        SetCount(inputs[0], cost);
        SetItem(outputs[0], product);
        SetCount(outputs[0], outputQuantity);
        return recipe;
    }

    private static ItemCount[] EnsureOneInput(SmeltingRecipe recipe)
    {
        var inputs = ReadItemCounts(recipe, "input");
        if (inputs.Length != 1) throw new InvalidOperationException("Smelting recipe template must have one input.");
        return inputs;
    }

    private static uint GetRecipeHash(uint mouldHash, IngotDefinition ingot)
    {
        // Multiplication by an odd number is one-to-one over UInt32 values.
        // Crystal retains its original mould recipe hashes for saved smelts.
        var variantOffset = ingot.ItemHash == IngotCatalog.Crystal.ItemHash
            ? 0u : unchecked(ingot.ItemHash * 0x85EBCA6Bu);
        return unchecked(mouldHash * RecipeHashMultiplier + RecipeHashOffset + variantOffset);
    }

    private static ulong GetTargetKey(uint mouldHash, uint ingotHash) => ((ulong)mouldHash << 32) | ingotHash;

    private static ItemCount[] ReadItemCounts(SmeltingRecipe recipe, string fieldName)
    {
        var field = typeof(SmeltingRecipe).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(typeof(SmeltingRecipe).FullName, fieldName);
        return field.GetValue(recipe) as ItemCount[]
            ?? throw new InvalidOperationException("Smelting recipe " + fieldName + " array is unavailable.");
    }

    private static void SetItem(ItemCount itemCount, Item item)
    {
        ItemCountItemField.SetValue(itemCount, item);
    }

    private static void SetCount(ItemCount itemCount, int count)
    {
        if (ItemCountCountField.FieldType != typeof(int))
        {
            throw new MissingFieldException(typeof(ItemCount).FullName, "count (expected Int32)");
        }

        ItemCountCountField.SetValue(itemCount, count);
    }

    private static void AssignStableHash(HashedGeneralValue value, uint hash, string name)
    {
        var field = typeof(HashedGeneralValue).GetField("hash", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(typeof(HashedGeneralValue).FullName, "hash");
        field.SetValue(value, unchecked((int)hash));
        if (value.Hash != hash) throw new InvalidOperationException("Could not assign stable recipe hash " + hash + " to " + name + ".");
    }

    private static Dictionary<uint, SmeltingRecipe> GetRecipeRegistry()
    {
        var registryType = typeof(HashedGeneralValue<SmeltingRecipe>);
        var field = registryType.GetField("items", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(registryType.FullName, "items");
        return field.GetValue(null) as Dictionary<uint, SmeltingRecipe>
            ?? throw new InvalidOperationException("Smelting recipe registry is unavailable.");
    }

    private static void AddRecipeToAllSmelterUpgradeSets(SmeltingRecipe recipe)
    {
        var recipesField = typeof(SmelterUpgrades).GetField("recipes", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(typeof(SmelterUpgrades).FullName, "recipes");
        foreach (var upgrades in SmelterUpgrades.All)
        {
            var recipes = recipesField.GetValue(upgrades) as SmeltingRecipe[]
                ?? throw new InvalidOperationException("Smelter upgrade recipe list is unavailable.");
            if (recipes.Any(existing => existing != null && existing.Hash == recipe.Hash)) continue;
            recipesField.SetValue(upgrades, recipes.Concat(new[] { recipe }).ToArray());
        }
    }
}
