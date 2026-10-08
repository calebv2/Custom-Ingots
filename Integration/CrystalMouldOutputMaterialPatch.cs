using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Alta.Blacksmithing;
using Alta.Chunks;
using Alta.Inventory;
using Alta.Networking;
using HarmonyLib;
using UnityEngine;

namespace CustomIngots.Config.Integration;

[HarmonyPatch]
internal static class CrystalMouldOutputMaterialPatch
{
    private static readonly FieldInfo CurrentRecipeField = typeof(Smelter).GetField("recipe", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(typeof(Smelter).FullName, "recipe");
    private static readonly MethodInfo SpawnRecipeOutputMethod = typeof(CrystalMouldOutputMaterialPatch)
        .GetMethod(nameof(SpawnRecipeOutput), BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(typeof(CrystalMouldOutputMaterialPatch).FullName, nameof(SpawnRecipeOutput));

    private static readonly Type[] SpawnParameterTypes =
    {
        typeof(NetworkPrefab),
        typeof(SpawnData),
        typeof(Chunk),
        typeof(Vector3),
        typeof(Quaternion),
        typeof(SpawnHelper.SpawningCallback)
    };

    private static readonly MethodInfo VanillaSpawnMethod = AccessTools.Method(typeof(SpawnHelper), nameof(SpawnHelper.Spawn), SpawnParameterTypes)
        ?? throw new MissingMethodException(typeof(SpawnHelper).FullName, nameof(SpawnHelper.Spawn));

    private static MethodBase? TargetMethod()
    {
        return PatchTargetResolver.FindSmelterCompletionMoveNext();
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
    {
        if (instructions == null) return Array.Empty<CodeInstruction>();

        var code = new List<CodeInstruction>(instructions);
        if (code.Count == 0) return code;

        // Another mod may already have wrapped the recipe-output spawn. Re-route
        // that wrapper to Crystal's callback so repeated Harmony passes stay safe.
        for (var index = 0; index < code.Count; index++)
        {
            var instruction = code[index];
            if (instruction == null || instruction.operand is not MethodInfo calledMethod) continue;
            if (calledMethod == SpawnRecipeOutputMethod) return code;
            if (!IsRecipeOutputWrapper(calledMethod)) continue;

            instruction.operand = SpawnRecipeOutputMethod;
            return code;
        }

        var spawnIndexes = new List<int>();
        for (var index = 0; index < code.Count; index++)
        {
            if (code[index] != null && code[index].Calls(VanillaSpawnMethod)) spawnIndexes.Add(index);
        }

        var locals = original == null ? null : original.GetMethodBody()?.LocalVariables;
        var recipeSpawnIndexes = spawnIndexes
            .Where(index => index > 0 && code[index - 1] != null && code[index - 1].opcode == OpCodes.Ldnull)
            .ToArray();
        if (recipeSpawnIndexes.Length != 1
            || locals == null
            || locals.Count <= 1
            || locals[1].LocalType != typeof(Smelter))
        {
            var localSummary = locals == null
                ? "<unavailable>"
                : string.Join(", ", locals.Select(local => local.LocalType.Name));
            WarnIfLoggerAvailable("Crystal mould output material patch was not applied: expected one recipe-output spawn with a null callback. Spawn calls="
                + spawnIndexes.Count + ", matching recipe-output calls=" + recipeSpawnIndexes.Length + ", locals=" + localSummary + ".");
            return code;
        }

        var recipeSpawnIndex = recipeSpawnIndexes[0];
        code[recipeSpawnIndex - 1].opcode = OpCodes.Ldloc_1;
        code[recipeSpawnIndex - 1].operand = null;
        code[recipeSpawnIndex].operand = SpawnRecipeOutputMethod;
        return code;
    }

    private static bool IsRecipeOutputWrapper(MethodInfo method)
    {
        if (method.Name != "SpawnRecipeOutput" || method.ReturnType != typeof(NetworkEntity)) return false;
        var parameters = method.GetParameters();
        return parameters.Length == 6
            && parameters[0].ParameterType == typeof(NetworkPrefab)
            && parameters[1].ParameterType == typeof(SpawnData)
            && parameters[2].ParameterType == typeof(Chunk)
            && parameters[3].ParameterType == typeof(Vector3)
            && parameters[4].ParameterType == typeof(Quaternion)
            && parameters[5].ParameterType == typeof(Smelter);
    }

    private static void WarnIfLoggerAvailable(string message)
    {
        var logger = Core.Logger;
        if (logger != null) logger.Warning(message);
    }

    private static NetworkEntity SpawnRecipeOutput(
        NetworkPrefab prefab,
        SpawnData spawnData,
        Chunk chunk,
        Vector3 position,
        Quaternion rotation,
        Smelter smelter)
    {
        return SpawnHelper.Spawn(prefab, spawnData, chunk, position, rotation,
            entity => ApplyCrystalMaterialToOutput(smelter, entity));
    }

    private static void ApplyCrystalMaterialToOutput(Smelter smelter, NetworkEntity entity)
    {
        if (CurrentRecipeField.GetValue(smelter) is not SmeltingRecipe recipe)
        {
            return;
        }

        var target = CrystalMouldRecipeRegistration.FindTargetForRecipe(recipe.Hash);
        if (target == null || !ReferenceEquals(CrystalMouldRecipeRegistration.FindRecipe(recipe.Hash), recipe))
        {
            return;
        }

        var pickup = entity == null ? null : entity.GetComponent<Pickup>();
        var materialPart = pickup == null ? null : pickup.PhysicalMaterial;
        var crystalMaterial = CrystalMaterialRegistration.FindRegistered(target.Ingot.MaterialHash);
        if (pickup == null || pickup.Item == null || pickup.Item.Hash != target.Product.Hash || materialPart == null || crystalMaterial == null)
        {
            Core.Logger.Warning("Crystal mould output was left unchanged because its expected pickup or physical material was unavailable. Recipe="
                + recipe.Hash + ", committed mould=" + target.Definition.Hash + ", product=" + target.Product.Hash + ".");
            return;
        }

        materialPart.SetMaterial(crystalMaterial);
        Core.Logger.Msg("Applied " + target.Ingot.ItemName + " material " + crystalMaterial.Hash
            + " to mould product " + pickup.Item.name + "(" + pickup.Item.Hash + ").");
    }
}
