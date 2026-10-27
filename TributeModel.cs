using System.Collections.Generic;
using System.Linq;
using Jotunn.Managers;
using UnityEngine;

namespace DeathAndTaxes
{
    /// Kitbashes the tribute table from vanilla models: a back wall with the definition's trophies
    /// mounted on item-stand plaques, and the table top covered in offerings.
    /// Only meshes and materials are copied, never the game logic of the source prefabs.
    /// Layout is computed from the actual mesh sizes, so it adapts if vanilla models change.
    public static class TributeModel
    {
        private const string WallPrefab = "woodwall";
        private const string StandPrefab = "itemstand";
        private const float TrophyFill = 0.85f;     // share of each trophy's slot width it may use
        private const float TrophyMargin = 0.03f;   // free space between trophies and the wall top / table top
        private const float WallOverlap = 0.02f;    // the wall's front sinks this far into the table's back edge

        // A trophy on more than one table gets the size it has on the first table (tables are built in order)
        private static readonly Dictionary<string, float> s_trophyScale = new Dictionary<string, float>();

        public const string DecorName = "lob_decor";
        public const string WallName = "wall";
        public const string WallColliderName = "wall_collider";
        public const string TrophyPrefix = "trophy_"; // + trophy prefab: shown/hidden per table by TributeTable

        private static readonly string[] PieceStateParts = { "worn", "broken" };
        private static readonly string[] SkippedParts = { "worn", "broken", "attach", "attachobj" };

        public static void Build(GameObject prefab, TributeDefinition def)
        {
            var root = prefab.transform;
            int layer = prefab.layer;

            if (def.Footprint != Vector2.zero) FitTable(root, def.Footprint);
            var tableBounds = SourceBounds(root, SkippedParts);
            var decor = NewChild(root, DecorName, layer);

            // Lay out along X with the wall on +Z; turn the whole decor if the table is longer along Z
            if (tableBounds.size.z > tableBounds.size.x)
                decor.localRotation = Quaternion.Euler(0f, 90f, 0f);
            var table = TransformBounds(tableBounds, Matrix4x4.Rotate(Quaternion.Inverse(decor.localRotation)));
            float top = table.max.y;

            float wallTop = top + def.WallHeightAboveTable;
            float wallFront = BuildWall(decor, table, wallTop, layer);
            BuildTrophies(decor, def, table, wallFront, wallTop, layer);
            BuildOfferings(decor, def, table, wallFront, layer);
            if (!string.IsNullOrEmpty(def.LookFrom)) ApplyLook(root, decor, def.LookFrom);
            if (def.LookTint != Color.white || def.LookGray > 0f || def.LookWhite > 0f)
                TintLook(root, decor, def.LookTint, def.LookGray, def.LookWhite);

            Debug.Log($"[DeathAndTaxes] Built {prefab.name} model: table {table.size} top {top:0.00}, wall front z {wallFront:0.00}");
        }

        /// Scales the base table's own parts (not its height) to `footprint` (width x depth in metres);
        /// the wall stands behind it. Snap points and colliders scale along.
        private static void FitTable(Transform root, Vector2 footprint)
        {
            var bounds = SourceBounds(root, SkippedParts);

            // Same orientation rule as the decor: the long side is the width
            bool alongZ = bounds.size.z > bounds.size.x;
            float width = alongZ ? bounds.size.z : bounds.size.x;
            float depth = alongZ ? bounds.size.x : bounds.size.z;
            float sw = footprint.x / width, sd = footprint.y / depth;
            var scale = alongZ ? new Vector3(sd, 1f, sw) : new Vector3(sw, 1f, sd);

            foreach (Transform child in root.Cast<Transform>().ToList())
                ScaleInParent(child, scale);
            foreach (var box in root.GetComponents<BoxCollider>())
            {
                box.center = Vector3.Scale(box.center, scale);
                box.size = Vector3.Scale(box.size, scale);
            }
            foreach (var other in root.GetComponents<Collider>())
                if (!(other is BoxCollider))
                    Debug.LogWarning($"[DeathAndTaxes] {root.name}: {other.GetType().Name} on the root isn't resized");
            foreach (var lod in root.GetComponentsInChildren<LODGroup>(true))
                lod.RecalculateBounds();

            Debug.Log($"[DeathAndTaxes] {root.name}: base table {width:0.00} x {depth:0.00} m scaled by {sw:0.00} x {sd:0.00} " +
                      $"to {footprint.x:0.##} x {footprint.y:0.##} m");
        }

