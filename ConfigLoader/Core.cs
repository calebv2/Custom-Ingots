using System;
using System.IO;
using CustomIngots.API;
using MelonLoader;
using MelonLoader.Utils;

[assembly: MelonInfo(typeof(CustomIngots.Config.Core), "Custom Ingots Config", "1.0.0", "Custom Ingots API")]
[assembly: MelonGame("Alta", "A Township Tale")]

namespace CustomIngots.Config;

public sealed class Core : MelonMod
{
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
        var folder = Path.Combine(MelonEnvironment.UserDataDirectory, "CustomIngots");
        var path = Path.Combine(folder, "ingots.json");
        Directory.CreateDirectory(folder);

        if (!File.Exists(path))
        {
            File.WriteAllText(path, StarterConfig);
            LoggerInstance.Warning("Created starter config at " + path
                + ". Edit its example IDs and ingredient hash, then restart the game.");
            return;
        }

        try
        {
            var entries = IngotConfigFile.Load(path);
            var registered = 0;
            foreach (var entry in entries)
            {
                try
                {
                    IngotCatalog.Register(entry.ToDefinition());
                    registered++;
                    LoggerInstance.Msg("Registered configured ingot: " + entry.ItemName);
                }
                catch (Exception exception)
                {
                    LoggerInstance.Error("Could not register configured ingot '" + entry.ItemName + "': " + exception.Message);
                }
            }

            LoggerInstance.Msg("Loaded " + registered + " configured ingot(s) from " + path + ".");
        }
        catch (Exception exception)
        {
            LoggerInstance.Error("Could not load " + path + ": " + exception);
        }
    }
}
