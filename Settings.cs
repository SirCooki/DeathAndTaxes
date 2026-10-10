using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using Jotunn.Configs;
using UnityEngine;

namespace DeathAndTaxes
{
    public enum Progression { Linear, Logarithmic }

    /// User settings in BepInEx/config/SirCookie.DeathAndTaxes.cfg.
    /// All are server settings (Jötunn admin-only): in single player your own file is used; on a server the
    /// server's file is used for everyone and players' own values are ignored while connected.
    /// Edits to the file are picked up while the game or server runs, and a server sends them to connected players.
    public static class Settings
    {
        public static ConfigEntry<float> BaseHoursPerStack;
        public static ConfigEntry<float> FinalHoursPerStack;
        public static ConfigEntry<Progression> ProgressionMode;
        public static ConfigEntry<bool> OverclockEnabled;
        public static ConfigEntry<int> MaxTablesPerPlayer;
        public static ConfigEntry<bool> NoRoofAllowed;
        private static ConfigEntry<float> s_noRoofPenalty;
        private static ConfigEntry<int> s_overclockMultiplier;

        /// Raised when any setting changes, including when the server's values arrive.
        public static event Action Changed;

        public static void Bind(ConfigFile config)
        {
            BaseHoursPerStack = config.Bind(ProductionSection, "Base hours per stack", 10f,
                Server("Hours a tribute table needs to fill one full stack of each resource, " +
                       "with only the trophies from its build cost."));
            FinalHoursPerStack = config.Bind(ProductionSection, "Final hours per stack", 1f,
                Server("Hours per full stack once the table holds the maximum number of trophies. " +
                       "Can't be slower than the base rate."));
            ProgressionMode = config.Bind(ProductionSection, "Progression", Progression.Logarithmic,
                Server("How the time per stack moves from the base to the final rate as trophies are added. " +
                       "Logarithmic: early trophies help most. Linear: every trophy takes off the same time."));

            NoRoofAllowed = config.Bind(ProductionSection, "No roof allowed", false,
                Server("Let tables produce without a roof, at a lower speed (see No roof penalty). " +
                       "Off: a table needs a roof and its clock stops without one."));
            s_noRoofPenalty = config.Bind(ProductionSection, "No roof penalty", 0.5f,
                Server("Speed without a roof when that is allowed: 0 = four times slower, 0.5 = half speed, " +
                       "1 = no penalty (speed = 4 ^ (value - 1)).",
                    new AcceptableValueRange<float>(0f, 1f)));

            OverclockEnabled = config.Bind(OverclockSection, "Enable overclocking", true,
                Server("Show the 'Remind them!' button: spend a boss trophy to speed up a table for one hour."));
            s_overclockMultiplier = config.Bind(OverclockSection, "Overclock multiplier", 2,
                Server("Production speed while reminded (2 = twice as fast). Between 2 and 5.",
                    new AcceptableValueRange<int>(2, 5)));

            foreach (var def in TributeDefinitions.All)
                def.ResourcesConfig = config.Bind(def.ConfigSection, "Generated resources",
                    string.Join(", ", def.AvailableResources),
                    Server("Comma-separated list of what this table generates (the window keeps its own order). " +
                           $"Only these can be used: {string.Join(", ", def.AvailableResources)}. " +
                           "Any other item name is ignored; this list can't add new resources."));

            MaxTablesPerPlayer = config.Bind(BuildingSection, "Tables per player", 1,
                Server("How many tables of each type (Meadows, Black Forest, ...) one player may build. 0 = no limit. " +
                       "Tables that already exist keep working; only building new ones is refused.",
                    new AcceptableValueRange<int>(0, 100)));

            MigrateOldSections(config);

            config.SettingChanged += (_, __) => Changed?.Invoke();
            Watch(config);
        }

        // Section order (user, 2026-10-09): 01 Production, 02 Building, 03 Overclock, 04 Meadows .. 10 Ashlands
        private const string ProductionSection = "01 - Production";
        private const string BuildingSection = "02 - Building";
        private const string OverclockSection = "03 - Overclock";