        /// Scales a child in its parent's axes, also when the child itself is turned (by 90 degree steps).
        private static void ScaleInParent(Transform t, Vector3 scale)
        {
            t.localPosition = Vector3.Scale(t.localPosition, scale);
            var r = t.localRotation;
            float Along(Vector3 axis)
            {
                var a = r * axis;
                return new Vector3(a.x * scale.x, a.y * scale.y, a.z * scale.z).magnitude;
            }
            t.localScale = Vector3.Scale(t.localScale, new Vector3(Along(Vector3.right), Along(Vector3.up), Along(Vector3.forward)));
        }

        /// Wall standing on the floor behind the table. Returns the z of its front face.
        private static float BuildWall(Transform decor, Bounds table, float wallTop, int layer)
        {
            var source = PrefabManager.Instance.GetPrefab(WallPrefab);
            if (!source)
            {
                Debug.LogWarning($"[DeathAndTaxes] Missing {WallPrefab}, no back wall");
                return table.max.z;
            }
            var wb = SourceBounds(source.transform, SkippedParts);

            // Wall's long side along X, turned so its smooth side faces the table (planks/beams at the back)
            bool alongZ = wb.size.z > wb.size.x;
            var rot = Quaternion.Euler(0f, alongZ ? 270f : 180f, 0f);
            float width = alongZ ? wb.size.z : wb.size.x;
            float height = wallTop - table.min.y;
            var scale = alongZ
                ? new Vector3(1f, height / wb.size.y, table.size.x / width)
                : new Vector3(table.size.x / width, height / wb.size.y, 1f);

            var placed = TransformBounds(wb, Matrix4x4.TRS(Vector3.zero, rot, scale));
            var pos = new Vector3(
                table.center.x - placed.center.x,
                table.min.y - placed.min.y,
                table.max.z - placed.min.z - WallOverlap);

            var wall = NewChild(decor, WallName, layer);
            wall.localPosition = pos;
            wall.localRotation = rot;
            wall.localScale = scale;
            CopyRenderers(source.transform, wall, SkippedParts, layer);

            // Solid, so it can be hit (hover/interact) and blocks walking through
            var col = NewChild(decor, WallColliderName, layer).gameObject.AddComponent<BoxCollider>();
            col.center = placed.center + pos;
            col.size = placed.size;

            return placed.min.z + pos.z;
        }

