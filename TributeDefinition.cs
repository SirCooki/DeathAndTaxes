using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;

namespace DeathAndTaxes
{
    /// One item model lying on the table top (decoration only).
    public class Offering
    {
        public string Prefab;
        public float U, V;        // position: U 0 = left .. 1 = right, V 0 = front edge .. 1 = back wall
        public float Yaw;         // turn in degrees, seen from above
        public float Tilt;        // roll in degrees; 90 = lying on its side
        public float MaxSize;     // metres: the model is scaled so its largest side is this long
        public bool AlongWall;    // turn the model's long side parallel to the back wall (before Yaw)
        public float Lift;        // metres above the table top, to stack on other offerings

        public Offering(string prefab, float u, float v, float yaw = 0f, float tilt = 0f,
            float maxSize = 1f, bool alongWall = false, float lift = 0f)
        {
            Prefab = prefab; U = u; V = v; Yaw = yaw; Tilt = tilt; MaxSize = maxSize; AlongWall = alongWall; Lift = lift;
        }
    }

    /// One trophy mounted on the back wall.
    public class WallTrophy
    {
        public string Trophy;
        public int Row;       // 0 = lowest row; trophies in a row share the table width equally (left to right)
        public float Height;  // centre as a fraction of the wall height (floor .. wall top)
        public float Size;    // extra size on top of the shared scale, for small trophies (skulls)
        public float Turn;    // degrees around the wall normal, e.g. 90 to hang a long trophy horizontally
        public bool Center;   // move the trophy model to the middle of its plaque (models that hang below the stand)

        public WallTrophy(string trophy, int row, float height, float size = 1f, float turn = 0f, bool center = false)
        {
            Trophy = trophy; Row = row; Height = height; Size = size; Turn = turn; Center = center;
        }
    }

    public enum Rarity { Common, Uncommon, Rare, Unique }

    public static class Rarities
    {
        /// How many trophies of one type a table accepts.
        public static int MaxPerTrophy(Rarity rarity) => rarity switch
        {
            Rarity.Common => 10,
            Rarity.Uncommon => 5,
            Rarity.Rare => 3,
            _ => 1,
        };

        // Share of each trophy rarity (Common, Uncommon, Rare, Unique) in the speed of a resource tier
        private static readonly float[][] Weights =
        {
            new[] { 1f },                          // common resources
            new[] { 0.35f, 0.65f },                // uncommon resources
            new[] { 0.15f, 0.35f, 0.50f },         // rare resources
            new[] { 0.10f, 0.20f, 0.25f, 0.45f },  // unique resources
        };

        public static float Weight(Rarity resourceTier, Rarity trophyRarity)
        {
            var weights = Weights[(int)resourceTier];
            int i = (int)trophyRarity;
            return i < weights.Length ? weights[i] : 0f;
        }

        public static string Color(Rarity rarity) => rarity switch
        {
            Rarity.Common => "#D8D8D8",
            Rarity.Uncommon => "#6ED36E",
            Rarity.Rare => "#5AA9FF",
            _ => "#FFB84D",
        };
    }

    /// Trophies of one rarity and the resources they unlock.
    public class TributeTier
    {
        public Rarity Rarity;
        public string[] Trophies;
        public string[] Resources;  // produced once UnlockAt trophies of this tier are on the table
        public int UnlockAt;        // 0 = produced from the start

        public int MaxPerTrophy => Rarities.MaxPerTrophy(Rarity);
        public int MaxTrophies => Trophies.Length * MaxPerTrophy;

        /// Trophies each type starts with: the build cost holds one of each common trophy.
        public int StartCount => Rarity == Rarity.Common ? 1 : 0;
    }

    /// Where a resource lives in the storage (and the window): column X of row Y, Y 0 = top row.
    public struct ResourceSlot
    {
        public string Resource;
        public int Tier;
        public int X, Y;
    }

    public class TributeDefinition
    {
        public string PieceName;
        public string DisplayName;     // build menu name
        public string TableName;       // storage window title and hover name
        public string Description;
        public string BossTrophy;      // must be in inventory to discover the piece; spent by "Remind them!"
        public string BaseTable;       // vanilla table the model is built on
        public string BuildMaterial;   // build cost, plus one of each common trophy
        public string LookFrom;        // optional prefab whose material re-skins the table and wall (biome look)
        public Color LookTint = Color.white; // multiplies the table and wall colours (white = unchanged)
        public float LookGray;         // 0..1: pulls the table and wall texture colours towards gray
        public float LookWhite;        // 0..1: then pulls them towards white (1 = plain white, no grain)
        public float TrophyScale;      // fixed shared trophy scale; 0 = computed so every trophy fits
        public bool AlignPlaques;      // row height = plaque centre (true) or centre of plaque + trophy together (false)
        public int BuildAmount;
        public float WallHeightAboveTable; // metres of back wall above the table top
        public Vector2 Footprint;      // metres, table width x depth (the wall stands behind it); zero = the base table's own size
        public TributeTier[] Tiers;    // common first
        public WallTrophy[] Wall;      // every trophy of every tier, as mounted on the back wall
        public string ConfigSection;   // config section for this table's own settings
        public Offering[] Offerings;   // item models shown on the table top
        public string[] TooManyTrophies; // extra messages for this table when more trophies are offered than fit
        public string[] Reminded;        // extra messages for this table after "Remind them!"

