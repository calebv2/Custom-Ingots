using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CustomIngots.API;

public static class IngotCatalog
{
    public const uint CrystalMaterialHash = 0x43574D00u;
    public const uint DarksteelMaterialHash = 55232u;
    public const uint CrysteelMaterialHash = 0x43574D02u;

    public static readonly IngotDefinition Crystal = new IngotDefinition(
        "Crystal Ingot", "Iron Ingot",
        0x43574901u, 0x5001u, 0x43575201u,
        CrystalMaterialHash, "Crysteel Material",
        new[] { new IngotIngredient(45754u, "Crystal Gem Blue", 1) },
        new Color(0f, 210f / 255f, 1f, 0.8f),
        new Color(185f / 255f, 1f, 254f / 255f, 1f),
        0x43575001u);

    public static readonly IngotDefinition Crysteel = new IngotDefinition(
        new IngotStatScaling(DarksteelMaterialHash, 1.10f, 1.10f),
        "Crysteel Ingot", "Iron Ingot",
        0x43574902u, 0x5002u, 0x43575202u,
        CrysteelMaterialHash, "Crysteel",
        new[]
        {
            new IngotIngredient(50292u, "Darksteel Ingot", 1),
            new IngotIngredient(45754u, "Crystal Gem Blue", 20)
        },
        new Color(0.18f, 0.80f, 1f, 0.85f),
        new Color(0.78f, 0.97f, 1f, 1f));

    private static readonly object Sync = new object();
    private static IngotDefinition[] Definitions = { Crystal, Crysteel };
    private static IReadOnlyList<IngotDefinition> ReadOnlyDefinitions = Array.AsReadOnly(Definitions);
    private static bool sealedForRuntime;

    public static IReadOnlyList<IngotDefinition> All => ReadOnlyDefinitions;
    public static bool IsSealed => sealedForRuntime;

    static IngotCatalog()
    {
        Validate(Definitions);
    }

    // Companion mods call this from OnInitializeMelon on both server and client.
    // Registration closes when Crysteel begins late initialization.
    public static void Register(IngotDefinition definition)
    {
        if (definition == null) throw new ArgumentNullException(nameof(definition));
        lock (Sync)
        {
            if (sealedForRuntime)
                throw new InvalidOperationException("Ingot registration is closed. Call IngotCatalog.Register during OnInitializeMelon.");
            if (ReferenceEquals(definition, Crystal)) return;

            var existingIndex = Array.FindIndex(Definitions, candidate => candidate.ItemHash == definition.ItemHash);
            if (existingIndex >= 0)
            {
                var existingDefinition = Definitions[existingIndex];
                if (AreEquivalent(existingDefinition, definition)) return;

                // The shared JSON may customize the bundled Crysteel definition while keeping
                // its stable saved-item and network identities.
                if (ReferenceEquals(existingDefinition, Crysteel)
                    && definition.ItemName == Crysteel.ItemName
                    && definition.PrefabHash == Crysteel.PrefabHash
                    && definition.RecipeHash == Crysteel.RecipeHash
                    && definition.MaterialHash == Crysteel.MaterialHash)
                {
                    var replaced = (IngotDefinition[])Definitions.Clone();
                    replaced[existingIndex] = definition;
                    Validate(replaced);
                    Definitions = replaced;
                    ReadOnlyDefinitions = Array.AsReadOnly(replaced);
                    return;
                }
            }

            var updated = Definitions.Concat(new[] { definition }).ToArray();
            Validate(updated);
            Definitions = updated;
            ReadOnlyDefinitions = Array.AsReadOnly(updated);
        }
    }

    public static IReadOnlyList<IngotDefinition> Seal()
    {
        lock (Sync)
        {
            sealedForRuntime = true;
            return ReadOnlyDefinitions;
        }
    }

    public static IngotDefinition? FindByMaterialHash(uint hash) => All.FirstOrDefault(definition => definition.MaterialHash == hash);
    public static IngotDefinition? FindByPrefabHash(uint hash) => All.FirstOrDefault(definition => definition.PrefabHash == hash);
    public static IngotDefinition? FindByItemHash(uint hash) => All.FirstOrDefault(definition => definition.ItemHash == hash);