        /// One item-stand plaque per trophy on the wall, with the trophy mounted like vanilla does.
        /// Trophies are laid out in rows (def.Wall): each gets an equal share of the table width within its row.
        private static void BuildTrophies(Transform decor, TributeDefinition def, Bounds table, float wallFront, float wallTop, int layer)
        {
            var standPrefab = PrefabManager.Instance.GetPrefab(StandPrefab);
            var stand = standPrefab ? standPrefab.GetComponent<ItemStand>() : null;
            if (!stand || !stand.m_attachOther)
            {
                Debug.LogWarning($"[DeathAndTaxes] Missing {StandPrefab}, no trophies on the wall");
                return;
            }

            var sb = SourceBounds(stand.transform, SkippedParts);
            var attach = Relative(stand.transform, stand.m_attachOther);

            // Front of the plaque = the side the attach point sits on; turn it to face away from the wall (-Z)
            Vector3 d = (Vector3)attach.GetColumn(3) - sb.center;
            var front = Mathf.Abs(d.x) > Mathf.Abs(d.z) ? new Vector3(Mathf.Sign(d.x), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(d.z));
            var rot = Quaternion.LookRotation(Vector3.back) * Quaternion.Inverse(Quaternion.LookRotation(front));
            var plaque = TransformBounds(sb, Matrix4x4.Rotate(rot));

            // Build each plaque + trophy at the origin first, then scale them all down to fit
            var wall = def.Wall;
            int n = wall.Length;
            var groups = new List<Transform>();
            var groupBounds = new List<Bounds>();
            for (int i = 0; i < n; i++)
            {
                var group = NewChild(decor, TrophyPrefix + wall[i].Trophy, layer);
                var standT = NewChild(group, "stand", layer);
                standT.localRotation = Quaternion.Euler(0f, 0f, wall[i].Turn) * rot;
                CopyRenderers(stand.transform, standT, SkippedParts, layer);

                var trophyPrefab = PrefabManager.Instance.GetPrefab(wall[i].Trophy);
                var attachPrefab = trophyPrefab ? ItemStand.GetAttachPrefab(trophyPrefab) : null;
                if (attachPrefab)
                {
                    // Same pose ItemStand.SetVisualItem uses
                    var attachT = NewChild(standT, "attach", layer);
                    SetLocal(attachT, attach);
                    var item = trophyPrefab.GetComponent<ItemDrop>().m_itemData;
                    var settings = new List<ItemStand.OrientationSettings>();
                    stand.GetOrientationSettings(item, ref settings);
                    var os = settings[0];

                    var visual = NewChild(attachT, "trophy", layer);
                    visual.localPosition = attachPrefab.transform.localPosition + os.m_position;
                    visual.localRotation = attachPrefab.transform.localRotation * Quaternion.Euler(os.m_rotation);
                    visual.localScale = Vector3.Scale(Vector3.Scale(attachPrefab.transform.localScale, item.GetScale(1)), os.m_scale);
                    CopyRenderers(ItemStand.GetAttachGameObject(attachPrefab).transform, visual, new string[0], layer);
                    if (wall[i].Center && visual.childCount > 0)
                    {
                        // Shift the model (in the wall plane) so it sits in the middle of the plaque
                        var plaqueBox = SourceBounds(group, new[] { "attach" });
                        var trophyBox = TransformBounds(SourceBounds(visual, new string[0]), Relative(group, visual));
                        var shift = plaqueBox.center - trophyBox.center;
                        shift.z = 0f;
                        visual.localPosition += visual.parent.InverseTransformVector(group.TransformVector(shift));
                    }
                    if (visual.childCount == 0)
                        Debug.LogWarning($"[DeathAndTaxes] {wall[i].Trophy}: nothing copied from its stand model:\n" +
                                         DescribeHierarchy(attachPrefab.transform));
                }
                else
                    Debug.LogWarning($"[DeathAndTaxes] No wall model for {wall[i].Trophy}");

                groups.Add(group);
                groupBounds.Add(SourceBounds(group, new string[0]));
            }

            // Trophy centres at the requested fraction of the wall height (floor to wall top)
            float floor = table.min.y;
            var centers = new float[n];
            for (int i = 0; i < n; i++)
                centers[i] = Mathf.Lerp(floor, wallTop, wall[i].Height);

            // One shared scale so every trophy fits its share of its row's width, and vertically stays between
            // the table top, the wall top and halfway to the neighbouring rows
            float scale = 1f;
            var xs = new float[n];
            for (int i = 0; i < n; i++)
            {
                var row = Enumerable.Range(0, n).Where(j => wall[j].Row == wall[i].Row).ToList();
                float cell = table.size.x / row.Count;
                xs[i] = table.center.x + (row.IndexOf(i) - (row.Count - 1) / 2f) * cell;

                float above = wallTop - TrophyMargin - centers[i];
                float below = centers[i] - (table.max.y + TrophyMargin);
                foreach (int j in Enumerable.Range(0, n))
                {
                    if (wall[j].Row == wall[i].Row + 1) above = Mathf.Min(above, (centers[j] - centers[i]) / 2f);
                    if (wall[j].Row == wall[i].Row - 1) below = Mathf.Min(below, (centers[i] - centers[j]) / 2f);
                }
                var b = groupBounds[i];
                // A size below 1 (a huge head shown smaller) lets the shared scale grow; sizes above 1 are extras on top
                float shrink = Mathf.Min(wall[i].Size, 1f);
                scale = Mathf.Min(scale, cell * TrophyFill / (b.size.x * shrink), 2f * Mathf.Min(above, below) / (b.size.y * shrink));
            }

            if (def.TrophyScale > 0f) scale = def.TrophyScale; // fixed per table, so sizes are plain multipliers

            for (int i = 0; i < n; i++)
            {
                var b = groupBounds[i];
                float s = scale * wall[i].Size;
                if (s_trophyScale.TryGetValue(wall[i].Trophy, out var earlier)) s = earlier;
                else s_trophyScale[wall[i].Trophy] = s;
                groups[i].localScale = Vector3.one * s;
                ResizePlaque(groups[i].Find("stand"), sb, s);
                groups[i].localPosition = new Vector3(
                    xs[i] - b.center.x * s,
                    centers[i] - (def.AlignPlaques ? (Quaternion.Euler(0f, 0f, wall[i].Turn) * plaque.center).y : b.center.y) * s,
                    wallFront - plaque.max.z * s); // plaque back flat against the wall
            }
            Debug.Log($"[DeathAndTaxes] Trophy scale {scale:0.00} (wall top {wallTop:0.00} m, sizes " +
                      string.Join(", ", groupBounds.Select(b => b.size.ToString("0.00"))) + ")");
        }