        private string[] m_available;

        /// Everything this table may generate (prefab names), tier by tier.
        public string[] AvailableResources => m_available ??= Tiers.SelectMany(t => t.Resources).ToArray();

        public IEnumerable<string> AllTrophies => Tiers.SelectMany(t => t.Trophies);

        /// The trophies in the build cost: one of each common trophy.
        public IEnumerable<string> StartTrophies => Tiers.Where(t => t.StartCount > 0).SelectMany(t => t.Trophies);

        public int TierOfTrophy(string trophy) => Array.FindIndex(Tiers, t => t.Trophies.Contains(trophy));

        /// Row of a tier in the storage and window: the highest tier on top (row 0), common at the bottom.
        public int RowOf(int tier) => Tiers.Length - 1 - tier;

        /// Speed score of a resource tier (0..1): every trophy rarity's share of its maximum, weighted per tier.
        /// E.g. rare resources with 12/30 common, 4/15 uncommon, 2/6 rare: 0.4*0.15 + 0.27*0.35 + 0.33*0.5 = 0.32.
        public float Score(int tier, Func<string, int> trophyCount)
        {
            var rarity = Tiers[tier].Rarity;
            float score = 0f;
            foreach (var t in Tiers)
            {
                float weight = Rarities.Weight(rarity, t.Rarity);
                if (weight > 0f && t.MaxTrophies > 0)
                    score += weight * t.Trophies.Sum(trophyCount) / t.MaxTrophies;
            }
            return Mathf.Clamp01(score);
        }

        /// Score of the build cost alone (3/30 common trophies): production runs at the base rate there.
        public float StartScore
        {
            get
            {
                var common = Tiers.FirstOrDefault(t => t.StartCount > 0);
                float start = common != null && common.MaxTrophies > 0
                    ? common.Trophies.Length * common.StartCount / (float)common.MaxTrophies
                    : 0.1f;
                return Mathf.Clamp(start, 0.01f, 0.99f);
            }
        }

        /// Hours to generate one full stack at a speed score: from the base rate (build-cost trophies only)
        /// to the final rate (score 1), linear or logarithmic in the score.
        public float HoursPerStack(float score)
        {
            float start = Settings.StartHours, end = Settings.FinalHours, zero = StartScore;
            float p = Settings.ProgressionMode.Value == Progression.Linear
                ? (score - zero) / (1f - zero)
                : Mathf.Log(Mathf.Max(score, zero) / zero) / Mathf.Log(1f / zero);
            return Mathf.Lerp(start, end, Mathf.Clamp01(p));
        }

        public ConfigEntry<string> ResourcesConfig;
        private string m_parsedResources;
        private string[] m_enabledResources;
        private ResourceSlot[] m_layout;
        private int m_gridWidth;

        /// Resources switched on in the config. Unknown names are ignored;
        /// if nothing valid is left, all available resources are used.
        public string[] EnabledResources { get { Parse(); return m_enabledResources; } }

        /// Storage slot of every enabled resource. A new array whenever the enabled resources change.
        public ResourceSlot[] Layout { get { Parse(); return m_layout; } }

        /// Storage size: the longest row of enabled resources by one row per tier.
        public int GridWidth { get { Parse(); return m_gridWidth; } }
        public int GridHeight => Tiers.Length;

        private void Parse()
        {
            string raw = ResourcesConfig != null ? ResourcesConfig.Value ?? "" : string.Join(",", AvailableResources);
            if (m_layout != null && raw == m_parsedResources) return;

            var enabled = new List<string>();
            foreach (var part in raw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string name = part.Trim();
                string match = AvailableResources.FirstOrDefault(r => string.Equals(r, name, StringComparison.OrdinalIgnoreCase));
                if (match == null)
                {
                    if (name.Length > 0)
                        Debug.LogWarning($"[DeathAndTaxes] '{name}' can't be generated by {TableName}, ignored");
                    continue;
                }
                if (!enabled.Contains(match)) enabled.Add(match);
            }
            if (enabled.Count == 0) enabled.AddRange(AvailableResources);

            // Each tier gets its own row, resources in definition order
            var layout = new List<ResourceSlot>();
            int width = 1;
            for (int tier = 0; tier < Tiers.Length; tier++)
            {
                int x = 0;
                foreach (var resource in Tiers[tier].Resources)
                    if (enabled.Contains(resource))
                        layout.Add(new ResourceSlot { Resource = resource, Tier = tier, X = x++, Y = RowOf(tier) });
                width = Math.Max(width, x);
            }

            m_parsedResources = raw;
            m_enabledResources = enabled.ToArray();
            m_gridWidth = width;
            m_layout = layout.ToArray();
        }
    }