    private static void Validate(IReadOnlyList<IngotDefinition> definitions)
    {
        CheckUnique(definitions, definition => definition.ItemHash, "item");
        CheckUnique(definitions, definition => definition.PrefabHash, "prefab");
        CheckUnique(definitions, definition => definition.RecipeHash, "recipe");
        CheckUnique(definitions, definition => definition.MaterialHash, "material");
        if (definitions.Select(definition => definition.ItemName).Distinct(StringComparer.Ordinal).Count() != definitions.Count)
            throw new InvalidOperationException("Two ingot definitions use the same item name.");

        var ingredientSignatures = new HashSet<string>(StringComparer.Ordinal);
        foreach (var definition in definitions)
        {
            var signature = string.Join(",", definition.Ingredients
                .OrderBy(ingredient => ingredient.ItemHash)
                .Select(ingredient => ingredient.ItemHash.ToString()));
            if (!ingredientSignatures.Add(signature))
                throw new InvalidOperationException("Two ingot definitions have the same smelting ingredient items.");
        }
        var aliases = new HashSet<uint>(definitions.Select(definition => definition.PrefabHash));
        foreach (var definition in definitions)
        {
            foreach (var alias in definition.LegacyPrefabHashes)
            {
                if (alias == 0 || !aliases.Add(alias))
                    throw new InvalidOperationException("Duplicate or empty legacy ingot prefab hash " + alias + ".");
            }
        }
    }

    private static void CheckUnique(IReadOnlyList<IngotDefinition> definitions, Func<IngotDefinition, uint> hash, string kind)
    {
        if (definitions.Select(hash).Distinct().Count() != definitions.Count)
            throw new InvalidOperationException("Two ingot definitions use the same " + kind + " hash.");
    }

    private static bool AreEquivalent(IngotDefinition left, IngotDefinition right)
    {
        if (left.ItemName != right.ItemName
            || left.SourceItemName != right.SourceItemName
            || left.PrefabHash != right.PrefabHash
            || left.RecipeHash != right.RecipeHash
            || left.MaterialHash != right.MaterialHash
            || left.MaterialName != right.MaterialName
            || !left.Tint.Equals(right.Tint)
            || !left.Emission.Equals(right.Emission)
            || left.Ingredients.Count != right.Ingredients.Count
            || left.LegacyPrefabHashes.Count != right.LegacyPrefabHashes.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Ingredients.Count; index++)
        {
            var leftIngredient = left.Ingredients[index];
            var rightIngredient = right.Ingredients[index];
            if (leftIngredient.ItemHash != rightIngredient.ItemHash
                || leftIngredient.ItemName != rightIngredient.ItemName
                || leftIngredient.Count != rightIngredient.Count)
            {
                return false;
            }
        }

        for (var index = 0; index < left.LegacyPrefabHashes.Count; index++)
        {
            if (left.LegacyPrefabHashes[index] != right.LegacyPrefabHashes[index]) return false;
        }

        if (left.StatScaling == null || right.StatScaling == null)
        {
            if (left.StatScaling != null || right.StatScaling != null) return false;
        }
        else if (left.StatScaling.SourceMaterialHash != right.StatScaling.SourceMaterialHash
            || !left.StatScaling.DamageScale.Equals(right.StatScaling.DamageScale)
            || !left.StatScaling.DurabilityScale.Equals(right.StatScaling.DurabilityScale))
        {
            return false;
        }

        if (left.Gradient == null || right.Gradient == null)
        {
            if (left.Gradient != null || right.Gradient != null) return false;
        }
        else if (!left.Gradient.Start.Equals(right.Gradient.Start)
            || !left.Gradient.End.Equals(right.Gradient.End)
            || left.Gradient.Reverse != right.Gradient.Reverse)
        {
            return false;
        }

        if (left.EmissionPulse == null || right.EmissionPulse == null)
            return left.EmissionPulse == null && right.EmissionPulse == null;

        if (!left.EmissionPulse.LowMultiplier.Equals(right.EmissionPulse.LowMultiplier)
            || !left.EmissionPulse.HighMultiplier.Equals(right.EmissionPulse.HighMultiplier))
            return false;

        var leftCycle = left.EmissionPulse.FadeCycle;
        var rightCycle = right.EmissionPulse.FadeCycle;
        if (leftCycle == null || rightCycle == null)
            return leftCycle == null && rightCycle == null
                && left.EmissionPulse.BeatsPerMinute.Equals(right.EmissionPulse.BeatsPerMinute);

        return leftCycle.OffHoldSeconds.Equals(rightCycle.OffHoldSeconds)
            && leftCycle.FadeInSeconds.Equals(rightCycle.FadeInSeconds)
            && leftCycle.GlowHoldSeconds.Equals(rightCycle.GlowHoldSeconds)
            && leftCycle.FadeOutSeconds.Equals(rightCycle.FadeOutSeconds);
    }
}
