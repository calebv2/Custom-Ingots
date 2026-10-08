using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Alta;
using CustomIngots.API;
using HarmonyLib;
using MelonLoader.Utils;

namespace CustomIngots.Config.Integration;

internal static class ClientConfigSync
{
    internal const string Prefix = "CUSTOM_INGOTS_CONFIG_V1|";
    private const int MaximumConfigBytes = 64 * 1024;
    private const int MaximumChunks = 512;
    private const int MaximumChunkCharacters = 220;
    private static readonly Dictionary<string, Transfer> Transfers = new Dictionary<string, Transfer>();

    internal static void Install(HarmonyLib.Harmony harmony)
    {
        var receive = AccessTools.Method(typeof(PlayerCommunicationManager), "HandlePlayerMessage");
        if (receive == null)
            throw new MissingMethodException(typeof(PlayerCommunicationManager).FullName, "HandlePlayerMessage");

        var display = AccessTools.Method(typeof(PlayerMessageDisplay), "Display",
            new[] { typeof(string), typeof(float), typeof(DisplayMessageType) });
        if (display == null)
            throw new MissingMethodException(typeof(PlayerMessageDisplay).FullName, "Display(String, Single, DisplayMessageType)");

        harmony.Patch(receive, prefix: new HarmonyMethod(typeof(ClientConfigSync), nameof(HandlePlayerMessagePrefix)));
        harmony.Patch(display, prefix: new HarmonyMethod(typeof(ClientConfigSync), nameof(DisplayPrefix)));
        Core.Logger.Msg("Client config receiver is listening for server messages.");
    }

    private static bool HandlePlayerMessagePrefix(PlayerTextMessage textMessage)
    {
        var message = textMessage.Message;
        if (message == null || !message.StartsWith(Prefix, StringComparison.Ordinal)) return true;
        ReceiveChunk(message.Substring(Prefix.Length));
        return false;
    }

    private static bool DisplayPrefix(string message)
    {
        if (message == null || !message.StartsWith(Prefix, StringComparison.Ordinal)) return true;
        ReceiveChunk(message.Substring(Prefix.Length));
        return false;
    }

    private static void ReceiveChunk(string frame)
    {
        try
        {
            var fields = frame.Split(new[] { '|' }, 4);
            if (fields.Length != 4
                || fields[0].Length != 32
                || !int.TryParse(fields[1], out var index)
                || !int.TryParse(fields[2], out var total)
                || total < 1 || total > MaximumChunks
                || index < 0 || index >= total
                || fields[3].Length > MaximumChunkCharacters)
            {
                Core.Logger.Warning("Ignored a malformed server Custom-Ingots config part.");
                return;
            }

            if (!Transfers.TryGetValue(fields[0], out var transfer))
            {
                if (Transfers.Count >= 8) Transfers.Clear();
                transfer = new Transfer(total);
                Transfers.Add(fields[0], transfer);
            }
            if (transfer.Chunks.Length != total)
            {
                Transfers.Remove(fields[0]);
                Core.Logger.Warning("Ignored a Custom-Ingots config transfer with inconsistent part counts.");
                return;
            }

            if (transfer.Chunks[index] == null)
            {
                transfer.Chunks[index] = fields[3];
                transfer.Received++;
            }
            if (transfer.Received != total) return;

            Transfers.Remove(fields[0]);
            var encoded = string.Concat(transfer.Chunks);
            var bytes = Convert.FromBase64String(encoded);
            if (bytes.Length == 0 || bytes.Length > MaximumConfigBytes)
                throw new InvalidDataException("The received config exceeded the allowed size.");

            var json = new UTF8Encoding(false, true).GetString(bytes);
            SaveConfig(json);
        }
        catch (Exception exception)
        {
            Core.Logger.Error("Could not install server Custom-Ingots config: " + exception.Message);
        }
    }

    private static void SaveConfig(string json)
    {
        var folder = Path.Combine(MelonEnvironment.UserDataDirectory, "CustomIngots");
        var path = Path.Combine(folder, "ingots.json");
        var tempPath = path + ".server-sync.tmp";
        var backupPath = path + ".before-server-sync.bak";
        Directory.CreateDirectory(folder);

        if (File.Exists(path) && string.Equals(File.ReadAllText(path), json, StringComparison.Ordinal))
        {
            Core.Logger.Msg("Server Custom-Ingots config matches the local file.");
            return;
        }

        File.WriteAllText(tempPath, json, new UTF8Encoding(false));
        ValidateConfig(tempPath);

        if (File.Exists(path))
        {
            if (!File.Exists(backupPath)) File.Copy(path, backupPath);
            File.Delete(path);
        }

        File.Move(tempPath, path);
        Core.Logger.Warning("Installed the server's ingots.json. Restart the client to load the server ingots.");
    }

    private static void ValidateConfig(string path)
    {
        var entries = IngotConfigFile.Load(path);
        if (entries.Length == 0 || entries.Length > 256)
            throw new InvalidDataException("The server config must contain between 1 and 256 ingot definitions.");

        var definitions = entries.Select(entry => entry.ToDefinition()).ToArray();
        RequireUnique(definitions.Select(definition => definition.ItemHash), "item");
        RequireUnique(definitions.Select(definition => definition.PrefabHash), "prefab");
        RequireUnique(definitions.Select(definition => definition.RecipeHash), "recipe");
        RequireUnique(definitions.Select(definition => definition.MaterialHash), "material");
        if (definitions.Select(definition => definition.ItemName).Distinct(StringComparer.Ordinal).Count() != definitions.Length)
            throw new InvalidDataException("The server config contains duplicate item names.");

        var ingredientSets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var definition in definitions)
        {
            var signature = string.Join(",", definition.Ingredients
                .OrderBy(ingredient => ingredient.ItemHash)
                .Select(ingredient => ingredient.ItemHash.ToString()));
            if (!ingredientSets.Add(signature))
                throw new InvalidDataException("The server config repeats a smelting ingredient set.");
        }
    }

    private static void RequireUnique(IEnumerable<uint> values, string kind)
    {
        if (values.Distinct().Count() == values.Count()) return;
        throw new InvalidDataException("The server config contains duplicate " + kind + " hashes.");
    }

    private sealed class Transfer
    {
        internal Transfer(int count) => Chunks = new string[count];
        internal string[] Chunks { get; }
        internal int Received { get; set; }
    }
}