    public static class TributeDefinitions
    {
        public static readonly TributeDefinition Meadows = new TributeDefinition
        {
            PieceName = "tribute_meadows",
            DisplayName = "Tribute - Meadows",
            TableName = "Tribute - Meadows",
            Description = "All fear you now, these wretches bring offerings to their new lord.",
            BossTrophy = "TrophyEikthyr",
            BaseTable = "piece_table",
            BuildMaterial = "Wood",
            BuildAmount = 10,
            WallHeightAboveTable = 1.4f,
            Tiers = new[]
            {
                new TributeTier
                {
                    Rarity = Rarity.Common,
                    Trophies = new[] { "TrophyBoar", "TrophyDeer", "TrophyNeck" },
                    Resources = new[] { "Wood", "Mushroom", "Flint", "Dandelion", "Raspberry", "LeatherScraps", "Feathers" },
                },
            },
            Wall = new[]
            {
                new WallTrophy("TrophyBoar", 0, 0.65f),
                new WallTrophy("TrophyDeer", 0, 0.80f),
                new WallTrophy("TrophyNeck", 0, 0.65f),
            },
            ConfigSection = "04 - Meadows",
            TooManyTrophies = new[]
            {
                "Should you go to the Black Forest?",
                "Having a power fantasy with black metal gear?",
            },
            Offerings = new[]
            {
                // Log along the back wall
                new Offering("Wood", 0.50f, 0.92f, maxSize: 1.5f, alongWall: true),
                // Mushrooms on their side, left
                new Offering("Mushroom", 0.07f, 0.62f, yaw: 15f, tilt: 90f, maxSize: 0.18f),
                new Offering("Mushroom", 0.16f, 0.52f, yaw: 105f, tilt: 90f, maxSize: 0.18f),
                new Offering("Mushroom", 0.08f, 0.42f, yaw: 230f, tilt: 90f, maxSize: 0.18f),
                // Little pile of raspberries in front of the mushrooms (two on top)
                new Offering("Raspberry", 0.07f, 0.18f, yaw: 40f, maxSize: 0.1f),
                new Offering("Raspberry", 0.15f, 0.22f, yaw: 160f, maxSize: 0.1f),
                new Offering("Raspberry", 0.11f, 0.13f, yaw: 250f, maxSize: 0.1f),
                new Offering("Raspberry", 0.11f, 0.27f, yaw: 95f, maxSize: 0.1f),
                new Offering("Raspberry", 0.04f, 0.25f, yaw: 300f, maxSize: 0.1f),
                new Offering("Raspberry", 0.18f, 0.15f, yaw: 10f, maxSize: 0.1f),
                new Offering("Raspberry", 0.11f, 0.20f, yaw: 200f, maxSize: 0.1f, lift: 0.06f),
                new Offering("Raspberry", 0.08f, 0.23f, yaw: 130f, maxSize: 0.1f, lift: 0.05f),
                // Flint in the middle at different angles
                new Offering("Flint", 0.40f, 0.55f, yaw: 10f, maxSize: 0.45f),
                new Offering("Flint", 0.56f, 0.45f, yaw: 75f, tilt: 15f, maxSize: 0.45f),
                new Offering("Flint", 0.47f, 0.28f, yaw: 150f, tilt: -10f, maxSize: 0.45f),
                // Feathers between the flint and the leather
                new Offering("Feathers", 0.70f, 0.62f, yaw: 40f, maxSize: 0.3f),
                new Offering("Feathers", 0.75f, 0.47f, yaw: 150f, maxSize: 0.28f),
                // Leather scraps back right (where the stones were), dandelions on their side in front of them
                new Offering("LeatherScraps", 0.88f, 0.70f, yaw: 30f, maxSize: 0.32f),
                new Offering("LeatherScraps", 0.94f, 0.55f, yaw: 110f, maxSize: 0.28f),
                new Offering("Dandelion", 0.87f, 0.34f, yaw: 80f, tilt: 90f, maxSize: 0.25f),
                new Offering("Dandelion", 0.80f, 0.27f, yaw: 20f, tilt: 90f, maxSize: 0.25f),
                new Offering("Dandelion", 0.92f, 0.22f, yaw: 145f, tilt: 75f, maxSize: 0.25f),
            },
        };

