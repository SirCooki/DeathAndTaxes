using System.Collections.Generic;
using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace DeathAndTaxes
{
    /// Trophies the game doesn't have, for creatures without one. For now: Lord Reto (Ashlands miniboss),
    /// a recoloured Charred warrior trophy until it gets its own model.
    [HarmonyPatch]
    internal static class NewTrophies
    {
        public const string LordReto = "TrophyLordReto";
        private const string LordRetoCreature = "Charred_Melee_Dyrnwyn";
        private const string LordRetoBase = "TrophyCharredMelee";
        private static readonly Color LordRetoTint = new Color(1f, 0.55f, 0.15f); // glowing ember orange

        public static void Add()
        {
            var item = new CustomItem(LordReto, LordRetoBase, new ItemConfig
            {
                Name = "Lord Reto trophy",
                Description = "The skull still burns with the pride of the one who held Dyrnwyn.",
            });
            Tint(item.ItemPrefab, LordRetoTint);
            var shared = item.ItemDrop.m_itemData.m_shared;
            if (shared.m_icons != null && shared.m_icons.Length > 0 && shared.m_icons[0])
                shared.m_icons = new[] { TintSprite(shared.m_icons[0], LordRetoTint) };
            ItemManager.Instance.AddItem(item);
        }

        // Lord Reto always drops his trophy (like the other minibosses), once per kill
        [HarmonyPatch(typeof(CharacterDrop), nameof(CharacterDrop.GenerateDropList))]
        [HarmonyPostfix]
        private static void AddLordRetoDrop(CharacterDrop __instance, List<KeyValuePair<GameObject, int>> __result)
        {
            if (__result == null || !__instance.name.StartsWith(LordRetoCreature)) return;
            var trophy = ObjectDB.instance ? ObjectDB.instance.GetItemPrefab(LordReto) : null;
            if (trophy) __result.Add(new KeyValuePair<GameObject, int>(trophy, 1));
        }

        /// Multiplies every material colour on the model (copies, so the vanilla trophy stays unchanged).
        private static void Tint(GameObject prefab, Color tint)
        {
            foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    if (!materials[i]) continue;
                    var copy = new Material(materials[i]);
                    if (copy.HasProperty("_Color")) copy.color *= tint;
                    if (copy.HasProperty("_EmissionColor")) copy.SetColor("_EmissionColor", copy.GetColor("_EmissionColor") * tint);
                    materials[i] = copy;
                }
                renderer.sharedMaterials = materials;
            }
        }

        /// Copies the icon through the GPU (the game's textures aren't readable) and tints it.
        private static Sprite TintSprite(Sprite source, Color tint)
        {
            var rect = source.textureRect;
            int w = (int)rect.width, h = (int)rect.height;
            var rt = RenderTexture.GetTemporary(source.texture.width, source.texture.height, 0, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            Graphics.Blit(source.texture, rt);
            RenderTexture.active = rt;
            var copy = new Texture2D(w, h, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(rect.x, rect.y, w, h), 0, 0);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            var pixels = copy.GetPixels();
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color(pixels[i].r * tint.r, pixels[i].g * tint.g, pixels[i].b * tint.b, pixels[i].a);
            copy.SetPixels(pixels);
            copy.Apply();
            return Sprite.Create(copy, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), source.pixelsPerUnit);
        }
    }
}
