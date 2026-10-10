using System.Collections.Generic;
using UnityEngine;

namespace DeathAndTaxes
{
    /// "Tables per player" (config): how many tables of each type one player may build. 0 = no limit.
    /// Only the server sees every table, so the player's game asks the server for its counts (by the piece's
    /// creator) when a tribute table is selected for building; in single player the game is its own server.
    /// Tables that already exist are never touched, only new placements are refused.
    public static class TableLimit
    {
        private const string RpcRequest = "DeathAndTaxes_TableCountRequest";
        private const string RpcReply = "DeathAndTaxes_TableCountReply";
        private const float RequestCooldown = 2f;

        private static ZRoutedRpc s_rpc;
        private static readonly Dictionary<string, int> s_counts = new Dictionary<string, int>();
        private static bool s_known;
        private static float s_requestedAt = -100f;

        /// Called when a game or server starts (Game.Start); registers the request/reply messages once per session.
        public static void Register()
        {
            var rpc = ZRoutedRpc.instance;
            if (rpc == null || ReferenceEquals(rpc, s_rpc)) return;
            s_rpc = rpc;
            s_counts.Clear();
            s_known = false;
            rpc.Register<long>(RpcRequest, OnRequest);
            rpc.Register<ZPackage>(RpcReply, OnReply);
        }

        /// Player side: ask the server how many tables of each type this player has built.
        public static void Request(bool force = false)
        {
            var player = Player.m_localPlayer;
            if (s_rpc == null || !player || Settings.MaxTablesPerPlayer.Value <= 0) return;
            if (!force && Time.time - s_requestedAt < RequestCooldown) return;
            s_requestedAt = Time.time;
            s_rpc.InvokeRoutedRPC(s_rpc.GetServerPeerID(), RpcRequest, player.GetPlayerID());
        }

        /// Server side: count the requesting player's tables per type and send the numbers back.
        private static void OnRequest(long sender, long playerId)
        {
            if (!ZNet.instance || !ZNet.instance.IsServer()) return;
            var pkg = new ZPackage();
            pkg.Write(TributeDefinitions.All.Length);
            foreach (var def in TributeDefinitions.All)
            {
                var zdos = new List<ZDO>();
                int index = 0;
                while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(def.PieceName, zdos, ref index)) { }
                int count = 0;
                foreach (var zdo in zdos)
                    if (zdo.GetLong(ZDOVars.s_creator) == playerId) count++;
                pkg.Write(def.PieceName);
                pkg.Write(count);
            }
            s_rpc.InvokeRoutedRPC(sender, RpcReply, pkg);
        }

        private static void OnReply(long sender, ZPackage pkg)
        {
            int n = pkg.ReadInt();
            for (int i = 0; i < n; i++)
            {
                string piece = pkg.ReadString();
                s_counts[piece] = pkg.ReadInt();
            }
            s_known = true;
        }

        /// Player side, before placing: false (with a message) when the player already has the maximum of this table.
        public static bool AllowPlacing(Player player, Piece piece)
        {
            int limit = Settings.MaxTablesPerPlayer.Value;
            var def = piece ? TributeDefinitions.ForPiece(piece.name) : null;
            if (def == null || limit <= 0) return true;
            if (!s_known)
            {
                Request(force: true);
                player.Message(MessageHud.MessageType.Center, "Counting your tribute tables, try again in a moment");
                return false;
            }
            s_counts.TryGetValue(def.PieceName, out int count);
            if (count < limit) return true;
            Request(); // in case one was torn down meanwhile
            player.Message(MessageHud.MessageType.Center,
                $"You can only have {limit} {def.DisplayName} {(limit == 1 ? "table" : "tables")}");
            return false;
        }

        /// Player side, after placing: count it right away (the server's count follows on the next request).
        public static void OnPlaced(Piece piece)
        {
            var def = piece ? TributeDefinitions.ForPiece(piece.name) : null;
            if (def == null) return;
            s_counts.TryGetValue(def.PieceName, out int count);
            s_counts[def.PieceName] = count + 1;
        }
    }
}