        public static readonly TributeDefinition BlackForest = new TributeDefinition
        {
            PieceName = "tribute_blackforest",
            DisplayName = "Tribute - Black Forest",
            TableName = "Tribute - Black Forest",
            Description = "The trees tremble at your passage. The forest sends tribute to its lord.",
            BossTrophy = "TrophyTheElder",
            BaseTable = "piece_table_oak",
            BuildMaterial = "RoundLog",
            BuildAmount = 10,
            Footprint = new Vector2(3f, 1.25f), // Meadows is 2.5 x 1.25 m (the oak table itself is 6.6 x 1.9 m)
            WallHeightAboveTable = 2.5f,        // wall top 3.3 m: three trophy rows above the corewood
            Tiers = new[]
            {
                new TributeTier
                {
                    Rarity = Rarity.Common,
                    Trophies = new[] { "TrophyGreydwarf", "TrophySkeleton", "TrophyFrostTroll" },
                    Resources = new[] { "Resin", "Stone", "Blueberries", "Thistle", "BoneFragments", "GreydwarfEye" },
                },
                new TributeTier
                {
                    Rarity = Rarity.Uncommon,
                    Trophies = new[] { "TrophyGreydwarfBrute", "TrophyGreydwarfShaman", "TrophyBjorn" },
                    Resources = new[] { "RoundLog", "Amber", "DeerHide", "MushroomYellow" },
                    UnlockAt = 6,
                },
                new TributeTier
                {
                    Rarity = Rarity.Rare,
                    Trophies = new[] { "TrophySkeletonPoison", "TrophyGhost" },
                    Resources = new[] { "BjornHide", "TrollHide", "TinOre" },
                    UnlockAt = 2,
                },
                new TributeTier
                {
                    Rarity = Rarity.Unique,
                    Trophies = new[] { "TrophySkeletonHildir" },
                    Resources = new[] { "CopperOre" },
                    UnlockAt = 1,
                },
            },
            Wall = new[]
            {
                // Wall top 3.3 m; rows at 1.5 / 2.2 / 2.95 m, the lowest one clear of the corewood log
                // Bottom row: common
                new WallTrophy("TrophyGreydwarf", 0, 0.455f, size: 1.5f),
                new WallTrophy("TrophySkeleton", 0, 0.455f, size: 2f),
                new WallTrophy("TrophyFrostTroll", 0, 0.455f),
                // Middle row: uncommon
                new WallTrophy("TrophyGreydwarfBrute", 1, 0.673f),
                new WallTrophy("TrophyGreydwarfShaman", 1, 0.673f),
                new WallTrophy("TrophyBjorn", 1, 0.673f),
                // Top row: rare, with the unique one in the middle, slightly higher
                new WallTrophy("TrophySkeletonPoison", 2, 0.89f, size: 2f),  // Rancid remains
                new WallTrophy("TrophySkeletonHildir", 2, 0.915f, size: 3f), // Brenna's skull
                new WallTrophy("TrophyGhost", 2, 0.89f, size: 3f),
            },
            ConfigSection = "05 - Black Forest",
            TooManyTrophies = new[]
            {
                "No more victims in the forest!",
                "Do you think this is Pandora?",
                "How have you found even more?",
                "Consider dying in the swamp.",
                "Iron is much better than bronze.",
                "Is it not time to end this bronze age of yours?",
                "It isn't 2020 anymore, you are not halfway.",
            },
            Offerings = new[]
            {
                // Corewood along the back wall
                new Offering("RoundLog", 0.50f, 0.92f, maxSize: 1.2f, alongWall: true),
                // Left to right, spread over the whole top (2x the first sizes):
                // greydwarf eyes, amber, thistles, yellow mushroom + blueberries, stones
                new Offering("GreydwarfEye", 0.085f, 0.45f, yaw: 0f, maxSize: 0.2f),
                new Offering("GreydwarfEye", 0.145f, 0.42f, yaw: 120f, maxSize: 0.2f),
                new Offering("GreydwarfEye", 0.115f, 0.44f, yaw: 240f, maxSize: 0.2f, lift: 0.12f),
                new Offering("Amber", 0.26f, 0.42f, yaw: 20f, maxSize: 0.28f),
                new Offering("Amber", 0.34f, 0.42f, yaw: 140f, maxSize: 0.28f),
                new Offering("Amber", 0.30f, 0.42f, yaw: 75f, maxSize: 0.24f, lift: 0.12f),
                new Offering("Thistle", 0.47f, 0.45f, yaw: 70f, tilt: 90f, maxSize: 0.7f),
                new Offering("Thistle", 0.53f, 0.28f, yaw: 155f, tilt: 90f, maxSize: 0.7f),
                new Offering("MushroomYellow", 0.66f, 0.55f, yaw: 20f, maxSize: 0.4f),
                new Offering("Blueberries", 0.70f, 0.28f, yaw: 0f, maxSize: 0.2f),
                new Offering("Blueberries", 0.77f, 0.30f, yaw: 80f, maxSize: 0.2f),
                new Offering("Blueberries", 0.73f, 0.13f, yaw: 160f, maxSize: 0.2f),
                new Offering("Blueberries", 0.80f, 0.15f, yaw: 250f, maxSize: 0.2f),
                new Offering("Stone", 0.89f, 0.70f, yaw: 30f, maxSize: 0.64f),
                new Offering("Stone", 0.93f, 0.35f, yaw: 110f, maxSize: 0.52f),
            },
        };

