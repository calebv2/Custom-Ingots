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

internal static class IngotSmeltingRecipeRegistration
{
    private static readonly FieldInfo ItemCountItemField = typeof(ItemCount).GetField("item", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(typeof(ItemCount).FullName, "item");
    private static readonly FieldInfo ItemCountCountField = typeof(ItemCount).GetField("count", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(typeof(ItemCount).FullName, "count");
    private static readonly FieldInfo RecipeInputField = typeof(SmeltingRecipe).GetField("input", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(typeof(SmeltingRecipe).FullName, "input");
    private static readonly FieldInfo RecipeOutputField = typeof(SmeltingRecipe).GetField("output", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(typeof(SmeltingRecipe).FullName, "output");

    private static readonly Dictionary<uint, SmeltingRecipe> RegisteredRecipes = new Dictionary<uint, SmeltingRecipe>();
    private static SmeltingRecipe? vanillaTemplate;

    internal static SmeltingRecipe GetVanillaTemplate()
    {
        if (vanillaTemplate != null) return vanillaTemplate;
        SmeltingRecipe.CheckItems();
        vanillaTemplate = SmeltingRecipe.All.FirstOrDefault(recipe =>
            GetCounts(RecipeInputField, recipe).Length == 1 && GetCounts(RecipeOutputField, recipe).Length == 1)
            ?? throw new InvalidOperationException("Could not find a vanilla smelting recipe template with one input and one output.");
        return vanillaTemplate;
    }

    internal static SmeltingRecipe Register(IngotDefinition definition, Item outputItem)
    {
        if (RegisteredRecipes.TryGetValue(definition.RecipeHash, out var existing)) return existing;

        Item.CheckItems();
        SmeltingRecipe.CheckItems();
        var ingredients = definition.Ingredients.Select(spec => new
        {
            Spec = spec,
            Item = Item.All.FirstOrDefault(item => item.Hash == spec.ItemHash
                && string.Equals(item.name, spec.ItemName, StringComparison.Ordinal))
        }).ToArray();
        var missing = ingredients.FirstOrDefault(entry => entry.Item == null);
        if (missing != null)
            throw new InvalidOperationException("Could not resolve ingredient " + missing.Spec.ItemName + "#" + missing.Spec.ItemHash + ".");
        if (outputItem == null || outputItem.Hash != definition.ItemHash)
            throw new InvalidOperationException("Smelting output item does not match " + definition.ItemName + ".");

        var template = GetVanillaTemplate();

        var registry = GetRecipeRegistry();
        if (registry.ContainsKey(definition.RecipeHash))
        {
            throw new InvalidOperationException(definition.ItemName + " smelting recipe hash is already registered.");
        }

        var recipe = UnityEngine.Object.Instantiate(template);
        recipe.name = definition.ItemName + " Recipe";
        AssignHash(recipe, definition.RecipeHash);
        var input = CreateInputs(recipe, ingredients.Length);
        var output = GetCounts(RecipeOutputField, recipe);
        for (var index = 0; index < ingredients.Length; index++)
        {
            SetItem(input[index], ingredients[index].Item!);
            SetCount(input[index], ingredients[index].Spec.Count);
        }
        SetItem(output[0], outputItem);
        SetCount(output[0], 1);

        registry.Add(recipe.Hash, recipe);
        AddRecipeToAllSmelterUpgradeSets(recipe);
        RegisteredRecipes.Add(definition.RecipeHash, recipe);
        return recipe;
    }

    internal static SmeltingRecipe? FindRecipe(uint hash) => RegisteredRecipes.TryGetValue(hash, out var recipe) ? recipe : null;

    private static ItemCount[] CreateInputs(SmeltingRecipe recipe, int count)
    {
        var inputs = GetCounts(RecipeInputField, recipe);
        if (inputs.Length < 1)
        {
            throw new InvalidOperationException("Smelting recipe template must have at least one input entry; found " + inputs.Length + ".");
        }

        var configured = new ItemCount[count];
        for (var index = 0; index < count; index++) configured[index] = new ItemCount();
        RecipeInputField.SetValue(recipe, configured);
        return configured;
    }

    private static ItemCount[] GetCounts(FieldInfo field, SmeltingRecipe recipe)
    {
        return field.GetValue(recipe) as ItemCount[]
            ?? throw new InvalidOperationException("Smelting recipe item-count field is unavailable.");
    }

    private static void SetItem(ItemCount entry, Item item) => ItemCountItemField.SetValue(entry, item);

    private static void SetCount(ItemCount entry, int count)
    {
        if (ItemCountCountField.FieldType != typeof(int))
        {
            throw new MissingFieldException(typeof(ItemCount).FullName, "count (expected Int32)");
        }

        ItemCountCountField.SetValue(entry, count);
    }

    private static void AssignHash(HashedGeneralValue value, uint hash)
    {
        var field = typeof(HashedGeneralValue).GetField("hash", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(typeof(HashedGeneralValue).FullName, "hash");
        field.SetValue(value, unchecked((int)hash));
        if (value.Hash != hash) throw new InvalidOperationException("Could not assign stable ingot recipe hash " + hash + ".");
    }

    private static Dictionary<uint, SmeltingRecipe> GetRecipeRegistry()
    {
        var field = typeof(HashedGeneralValue<SmeltingRecipe>).GetField("items", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(typeof(HashedGeneralValue<SmeltingRecipe>).FullName, "items");
        return field.GetValue(null) as Dictionary<uint, SmeltingRecipe>
            ?? throw new InvalidOperationException("The smelting recipe registry is unavailable.");
    }

    private static void AddRecipeToAllSmelterUpgradeSets(SmeltingRecipe recipe)
    {
        var field = typeof(SmelterUpgrades).GetField("recipes", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(typeof(SmelterUpgrades).FullName, "recipes");
        foreach (var upgrades in SmelterUpgrades.All)
        {
            var registered = field.GetValue(upgrades) as SmeltingRecipe[]
                ?? throw new InvalidOperationException("A smelter upgrade's recipe list is unavailable.");
            if (registered.Any(existing => existing != null && existing.Hash == recipe.Hash)) continue;
            field.SetValue(upgrades, registered.Concat(new[] { recipe }).ToArray());
        }
    }
}
