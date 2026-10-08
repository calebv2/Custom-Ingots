using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Alta.Blacksmithing;
using Alta.Inventory;
using HarmonyLib;
using UnityEngine;

using CustomIngots.API;

namespace CustomIngots.Config.Integration;

[HarmonyPatch(typeof(Smelter), "FindRecipe")]
[HarmonyPriority(Priority.First)]
internal static class CrystalSmelterRecipeGate
{
    private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly FieldInfo CurrentMouldField = GetField("currentMould");
    private static readonly FieldInfo OreDocksField = GetField("oreDocks");
    private static readonly FieldInfo SmeltingPlayersField = GetField("smeltingPlayers");
    private static readonly FieldInfo OreSpriteField = GetField("oreSprite");
    private static readonly FieldInfo OreAndroidRendererField = GetField("oreAndroidRenderer");
    private static readonly MethodInfo CheckSpriteZeroMethod = typeof(Smelter).GetMethod(
        "CheckSpriteZero", PrivateInstance, null,
        new[] { typeof(PickupDock[]), typeof(SpriteRenderer), typeof(Renderer) }, null)
        ?? throw new MissingMethodException(typeof(Smelter).FullName, "CheckSpriteZero");

    private static bool Prefix(
        Smelter __instance,
        float totalFuel,
        Dictionary<Item, int> input,
        ref float requiredFuel,
        ref SmeltingRecipe? __result)
    {
        if (input == null || input.Count == 0) return true;

        // A registered ingot is the only input to its mould recipe. The
        // mould determines the normal ingot cost and the output product.
        var mouldIngot = input.Count == 1 ? IngotCatalog.FindByItemHash(input.First().Key?.Hash ?? 0u) : null;
        if (mouldIngot != null)
        {
            var mould = CurrentMouldField.GetValue(__instance) as Mould;
            var definition = mould?.Definition;
            if (definition != null)
            {
                var target = CrystalMouldRecipeRegistration.FindTarget(
                    definition.Hash, definition.Product == null ? 0u : definition.Product.Hash, mouldIngot.ItemHash);
                if (target == null)
                    return Reject(ref requiredFuel, ref __result, mouldIngot.ItemName + " has no registered matching mould.");

                return Select(__instance, totalFuel, input, target.Recipe,
                    new Dictionary<uint, int> { [mouldIngot.ItemHash] = target.Cost },
                    ref requiredFuel, ref __result);
            }
        }

        // Each ingot definition can have one or more ore ingredients. Other
        // ingredient combinations remain available to vanilla and other mods.
        foreach (var ingot in IngotCatalog.All)
        {
            if (input.Count != ingot.Ingredients.Count) continue;
            if (ingot.Ingredients.Any(ingredient => !input.Any(entry => entry.Key?.Hash == ingredient.ItemHash))) continue;

            var recipe = IngotSmeltingRecipeRegistration.FindRecipe(ingot.RecipeHash);
            if (recipe == null) return Reject(ref requiredFuel, ref __result, ingot.ItemName + " smelting recipe is unavailable.");
            var costs = ingot.Ingredients.ToDictionary(ingredient => ingredient.ItemHash, ingredient => ingredient.Count);
            return Select(__instance, totalFuel, input, recipe, costs, ref requiredFuel, ref __result);
        }

        return true;
    }

