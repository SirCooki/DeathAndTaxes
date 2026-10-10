using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DeathAndTaxes
{
    /// Tracks offered trophies and generates resources into the attached Container.
    /// Generation follows the world clock: at most one stack per resource, whether or not
    /// items were taken out. Anything that doesn't fit is lost.
    /// Trophies come in tiers (common .. unique); a tier's resources are produced once enough of its trophies
    /// are on the table, at a speed weighted over all tiers' trophies (see TributeDefinition.Score).
    /// The storage is not a chest: everybody may take, nobody may add except the table itself.
    /// Each resource has its own slot (Definition.Layout). Adds are refused at Inventory.AddItem, and a per-resource
    /// allowance (what the table produced minus what was taken) catches anything that gets in another way.
    public class TributeTable : MonoBehaviour
    {
        // Plain C# data isn't copied by Unity when the prefab is instantiated, so look it up by piece name
        private TributeDefinition m_definition;
        public TributeDefinition Definition => m_definition ??= TributeDefinitions.ForPiece(Utils.GetPrefabName(gameObject));

        public const double OverclockHours = 1.0;

        private const float TickSeconds = 10f;
        private const float VisualSeconds = 1f;
        private const string KeyLastTime = "lob_last";
        private const string KeyPaused = "lob_paused";
        private const string KeyOpenAir = "lob_open"; // producing without a roof (at the no-roof speed) since lob_last
        private const string KeyBoostFrom = "lob_boost_from";
        private const string KeyBoostUntil = "lob_boost_until";
        private static string TrophyKey(string t) => "lob_t_" + t;
        private static string AccKey(string r) => "lob_acc_" + r;
        private static string AllowedKey(string r) => "lob_have_" + r;

        // Keys read by other mods: MultiUserChest leaves the table single-use, ChestButler never sorts into it
        private const string KeyMultiUserChestIgnore = "MUC_Ignore";
        private const string KeyChestButlerManualOnly = "psort_manual";

        private static readonly HashSet<TributeTable> s_instances = new HashSet<TributeTable>();
        private static readonly Dictionary<Inventory, TributeTable> s_byInventory = new Dictionary<Inventory, TributeTable>();
        private static bool s_producing;

        private ZNetView m_nview;
        private Container m_container;
        private Inventory m_inventory;
        private bool m_enforcing;
        private readonly Dictionary<string, GameObject> m_wallTrophies = new Dictionary<string, GameObject>();

        private Vector3 FrontOfTable => transform.position - transform.forward + Vector3.up;

        private void Awake()
        {
            m_nview = GetComponent<ZNetView>();
            m_container = GetComponent<Container>();
            FindWallTrophies();
            UpdateWallTrophies(); // also on the build ghost: only the build-cost trophies are shown
            if (!m_nview || !m_nview.IsValid()) return;
            s_instances.Add(this);
            InvokeRepeating(nameof(Tick), 2f, TickSeconds);
            InvokeRepeating(nameof(UpdateWallTrophies), VisualSeconds, VisualSeconds);
        }

        // After Container.Awake has created the storage
        private void Start()
        {
            if (!m_nview || !m_nview.IsValid()) return;
            m_inventory = m_container ? m_container.GetInventory() : null;
            if (m_inventory != null)
            {
                s_byInventory[m_inventory] = this;
                m_inventory.m_onChanged += OnStorageChanged;
            }
            ApplyResourceLayout();
        }

        private void OnDestroy()
        {
            s_instances.Remove(this);
            if (m_inventory == null) return;
            s_byInventory.Remove(m_inventory);
            m_inventory.m_onChanged -= OnStorageChanged;
        }

        /// Called when settings change (also when the server's settings arrive).
        public static void OnSettingsChanged()
        {
            foreach (var table in s_instances.ToList())
                if (table) table.ApplyResourceLayout();
        }

        /// False when something other than the table's own production tries to put items in its storage.
        public static bool AllowsAdd(Inventory inventory)
        {
            if (inventory == null || !s_byInventory.TryGetValue(inventory, out var table)) return true;
            if (!table) return true;
            return s_producing || (table.m_container && table.m_container.m_loading);
        }

        /// The storage has one row per tier and one slot per enabled resource. Items are sorted into their slots by the owner.
        public void ApplyResourceLayout()
        {
            var inv = m_container ? m_container.GetInventory() : null;
            if (inv == null || Definition == null) return;
            if (inv.GetWidth() != Definition.GridWidth) inv.m_width = Definition.GridWidth;
            if (inv.GetHeight() != Definition.GridHeight) inv.m_height = Definition.GridHeight;
            Enforce();
        }

        // Only once the saved storage has been loaded; before that it looks empty
        private bool IsStorageLoaded => m_container && m_container.m_lastRevision != uint.MaxValue;

        // The local storage matches what was last saved. Someone else's change (a reload is pending) makes it
        // stale; acting on a stale copy could push out items another player already took.
        private bool InSync => m_container && m_container.m_lastRevision == m_nview.GetZDO().DataRevision;

        /// ZDO write that doesn't count as "someone else's change": our own keys never touch the saved items.
        private void Write(Action<ZDO> write)
        {
            var zdo = m_nview.GetZDO();
            bool inSync = InSync;
            write(zdo);
            if (inSync) m_container.m_lastRevision = zdo.DataRevision;
        }

        private void OnStorageChanged()
        {
            if (m_enforcing || s_producing || (m_container && m_container.m_loading)) return;
            Enforce();
        }

        /// Owner only: puts every item in its own slot and pushes out anything the table didn't produce itself.
        /// Runs on every storage change, every tick, when the window opens and before "Take all".
        public void Enforce()
        {
            if (m_enforcing || !m_nview || !m_nview.IsValid() || !m_nview.IsOwner() || Definition == null) return;
            if (m_container.GetInventory() == null || !IsStorageLoaded || !InSync) return;
            m_enforcing = true;
            try
            {
                EnforceSlots();
                EnforceAllowance();
            }
            finally
            {
                m_enforcing = false;
            }
        }

        /// Taking lowers the allowance, only production raises it; anything above it is pushed out.
        private void EnforceAllowance()
        {
            var zdo = m_nview.GetZDO();
            var inv = m_container.GetInventory();
            foreach (var slot in Definition.Layout)
            {
                var item = inv.GetItemAt(slot.X, slot.Y);
                int count = item != null ? item.m_stack : 0;
                int allowed = zdo.GetInt(AllowedKey(slot.Resource), -1);
                if (allowed < 0 || count < allowed)
                {
                    // First look at a table from an older version, or something was taken
                    Write(z => z.Set(AllowedKey(slot.Resource), count));
                }
                else if (count > allowed)
                {
                    PushOut(inv, item, count - allowed);
                }
            }
        }

        /// Removes `amount` of an item from the storage and drops it in front of the table.
        private void PushOut(Inventory inv, ItemDrop.ItemData item, int amount)
        {
            if (!item.m_dropPrefab)
                item.m_dropPrefab = ObjectDB.instance.m_items.FirstOrDefault(p =>
                    p.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_name == item.m_shared.m_name);
            if (item.m_dropPrefab) ItemDrop.DropItem(item, amount, FrontOfTable, Quaternion.identity);
            else Debug.LogWarning($"[DeathAndTaxes] Removed {amount}x {item.m_shared.m_name} from {Definition.TableName}");
            inv.RemoveItem(item, amount);
        }

        /// Slot of an item's resource, or null if the table doesn't generate it.
        private ResourceSlot? SlotOf(ItemDrop.ItemData item)
        {
            foreach (var slot in Definition.Layout)
            {
                var drop = GetDrop(slot.Resource);
                if (drop && drop.m_itemData.m_shared.m_name == item.m_shared.m_name) return slot;
            }
            return null;
        }

        private static bool IsAt(ItemDrop.ItemData item, ResourceSlot slot) => item.m_gridPos.x == slot.X && item.m_gridPos.y == slot.Y;

        /// Owner only: anything that isn't a generated resource in its own slot is moved there,
        /// or dropped in front of the table if it doesn't belong or doesn't fit.
        private void EnforceSlots()
        {
            var inv = m_container.GetInventory();
            var items = inv.GetAllItems();
            bool valid = items.All(i => SlotOf(i) is ResourceSlot s && IsAt(i, s)) &&
                         items.Select(i => i.m_gridPos.y * 1000 + i.m_gridPos.x).Distinct().Count() == items.Count;
            if (valid) return;

            var all = items.ToList();
            items.Clear();
            foreach (var item in all)
            {
                var slot = SlotOf(item);
                var existing = slot.HasValue ? items.FirstOrDefault(i => IsAt(i, slot.Value)) : null;
                if (slot.HasValue && existing == null)
                {
                    item.m_gridPos = new Vector2i(slot.Value.X, slot.Value.Y);
                    items.Add(item);
                    continue;
                }
                if (existing != null)
                {
                    int merge = Mathf.Min(item.m_stack, existing.m_shared.m_maxStackSize - existing.m_stack);
                    existing.m_stack += merge;
                    item.m_stack -= merge;
                }
                if (item.m_stack > 0)
                {
                    item.m_gridPos = new Vector2i(-1, -1);
                    items.Add(item);
                    PushOut(inv, item, item.m_stack);
                }
            }
            inv.Changed();
        }

        /// Owner only: tell other mods to treat the table as single-use and not as a sorting target.
        private void EnsureModFlags()
        {
            var zdo = m_nview.GetZDO();
            if (!zdo.GetBool(KeyMultiUserChestIgnore)) Write(z => z.Set(KeyMultiUserChestIgnore, true));
            if (Compat.ChestButlerInstalled && !zdo.GetBool(KeyChestButlerManualOnly)) Write(z => z.Set(KeyChestButlerManualOnly, true));
        }

        // ---- trophies and tiers ----

        /// Trophies of one type on the table. Unset means the start count: 1 for the build-cost (common) trophies.
        public int GetTrophyCount(string trophy)
        {
            int tier = Definition?.TierOfTrophy(trophy) ?? -1;
            int start = tier >= 0 ? Definition.Tiers[tier].StartCount : 0;
            return m_nview && m_nview.IsValid() ? m_nview.GetZDO().GetInt(TrophyKey(trophy), start) : start;
        }

        public int MaxTrophies(string trophy)
        {
            int tier = Definition.TierOfTrophy(trophy);
            return tier >= 0 ? Definition.Tiers[tier].MaxPerTrophy : 0;
        }

        /// All trophies of a tier on the table.
        public int TierCount(int tier) => Definition.Tiers[tier].Trophies.Sum(GetTrophyCount);

        /// A tier's resources are produced once enough of its trophies are on the table.
        public bool IsUnlocked(int tier) => TierCount(tier) >= Definition.Tiers[tier].UnlockAt;

        public float HoursPerStack(int tier) => Definition.HoursPerStack(Definition.Score(tier, GetTrophyCount));

        public bool IsSheltered()
        {
            Cover.GetCoverForPoint(transform.position + Vector3.up, out float cover, out bool underRoof);
            return underRoof && cover >= 0.7f;
        }

        /// The wall models of the trophies (built by TributeModel as "trophy_<prefab>").
        private void FindWallTrophies()
        {
            var decor = transform.Find(TributeModel.DecorName);
            if (!decor || Definition == null) return;
            foreach (var trophy in Definition.AllTrophies)
            {
                var t = decor.Find(TributeModel.TrophyPrefix + trophy);
                if (t) m_wallTrophies[trophy] = t.gameObject;
            }
        }

        /// Every client: a trophy (and its plaque) only shows on the wall once at least one is on the table.
        private void UpdateWallTrophies()
        {
            foreach (var pair in m_wallTrophies)
            {
                bool show = GetTrophyCount(pair.Key) > 0;
                if (pair.Value && pair.Value.activeSelf != show) pair.Value.SetActive(show);
            }
        }

        // ---- production ----

        /// Owner only. Progress is only saved when whole items come out or the shelter state changes,
        /// so nearby players don't reload the storage every tick.
        private void Tick()
        {
            if (!m_nview.IsValid() || !m_nview.IsOwner() || Definition == null) return;
            EnsureModFlags();
            Enforce();

            var zdo = m_nview.GetZDO();
            long now = ZNet.instance.GetTime().Ticks;
            bool sheltered = IsSheltered();
            if (!sheltered && !Settings.NoRoofAllowed.Value)
            {
                // Credit the time up to now, then stop the clock until it is sheltered again
                if (!zdo.GetBool(KeyPaused))
                {
                    Produce(now, force: true);
                    Write(z => z.Set(KeyPaused, true));
                }
                return;
            }
            if (zdo.GetBool(KeyPaused))
            {
                Write(z => z.Set(KeyPaused, false));
                Write(z => z.Set(KeyLastTime, now));
                Write(z => z.Set(KeyOpenAir, !sheltered));
                return;
            }
            if (zdo.GetBool(KeyOpenAir) == sheltered)
            {
                // Roof built or removed: credit the time so far at the old speed
                Produce(now, force: true);
                Write(z => z.Set(KeyOpenAir, !sheltered));
                return;
            }
            Produce(now, force: false);
        }

        /// Adds the items earned since the last save. Saves nothing unless at least one whole item came out
        /// (or `force` is set). Locked tiers earn nothing.
        private void Produce(long now, bool force)
        {
            if (!IsStorageLoaded || !InSync) return; // a stale copy: try again next tick
            var zdo = m_nview.GetZDO();
            long last = zdo.GetLong(KeyLastTime, 0L);
            if (last == 0 || now <= last)
            {
                if (last == 0) Write(z => z.Set(KeyLastTime, now));
                return;
            }

            // Time spent overclocked counts multiple times
            double hours = TimeSpan.FromTicks(now - last).TotalHours;
            hours += (Settings.OverclockMultiplier - 1) * BoostedHours(last, now);
            if (zdo.GetBool(KeyOpenAir)) hours *= Settings.NoRoofSpeed;

            var tiers = Definition.Tiers;
            var unlocked = new bool[tiers.Length];
            var hoursPerStack = new float[tiers.Length];
            for (int t = 0; t < tiers.Length; t++)
            {
                unlocked[t] = IsUnlocked(t);
                hoursPerStack[t] = HoursPerStack(t);
            }

            var layout = Definition.Layout;
            var acc = new float[layout.Length];
            bool anyWhole = false;
            for (int i = 0; i < layout.Length; i++)
            {
                if (!unlocked[layout[i].Tier]) continue;
                string res = layout[i].Resource;
                acc[i] = zdo.GetFloat(AccKey(res), 0f) + (float)(StackSize(res) * hours / hoursPerStack[layout[i].Tier]);
                anyWhole |= acc[i] >= 1f;
            }
            if (!anyWhole && !force) return;

            var inv = m_container.GetInventory();
            for (int i = 0; i < layout.Length; i++)
            {
                var slot = layout[i];
                if (!unlocked[slot.Tier]) continue;
                int whole = Mathf.FloorToInt(acc[i]);
                float rest = acc[i] - whole;
                Write(z => z.Set(AccKey(slot.Resource), rest));
                var drop = GetDrop(slot.Resource);
                if (whole <= 0 || !drop) continue;

                var existing = inv.GetItemAt(slot.X, slot.Y);
                int have = existing != null ? existing.m_stack : 0;
                int add = Mathf.Min(whole, drop.m_itemData.m_shared.m_maxStackSize - have);
                if (add <= 0) continue; // full: the rest is lost

                var item = drop.m_itemData.Clone();
                item.m_stack = add;
                item.m_dropPrefab = drop.gameObject;
                Write(z => z.Set(AllowedKey(slot.Resource), have + add));
                s_producing = true;
                try { inv.AddItem(item, new Vector2i(slot.X, slot.Y)); }
                finally { s_producing = false; }
            }
            Write(z => z.Set(KeyLastTime, now));
        }

        /// Owner only: produce everything earned so far at the current speed, before trophies or an overclock change it.
        private void BankProduction()
        {
            if (!m_nview.IsOwner() || m_nview.GetZDO().GetBool(KeyPaused)) return;
            Enforce();
            Produce(Now, force: true);
        }

        public static ItemDrop GetDrop(string prefab)
        {
            var go = ObjectDB.instance ? ObjectDB.instance.GetItemPrefab(prefab) : null;
            return go ? go.GetComponent<ItemDrop>() : null;
        }

        public static string PrefabName(ItemDrop.ItemData item) =>
            item != null && item.m_dropPrefab ? item.m_dropPrefab.name : null;

        public bool IsTrophy(ItemDrop.ItemData item) => Definition.AllTrophies.Contains(PrefabName(item));

        // ---- offering ----

        /// Moves up to `amount` of a trophy item from an inventory onto the table, within the cap.
        /// Returns how many were taken.
        private int TakeTrophies(Inventory from, ItemDrop.ItemData item, int amount)
        {
            string prefab = PrefabName(item);
            int count = GetTrophyCount(prefab);
            int take = Mathf.Min(amount, item.m_stack, MaxTrophies(prefab) - count);
            if (take <= 0) return 0;
            m_nview.ClaimOwnership();
            BankProduction(); // the time so far still counts at the old speed and unlocks
            Write(z => z.Set(TrophyKey(prefab), count + take));
            from.RemoveItem(item, take);
            UpdateWallTrophies();
            return take;
        }

        /// Offer up to `amount` of a trophy item from an inventory. Returns how many were taken.
        public int Offer(Humanoid user, Inventory from, ItemDrop.ItemData item, int amount)
        {
            if (!IsTrophy(item)) return 0;
            var unlockedBefore = UnlockedTiers();
            int wanted = Mathf.Min(amount, item.m_stack);
            int take = TakeTrophies(from, item, wanted);
            ShowOfferResult(user, take, wanted - take, unlockedBefore);
            return take;
        }

        /// Offer every matching trophy from the player's inventory, optionally only one trophy type.
        public void OfferAll(Humanoid user, string onlyTrophy = null)
        {
            var unlockedBefore = UnlockedTiers();
            int offered = 0, refused = 0;
            foreach (var item in user.GetInventory().GetAllItems().ToList())
            {
                if (!IsTrophy(item) || (onlyTrophy != null && PrefabName(item) != onlyTrophy)) continue;
                int stack = item.m_stack;
                int take = TakeTrophies(user.GetInventory(), item, stack);
                offered += take;
                refused += stack - take;
            }
            if (offered == 0 && refused == 0)
            {
                string what = onlyTrophy != null ? DisplayName(onlyTrophy) : "trophies";
                user.Message(MessageHud.MessageType.Center, $"You have no {what} to offer");
                return;
            }
            ShowOfferResult(user, offered, refused, unlockedBefore);
        }

        private bool[] UnlockedTiers() => Enumerable.Range(0, Definition.Tiers.Length).Select(IsUnlocked).ToArray();

        // Shown when more trophies are offered than the table accepts (plus the definition's own lines)
        private static readonly string[] TooManyTrophies =
        {
            "Haven't you killed enough? You monster!",
            "What is wrong with you?",
            "They are all dead!",
            "This isn't order 66, you maniac",
            "There are still more left?",
        };

        private void ShowOfferResult(Humanoid user, int offered, int refused, bool[] unlockedBefore)
        {
            var lines = new List<string>();
            if (offered > 0) lines.Add($"Offered {offered} {(offered == 1 ? "trophy" : "trophies")}");
            for (int t = 0; t < Definition.Tiers.Length; t++)
            {
                if (unlockedBefore[t] || !IsUnlocked(t)) continue;
                var tier = Definition.Tiers[t];
                var names = Definition.Layout.Where(s => s.Tier == t).Select(s => DisplayName(s.Resource));
                lines.Add($"<color={Rarities.Color(tier.Rarity)}>{tier.Rarity} tribute unlocked:</color> {string.Join(", ", names)}");
            }
            if (refused > 0)
            {
                var quips = TooManyTrophies.Concat(Definition.TooManyTrophies ?? new string[0]).ToArray();
                lines.Add(quips[UnityEngine.Random.Range(0, quips.Length)]);
            }
            user.Message(MessageHud.MessageType.Center, string.Join("\n", lines));
        }

        // ---- overclock ("Remind them!") ----

        private long Now => ZNet.instance ? ZNet.instance.GetTime().Ticks : 0L;
        private long BoostFrom => m_nview && m_nview.IsValid() ? m_nview.GetZDO().GetLong(KeyBoostFrom, 0L) : 0L;
        private long BoostUntil => m_nview && m_nview.IsValid() ? m_nview.GetZDO().GetLong(KeyBoostUntil, 0L) : 0L;

        public bool IsOverclocked => Settings.OverclockEnabled.Value && BoostUntil > Now;

        public TimeSpan OverclockLeft => IsOverclocked ? TimeSpan.FromTicks(BoostUntil - Now) : TimeSpan.Zero;

        /// Hours between `last` and `now` that fall inside the overclock window.
        private double BoostedHours(long last, long now)
        {
            if (!Settings.OverclockEnabled.Value) return 0.0;
            long from = Math.Max(last, BoostFrom), until = Math.Min(now, BoostUntil);
            return until > from ? TimeSpan.FromTicks(until - from).TotalHours : 0.0;
        }

        /// Spend one boss trophy to multiply production for an hour; extends a running overclock.
        public void Overclock(Humanoid user)
        {
            if (!Settings.OverclockEnabled.Value) return;
            if (!IsSheltered() && !Settings.NoRoofAllowed.Value)
            {
                user.Message(MessageHud.MessageType.Center, "Nobody works here without a roof, reminding them is useless");
                return;
            }
            var inv = user.GetInventory();
            var trophy = inv.GetAllItems().FirstOrDefault(i => PrefabName(i) == Definition.BossTrophy);
            if (trophy == null)
            {
                user.Message(MessageHud.MessageType.Center,
                    $"Reminding them costs 1 {DisplayName(Definition.BossTrophy)}, you have none");
                return;
            }

            // Bank the production so far at the old speed before the boost starts
            m_nview.ClaimOwnership();
            BankProduction();
            long now = Now;

            inv.RemoveItem(trophy, 1);
            long extra = TimeSpan.FromHours(OverclockHours).Ticks;
            if (BoostUntil > now)
            {
                Write(z => z.Set(KeyBoostUntil, BoostUntil + extra));
            }
            else
            {
                Write(z => z.Set(KeyBoostFrom, now));
                Write(z => z.Set(KeyBoostUntil, now + extra));
            }
            var lines = Reminded.Concat(Definition.Reminded ?? new string[0]).ToArray();
            user.Message(MessageHud.MessageType.Center, lines[UnityEngine.Random.Range(0, lines.Length)]);
        }

        // Shown after "Remind them!" (plus the definition's own lines); the boost itself is in the window's status line
        private static readonly string[] Reminded =
        {
            "They are reminded!",
            "Once more they shiver!",
            "Life before wasn't so frightening!",
            "They realize how little they mean!",
            "They rush to keep you away!",
            "They remember your wrath!",
        };

        public static string FormatTime(TimeSpan t) =>
            t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes:00}m" : $"{t.Minutes}m {t.Seconds:00}s";

        private static string ShortTime(TimeSpan t) =>
            t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes:00}m" : t.TotalMinutes >= 1 ? $"{(int)t.TotalMinutes}m" : $"{t.Seconds}s";

        // ---- rates and texts ----

        public static int StackSize(string prefab)
        {
            var drop = GetDrop(prefab);
            return drop ? drop.m_itemData.m_shared.m_maxStackSize : 0;
        }

        /// Items per hour right now (0 while locked): offered trophies and a running overclock included.
        public float CurrentRate(ResourceSlot slot) => !IsUnlocked(slot.Tier) ? 0f :
            StackSize(slot.Resource) / HoursPerStack(slot.Tier) * (IsOverclocked ? Settings.OverclockMultiplier : 1) * RoofSpeed;

        /// 1 under a roof; without one the no-roof speed if that's allowed (rates show what a roof would give otherwise).
        private float RoofSpeed => !IsSheltered() && Settings.NoRoofAllowed.Value ? Settings.NoRoofSpeed : 1f;

        /// Small status line at the top of the window.
        public string StatusText()
        {
            string status = IsSheltered()
                ? "Production: <color=#8FBF6F>On</color>"
                : Settings.NoRoofAllowed.Value
                    ? $"Production: <color=#8FBF6F>On</color> (no roof: {Settings.NoRoofSpeed * 100f:0}%)"
                    : "Production: <color=#C86464>Off</color> (needs a roof)";
            if (IsOverclocked)
                status += $"  |  Modifier: <color=#D9B860>{Settings.OverclockMultiplier}x</color> ({ShortTime(OverclockLeft)})";
            return status;
        }

        public string GetHoverText() => Localization.instance.Localize(
            Definition.TableName + "\n[<color=yellow><b>$KEY_Use</b></color>] $piece_container_open");

        public static string DisplayName(string prefab)
        {
            var drop = GetDrop(prefab);
            return drop ? Localization.instance.Localize(drop.m_itemData.m_shared.m_name) : prefab;
        }
    }
}
