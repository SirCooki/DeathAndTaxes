using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace DeathAndTaxes
{
    [HarmonyPatch]
    internal static class Patches
    {
        // Discovery: tribute pieces stay hidden until the boss trophy is carried.
        [HarmonyPatch(typeof(Player), nameof(Player.HaveRequirements), typeof(Piece), typeof(Player.RequirementMode))]
        [HarmonyPostfix]
        private static void HaveRequirements(Player __instance, Piece piece, Player.RequirementMode mode, ref bool __result)
        {
            if (mode != Player.RequirementMode.IsKnown || !piece) return;
            var def = TributeDefinitions.ForPiece(piece.name);
            if (def == null) return;
            // Only the boss trophy decides discovery, regardless of which materials vanilla considers known
            __result = HasItem(__instance, def.BossTrophy);
        }

        // Vanilla only re-checks recipes when a never-seen item is picked up; also re-check when a boss trophy arrives.
        [HarmonyPatch(typeof(Player), nameof(Player.OnInventoryChanged))]
        [HarmonyPostfix]
        private static void RecheckRecipes(Player __instance)
        {
            if (__instance != Player.m_localPlayer || __instance.m_isLoading) return;
            if (TributeDefinitions.All.Any(d => !__instance.m_knownRecipes.Contains(d.DisplayName) && HasItem(__instance, d.BossTrophy)))
                __instance.UpdateKnownRecipesList();
        }

        private static bool HasItem(Player player, string prefab) =>
            player.GetInventory().GetAllItems().Any(i => i.m_dropPrefab && i.m_dropPrefab.name == prefab);

        // Messages for the per-player table limit, once the network is up (also on a dedicated server).
        [HarmonyPatch(typeof(Game), nameof(Game.Start))]
        [HarmonyPostfix]
        private static void RegisterRpcs() => TableLimit.Register();

        // "Tables per player": refuse placing another table of a type the player already has the maximum of.
        [HarmonyPatch(typeof(Player), nameof(Player.TryPlacePiece))]
        [HarmonyPrefix]
        private static bool LimitTables(Player __instance, Piece piece, ref bool __result)
        {
            if (TableLimit.AllowPlacing(__instance, piece)) return true;
            __result = false;
            return false;
        }

        [HarmonyPatch(typeof(Player), nameof(Player.TryPlacePiece))]
        [HarmonyPostfix]
        private static void CountPlacedTable(Piece piece, bool __result)
        {
            if (__result) TableLimit.OnPlaced(piece);
        }

        // When a tribute piece is selected for building, start with its front (the side without the wall) facing the player.
        [HarmonyPatch(typeof(Player), nameof(Player.SetupPlacementGhost))]
        [HarmonyPostfix]
        private static void FacePlayerWhenBuilding(Player __instance)
        {
            var ghost = __instance.m_placementGhost;
            if (!ghost || !ghost.GetComponent<TributeTable>()) return;
            TableLimit.Request(); // fresh counts for the table limit before it is placed
            var cam = GameCamera.instance ? GameCamera.instance.transform : __instance.transform;
            var flat = Vector3.ProjectOnPlane(cam.forward, Vector3.up);
            if (flat.sqrMagnitude < 0.0001f) return;
            float yaw = Quaternion.LookRotation(flat).eulerAngles.y;
            __instance.m_placeRotation = Mathf.RoundToInt(yaw / __instance.m_placeRotationDegrees);
        }

        // Keeps vanilla's "no access" text inside someone else's ward.
        [HarmonyPatch(typeof(Container), nameof(Container.GetHoverText))]
        [HarmonyPostfix]
        private static void GetHoverText(Container __instance, ref string __result)
        {
            var table = __instance.GetComponent<TributeTable>();
            if (!table) return;
            if (__instance.m_checkGuardStone && !PrivateArea.CheckAccess(__instance.transform.position, 0f, flash: false)) return;
            __result = table.GetHoverText();
        }

        // Holding E on an open container (and other mods) call StackAll: nothing may be stacked into a tribute table.
        [HarmonyPatch(typeof(Container), nameof(Container.StackAll))]
        [HarmonyPrefix]
        private static bool StackAll(Container __instance) => !__instance.GetComponent<TributeTable>();

        // "Collect tribute" (Take all) only hands out what the table produced itself.
        [HarmonyPatch(typeof(Container), nameof(Container.TakeAll))]
        [HarmonyPrefix]
        private static void TakeAll(Container __instance)
        {
            var table = __instance.GetComponent<TributeTable>();
            if (table) table.Enforce();
        }

        // The window's "Place stacks" button (renamed "Add trophies") stacks straight into the inventory,
        // bypassing Container.StackAll.
        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.OnStackAll))]
        [HarmonyPrefix]
        private static bool OnStackAll(InventoryGui __instance)
        {
            var container = __instance.m_currentContainer;
            var table = container ? container.GetComponent<TributeTable>() : null;
            if (!table) return true;
            __instance.SetupDragItem(null, null, 1);
            if (Player.m_localPlayer) table.OfferAll(Player.m_localPlayer);
            return false;
        }

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Show))]
        [HarmonyPostfix]
        private static void ShowInventory(InventoryGui __instance) => TributePanel.Ensure(__instance);

        // In the window: nothing can be put into the storage or moved around inside it. Trophies are offered instead.
        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.OnSelectedItem))]
        [HarmonyPrefix]
        private static bool OnSelectedItem(InventoryGui __instance, InventoryGrid grid, ItemDrop.ItemData item,
            InventoryGrid.Modifier mod)
        {
            var container = __instance.m_currentContainer;
            var table = container ? container.GetComponent<TributeTable>() : null;
            if (!table) return true;
            var player = Player.m_localPlayer;
            var storage = container.GetInventory();
            var drag = __instance.m_dragItem;

            // Dropping anything onto the storage
            if (grid == __instance.m_containerGrid && drag != null)
            {
                if (__instance.m_dragInventory != storage)
                {
                    if (table.IsTrophy(drag))
                        table.Offer(player, __instance.m_dragInventory, drag, __instance.m_dragAmount);
                    else
                        player.Message(MessageHud.MessageType.Center, "Only trophies can be offered here");
                }
                __instance.SetupDragItem(null, null, 1); // produce picked up from the storage just stays in its slot
                return false;
            }

            // Ctrl+click on a player item moves it into the storage
            if (grid == __instance.m_playerGrid && mod == InventoryGrid.Modifier.Move && item != null)
            {
                if (table.IsTrophy(item))
                    table.Offer(player, player.GetInventory(), item, item.m_stack);
                else
                    player.Message(MessageHud.MessageType.Center, "Only trophies can be offered here");
                return false;
            }

            // Dropping produce onto a different player item would swap that item into the storage
            if (grid == __instance.m_playerGrid && drag != null && __instance.m_dragInventory == storage &&
                item != null && item.m_shared.m_name != drag.m_shared.m_name)
                return false;

            return true;
        }

        // The storage is not a chest: only the table's own production may add items, whatever mod tries.
        // Runs before MultiUserChest's prefixes (priority 100), which honour a skipped original.
        [HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), typeof(ItemDrop.ItemData))]
        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        private static bool AddItem(Inventory __instance, ref bool __result) => Allow(__instance, ref __result);

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), typeof(ItemDrop.ItemData), typeof(Vector2i))]
        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        private static bool AddItemAt(Inventory __instance, ref bool __result) => Allow(__instance, ref __result);

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), typeof(ItemDrop.ItemData), typeof(int), typeof(int), typeof(int), typeof(bool))]
        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        private static bool AddItemAmountAt(Inventory __instance, ref bool __result) => Allow(__instance, ref __result);

        private static bool Allow(Inventory inventory, ref bool result)
        {
            if (TributeTable.AllowsAdd(inventory)) return true;
            result = false;
            return false;
        }
    }
}