        public static readonly TributeDefinition Swamp = new TributeDefinition
        {
            PieceName = "tribute_swamp",
            DisplayName = "Tribute - Swamp",
            TableName = "Tribute - Swamp",
            Description = "The dead rise only to kneel. The mire gives up its treasures to its lord.",
            BossTrophy = "TrophyBonemass",
            BaseTable = "piece_table_oak",
            BuildMaterial = "ElderBark",
            BuildAmount = 10,
            LookGray = 0.75f,                  // same wood as the other tables, but gray instead of brown (user, 2026-10-07)
            LookTint = new Color(1.15f, 1.15f, 1.15f), // a bit lighter: the gray wood came out dark
            TrophyScale = 0.33f,               // fixed, so the sizes below are plain multipliers (tuned from screenshots)
            AlignPlaques = true,
            Footprint = new Vector2(3f, 1.25f), // same size as the Black Forest table
            WallHeightAboveTable = 3f,          // wall top 3.8 m: room for the bigger Draugr, Surtling, Wraith and Abomination
            Tiers = new[]
            {
                new TributeTier
                {
                    Rarity = Rarity.Common,
                    Trophies = new[] { "TrophyDraugr", "TrophyLeech", "TrophySkeleton" },
                    Resources = new[] { "ElderBark", "Entrails", "Bloodbag", "Coal" },
                },
                new TributeTier
                {
                    Rarity = Rarity.Uncommon,
                    Trophies = new[] { "TrophySurtling", "TrophyBlob", "TrophyWraith" },
                    Resources = new[] { "Guck", "Ooze", "Chain" },
                    UnlockAt = 6,
                },
                new TributeTier
                {
                    Rarity = Rarity.Rare,
                    Trophies = new[] { "TrophyDraugrElite", "TrophyAbomination" },
                    Resources = new[] { "Root", "SurtlingCore", "AmberPearl" },
                    UnlockAt = 2,
                },
                new TributeTier
                {
                    Rarity = Rarity.Unique,
                    Trophies = new[] { "TrophyKvastur" },
                    Resources = new[] { "IronScrap" },
                    UnlockAt = 1,
                },
            },
            Wall = new[]
            {
                // Same rows as the Black Forest; sizes tuned from the first in-game screenshots (2026-10-06)
                // Bottom row: common
                new WallTrophy("TrophyDraugr", 0, 0.38f, size: 3.375f),     // 3x, then 75% (beard hung too low)
                new WallTrophy("TrophyLeech", 0, 0.38f, size: 1.5f),
                new WallTrophy("TrophySkeleton", 0, 0.38f, size: 2.36f),    // same size as on the Black Forest wall
                // Middle row: uncommon
                new WallTrophy("TrophySurtling", 1, 0.617f, size: 3.2f),     // 2x, then 80%
                new WallTrophy("TrophyBlob", 1, 0.617f, size: 4.5f),        // 3x
                new WallTrophy("TrophyWraith", 1, 0.617f, size: 0.92f),      // as tall as the Black Forest troll head
                // Top row: rare, with the unique one in the middle, horizontal and a little higher
                new WallTrophy("TrophyDraugrElite", 2, 0.853f, size: 3.375f), // same size as the Draugr
                new WallTrophy("TrophyKvastur", 2, 0.89f, size: 1.05f, turn: 90f), // ~0.85 m wide, fits its slot
                new WallTrophy("TrophyAbomination", 2, 0.853f, size: 1f),    // 2x
            },
            ConfigSection = "06 - Swamp",
            Offerings = new[]
            {
                // Ancient bark along the back wall
                new Offering("ElderBark", 0.50f, 0.92f, maxSize: 1.2f, alongWall: true),
                // Left to right: bloodbags, entrails, guck + ooze, chain, scrap iron with a glowing surtling core
                new Offering("Bloodbag", 0.08f, 0.45f, yaw: 20f, maxSize: 0.25f),
                new Offering("Bloodbag", 0.15f, 0.40f, yaw: 140f, maxSize: 0.25f),
                new Offering("Bloodbag", 0.11f, 0.43f, yaw: 260f, maxSize: 0.22f, lift: 0.12f),
                new Offering("Entrails", 0.26f, 0.50f, yaw: 60f, maxSize: 0.35f),
                new Offering("Entrails", 0.30f, 0.25f, yaw: 200f, maxSize: 0.32f),
                new Offering("Guck", 0.45f, 0.45f, yaw: 0f, maxSize: 0.35f),
                new Offering("Ooze", 0.55f, 0.30f, yaw: 90f, maxSize: 0.25f),
                new Offering("Ooze", 0.50f, 0.18f, yaw: 210f, maxSize: 0.25f),
                new Offering("Chain", 0.68f, 0.45f, yaw: 30f, tilt: 90f, maxSize: 0.5f), // lying flat
                new Offering("AmberPearl", 0.72f, 0.18f, yaw: 0f, maxSize: 0.12f),
                new Offering("IronScrap", 0.87f, 0.65f, yaw: 30f, maxSize: 0.45f),
                new Offering("IronScrap", 0.92f, 0.40f, yaw: 120f, maxSize: 0.40f),
                new Offering("SurtlingCore", 0.84f, 0.25f, yaw: 45f, maxSize: 0.2f),
            },
        };

