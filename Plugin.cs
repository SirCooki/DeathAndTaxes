using BepInEx;
using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using Jotunn.Utils;
using UnityEngine;

namespace DeathAndTaxes
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Patch)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGUID = "SirCookie.DeathAndTaxes";
        public const string PluginName = "Death & Taxes";
        public const string PluginVersion = "1.0.5"; // minor = biomes (Ashlands 1.0, Deep North 1.1), patch = chat session

        private void Awake()
        {
            MigrateOldConfig();
            Settings.Bind(Config);
            Settings.Changed += TributeTable.OnSettingsChanged;
            PrefabManager.OnVanillaPrefabsAvailable += AddPieces;
            m_harmony = new Harmony(PluginGUID);
            m_harmony.PatchAll();
            CommandManager.Instance.AddConsoleCommand(new TrophyKitCommand()); // "dt_trophies" test kit (cheat)
            Jotunn.Logger.LogInfo($"{PluginName} {PluginVersion} loaded");
        }

        private Harmony m_harmony;

        /// Build menu icon = a picture of the table itself (front three-quarter view, top slightly visible), made by
        /// Jötunn's renderer once it is ready; until then the base table's icon is shown.
        private static void RenderIcon(GameObject prefab)
        {
            var piece = prefab.GetComponent<Piece>();
            // Jötunn's render camera looks at the model's back (+Z); turn the front (no wall) towards it
            var request = new RenderManager.RenderRequest(prefab)
            {
                Rotation = Quaternion.Euler(14f, 0f, 0f) * Quaternion.Euler(0f, 205f, 0f),
                Width = 128,
                Height = 128,
            };
            RenderManager.Instance.EnqueueRender(request, sprite =>
            {
                if (sprite && piece) piece.m_icon = sprite;
                else Jotunn.Logger.LogWarning($"No build menu icon rendered for {prefab.name}");
            });
        }

        /// Until 0.3.1 the mod was "Lord of Biomes": take over its settings once (the old file stays untouched).
        private void MigrateOldConfig()
        {
            string oldPath = System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "SirCookie.LordOfBiomes.cfg");
            if (!System.IO.File.Exists(oldPath) || Config.Keys.Count > 0) return;
            string current = System.IO.File.Exists(Config.ConfigFilePath) ? System.IO.File.ReadAllText(Config.ConfigFilePath) : "";
            if (current.Contains("[")) return; // already has its own settings
            System.IO.File.Copy(oldPath, Config.ConfigFilePath, true);
            Config.Reload();
            Jotunn.Logger.LogInfo("Settings taken over from SirCookie.LordOfBiomes.cfg");
        }

        // After every plugin's Awake: optional patches into other mods
        private void Start()
        {
            Compat.PatchServersideQoL(m_harmony);
            Compat.PatchOttoFuel(m_harmony);
        }

        private void Update()
        {
            Settings.ReloadIfChanged();
            TrophyStands.ShowNearbyRanges();
        }

        private void OnDestroy() => Settings.StopWatching();

        private void AddPieces()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= AddPieces;
            NewTrophies.Add();
            foreach (var def in TributeDefinitions.All)
                AddTribute(def);
        }

        private static void AddTribute(TributeDefinition def)
        {
            var config = new PieceConfig
            {
                Name = def.DisplayName,
                Description = def.Description,
                PieceTable = PieceTables.Hammer,
                Category = PieceCategories.Misc,
            };
            config.AddRequirement(new RequirementConfig(def.BuildMaterial, def.BuildAmount, 0, true));
            foreach (var trophy in def.StartTrophies)
                config.AddRequirement(new RequirementConfig(trophy, 1, 0, true));

            // Vanilla table with a trophy wall and offerings kitbashed onto it
            var piece = new CustomPiece(def.PieceName, def.BaseTable, config);
            var prefab = piece.PiecePrefab;
            TributeModel.Build(prefab, def);
            RenderIcon(prefab);
            prefab.GetComponent<Piece>().m_randomInitBuildRotation = false; // starts facing the player instead

            // Storage for generated resources: one row per tier, one slot per resource
            var container = prefab.AddComponent<Container>();
            container.m_name = def.TableName;
            container.m_width = def.GridWidth; // follows the enabled resources per table
            container.m_height = def.GridHeight;
            container.m_privacy = Container.PrivacySetting.Public;
            container.m_checkGuardStone = true; // ward rules, like vanilla chests: only permitted players can open it
            var chest = PrefabManager.Instance.GetPrefab("piece_chest_wood");
            var chestContainer = chest ? chest.GetComponent<Container>() : null;
            if (chestContainer)
            {
                container.m_bkg = chestContainer.m_bkg;
                container.m_openEffects = chestContainer.m_openEffects;
                container.m_closeEffects = chestContainer.m_closeEffects;
            }

            prefab.AddComponent<TributeTable>(); // finds its definition from the prefab name
            PieceManager.Instance.AddPiece(piece);
        }
    }
}