    private static bool Select(
        Smelter smelter,
        float totalFuel,
        Dictionary<Item, int> input,
        SmeltingRecipe recipe,
        IReadOnlyDictionary<uint, int> costs,
        ref float requiredFuel,
        ref SmeltingRecipe? result)
    {
        if (input.Any(entry => entry.Key == null || entry.Value <= 0))
            return Reject(ref requiredFuel, ref result, "Input contains a missing item or nonpositive count.");
        foreach (var cost in costs)
        {
            if (!input.Any(entry => entry.Key.Hash == cost.Key && entry.Value >= cost.Value))
                return Reject(ref requiredFuel, ref result, "Input count for item " + cost.Key + " is below " + cost.Value + ".");
        }

        if (totalFuel < recipe.Duration)
            return Reject(ref requiredFuel, ref result, "Available fuel is below recipe duration " + recipe.Duration + ".");

        try
        {
            if (OreDocksField.GetValue(smelter) is not PickupDock[] oreDocks)
                return Reject(ref requiredFuel, ref result, "Smelter ore docks are unavailable.");

            var dockHashes = new uint[oreDocks.Length];
            var dockCounts = new int[oreDocks.Length];
            var dockHasPickups = new bool[oreDocks.Length];
            for (var index = 0; index < oreDocks.Length; index++)
            {
                var dock = oreDocks[index];
                dockHashes[index] = dock?.dockedType?.Hash ?? 0u;
                dockCounts[index] = dock?.QuantityStored ?? 0;
                dockHasPickups[index] = dock?.DockedPickup != null;
            }

            if (!IngotConsumptionPolicy.TryPlan(dockHashes, dockCounts, dockHasPickups,
                    costs, out var amountsByDock, out var blockedDockIndex))
            {
                var reason = blockedDockIndex >= 0
                    ? "Ore dock " + blockedDockIndex + " has no live pickup and cannot be partially consumed."
                    : "Required ore ingredients are unavailable across the smelter docks.";
                return Reject(ref requiredFuel, ref result, reason);
            }

            ConsumePlannedInputs(smelter, oreDocks, amountsByDock);
            requiredFuel = recipe.Duration;
            result = recipe;
            Core.Logger.Msg("Crysteel selected recipe " + recipe.name + "#" + recipe.Hash + ".");
            return false;
        }
        catch (Exception exception)
        {
            requiredFuel = 0f;
            result = null;
            Core.Logger.Error("Crysteel input consumption failed for recipe " + recipe.Hash + ": " + exception);
            return false;
        }
    }

    private static void ConsumePlannedInputs(Smelter smelter, PickupDock[] oreDocks, int[] amountsByDock)
    {
        // Validate the whole plan before changing any dock.
        for (var index = 0; index < oreDocks.Length; index++)
        {
            var amount = amountsByDock[index];
            if (amount <= 0) continue;
            var dock = oreDocks[index];
            if (dock == null || dock.QuantityStored < amount
                || (dock.DockedPickup == null && dock.QuantityStored != amount))
                throw new InvalidOperationException("Ore dock changed after the consumption plan was created.");
        }

        var smeltingPlayers = SmeltingPlayersField.GetValue(smelter) as HashSet<Player>;
        smeltingPlayers?.Clear();
        for (var index = 0; index < oreDocks.Length; index++)
        {
            var amount = amountsByDock[index];
            if (amount <= 0) continue;
            var dock = oreDocks[index];
            var pickup = dock.DockedPickup;
            if (pickup == null)
            {
                dock.QuantityStored = 0;
                Core.Logger.Warning("Consumed exact server-stored ore count from dock '" + dock.name + "' with no live pickup.");
                continue;
            }

            var player = pickup.LastPlayerInteractor?.NetworkPlayer;
            if (player != null) smeltingPlayers?.Add(player);
            dock.DestroyAmount(amount);
        }

        CheckSpriteZeroMethod.Invoke(smelter, new[]
        {
            (object)oreDocks,
            OreSpriteField.GetValue(smelter),
            OreAndroidRendererField.GetValue(smelter)
        });
    }

    private static bool Reject(ref float requiredFuel, ref SmeltingRecipe? result, string reason)
    {
        requiredFuel = 0f;
        result = null;
        Core.Logger.Msg("Crysteel recipe rejected: " + reason);
        return false;
    }

    private static FieldInfo GetField(string name) => typeof(Smelter).GetField(name, PrivateInstance)
        ?? throw new MissingFieldException(typeof(Smelter).FullName, name);
}