        /// Item-stand plaque size on every table: the one the Drake (TrophyHatchling) has on the Mountains table (user).
        private static float PlaqueScale
        {
            get
            {
                foreach (var d in TributeDefinitions.All)
                    if (d.TrophyScale > 0f && d.Wall != null)
                        foreach (var w in d.Wall)
                            if (w.Trophy == "TrophyHatchling") return d.TrophyScale * w.Size;
                return 0.82f;
            }
        }

        /// Gives a plaque the shared plaque size whatever its trophy's scale `s`: the plaque meshes are scaled around
        /// the middle of their back face (so they stay flat on the wall), the trophy moves with the plaque's front.
        private static void ResizePlaque(Transform standT, Bounds plaqueBounds, float s)
        {
            if (!standT || s <= 0f) return;
            float k = PlaqueScale / s;
            if (Mathf.Abs(k - 1f) < 0.001f) return;
            var attachT = standT.Find("attach");
            if (!attachT) return;
            // Front = from the plaque's centre towards the attach point, along the plaque's thinnest axis
            var e = plaqueBounds.extents;
            Vector3 d = attachT.localPosition - plaqueBounds.center;
            Vector3 front = e.x <= e.y && e.x <= e.z ? new Vector3(Mathf.Sign(d.x), 0f, 0f)
                          : e.z <= e.y ? new Vector3(0f, 0f, Mathf.Sign(d.z)) : new Vector3(0f, Mathf.Sign(d.y), 0f);
            float half = Vector3.Dot(new Vector3(Mathf.Abs(front.x), Mathf.Abs(front.y), Mathf.Abs(front.z)), e);
            var pivot = plaqueBounds.center - front * half;
            foreach (Transform part in standT)
            {
                if (part == attachT) continue;
                part.localPosition = pivot + (part.localPosition - pivot) * k;
                part.localScale *= k;
            }
            attachT.localPosition += front * (2f * half * (k - 1f));
        }

        private static bool IsPlaquePart(Transform t, Transform decor)
        {
            for (var p = t; p && p != decor; p = p.parent)
            {
                if (p.name == "attach") return false;
                if (p.name == "stand") return true;
            }
            return false;
        }

        /// Offerings placed on the table top as listed in the definition.
        private static void BuildOfferings(Transform decor, TributeDefinition def, Bounds table, float wallFront, int layer)
        {
            if (def.Offerings == null) return;
            const float margin = 0.03f;
            float backLimit = Mathf.Min(table.max.z, wallFront);
            var log = new List<string>();

            foreach (var o in def.Offerings)
            {
                var source = PrefabManager.Instance.GetPrefab(o.Prefab);
                if (!source)
                {
                    Debug.LogWarning($"[DeathAndTaxes] Missing offering {o.Prefab}");
                    continue;
                }
                // Some items only have their model under "attach"; use it when nothing else is visible
                var skip = SkippedParts;
                var ib = SourceBounds(source.transform, skip);
                if (ib.size == Vector3.zero)
                {
                    skip = PieceStateParts;
                    ib = SourceBounds(source.transform, skip);
                }
                if (ib.size == Vector3.zero)
                {
                    Debug.LogWarning($"[DeathAndTaxes] No model found for offering {o.Prefab}:\n" + DescribeHierarchy(source.transform));
                    continue;
                }

                // Tilt first (lay it down), then optionally line up with the wall, then turn
                var rot = Quaternion.Euler(0f, 0f, o.Tilt);
                if (o.AlongWall)
                {
                    var tilted = TransformBounds(ib, Matrix4x4.Rotate(rot));
                    if (tilted.size.z > tilted.size.x) rot = Quaternion.Euler(0f, 90f, 0f) * rot;
                }
                rot = Quaternion.Euler(0f, o.Yaw, 0f) * rot;

                float largest = Mathf.Max(ib.size.x, ib.size.y, ib.size.z);
                float s = o.MaxSize / largest; // may be bigger than the vanilla item
                var placed = TransformBounds(ib, Matrix4x4.TRS(Vector3.zero, rot, Vector3.one * s));

                float x = Mathf.Lerp(table.min.x, table.max.x, o.U) - placed.center.x;
                float z = Mathf.Lerp(table.min.z, backLimit, o.V) - placed.center.z;
                x = Mathf.Clamp(x, table.min.x + margin - placed.min.x, table.max.x - margin - placed.max.x);
                z = Mathf.Clamp(z, table.min.z + margin - placed.min.z, backLimit - margin - placed.max.z);

                var t = NewChild(decor, "offering_" + o.Prefab, layer);
                t.localPosition = new Vector3(x, table.max.y - placed.min.y + o.Lift, z);
                t.localRotation = rot;
                t.localScale = Vector3.one * s;
                CopyRenderers(source.transform, t, skip, layer);
                log.Add($"{o.Prefab} {largest:0.00}m x{s:0.00}");
            }
            Debug.Log("[DeathAndTaxes] Offerings: " + string.Join(", ", log));
        }

