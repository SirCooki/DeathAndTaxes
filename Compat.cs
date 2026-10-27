using System;
using System.Collections.Generic;
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

        // ---- OttoFuel (every client) ----

        public const string OttoFuelGuid = "potto007.OttoFuel";

        /// OttoFuel feeds smelters, kilns, fires, torches, ovens and ballistas from every container in range, tribute
        /// tables included. With "Block automatic taking" on, tribute tables are left out of its nearby-container list.
        public static void PatchOttoFuel(Harmony harmony)
        {
            if (!Chainloader.PluginInfos.ContainsKey(OttoFuelGuid)) return;
            try
            {
                var utils = AccessTools.TypeByName("OttoFuel.TastyUtils");
                var nearby = utils != null ? AccessTools.Method(utils, "GetNearbyContainers") : null;
                if (nearby == null || nearby.ReturnType != typeof(List<Container>))
                {
                    Jotunn.Logger.LogWarning("[DeathAndTaxes] OttoFuel found but not the expected code; \"Block automatic taking\" won't affect it");
                    return;
                }
                harmony.Patch(nearby, postfix: new HarmonyMethod(typeof(Compat), nameof(NoOttoFuelFromTables)));
                Jotunn.Logger.LogInfo("[DeathAndTaxes] OttoFuel: tribute tables skipped while \"Block automatic taking\" is on");
            }
            catch (Exception e)
            {
                Jotunn.Logger.LogWarning($"[DeathAndTaxes] Couldn't adjust OttoFuel: {e.Message}");
            }
        }

        private static void NoOttoFuelFromTables(List<Container> __result)
        {
            if (__result == null || !Settings.BlockAutoTake.Value) return;
            __result.RemoveAll(c => c && TributeDefinitions.ForPiece(c.gameObject.name) != null);
        }

        // ---- ServersideQoL (server only) ----

        public const string ServersideQoLGuid = "ArgusMagnus.ServersideQoL";
        public const string ServersideQoLAutoStoreGuid = "ArgusMagnus.ServersideQoL.AutoStore";

        /// ServersideQoL edits container contents in the save data on the server, past Inventory.AddItem.
        /// Its AutoStore would put dropped/stacked items into tribute tables (the table pushes them out again: a loop)
        /// and re-sort them (fighting the fixed resource slots). Tribute tables get pickup range 0, AutoStore's own
        /// "never store into this chest" (what its chest sign "🧲0" does), and AutoStore's sorting is not attached to
        /// them. Taking out stays allowed (AutoProcess feeding smelters, TameAssist feeding tames) unless
        /// "Block automatic taking" is on: then their feed ranges for tribute tables are 0 as well.
        /// Called from Plugin.Start, after every plugin has loaded; ServersideQoL looks objects up lazily later.
        public static void PatchServersideQoL(Harmony harmony)
        {
            if (!Chainloader.PluginInfos.ContainsKey(ServersideQoLGuid)) return;
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
        /// With "Block automatic taking" on, AutoProcess and TameAssist get feed range 0 too (their sign "↔️0"/"🐗0").
        /// ServersideQoL keeps this state per container, so a changed setting applies after a server restart.
        private static void NoAutoStoreIntoTables(Container container, object __result)
        {
            if (__result == null || !container || TributeDefinitions.ForPiece(container.gameObject.name) == null) return;
            SetRangeZero(__result, "AutoStorePickupRange");
            if (!Settings.BlockAutoTake.Value) return;
            SetRangeZero(__result, "AutoProcessFeedRange");
            SetRangeZero(__result, "TameAssistFeedRange");
        }

        private static void SetRangeZero(object state, string name)
        {
            var range = AccessTools.Property(state.GetType(), name);
            if (range == null || !range.CanWrite || Equals(range.GetValue(state), 0f)) return;
            range.SetValue(state, range.PropertyType == typeof(float) ? (object)0f : (float?)0f);
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