        public static readonly TributeDefinition Mountains = new TributeDefinition
        {
            PieceName = "tribute_mountains",
            DisplayName = "Tribute - Mountains",
            TableName = "Tribute - Mountains",
            Description = "Even the wolves lower their heads. The peaks send tribute to their lord.",
            BossTrophy = "TrophyDragonQueen",
            BaseTable = "piece_table_oak",
            BuildMaterial = "Stone",
            BuildAmount = 10,
            LookGray = 1f,                     // same wood as the other tables, made white (user, 2026-10-08)
            LookWhite = 0.4f,                  // gray-white, the grain stays visible (0.75 glared under torch light)
            LookTint = new Color(0.9f, 0.9f, 0.9f),
            TrophyScale = 0.41f,               // fixed, so sizes are plain multipliers
            AlignPlaques = true,
            Footprint = new Vector2(3f, 1.25f),
            WallHeightAboveTable = 2.5f,
            Tiers = new[]
            {
                new TributeTier { Rarity = Rarity.Common,
                    Trophies = new[] { "TrophyWolf", "TrophyFenring", "TrophyDraugr" },
                    Resources = new[] { "Obsidian", "WolfPelt", "WolfFang", "WolfMeat", "WolfClaw" } },
                new TributeTier { Rarity = Rarity.Uncommon,
                    Trophies = new[] { "TrophyUlv", "TrophyCultist", "TrophyHatchling" },
                    Resources = new[] { "WolfHairBundle", "FreezeGland", "JuteRed" }, UnlockAt = 6 },
                new TributeTier { Rarity = Rarity.Rare,
                    Trophies = new[] { "TrophySGolem" },
                    Resources = new[] { "Crystal" }, UnlockAt = 1 },
                new TributeTier { Rarity = Rarity.Unique,
                    Trophies = new[] { "TrophyCultist_Hildir" },
                    Resources = new[] { "SilverOre" }, UnlockAt = 1 },
            },
            Wall = new[]
            {
                new WallTrophy("TrophyWolf", 0, 0.455f, size: 2f),           // 2x
                new WallTrophy("TrophyFenring", 0, 0.455f, center: true),
                new WallTrophy("TrophyDraugr", 0, 0.455f),                 // size taken from the Swamp table (first table with it)
                new WallTrophy("TrophyUlv", 1, 0.69f),
                new WallTrophy("TrophyHatchling", 1, 0.69f, size: 2f),       // 2x (Drake); in the middle, clear of the Draugr
                new WallTrophy("TrophyCultist", 1, 0.69f),
                new WallTrophy("TrophySGolem", 2, 0.89f),
                new WallTrophy("TrophyCultist_Hildir", 2, 0.915f),
            },
            ConfigSection = "07 - Mountains",
            Offerings = new[]
            {
                new Offering("WolfPelt", 0.50f, 0.90f, maxSize: 1.2f, alongWall: true),
                new Offering("Obsidian", 0.08f, 0.50f, yaw: 20f, maxSize: 0.35f),
                new Offering("Obsidian", 0.15f, 0.30f, yaw: 140f, maxSize: 0.3f),
                new Offering("WolfFang", 0.27f, 0.45f, yaw: 60f, tilt: 90f, maxSize: 0.25f),
                new Offering("WolfFang", 0.32f, 0.30f, yaw: 200f, tilt: 90f, maxSize: 0.25f),
                new Offering("WolfMeat", 0.45f, 0.45f, yaw: 30f, maxSize: 0.35f),
                new Offering("FreezeGland", 0.56f, 0.25f, yaw: 0f, maxSize: 0.2f),
                new Offering("WolfClaw", 0.66f, 0.45f, yaw: 110f, maxSize: 0.3f),
                new Offering("Crystal", 0.80f, 0.60f, yaw: 15f, maxSize: 0.45f),
                new Offering("SilverOre", 0.90f, 0.30f, yaw: 70f, maxSize: 0.4f),
            },
        };

        public static readonly TributeDefinition Plains = new TributeDefinition
        {
            PieceName = "tribute_plains",
            DisplayName = "Tribute - Plains",
            TableName = "Tribute - Plains",
            Description = "The fulings have learned whose land this is. The plains pay their tithe to their lord.",
            BossTrophy = "TrophyGoblinKing",
            BaseTable = "piece_table_oak",
            BuildMaterial = "FineWood",
            BuildAmount = 10,
            LookGray = 0.6f,                   // oak wood recoloured (2026-10-08): pale straw, like fine wood
            LookWhite = 0.35f,
            LookTint = new Color(1f, 0.95f, 0.8f),
            AlignPlaques = true,
            Footprint = new Vector2(3f, 1.25f),
            WallHeightAboveTable = 3f,          // wall top 3.8 m, rows spread like the Swamp
            TrophyScale = 0.34f,               // fixed, so sizes are plain multipliers
            Tiers = new[]
            {
                new TributeTier { Rarity = Rarity.Common,
                    Trophies = new[] { "TrophyGoblin", "TrophyGrowth" },
                    Resources = new[] { "Tar", "Cloudberry", "FineWood", "RottenMeat" } },
                new TributeTier { Rarity = Rarity.Uncommon,
                    Trophies = new[] { "TrophyDeathsquito", "TrophyGoblinBrute", "TrophyGoblinShaman" },
                    Resources = new[] { "Needle", "Flax", "Barley", "AmberPearl" }, UnlockAt = 6 },
                new TributeTier { Rarity = Rarity.Rare,
                    Trophies = new[] { "TrophyLox", "TrophyBjornUndead" },
                    Resources = new[] { "LoxPelt", "Ruby", "UndeadBjornRibcage" }, UnlockAt = 2 },
                new TributeTier { Rarity = Rarity.Unique,
                    Trophies = new[] { "TrophyGoblinBruteBrosBrute", "TrophyGoblinBruteBrosShaman" },
                    Resources = new[] { "BlackMetalScrap" }, UnlockAt = 2 },
            },
            Wall = new[]
            {
                // Sizes from the approved Swamp/Mountains walls (2026-10-08): about 0.6-0.75 m per trophy,
                // top row of four within its 0.75 m share each
                new WallTrophy("TrophyGoblin", 0, 0.38f, size: 3.5f),
                new WallTrophy("TrophyGrowth", 0, 0.38f, size: 3.5f),
                new WallTrophy("TrophyDeathsquito", 1, 0.617f, size: 3f),
                new WallTrophy("TrophyGoblinBrute", 1, 0.617f, size: 1.2f),
                new WallTrophy("TrophyGoblinShaman", 1, 0.617f, size: 2.7f),
                new WallTrophy("TrophyLox", 2, 0.853f, size: 1f),            // huge head, kept like the Abomination
                new WallTrophy("TrophyGoblinBruteBrosBrute", 2, 0.89f, size: 1.3f),
                new WallTrophy("TrophyGoblinBruteBrosShaman", 2, 0.89f, size: 3.8f),
                new WallTrophy("TrophyBjornUndead", 2, 0.853f, size: 1.5f),
            },
            ConfigSection = "08 - Plains",
            Offerings = new[]
            {
                new Offering("Flax", 0.50f, 0.90f, maxSize: 1.2f, alongWall: true),
                new Offering("Cloudberry", 0.07f, 0.45f, yaw: 0f, maxSize: 0.12f),
                new Offering("Cloudberry", 0.12f, 0.40f, yaw: 120f, maxSize: 0.12f),
                new Offering("Cloudberry", 0.10f, 0.50f, yaw: 240f, maxSize: 0.12f),
                new Offering("Barley", 0.24f, 0.45f, yaw: 70f, tilt: 90f, maxSize: 0.6f),
                new Offering("Barley", 0.28f, 0.30f, yaw: 160f, tilt: 90f, maxSize: 0.6f),
                new Offering("Tar", 0.43f, 0.40f, yaw: 0f, maxSize: 0.3f),
                new Offering("Needle", 0.55f, 0.25f, yaw: 45f, tilt: 90f, maxSize: 0.3f),
                new Offering("RottenMeat", 0.62f, 0.50f, yaw: 30f, maxSize: 0.3f),
                new Offering("Ruby", 0.73f, 0.25f, yaw: 0f, maxSize: 0.12f),
                new Offering("BlackMetalScrap", 0.85f, 0.60f, yaw: 30f, maxSize: 0.45f),
                new Offering("BlackMetalScrap", 0.92f, 0.35f, yaw: 120f, maxSize: 0.4f),
            },
        };

