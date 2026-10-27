using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace DeathAndTaxes
{
    /// Trophies near a table count towards it while they are there ("Nearby trophy range", 0 = off): on player-built
    /// item stands (vanilla or other mods' stands) and lying loose on the ground. A stand counts for one table only: of the tables in range that use the trophy,
    /// the earliest biome (TributeDefinitions.All order), then the nearest. A counted stand that gets broken or
    /// removed destroys its trophy instead of dropping it, so stands can't be used to get trophies back.
    /// Taking the trophy off by hand is allowed; it just stops counting.
    public static class TrophyStands
    {
        private static readonly HashSet<ItemStand> s_stands = new HashSet<ItemStand>();

        // Per table: trophy -> count, recalculated at most once per second
        private static readonly Dictionary<TributeTable, KeyValuePair<float, Dictionary<string, int>>> s_cache =
            new Dictionary<TributeTable, KeyValuePair<float, Dictionary<string, int>>>();
        private const float CacheSeconds = 1f;

        public static float Range => Settings.StandRange.Value;

        /// Trophies of one type on stands that count for this table.
        public static int Count(TributeTable table, string trophy)
        {
            if (Range <= 0f || !table) return 0;
            if (!s_cache.TryGetValue(table, out var entry) || Time.time - entry.Key > CacheSeconds)
            {
                entry = new KeyValuePair<float, Dictionary<string, int>>(Time.time, CountAll(table));
                s_cache[table] = entry;
            }
            return entry.Value.TryGetValue(trophy, out int n) ? n : 0;
        }

        /// A short signature of what counts for this table, to notice changes (production is banked first).
        public static string Signature(TributeTable table) =>
            Range <= 0f ? "" : string.Join(",", CountAll(table).OrderBy(p => p.Key).Select(p => p.Key + p.Value));

        private static Dictionary<string, int> CountAll(TributeTable table)
        {
            var counts = new Dictionary<string, int>();
            if (table.Definition == null) return counts;
            s_stands.RemoveWhere(s => !s);
            foreach (var stand in s_stands)
            {
                string trophy = TrophyOn(stand);
                if (trophy == null || !table.Definition.AllTrophies.Contains(trophy)) continue;
                if (CountingTable(stand.transform.position, trophy) != table) continue;
                counts[trophy] = counts.TryGetValue(trophy, out int n) ? n + 1 : 1;
            }
            // Trophies lying loose on the ground count too
            foreach (var drop in ItemDrop.s_instances)
            {
                if (!drop || drop.m_itemData == null) continue;
                string trophy = Utils.GetPrefabName(drop.gameObject);
                if (!table.Definition.AllTrophies.Contains(trophy)) continue;
                if (CountingTable(drop.transform.position, trophy) != table) continue;
                int stack = Mathf.Max(1, drop.m_itemData.m_stack);
                counts[trophy] = counts.TryGetValue(trophy, out int n) ? n + stack : stack;
            }
            return counts;
        }

        /// The trophy prefab on a player-built stand (not boss altars), or null.
        private static string TrophyOn(ItemStand stand)
        {
            if (!stand || stand.m_guardianPower || !stand.GetComponentInParent<Piece>()) return null;
            var nview = stand.m_nview;
            if (!nview || !nview.IsValid()) return null;
            // Valheim 1.0 keeps the attached item as a prefab hash
            int hash = stand.GetAttachedItem();
            var prefab = hash != 0 && ObjectDB.instance ? ObjectDB.instance.GetItemPrefab(hash) : null;
            if (prefab) return prefab.name;
            string item = nview.GetZDO().GetString(ZDOVars.s_item);
            return string.IsNullOrEmpty(item) ? null : item;
        }

        /// The one table this stand's trophy counts for, or null.
        private static TributeTable CountingTable(Vector3 position, string trophy)
        {
            float range = Range;
            TributeTable best = null;
            int bestOrder = int.MaxValue;
            float bestDistance = float.MaxValue;
            foreach (var table in TributeTable.Instances)
            {
                var def = table ? table.Definition : null;
                if (def == null || !def.AllTrophies.Contains(trophy)) continue;
                float distance = Vector3.Distance(table.transform.position, position);
                if (distance > range) continue;
                int order = System.Array.IndexOf(TributeDefinitions.All, def);
                if (order < bestOrder || (order == bestOrder && distance < bestDistance))
                {
                    best = table;
                    bestOrder = order;
                    bestDistance = distance;
                }
            }
            return best;
        }

        public static bool IsCounted(ItemStand stand)
        {
            if (Range <= 0f) return false;
            string trophy = TrophyOn(stand);
            return trophy != null && CountingTable(stand.transform.position, trophy) != null;
        }

        /// While the player places an item stand or looks at one, the range circles of tables nearby show.
        public static void ShowNearbyRanges()
        {
            var player = Player.m_localPlayer;
            if (!player || Range <= 0f) return;
            var ghost = player.m_placementGhost;
            var hover = player.GetHoverObject();
            Vector3? at = ghost && ghost.GetComponent<ItemStand>() ? ghost.transform.position
                : hover && hover.GetComponentInParent<ItemStand>() ? hover.transform.position : (Vector3?)null;
            if (at == null) return;
            foreach (var table in TributeTable.Instances)
                if (table && Vector3.Distance(table.transform.position, at.Value) <= Range + 15f) table.ShowRange();
        }

        /// The hover line on a stand: which table its trophy counts for.
        private static string CountsForText(ItemStand stand)
        {
            string trophy = TrophyOn(stand);
            var table = trophy != null && Range > 0f ? CountingTable(stand.transform.position, trophy) : null;
            return table ? $"\n<color=orange>Counts towards {table.Definition.TableName}</color>" : "";
        }

        /// After the player hangs a trophy: confirm the table it counts towards (nothing when it doesn't count).
        private static System.Collections.IEnumerator ReportAfterHanging(ItemStand stand, string trophy)
        {
            yield return new WaitForSeconds(1f); // the stand's owner sets the item
            var player = Player.m_localPlayer;
            if (!player || !stand || TrophyOn(stand) != trophy) yield break;
            var table = CountingTable(stand.transform.position, trophy);
            if (!table) yield break;
            int count = table.GetTrophyCount(trophy), max = table.MaxTrophies(trophy);
            string full = count >= max ? ", full" : "";
            player.Message(MessageHud.MessageType.Center,
                $"{TributeTable.DisplayName(trophy)} counts towards {table.Definition.TableName} ({count}/{max}{full})");
        }

        [HarmonyPatch(typeof(ItemStand), nameof(ItemStand.UseItem))]
        private static class StandUseItem
        {
            private static void Postfix(ItemStand __instance, Humanoid user, ItemDrop.ItemData item, bool __result)
            {
                if (!__result || Range <= 0f || user != Player.m_localPlayer) return;
                string trophy = TributeTable.PrefabName(item);
                if (trophy == null || !TributeDefinitions.All.Any(d => d.AllTrophies.Contains(trophy))) return;
                __instance.StartCoroutine(ReportAfterHanging(__instance, trophy));
            }
        }

        [HarmonyPatch(typeof(ItemStand), nameof(ItemStand.GetHoverText))]
        private static class StandHover
        {
            private static void Postfix(ItemStand __instance, ref string __result) => __result += CountsForText(__instance);
        }

        public static void Forget(TributeTable table) => s_cache.Remove(table);

        [HarmonyPatch(typeof(ItemStand), nameof(ItemStand.Awake))]
        private static class StandAwake
        {
            private static void Postfix(ItemStand __instance) => s_stands.Add(__instance);
        }

        /// A counted stand that is broken or removed loses its trophy instead of dropping it.
        [HarmonyPatch(typeof(ItemStand), nameof(ItemStand.OnDestroyed))]
        private static class StandDestroyed
        {
            private static bool Prefix(ItemStand __instance)
            {
                if (!__instance.m_nview || !__instance.m_nview.IsValid() || !__instance.m_nview.IsOwner()) return true;
                if (!IsCounted(__instance)) return true;
                return false; // no drop; the stand and its trophy are destroyed
            }
        }
    }
}