        // ---- mesh copying helpers ----

        private static Transform NewChild(Transform parent, string name, int layer)
        {
            var go = new GameObject(name) { layer = layer };
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        private static void SetLocal(Transform t, Matrix4x4 m)
        {
            t.localPosition = m.GetColumn(3);
            t.localRotation = m.rotation;
            t.localScale = m.lossyScale;
        }

        private static Matrix4x4 Relative(Transform root, Transform t) => root.worldToLocalMatrix * t.localToWorldMatrix;

        /// Visible mesh renderers of a model: active, highest LOD only, not in skipped parts.
        private static IEnumerable<Renderer> VisibleRenderers(Transform root, string[] skip)
        {
            // Lower LODs only; a renderer that is also in LOD 0 (e.g. the Blob trophy) is the close-up model and stays
            var lowLods = new HashSet<Renderer>();
            foreach (var lodGroup in root.GetComponentsInChildren<LODGroup>(true))
            {
                var lods = lodGroup.GetLODs();
                if (lods.Length == 0) continue;
                var closeUp = new HashSet<Renderer>(lods[0].renderers.Where(r => r));
                if (closeUp.Count == 0) continue; // no close-up model listed: keep everything
                foreach (var lod in lods.Skip(1))
                    foreach (var r in lod.renderers)
                        if (r && !closeUp.Contains(r)) lowLods.Add(r);
            }

            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (lowLods.Contains(r)) continue;
                if (!(r is MeshRenderer || r is SkinnedMeshRenderer) || !MeshOf(r)) continue;
                bool hidden = false;
                for (var t = r.transform; t && t != root; t = t.parent)
                    if (!t.gameObject.activeSelf || skip.Contains(t.name)) { hidden = true; break; }
                if (!hidden) yield return r;
            }
        }

        /// The mesh a renderer draws. Skinned meshes (e.g. the Blob and Kvastur trophies) are copied in their rest pose.
        private static Mesh MeshOf(Renderer r) => r is SkinnedMeshRenderer s ? s.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;

        private static Bounds SourceBounds(Transform root, string[] skip)
        {
            bool any = false;
            var bounds = new Bounds();
            foreach (var r in VisibleRenderers(root, skip))
            {
                var b = TransformBounds(MeshOf(r).bounds, Relative(root, r.transform));
                if (!any) { bounds = b; any = true; }
                else bounds.Encapsulate(b);
            }
            return bounds;
        }


