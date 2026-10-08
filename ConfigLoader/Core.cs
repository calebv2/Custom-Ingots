using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Alta.Inventory;
using CustomIngots.API;
using CustomIngots.Config.Integration;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;

[assembly: MelonInfo(typeof(CustomIngots.Config.Core), "Custom Ingots", "1.2.5", "Custom Ingots API")]
[assembly: MelonGame("Alta", "A Township Tale")]

namespace CustomIngots.Config;

public sealed class Core : MelonMod
{
    internal static MelonLogger.Instance Logger { get; private set; } = null!;
    internal static string? ServerConfigJson { get; private set; }
    private bool clientRuntimeEnabled;
    private bool serverRuntimeEnabled;

    private const string StarterConfig = "{\n"
        + "  \"ingots\": [\n"
        + "    {\n"
        + "      \"itemName\": \"Example Alloy Ingot\",\n"
        + "      \"sourceItemName\": \"Iron Ingot\",\n"
        + "      \"itemHash\": \"0x45584901\",\n"
        + "      \"prefabHash\": \"0x5101\",\n"
        + "      \"recipeHash\": \"0x45585201\",\n"
        + "      \"materialHash\": \"0x45584D01\",\n"
        + "      \"materialName\": \"Example Alloy\",\n"
        + "      \"ingredients\": [\n"
        + "        { \"itemHash\": \"12345\", \"itemName\": \"Iron Ingot\", \"count\": 2 }\n"
        + "      ],\n"
        + "      \"tint\": { \"r\": 0.35, \"g\": 0.75, \"b\": 0.55, \"a\": 1.0 },\n"
        + "      \"emission\": { \"r\": 0.05, \"g\": 0.15, \"b\": 0.08, \"a\": 1.0 }\n"
        + "    }\n"
        + "  ]\n"
        + "}\n";

    public override void OnInitializeMelon()
    {
        Logger = LoggerInstance;
        var folder = Path.Combine(MelonEnvironment.UserDataDirectory, "CustomIngots");
        var path = Path.Combine(folder, "ingots.json");
        Directory.CreateDirectory(folder);

        if (!File.Exists(path))
        {
            File.WriteAllText(path, StarterConfig);
            Logger.Warning("Created starter config at " + path
                + ". Edit its example IDs and ingredient hash, then restart the game.");
        }
        else
        {
            try
            {
                var entries = IngotConfigFile.Load(path);
                ServerConfigJson = File.ReadAllText(path);
                var registered = 0;
                foreach (var entry in entries)
                {
                    try
                    {
                        IngotCatalog.Register(entry.ToDefinition());
                        registered++;
                        Logger.Msg("Loaded configured ingot: " + entry.ItemName);
                    }
                    catch (Exception exception)
                    {
                        Logger.Error("Could not register configured ingot '" + entry.ItemName + "': " + exception.Message);
                    }
                }

                Logger.Msg("Loaded " + registered + " configured ingot(s) from " + path + ".");
            }
            catch (Exception exception)
            {
                Logger.Error("Could not load " + path + ": " + exception);
            }
        }

        if (IsServerRuntime())
        {
            HarmonyInstance.PatchAll();
            ServerConfigSync.Install(HarmonyInstance);
            IngotSpawnActivation.Install(HarmonyInstance, message => Logger.Msg(message));
            serverRuntimeEnabled = true;
            Logger.Msg("Custom Ingots initialized in server mode.");
            return;
        }

        var harmony = new HarmonyLib.Harmony("ATT.CustomIngots.Client");
        CrystalClientAppearancePatch.Install(harmony);
        ClientConfigSync.Install(harmony);
        IngotSpawnActivation.Install(harmony, message => Logger.Msg(message));
        clientRuntimeEnabled = true;
        Logger.Msg("Custom Ingots initialized in client mode.");
    }

    public override void OnLateInitializeMelon()
    {
        if (IsServerRuntime())
        {
            RegisterServerIngots();
            return;
        }

        try
        {
            foreach (var definition in IngotCatalog.Seal())
            {
                try
                {
                    var material = CreateMaterial(definition);
                    var ingot = IngotRegistration.CreateAndRegister(definition, material);
                    IngotForgeUnlock.Register(ingot, material);
                    Logger.Msg("Registered " + definition.ItemName + " on client: item=" + ingot.Hash
                        + ", prefab=" + ingot.Prefab.Hash + ", material=" + material.Hash + ".");
                }
                catch (Exception exception)
                {
                    Logger.Error("Could not register client ingot " + definition.ItemName + "#" + definition.ItemHash + ": " + exception);
                }
            }
        }
        catch (Exception exception)
        {
            Logger.Error("Client ingot/material registration failed: " + exception);
        }
    }

    public override void OnUpdate()
    {
        if (clientRuntimeEnabled) IngotClientAppearance.Update();
        if (serverRuntimeEnabled) ServerConfigSync.Update();
    }

    private void RegisterServerIngots()
    {
        try
        {
            var definitions = IngotCatalog.Seal();
            var allTargets = new List<CrystalMouldTarget>();
            foreach (var definition in definitions)
            {
                try
                {
                    var material = CreateMaterial(definition);
                    var ingot = IngotRegistration.CreateAndRegister(definition, material);
                    IngotForgeUnlock.Register(ingot, material);
                    var ingotRecipe = IngotSmeltingRecipeRegistration.Register(definition, ingot);
                    foreach (var ingredient in definition.Ingredients)
                    {
                        var item = Item.All.FirstOrDefault(candidate => candidate.Hash == ingredient.ItemHash);
                        if (item != null) CrystalSmelterInputFilter.Allow(item);
                    }
                    CrystalSmelterInputFilter.Allow(ingot);
                    allTargets.AddRange(CrystalMouldRecipeRegistration.Register(definition, ingot));
                    Logger.Msg("Registered " + definition.ItemName + ": item=" + ingot.Hash
                        + ", prefab=" + ingot.Prefab.Hash + ", material=" + material.Hash
                        + ", smeltingRecipe=" + ingotRecipe.Hash + ".");
                }
                catch (Exception exception)
                {
                    Logger.Error("Could not register ingot " + definition.ItemName + "#" + definition.ItemHash + ": " + exception);
                }
            }

            if (allTargets.Count == 0) Logger.Warning("Custom Ingots did not register any valid mould recipes.");
            Logger.Msg("Custom Ingots initialized with " + allTargets.Count + " forge mould recipes.");
        }
        catch (Exception exception)
        {
            Logger.Error("Custom Ingots late initialization failed: " + exception);
        }
    }

    private static PhysicalMaterial CreateMaterial(IngotDefinition definition)
    {
        return definition.MaterialHash == IngotCatalog.CrystalMaterialHash
            ? CrystalMaterialRegistration.CreateAndRegister(definition, CrystalMaterialDefaults.ApplyCrystal)
            : CrystalMaterialRegistration.CreateAndRegister(definition);
    }

    private static bool IsServerRuntime()
    {
        return Application.isBatchMode || NetworkSceneManager.IsServer || ServerAssemblyIsLoaded();
    }

    private static bool ServerAssemblyIsLoaded()
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (string.Equals(assembly.GetName().Name, "Alta.Server", StringComparison.OrdinalIgnoreCase)
                || string.Equals(assembly.GetName().Name, "Crysteel", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