        // Older section names -> current ones (1.0.1 renumbering; "3 - Overclock" is the 0.2.x name)
        private static readonly Dictionary<string, string> RenamedSections = new Dictionary<string, string>
        {
            { "1 - Production", ProductionSection },
            { "2 - Overclock", OverclockSection },
            { "3 - Overclock", OverclockSection },
            { "3 - Meadows", "04 - Meadows" },
            { "4 - Black Forest", "05 - Black Forest" },
            { "5 - Building", BuildingSection },
            { "6 - Swamp", "06 - Swamp" },
            { "7 - Mountains", "07 - Mountains" },
            { "8 - Plains", "08 - Plains" },
            { "9 - Mistlands", "09 - Mistlands" },
        };

        /// Sections were renamed (0.3.0, and 1.0.1: two-digit numbers in a new order). Values in old sections move to the
        /// new names, unless the file already has that new section (then the file's new section wins). The 0.2.x
        /// "2 - Meadows" list is dropped (its resources changed). Without this the file would show both copies.
        private static void MigrateOldSections(ConfigFile config)
        {
            var orphans = Traverse.Create(config).Property("OrphanedEntries").GetValue<Dictionary<ConfigDefinition, string>>();
            if (orphans == null) return;
            var old = orphans.Keys.Where(k => k.Section == "2 - Meadows" || RenamedSections.ContainsKey(k.Section)).ToList();
            if (old.Count == 0) return;
            string text = File.Exists(config.ConfigFilePath) ? File.ReadAllText(config.ConfigFilePath) : "";
            foreach (var key in old)
            {
                if (RenamedSections.TryGetValue(key.Section, out var section) && !text.Contains("[" + section + "]"))
                {
                    var moved = new ConfigDefinition(section, key.Key);
                    if (config.ContainsKey(moved)) config[moved].SetSerializedValue(orphans[key]);
                }
                orphans.Remove(key);
            }
            config.Save();
            Debug.Log("[DeathAndTaxes] Moved the settings to the renamed sections");
        }

        private static ConfigDescription Server(string description, AcceptableValueBase range = null) =>
            new ConfigDescription("[Server] " + description + " On a server, the server's value is used for everyone.",
                range, new ConfigurationManagerAttributes { IsAdminOnly = true });

        public static float StartHours => Mathf.Max(0.01f, BaseHoursPerStack.Value);
        public static float FinalHours => Mathf.Clamp(FinalHoursPerStack.Value, 0.01f, StartHours);
        public static int OverclockMultiplier => Mathf.Clamp(s_overclockMultiplier.Value, 2, 5);

        /// Production speed without a roof: 4^(penalty-1), so 0 -> 25%, 0.5 -> 50%, 1 -> 100%.
        public static float NoRoofSpeed => Mathf.Pow(4f, Mathf.Clamp01(s_noRoofPenalty.Value) - 1f);

        // ---- live reload of the config file ----

        private static ConfigFile s_config;
        private static FileSystemWatcher s_watcher;
        private static long s_changedAtTicks; // set from the watcher's thread

        private static void Watch(ConfigFile config)
        {
            s_config = config;
            try
            {
                s_watcher = new FileSystemWatcher(Path.GetDirectoryName(config.ConfigFilePath), Path.GetFileName(config.ConfigFilePath))
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                };
                FileSystemEventHandler changed = (_, __) => s_changedAtTicks = DateTime.UtcNow.Ticks;
                s_watcher.Changed += changed;
                s_watcher.Created += changed;
                s_watcher.Renamed += (_, __) => s_changedAtTicks = DateTime.UtcNow.Ticks;
                s_watcher.EnableRaisingEvents = true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DeathAndTaxes] Can't watch the config file, edits need a restart: {e.Message}");
            }
        }

        /// Call from the main thread. Reloads the file once edits have settled for a second. When this game is the
        /// server (single player, hosting or a dedicated server), Jötunn then sends the new values to connected players.
        /// When connected to someone else's server, the local file is left alone: the server's values apply.
        public static void ReloadIfChanged()
        {
            long changedAt = s_changedAtTicks;
            if (changedAt == 0 || DateTime.UtcNow.Ticks - changedAt < TimeSpan.TicksPerSecond) return;
            s_changedAtTicks = 0;
            if (ZNet.instance && !ZNet.instance.IsServer()) return;
            if (!File.Exists(s_config.ConfigFilePath)) return;
            try
            {
                s_config.Reload();
                Debug.Log("[DeathAndTaxes] Settings reloaded from the config file");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DeathAndTaxes] Couldn't reload the config file: {e.Message}");
            }
        }

        public static void StopWatching()
        {
            s_watcher?.Dispose();
            s_watcher = null;
        }
    }
}
