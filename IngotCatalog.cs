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
        new IngotStatScaling(DarksteelMaterialHash, 1.10f, 0.90f),
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
}