        /// Multiplies the colour of the table and the back wall (copies of their materials, vanilla stays unchanged),
        /// e.g. a cool gray for the Swamp. The item-stand plaques get the same look; trophies and offerings keep theirs.
        private static void TintLook(Transform root, Transform decor, Color tint, float gray, float white)
        {
            var wall = decor.Find("wall");
            var copies = new Dictionary<Material, Material>();
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true)
                         .Where(r => !r.transform.IsChildOf(decor) || (wall && r.transform.IsChildOf(wall)) ||
                                     IsPlaquePart(r.transform, decor)))
            {
                var materials = r.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    var m = materials[i];
                    if (!m || !m.HasProperty("_Color")) continue;
                    if (!copies.TryGetValue(m, out var copy))
                    {
                        copy = new Material(m) { name = m.name + "_tinted" };
                        copy.color = m.color * tint;
                        if ((gray > 0f || white > 0f) && m.mainTexture is Texture2D tex) copy.mainTexture = Desaturate(tex, gray, white);
                        copies[m] = copy;
                    }
                    materials[i] = copy;
                }
                r.sharedMaterials = materials;
            }
            Debug.Log($"[DeathAndTaxes] {root.name}: {copies.Count} table/wall materials tinted {tint}, gray {gray:0.00}, white {white:0.00}");
        }

        /// A copy of a texture with its colour pulled towards gray (0 = unchanged, 1 = fully gray). Game textures
        /// aren't readable, so it is copied through the GPU first.
        private static Texture2D Desaturate(Texture2D source, float amount, float white = 0f)
        {
            int w = source.width, h = source.height;
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active;
            Graphics.Blit(source, rt);
            RenderTexture.active = rt;
            var copy = new Texture2D(w, h, TextureFormat.RGBA32, true, false)
            {
                name = source.name + "_gray",
                wrapMode = source.wrapMode,
                filterMode = source.filterMode,
                anisoLevel = source.anisoLevel,
            };
            copy.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);

            var pixels = copy.GetPixels32();
            for (int i = 0; i < pixels.Length; i++)
            {
                var p = pixels[i];
                float l = 0.299f * p.r + 0.587f * p.g + 0.114f * p.b;
                // Towards gray, then towards white (the grain stays visible as long as white < 1)
                float r = Mathf.Lerp(Mathf.Lerp(p.r, l, amount), 255f, white);
                float g = Mathf.Lerp(Mathf.Lerp(p.g, l, amount), 255f, white);
                float b = Mathf.Lerp(Mathf.Lerp(p.b, l, amount), 255f, white);
                pixels[i] = new Color32((byte)r, (byte)g, (byte)b, p.a);
            }
            copy.SetPixels32(pixels);
            copy.Apply(true, true); // mipmaps, then free the CPU copy
            return copy;
        }

        /// Gives the table and the back wall the material of another prefab (e.g. Ancient bark for the Swamp),
        /// so each biome's table has its own look. Trophies, plaques and offerings keep their own materials.
        private static void ApplyLook(Transform root, Transform decor, string lookFrom)
        {
            var source = PrefabManager.Instance.GetPrefab(lookFrom);
            var material = source ? VisibleRenderers(source.transform, SkippedParts).Select(r => r.sharedMaterial).FirstOrDefault(m => m) : null;
            if (!material)
            {
                Debug.LogWarning($"[DeathAndTaxes] {root.name}: no material found on {lookFrom}, keeping the vanilla look");
                return;
            }
            var wall = decor.Find("wall");
            var targets = root.GetComponentsInChildren<MeshRenderer>(true)
                .Where(r => !r.transform.IsChildOf(decor) || (wall && r.transform.IsChildOf(wall)));
            int count = 0;
            foreach (var r in targets)
            {
                r.sharedMaterials = Enumerable.Repeat(material, r.sharedMaterials.Length).ToArray();
                count++;
            }
            Debug.Log($"[DeathAndTaxes] {root.name}: {count} table/wall parts use the {lookFrom} material ({material.name})");
        }
        private static void CopyRenderers(Transform source, Transform parent, string[] skip, int layer)
        {
            foreach (var r in VisibleRenderers(source, skip))
            {
                var t = NewChild(parent, r.name, layer);
                SetLocal(t, Relative(source, r.transform));
                t.gameObject.AddComponent<MeshFilter>().sharedMesh = MeshOf(r);
                var copy = t.gameObject.AddComponent<MeshRenderer>();
                copy.sharedMaterials = r.sharedMaterials;
                copy.shadowCastingMode = r.shadowCastingMode;
                copy.receiveShadows = r.receiveShadows;
            }
        }

        /// Debug listing of a prefab's children and components, for models that can't be copied.
        private static string DescribeHierarchy(Transform t, string indent = "  ")
        {
            var parts = t.GetComponents<Component>().Where(c => c).Select(c => c.GetType().Name);
            string line = $"{indent}{t.name}{(t.gameObject.activeSelf ? "" : " (inactive)")}: {string.Join(", ", parts)}\n";
            foreach (Transform child in t)
                line += DescribeHierarchy(child, indent + "  ");
            return line;
        }

        private static Bounds TransformBounds(Bounds b, Matrix4x4 m)
        {
            var result = new Bounds(m.MultiplyPoint3x4(b.center), Vector3.zero);
            for (int i = 0; i < 8; i++)
            {
                var corner = b.center + Vector3.Scale(b.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                result.Encapsulate(m.MultiplyPoint3x4(corner));
            }
            return result;
        }
    }
}