        public static readonly TributeDefinition Mistlands = new TributeDefinition
        {
            PieceName = "tribute_mistlands",
            DisplayName = "Tribute - Mistlands",
            TableName = "Tribute - Mistlands",
            Description = "Out of the mist they crawl, bearing gifts. The hive serves a new lord.",
            BossTrophy = "TrophySeekerQueen",
            BaseTable = "piece_table_oak",
            BuildMaterial = "YggdrasilWood",
            BuildAmount = 10,
            LookGray = 1f,                     // oak wood recoloured (2026-10-08): blue-gray, like Yggdrasil wood
            LookWhite = 0.15f,
            LookTint = new Color(0.75f, 0.85f, 1f),
            AlignPlaques = true,
            Footprint = new Vector2(3f, 1.25f),
            WallHeightAboveTable = 3f,          // wall top 3.8 m, rows spread like the Swamp
            TrophyScale = 0.29f,               // fixed, so sizes are plain multipliers
            Tiers = new[]
            {
                new TributeTier { Rarity = Rarity.Common,
                    Trophies = new[] { "TrophyTick", "TrophySeeker" },
                    Resources = new[] { "YggdrasilWood", "BlackMarble", "JuteBlue", "Carapace", "Mandible" } },
                new TributeTier { Rarity = Rarity.Uncommon,
                    Trophies = new[] { "TrophySeekerBrute", "TrophyHare" },
                    Resources = new[] { "RoyalJelly", "ScaleHide", "HareMeat", "BugMeat", "Bilebag" }, UnlockAt = 6 },
                new TributeTier { Rarity = Rarity.Rare,
                    Trophies = new[] { "TrophyDvergr", "TrophyGjall" },
                    Resources = new[] { "Softtissue", "Sap", "GiantBloodSack" }, UnlockAt = 2 },
            },
            Wall = new[]
            {
                // Sizes from the approved Swamp/Mountains walls (2026-10-08): about 0.6-0.75 m per trophy
                new WallTrophy("TrophyTick", 0, 0.38f, size: 3.9f),
                new WallTrophy("TrophySeeker", 0, 0.38f, size: 0.72f),       // very wide head: ~0.9 m
                new WallTrophy("TrophySeekerBrute", 1, 0.617f, size: 1f),
                new WallTrophy("TrophyHare", 1, 0.617f, size: 4.1f),
                new WallTrophy("TrophyDvergr", 2, 0.853f, size: 3.7f),       // also sets its size on the Ashlands wall
                new WallTrophy("TrophyGjall", 2, 0.853f, size: 0.8f),        // huge, kept like the Abomination
            },
            ConfigSection = "09 - Mistlands",
            Offerings = new[]
            {
                new Offering("YggdrasilWood", 0.50f, 0.92f, maxSize: 1.2f, alongWall: true),
                new Offering("Carapace", 0.10f, 0.45f, yaw: 20f, maxSize: 0.4f),
                new Offering("Mandible", 0.22f, 0.30f, yaw: 120f, maxSize: 0.3f),
                new Offering("RoyalJelly", 0.33f, 0.50f, yaw: 0f, maxSize: 0.2f),
                new Offering("Sap", 0.45f, 0.35f, yaw: 60f, maxSize: 0.25f),
                new Offering("JuteBlue", 0.57f, 0.50f, yaw: 30f, tilt: 90f, maxSize: 0.4f),
                new Offering("Bilebag", 0.68f, 0.25f, yaw: 90f, maxSize: 0.25f),
                new Offering("ScaleHide", 0.82f, 0.60f, yaw: 15f, maxSize: 0.45f),
                new Offering("GiantBloodSack", 0.90f, 0.30f, yaw: 200f, maxSize: 0.25f),
            },
        };

