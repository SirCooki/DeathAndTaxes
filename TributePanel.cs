using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DeathAndTaxes
{
    /// Extra UI on the vanilla container window while a tribute table is open. Bottom to top:
    /// the buttons, then one row per tier (common at the bottom): the tier's trophies on the left and its resources
    /// (the vanilla storage slots, moved into place) on the right with their rate underneath; at the top the
    /// centred title with a small status line on its left. The window is resized to fit and restored afterwards.
    public class TributePanel : MonoBehaviour
    {
        private static readonly Color LabelColor = new Color(1f, 0.82f, 0.35f);
        private static readonly Color StatusColor = new Color(0.78f, 0.78f, 0.78f);
        private static readonly Color EmptyTint = new Color(1f, 1f, 1f, 0.28f);        // unlocked, nothing waiting
        private static readonly Color LockedTint = new Color(0.12f, 0.12f, 0.12f, 0.85f);
        private static readonly Color NoTrophyTint = new Color(1f, 1f, 1f, 0.3f);
        private const float RefreshSeconds = 0.2f;

        // Layout, in window units
        private const float MarginX = 20f;         // window edges to the slots and buttons
        private const float ButtonMarginY = 8f;    // window bottom edge to the buttons
        private const float ButtonGap = 8f;
        private const float ButtonPadding = 28f;   // button width = text width + this (never narrower than vanilla)
        private const float TitleRowHeight = 46f;  // window top edge to the first tier row
        private const float LabelHeight = 16f;     // rate / tier text under each row of slots
        private const float RowGap = 4f;
        private const float GroupGap = 0.5f;       // between a row's trophies and its resources, in slot spacings
        private const float BottomGap = 6f;        // lowest row's labels to the buttons

        private static TributePanel _instance;
        private static bool s_loggedWindow;
        private static readonly Vector3[] s_corners = new Vector3[4];

        private InventoryGui m_gui;
        private RectTransform m_root;
        private RectTransform m_slotHolder; // the vanilla storage slots, while a tribute table is open
        private TributeTable m_table;
        private ResourceSlot[] m_layout;    // the table's storage layout the UI was built for
        private GameObject m_content;
        private float m_refreshTimer;
        private TributeButton m_addButton;
        private TributeButton m_collectButton;
        private TributeButton m_remindButton;
        private readonly WindowState m_window = new WindowState();
        private GameObject m_hiddenBar;

        /// The vanilla window's size, title and scroll bar, changed while a tribute table is open.
        private class WindowState
        {
            public bool Saved;
            public Vector2 Size, Position;   // m_container sizeDelta / anchoredPosition
            public float Width;              // m_container width in its own units
            public Vector2 TitlePosition;
            public HorizontalAlignmentOptions TitleAlignment;
            public ScrollRect Scroll;
            public bool ScrollEnabled;
            public GameObject Scrollbar;
            public bool ScrollbarActive;
        }

        /// Renames and moves a vanilla window button while a tribute table is open, and restores it afterwards.
        private class TributeButton
        {
            private readonly RectTransform m_rect;
            private readonly TMP_Text m_text;
            private readonly string m_custom;
            private readonly bool m_ownButton; // created by this mod: hidden instead of restored
            private string m_originalText;
            private Vector2 m_originalPivot, m_originalPos, m_originalSize;
            private float m_width; // fitted to the custom text, measured once
            private bool m_moved;

            public TributeButton(Button button, string custom, bool ownButton = false)
            {
                m_rect = button ? (RectTransform)button.transform : null;
                m_text = button ? button.GetComponentInChildren<TMP_Text>(true) : null;
                m_custom = custom;
                m_ownButton = ownButton;
            }

            /// Places the button's bottom-left corner at `bottomLeft` (in root space). Returns its size in root space.
            public Vector2 Apply(RectTransform root, Vector2 bottomLeft)
            {
                if (!m_rect) return Vector2.zero;
                if (m_ownButton && !m_rect.gameObject.activeSelf) m_rect.gameObject.SetActive(true);
                if (m_text)
                {
                    if (m_text.text != m_custom) m_originalText = m_text.text;
                    m_text.text = m_custom;
                }
                if (!m_moved)
                {
                    m_originalPivot = m_rect.pivot;
                    m_originalPos = m_rect.anchoredPosition;
                    m_originalSize = m_rect.sizeDelta;
                    m_moved = true;
                }
                if (m_width <= 0f)
                {
                    float text = m_text ? m_text.GetPreferredValues(m_custom).x : 0f;
                    m_width = Mathf.Max(m_originalSize.x, text + ButtonPadding);
                }
                m_rect.sizeDelta = new Vector2(m_width, m_originalSize.y);
                m_rect.pivot = Vector2.zero;
                m_rect.position = root.TransformPoint(bottomLeft);
                return m_rect.rect.size * m_rect.lossyScale.x / root.lossyScale.x;
            }

            public void Restore()
            {
                if (!m_rect) return;
                if (m_ownButton)
                {
                    m_rect.gameObject.SetActive(false);
                    return;
                }
                if (m_text && m_originalText != null && m_text.text == m_custom) m_text.text = m_originalText;
                if (!m_moved) return;
                m_rect.sizeDelta = m_originalSize;
                m_rect.pivot = m_originalPivot;
                m_rect.anchoredPosition = m_originalPos;
                m_moved = false;
            }
        }

        private class TrophySlot
        {
            public string Trophy;
            public int Tier;
            public RectTransform Rect;
            public Image Icon;
            public TMP_Text Amount;
        }

        private class ResourceView
        {
            public ResourceSlot Slot;
            public Image Ghost;       // faint icon while the slot is empty
            public TMP_Text Rate;     // "12.5/h"; empty while locked
            public string Topic, Tooltip;
        }

        private readonly List<TrophySlot> m_trophySlots = new List<TrophySlot>();
        private readonly List<ResourceView> m_resources = new List<ResourceView>();
        private readonly List<TMP_Text> m_tierLabels = new List<TMP_Text>(); // index = tier
        private bool[] m_unlocked = new bool[0];
        private TMP_Text m_status;

        public static void Ensure(InventoryGui gui)
        {
            if (_instance) return;
            var go = new GameObject("DeathAndTaxes_TributePanel", typeof(RectTransform));
            go.transform.SetParent(gui.m_container, false);
            _instance = go.AddComponent<TributePanel>();
            _instance.m_gui = gui;
            _instance.m_root = Stretch((RectTransform)go.transform);
            var holder = new GameObject("slots", typeof(RectTransform));
            holder.transform.SetParent(go.transform, false);
            _instance.m_slotHolder = Stretch((RectTransform)holder.transform);
            _instance.m_addButton = new TributeButton(gui.m_stackAllButton, "Add trophies");
            _instance.m_collectButton = new TributeButton(gui.m_takeAllButton, "Collect tribute");
            _instance.m_remindButton = new TributeButton(CreateRemindButton(gui), "Remind them!", ownButton: true);
        }

        private static RectTransform Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        /// Copy of the vanilla "Take all" button, stripped of its original click and gamepad shortcut.
        private static Button CreateRemindButton(InventoryGui gui)
        {
            if (!gui.m_takeAllButton) return null;
            var source = gui.m_takeAllButton.gameObject;
            var go = Instantiate(source, source.transform.parent, false);
            go.name = "DeathAndTaxes_RemindButton";
            foreach (var pad in go.GetComponentsInChildren<UIGamePad>(true))
            {
                // Only the copy's own hint: a reference outside the copy still points at the vanilla button's hint
                if (pad.m_hint && pad.m_hint.transform.IsChildOf(go.transform)) Destroy(pad.m_hint);
                Destroy(pad);
            }
            var button = go.GetComponent<Button>();
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(() => _instance.OnRemindClicked());
            go.SetActive(false);
            return button;
        }

        private void OnRemindClicked()
        {
            var player = Player.m_localPlayer;
            if (player && m_table) m_table.Overclock(player);
            m_refreshTimer = 0f;
        }

        private TributeTable CurrentTable()
        {
            var container = m_gui && m_gui.IsContainerOpen() ? m_gui.m_currentContainer : null;
            return container ? container.GetComponent<TributeTable>() : null;
        }

        private void LateUpdate()
        {
            var table = CurrentTable();
            var layout = table ? table.Definition.Layout : null;
            // Rebuild on a new table, or when the enabled resources changed in the settings.
            // ReferenceEquals: a table destroyed while the window was closed still counts as a change
            // (Unity's == would call it equal to null and leave the tribute UI on the next chest).
            if (!ReferenceEquals(table, m_table) || !ReferenceEquals(layout, m_layout))
            {
                Clear();
                m_table = table;
                m_layout = layout;
                if (table)
                {
                    table.Enforce(); // show only what the table produced itself
                    Build();
                }
                else
                {
                    RestoreWindow();
                }
                m_refreshTimer = 0f;
            }
            if (!m_table || !m_content) return;

            // Every frame: the game moves some of this back (button texts, rebuilt storage slots)
            ResizeWindow();
            PlaceTitle();
            PlaceButtons();
            HideScrollbar();
            HideChestButlerBar();
            m_refreshTimer -= Time.deltaTime;
            if (m_refreshTimer <= 0f)
            {
                m_refreshTimer = RefreshSeconds;
                Refresh();
            }
            Layout();
            ShowEmptySlots(); // after vanilla's grid update, which clears the tooltips of empty slots
        }

        // ---- window ----

        private float Spacing => m_gui.m_containerGrid.m_elementSpace;

        private Vector2 SlotSize
        {
            get
            {
                var prefab = m_gui.m_containerGrid.m_elementPrefab;
                var size = prefab ? ((RectTransform)prefab.transform).sizeDelta : Vector2.zero;
                return size.x > 1f && size.y > 1f ? size : new Vector2(Spacing - 6f, Spacing - 6f);
            }
        }

        private int TrophyColumns => m_table.Definition.Tiers.Max(t => t.Trophies.Length);
        private float RowHeight => Spacing + LabelHeight + RowGap;

        private float ButtonHeight()
        {
            var b = m_gui.m_takeAllButton ? (RectTransform)m_gui.m_takeAllButton.transform : null;
            return b ? b.rect.height * b.lossyScale.y / m_root.lossyScale.y : 40f;
        }

        /// Window size for the table: all tier rows between the title and the buttons.
        private Vector2 NeededSize()
        {
            float s = Spacing;
            var def = m_table.Definition;
            float width = 2f * MarginX + (TrophyColumns + GroupGap + def.GridWidth) * s - (s - SlotSize.x);
            float height = TitleRowHeight + def.Tiers.Length * RowHeight + BottomGap + ButtonHeight() + ButtonMarginY;
            return new Vector2(width, height);
        }

        /// Grows (or shrinks) the window to fit, keeping its top-left corner in place. Never narrower than vanilla.
        private void ResizeWindow()
        {
            var rt = m_gui.m_container;
            if (!m_window.Saved) SaveWindow();
            var need = NeededSize();
            float width = Mathf.Max(m_window.Width, need.x);
            if (Mathf.Abs(rt.rect.width - width) < 0.5f && Mathf.Abs(rt.rect.height - need.y) < 0.5f) return;

            rt.GetWorldCorners(s_corners);
            var topLeft = s_corners[1];
            rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
            rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, need.y);
            rt.GetWorldCorners(s_corners);
            rt.position += topLeft - s_corners[1];
        }

        private void SaveWindow()
        {
            var rt = m_gui.m_container;
            m_window.Saved = true;
            m_window.Size = rt.sizeDelta;
            m_window.Position = rt.anchoredPosition;
            m_window.Width = rt.rect.width;
            var title = m_gui.m_containerName;
            if (title)
            {
                m_window.TitlePosition = title.rectTransform.anchoredPosition;
                m_window.TitleAlignment = title.horizontalAlignment;
            }
            var grid = m_gui.m_containerGrid;
            m_window.Scroll = grid.m_gridRoot ? grid.m_gridRoot.GetComponentInParent<ScrollRect>() : null;
            m_window.ScrollEnabled = m_window.Scroll && m_window.Scroll.enabled;
            m_window.Scrollbar = grid.m_scrollbar ? grid.m_scrollbar.gameObject : null;
            m_window.ScrollbarActive = m_window.Scrollbar && m_window.Scrollbar.activeSelf;
            LogWindow();
        }

        private void RestoreWindow()
        {
            m_addButton.Restore();
            m_collectButton.Restore();
            m_remindButton.Restore();
            if (m_hiddenBar) m_hiddenBar.SetActive(true);
            m_hiddenBar = null;
            ReleaseSlots();

            if (!m_window.Saved) return;
            m_window.Saved = false;
            var rt = m_gui.m_container;
            rt.sizeDelta = m_window.Size;
            rt.anchoredPosition = m_window.Position;
            var title = m_gui.m_containerName;
            if (title)
            {
                title.rectTransform.anchoredPosition = m_window.TitlePosition;
                title.horizontalAlignment = m_window.TitleAlignment;
            }
            if (m_window.Scroll) m_window.Scroll.enabled = m_window.ScrollEnabled;
            if (m_window.Scrollbar) m_window.Scrollbar.SetActive(m_window.ScrollbarActive);
        }

        /// Hands the storage slots back to the vanilla grid: hidden now, rebuilt by the grid next frame.
        private void ReleaseSlots()
        {
            var grid = m_gui.m_containerGrid;
            bool any = false;
            foreach (var element in grid.m_elements)
            {
                if (!element || element.transform.parent != m_slotHolder) continue;
                element.gameObject.SetActive(false);
                any = true;
            }
            if (any) grid.m_width = 0; // size change = rebuild
        }

        /// The storage slots are laid out by this panel, so the grid's own scrolling has nothing to do.
        private void HideScrollbar()
        {
            if (m_window.Scroll && m_window.Scroll.enabled) m_window.Scroll.enabled = false;
            if (m_window.Scrollbar && m_window.Scrollbar.activeSelf) m_window.Scrollbar.SetActive(false);
        }

        /// The vanilla title, centred over the (wider) window.
        private void PlaceTitle()
        {
            var title = m_gui.m_containerName;
            if (!title) return;
            title.horizontalAlignment = HorizontalAlignmentOptions.Center;
            float shift = m_root.rect.center.x - RootBox(title.rectTransform).center.x;
            if (Mathf.Abs(shift) > 0.5f) title.rectTransform.position += m_root.TransformVector(new Vector3(shift, 0f, 0f));
        }

        /// "Add trophies", "Collect tribute" and (if enabled) "Remind them!" along the bottom edge from the left.
        private void PlaceButtons()
        {
            var pos = m_root.rect.min + new Vector2(MarginX, ButtonMarginY);
            var size = m_addButton.Apply(m_root, pos);
            pos.x += size.x + ButtonGap;
            size = m_collectButton.Apply(m_root, pos);
            pos.x += size.x + ButtonGap;
            if (Settings.OverclockEnabled.Value) m_remindButton.Apply(m_root, pos);
            else m_remindButton.Restore();
        }

        /// ChestButler's sorting buttons make no sense on a tribute table (and would cover the slots).
        private void HideChestButlerBar()
        {
            if (!Compat.ChestButlerInstalled || !m_gui.m_takeAllButton) return;
            var bar = m_gui.m_takeAllButton.transform.parent.Find(Compat.ChestButlerBarName);
            if (!bar || !bar.gameObject.activeSelf) return;
            bar.gameObject.SetActive(false);
            m_hiddenBar = bar.gameObject;
        }

        /// A RectTransform's rectangle in window (root) units.
        private Rect RootBox(RectTransform rt)
        {
            rt.GetWorldCorners(s_corners);
            Vector2 min = m_root.InverseTransformPoint(s_corners[0]);
            Vector2 max = m_root.InverseTransformPoint(s_corners[2]);
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        /// Once per game: the vanilla window's layout in Player.log, to tune this panel against.
        private void LogWindow()
        {
            if (s_loggedWindow) return;
            s_loggedWindow = true;
            var sb = new StringBuilder("[DeathAndTaxes] Container window before resizing:\n");
            Describe(m_gui.m_container, sb, "  ", 3);
            sb.Append($"  element space {Spacing}, slot {SlotSize}, button height {ButtonHeight():0.#}, " +
                      $"root scale {m_root.lossyScale.x:0.###}, needed {NeededSize()}");
            Debug.Log(sb.ToString());
        }

        private void Describe(RectTransform rt, StringBuilder sb, string indent, int depth)
        {
            sb.Append($"{indent}{rt.name}{(rt.gameObject.activeSelf ? "" : " (off)")}: size {rt.rect.size}, anchors {rt.anchorMin}-{rt.anchorMax}, " +
                      $"pivot {rt.pivot}, pos {rt.anchoredPosition}, scale {rt.localScale.x:0.##}\n");
            if (depth <= 0 || rt == m_gui.m_containerGrid.m_gridRoot) return;
            foreach (Transform child in rt)
                if (child is RectTransform c && c != m_root) Describe(c, sb, indent + "  ", depth - 1);
        }

        // ---- content ----

        private void Clear()
        {
            if (m_content) Destroy(m_content);
            m_content = null;
            m_trophySlots.Clear();
            m_resources.Clear();
            m_tierLabels.Clear();
            m_status = null;
        }

        private void Build()
        {
            // After the slot holder, so it is drawn on top of the storage slots
            m_content = new GameObject("content", typeof(RectTransform));
            m_content.transform.SetParent(m_root, false);
            Stretch((RectTransform)m_content.transform);

            var def = m_table.Definition;
            for (int tier = 0; tier < def.Tiers.Length; tier++)
            {
                foreach (var trophy in def.Tiers[tier].Trophies)
                    m_trophySlots.Add(CreateTrophySlot(trophy, tier));
                m_tierLabels.Add(CreateText(12f, TextAlignmentOptions.TopRight, new Vector2(200f, LabelHeight), autoSize: true));
            }
            foreach (var slot in m_layout)
                m_resources.Add(new ResourceView
                {
                    Slot = slot,
                    Ghost = CreateGhost(slot.Resource),
                    Rate = CreateText(12f, TextAlignmentOptions.Top, new Vector2(Spacing, LabelHeight), autoSize: true),
                });
            m_unlocked = new bool[def.Tiers.Length];

            m_status = CreateText(12f, TextAlignmentOptions.MidlineLeft, new Vector2(300f, 20f), autoSize: true);
            m_status.color = StatusColor;
        }

        private TrophySlot CreateTrophySlot(string trophy, int tier)
        {
            // Reuse the vanilla inventory slot so it looks like the rest of the window
            var go = Instantiate(m_gui.m_containerGrid.m_elementPrefab, m_content.transform, false);
            go.name = "trophy_" + trophy;
            var element = go.GetComponent<InventoryElement>();
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            var slot = new TrophySlot { Trophy = trophy, Tier = tier, Rect = rt };

            var drop = TributeTable.GetDrop(trophy);
            var rarity = m_table.Definition.Tiers[tier].Rarity;
            if (element)
            {
                // Only the icon and the amount ("3/10") are used
                foreach (Transform child in go.transform)
                    child.gameObject.SetActive(IsBranch(element.m_icon, child) || IsBranch(element.m_amount, child));
                if (element.m_icon)
                {
                    element.m_icon.gameObject.SetActive(true);
                    element.m_icon.sprite = drop ? drop.m_itemData.GetIcon() : null;
                    element.m_icon.enabled = element.m_icon.sprite;
                    slot.Icon = element.m_icon;
                }
                if (element.m_amount)
                {
                    element.m_amount.gameObject.SetActive(true);
                    element.m_amount.enabled = true;
                    slot.Amount = element.m_amount;
                }
                if (element.m_tooltip)
                {
                    element.m_tooltip.m_topic = TributeTable.DisplayName(trophy);
                    element.m_tooltip.m_text = $"<color={Rarities.Color(rarity)}>{rarity}</color> trophy, up to " +
                                               $"{Rarities.MaxPerTrophy(rarity)} on the table.\n" +
                                               "Click to offer all of these from your inventory, or drop a dragged trophy here.";
                }
                if (element.m_button)
                {
                    // A new event also drops listeners saved in the vanilla slot prefab
                    element.m_button.onClick = new Button.ButtonClickedEvent();
                    element.m_button.onClick.AddListener(() => OnTrophyClicked(trophy));
                }
                Destroy(element);
            }
            return slot;
        }

        private static bool IsBranch(Component part, Transform child) => part && part.transform.IsChildOf(child);

        private Image CreateGhost(string resource)
        {
            var go = new GameObject("ghost_" + resource, typeof(RectTransform));
            go.transform.SetParent(m_content.transform, false);
            var image = go.AddComponent<Image>();
            var drop = TributeTable.GetDrop(resource);
            image.sprite = drop ? drop.m_itemData.GetIcon() : null;
            image.preserveAspect = true;
            image.raycastTarget = false; // clicks and drops still reach the storage slot below
            image.enabled = false;
            return image;
        }

        private TMP_Text CreateText(float size, TextAlignmentOptions align, Vector2 box, bool autoSize = false)
        {
            var go = new GameObject("text", typeof(RectTransform));
            go.transform.SetParent(m_content.transform, false);
            var text = go.AddComponent<TextMeshProUGUI>();
            text.font = DefaultFont();
            text.fontSharedMaterial = text.font.material;
            text.fontSize = size;
            text.color = LabelColor;
            text.alignment = align;
            text.richText = true;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            if (autoSize)
            {
                text.enableAutoSizing = true;
                text.fontSizeMin = 7f;
                text.fontSizeMax = size;
            }
            text.rectTransform.sizeDelta = box;
            return text;
        }

        private static TMP_FontAsset _defaultFont;

        /// Valheim's standard text font (the one used for item names, tooltips and buttons).
        private TMP_FontAsset DefaultFont()
        {
            if (_defaultFont) return _defaultFont;
            foreach (var font in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
                if (font.name == "Valheim-AveriaSerifLibre")
                    return _defaultFont = font;
            return _defaultFont = m_gui.m_containerName.font;
        }

        private void OnTrophyClicked(string trophy)
        {
            var player = Player.m_localPlayer;
            if (!player || !m_table) return;
            var drag = m_gui.m_dragItem;
            if (drag == null)
            {
                m_table.OfferAll(player, trophy);
            }
            else if (TributeTable.PrefabName(drag) == trophy)
            {
                m_table.Offer(player, m_gui.m_dragInventory, drag, m_gui.m_dragAmount);
                m_gui.SetupDragItem(null, null, 1);
            }
            else
            {
                player.Message(MessageHud.MessageType.Center, $"Only {TributeTable.DisplayName(trophy)} goes here");
            }
            m_refreshTimer = 0f;
        }

        /// Positions everything in the window, row by row from the top (highest tier) down to common.
        private void Layout()
        {
            var def = m_table.Definition;
            float s = Spacing;
            var slot = SlotSize;
            var r = m_root.rect;
            float left = r.xMin + MarginX, top = r.yMax - TitleRowHeight;
            int trophyColumns = TrophyColumns;
            float trophyRight = left + trophyColumns * s - (s - slot.x);
            float resourceLeft = left + (trophyColumns + GroupGap) * s;

            float SlotY(int tier) => top - def.RowOf(tier) * RowHeight - slot.y / 2f;
            float LabelY(int tier) => SlotY(tier) - slot.y / 2f - LabelHeight / 2f - 1f;
            float TrophyX(int column) => left + slot.x / 2f + column * s;
            float ResourceX(int column) => resourceLeft + slot.x / 2f + column * s;

            // Trophies right-aligned against their resources, tier total underneath
            for (int tier = 0; tier < def.Tiers.Length; tier++)
            {
                var trophies = m_trophySlots.Where(ts => ts.Tier == tier).ToList();
                for (int j = 0; j < trophies.Count; j++)
                {
                    trophies[j].Rect.sizeDelta = slot;
                    trophies[j].Rect.localPosition = new Vector3(TrophyX(trophyColumns - trophies.Count + j), SlotY(tier), 0f);
                }
                var label = m_tierLabels[tier].rectTransform;
                float width = trophyColumns * s;
                label.sizeDelta = new Vector2(width, LabelHeight);
                label.localPosition = new Vector3(trophyRight - width / 2f, LabelY(tier), 0f);
            }

            // The vanilla storage slots, moved out of the grid into their tier's row; unused ones hidden
            foreach (var element in m_gui.m_containerGrid.m_elements)
            {
                if (!element) continue;
                var ert = (RectTransform)element.transform;
                Adopt(ert, slot);
                var pos = element.Position;
                int index = Array.FindIndex(m_layout, rs => rs.X == pos.x && rs.Y == pos.y);
                if (element.gameObject.activeSelf != index >= 0) element.gameObject.SetActive(index >= 0);
                if (index >= 0)
                    ert.localPosition = new Vector3(ResourceX(m_layout[index].X), SlotY(m_layout[index].Tier), 0f);
            }

            foreach (var view in m_resources)
            {
                float x = ResourceX(view.Slot.X), y = SlotY(view.Slot.Tier);
                view.Ghost.rectTransform.sizeDelta = slot * 0.78f;
                view.Ghost.rectTransform.localPosition = new Vector3(x, y, 0f);
                view.Rate.rectTransform.sizeDelta = new Vector2(s, LabelHeight);
                view.Rate.rectTransform.localPosition = new Vector3(x, LabelY(view.Slot.Tier), 0f);
            }

            // Small status line in the title row, left of the centred title
            var title = m_gui.m_containerName;
            float titleY = title ? RootBox(title.rectTransform).center.y : r.yMax - TitleRowHeight / 2f;
            float titleHalf = title ? title.preferredWidth * title.rectTransform.lossyScale.x / m_root.lossyScale.x / 2f : 120f;
            float statusWidth = Mathf.Max(80f, r.center.x - titleHalf - 12f - left);
            m_status.rectTransform.sizeDelta = new Vector2(statusWidth, 20f);
            m_status.rectTransform.localPosition = new Vector3(left + statusWidth / 2f, titleY, 0f);
        }

        /// Moves a vanilla storage slot under this panel (outside the grid's scroll area).
        private void Adopt(RectTransform element, Vector2 size)
        {
            if (element.parent == m_slotHolder) return;
            element.SetParent(m_slotHolder, false);
            element.anchorMin = element.anchorMax = element.pivot = new Vector2(0.5f, 0.5f);
            element.sizeDelta = size;
            element.localScale = Vector3.one;
        }

        /// Empty resource slots show a faint icon (dark while locked) and a tooltip; vanilla leaves them blank.
        private void ShowEmptySlots()
        {
            var inv = m_gui.m_currentContainer ? m_gui.m_currentContainer.GetInventory() : null;
            if (inv == null) return;
            var grid = m_gui.m_containerGrid;
            foreach (var view in m_resources)
            {
                var rs = view.Slot;
                bool empty = inv.GetItemAt(rs.X, rs.Y) == null;
                if (view.Ghost.enabled != empty) view.Ghost.enabled = empty;
                view.Ghost.color = m_unlocked[rs.Tier] ? EmptyTint : LockedTint;
                if (!empty || view.Topic == null) continue;
                var element = grid.GetElement(rs.X, rs.Y, grid.m_width);
                if (element && element.m_tooltip) element.m_tooltip.Set(view.Topic, view.Tooltip);
            }
        }

        private void Refresh()
        {
            var def = m_table.Definition;
            for (int tier = 0; tier < def.Tiers.Length; tier++)
            {
                var t = def.Tiers[tier];
                int count = m_table.TierCount(tier);
                m_unlocked[tier] = m_table.IsUnlocked(tier);
                string label = $"<color={Rarities.Color(t.Rarity)}>{t.Rarity}</color> {count}/{t.MaxTrophies}";
                if (!m_unlocked[tier])
                {
                    int more = t.UnlockAt - count;
                    label += $" <color=#9E9E9E>- {more} more {(more == 1 ? "trophy" : "trophies")}</color>";
                }
                m_tierLabels[tier].text = label;
            }

            foreach (var ts in m_trophySlots)
            {
                int count = m_table.GetTrophyCount(ts.Trophy);
                if (ts.Amount) ts.Amount.text = $"{count}/{m_table.MaxTrophies(ts.Trophy)}";
                if (ts.Icon) ts.Icon.color = count > 0 ? Color.white : NoTrophyTint;
            }

            foreach (var view in m_resources)
            {
                var rs = view.Slot;
                var tier = def.Tiers[rs.Tier];
                view.Topic = TributeTable.DisplayName(rs.Resource);
                if (m_unlocked[rs.Tier])
                {
                    float rate = m_table.CurrentRate(rs);
                    view.Rate.text = $"{rate:0.#}/h";
                    view.Rate.color = LabelColor;
                    view.Tooltip = $"Produced here: {rate:0.#} per hour.";
                }
                else
                {
                    int count = m_table.TierCount(rs.Tier);
                    view.Rate.text = ""; // the tier label under the trophies shows what's missing
                    view.Tooltip = $"Locked: needs {tier.UnlockAt} <color={Rarities.Color(tier.Rarity)}>{tier.Rarity.ToString().ToLower()}</color> " +
                                   $"trophies on the table ({count}/{tier.UnlockAt}).";
                }
            }

            m_status.text = m_table.StatusText();
        }
    }
}
