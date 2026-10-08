using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Alta.Networking.Servers;
using HarmonyLib;
using UnityEngine;

namespace CustomIngots.Config.Integration;

internal static class ServerConfigSync
{
    private const int ChunkLength = 220;
    private const int MaximumConfigBytes = 64 * 1024;
    private static readonly System.Collections.Generic.HashSet<ServerHandler> RegisteredServers =
        new System.Collections.Generic.HashSet<ServerHandler>();
    private static readonly Dictionary<Player, float> PendingPlayers = new Dictionary<Player, float>();

    internal static void Install(HarmonyLib.Harmony harmony)
    {
        var initialize = AccessTools.Method(typeof(ServerHandler), "Initialize");
        if (initialize == null)
            throw new MissingMethodException(typeof(ServerHandler).FullName, "Initialize");

        harmony.Patch(initialize, prefix: new HarmonyMethod(typeof(ServerConfigSync), nameof(InitializePrefix)));
    }

    private static void InitializePrefix(ServerHandler __instance) => Register(__instance);

    private static void Register(ServerHandler server)
    {
        if (server == null || !RegisteredServers.Add(server)) return;
        server.PlayerJoined += QueueConfigForPlayer;
        Core.Logger.Msg("Server config delivery is listening for player joins.");
    }

    private static void QueueConfigForPlayer(Player player)
    {
        if (player == null || PendingPlayers.ContainsKey(player)) return;
        PendingPlayers.Add(player, Time.realtimeSinceStartup);
        Core.Logger.Msg("Queued server config for a joining player; waiting for its network player controller.");
    }

    internal static void Update()
    {
        if (PendingPlayers.Count == 0) return;

        var manager = PlayerCommunicationManager.Instance;
        foreach (var pending in PendingPlayers.ToArray())
        {
            var player = pending.Key;
            if (player == null)
            {
                PendingPlayers.Remove(player!);
                continue;
            }

            var connection = player.ConnectionToRemotePlayer;
            if (manager != null && connection != null)
            {
                var recipient = connection.Player;
                if (recipient != null && recipient.PlayerController != null)
                {
                    PendingPlayers.Remove(player);
                    SendConfigToPlayer(connection, recipient, manager);
                    continue;
                }
            }

            if (Time.realtimeSinceStartup - pending.Value < 60f) continue;
            PendingPlayers.Remove(player);
            Core.Logger.Warning("Could not send Custom-Ingots config: the joining player's network controller did not become ready within 60 seconds.");
        }
    }

    private static void SendConfigToPlayer(Alta.Networking.Connection connection,
        Alta.Networking.Scripts.Player.IPlayer recipient, PlayerCommunicationManager manager)
    {
        var json = Core.ServerConfigJson;
        if (string.IsNullOrEmpty(json))
        {
            Core.Logger.Warning("Could not send Custom-Ingots config: no valid server ingots.json was loaded.");
            return;
        }
        var bytes = Encoding.UTF8.GetBytes(json);
        if (bytes.Length == 0 || bytes.Length > MaximumConfigBytes)
        {
            Core.Logger.Warning("Custom-Ingots config delivery skipped: config size was " + bytes.Length
                + " bytes; maximum is " + MaximumConfigBytes + ".");
            return;
        }

        var encoded = Convert.ToBase64String(bytes);
        var total = (encoded.Length + ChunkLength - 1) / ChunkLength;
        var transferId = Guid.NewGuid().ToString("N");
        try
        {
            for (var index = 0; index < total; index++)
            {
                var start = index * ChunkLength;
                var length = Math.Min(ChunkLength, encoded.Length - start);
                var chunk = encoded.Substring(start, length);
                var message = ClientConfigSync.Prefix + transferId + "|" + index + "|" + total + "|" + chunk;
                manager.SendMessageToPlayer(recipient, message, 0f);
            }

            Core.Logger.Msg("Sent Custom-Ingots config to " + connection.PlayerName + " in " + total + " part(s).");
        }
        catch (Exception exception)
        {
            Core.Logger.Error("Could not send Custom-Ingots config to a joining player: " + exception.Message);
        }
    }
}