        public static readonly TributeDefinition Ashlands = new TributeDefinition
        {
            PieceName = "tribute_ashlands",
            DisplayName = "Tribute - Ashlands",
            TableName = "Tribute - Ashlands",
            Description = "From the ashes they bow. Even the burning dead pay tribute to their lord.",
            BossTrophy = "TrophyFader",
            BaseTable = "piece_table_oak",
            BuildMaterial = "Blackwood",
            BuildAmount = 10,
            LookGray = 1f,                     // oak wood recoloured (2026-10-08): near-black, like ashwood
            LookTint = new Color(0.35f, 0.33f, 0.33f),
            AlignPlaques = true,
            Footprint = new Vector2(3f, 1.25f),
            WallHeightAboveTable = 3f,          // wall top 3.8 m, rows spread like the Swamp
            TrophyScale = 0.35f,               // fixed, so sizes are plain multipliers
            Tiers = new[]
            {
                new TributeTier { Rarity = Rarity.Common,
                    Trophies = new[] { "TrophyCharredArcher", "TrophyCharredMage", "TrophyCharredMelee" },
                    Resources = new[] { "Blackwood", "Grausten", "CharredBone", "Charredskull", "SulfurStone",
                                        "CharcoalResin", "Fiddleheadfern", "MushroomSmokePuff" } },
                new TributeTier { Rarity = Rarity.Uncommon,
                    Trophies = new[] { "TrophyBlob_Lava", "TrophyFallenValkyrie", "TrophyAsksvin", "TrophyDvergr" },
                    Resources = new[] { "ProustitePowder", "CelestialFeather", "MoltenCore", "MorgenSinew", "MorgenHeart" },
                    UnlockAt = 6 },
                new TributeTier { Rarity = Rarity.Rare,
                    Trophies = new[] { "TrophyMorgen", "TrophyBonemawSerpent", "TrophyVolture" },
                    Resources = new[] { "AskBladder", "AskHide", "BonemawSerpentTooth" }, UnlockAt = 2 },
                new TributeTier { Rarity = Rarity.Unique,
                    Trophies = new[] { NewTrophies.LordReto },
                    Resources = new[] { "FlametalOreNew", "GemstoneBlue", "GemstoneGreen", "GemstoneRed" }, UnlockAt = 1 },
            },
            Wall = new[]
            {
                // Sizes from the approved Swamp/Mountains walls (2026-10-08): about 0.6-0.75 m per trophy,
                // rows of four within their 0.75 m share each; the Dvergr takes its size from the Mistlands wall
                new WallTrophy("TrophyCharredArcher", 0, 0.38f, size: 1.33f),
                new WallTrophy("TrophyCharredMage", 0, 0.38f, size: 1.56f),
                new WallTrophy("TrophyCharredMelee", 0, 0.38f, size: 3.4f),
                new WallTrophy("TrophyBlob_Lava", 1, 0.617f, size: 3.4f),
                new WallTrophy("TrophyFallenValkyrie", 1, 0.617f, size: 1.3f),
                new WallTrophy("TrophyAsksvin", 1, 0.617f, size: 2.2f),
                new WallTrophy("TrophyDvergr", 1, 0.617f),
                new WallTrophy("TrophyMorgen", 2, 0.853f, size: 1.8f),
                new WallTrophy("TrophyBonemawSerpent", 2, 0.853f, size: 0.43f), // huge, kept like the Abomination
                new WallTrophy(NewTrophies.LordReto, 2, 0.89f, size: 3.6f),
                new WallTrophy("TrophyVolture", 2, 0.853f, size: 1f),
            },
            ConfigSection = "10 - Ashlands",
            Offerings = new[]
            {
                new Offering("Blackwood", 0.50f, 0.92f, maxSize: 1.2f, alongWall: true),
                new Offering("Charredskull", 0.08f, 0.45f, yaw: 30f, maxSize: 0.25f),
                new Offering("CharredBone", 0.18f, 0.30f, yaw: 120f, tilt: 90f, maxSize: 0.35f),
                new Offering("SulfurStone", 0.30f, 0.50f, yaw: 0f, maxSize: 0.3f),
                new Offering("MushroomSmokePuff", 0.40f, 0.28f, yaw: 60f, maxSize: 0.2f),
                new Offering("MoltenCore", 0.52f, 0.45f, yaw: 0f, maxSize: 0.25f),
                new Offering("CelestialFeather", 0.63f, 0.28f, yaw: 45f, maxSize: 0.3f),
                new Offering("GemstoneRed", 0.72f, 0.50f, yaw: 0f, maxSize: 0.15f),
                new Offering("GemstoneBlue", 0.76f, 0.40f, yaw: 60f, maxSize: 0.15f),
                new Offering("GemstoneGreen", 0.70f, 0.35f, yaw: 120f, maxSize: 0.15f),
                new Offering("FlametalOreNew", 0.88f, 0.55f, yaw: 30f, maxSize: 0.45f),
                new Offering("Grausten", 0.92f, 0.25f, yaw: 110f, maxSize: 0.35f),
            },
        };

        public static readonly TributeDefinition[] All = { Meadows, BlackForest, Swamp, Mountains, Plains, Mistlands, Ashlands };

        public static TributeDefinition ForPiece(string pieceName)
        {
            foreach (var d in All)
                if (pieceName.StartsWith(d.PieceName)) return d;
            return null;
        }
    }
}
