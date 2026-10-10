using System;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;

namespace DeathAndTaxes
{
    /// Optional support for other mods. None of them is required.
    public static class Compat
    {
        public const string ChestButlerGuid = "eksolutions.chestbutler";

        /// ChestButler may take tribute out (pull, gather) but must never sort items into a tribute table.
        public static bool ChestButlerInstalled => Chainloader.PluginInfos.ContainsKey(ChestButlerGuid);

        /// Name of ChestButler's button bar, placed next to the vanilla "Take all" button.
        public const string ChestButlerBarName = "psort_bar";

        // ---- ServersideQoL (server only) ----

        public const string ServersideQoLAutoStoreGuid = "ArgusMagnus.ServersideQoL.AutoStore";

        /// ServersideQoL edits container contents in the save data on the server, past Inventory.AddItem.
        /// Its AutoStore would put dropped/stacked items into tribute tables (the table pushes them out again: a loop)
        /// and re-sort them (fighting the fixed resource slots). Tribute tables get pickup range 0, AutoStore's own
        /// "never store into this chest" (what its chest sign "🧲0" does), and AutoStore's sorting is not attached to
        /// them. Everything else stays: e.g. AutoProcess may still feed smelters from a table (taking out is fine).
        /// Called from Plugin.Start, after every plugin has loaded; ServersideQoL looks objects up lazily later.
        public static void PatchServersideQoL(Harmony harmony)
        {
            if (!Chainloader.PluginInfos.ContainsKey(ServersideQoLAutoStoreGuid)) return;
            try
            {
                var registry = AccessTools.TypeByName("ServersideQoL.Processors.ContainerRegistryProcessor");
                var processor = AccessTools.TypeByName("ServersideQoL.Processor");
                var getState = registry != null ? AccessTools.Method(registry, "GetStateCore") : null;
                var initPrefab = processor != null ? AccessTools.Method(processor, "InitializePrefabInfoInternal") : null;
                if (getState == null || initPrefab == null)
                {
                    Jotunn.Logger.LogWarning("[DeathAndTaxes] ServersideQoL found but not the expected code; AutoStore may store into tribute tables");
                    return;
                }
                harmony.Patch(getState, postfix: new HarmonyMethod(typeof(Compat), nameof(NoAutoStoreIntoTables)));
                harmony.Patch(initPrefab, prefix: new HarmonyMethod(typeof(Compat), nameof(NoAutoSortOfTables)));
                Jotunn.Logger.LogInfo("[DeathAndTaxes] ServersideQoL AutoStore: tribute tables excluded from auto pickup, stacking and sorting");
            }
            catch (Exception e)
            {
                Jotunn.Logger.LogWarning($"[DeathAndTaxes] Couldn't adjust ServersideQoL AutoStore: {e.Message}");
            }
        }

        /// A tribute table's container state gets pickup range 0: AutoStore never puts dropped or stacked items in it.
        private static void NoAutoStoreIntoTables(Container container, object __result)
        {
            if (__result == null || !container || TributeDefinitions.ForPiece(container.gameObject.name) == null) return;
            var range = AccessTools.Property(__result.GetType(), "AutoStorePickupRange");
            if (range != null && !Equals(range.GetValue(__result), 0f)) range.SetValue(__result, (float?)0f);
        }

        /// AutoStore's container processor (auto sort) is never attached to tribute tables.
        private static bool NoAutoSortOfTables(object __instance, object prefabInfo, ref bool __result)
        {
            if (__instance == null || __instance.GetType().FullName != "ServersideQoL.AutoStore.ContainerProcessor") return true;
            string name = Traverse.Create(prefabInfo).Property("PrefabName").GetValue<string>();
            if (name == null || TributeDefinitions.ForPiece(name) == null) return true;
            __result = false;
            return false;
        }
    }
}
