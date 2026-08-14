using MelonLoader;
using UnityEngine;

namespace ArcadeParadiseFreePlayMod
{
    public partial class Core
    {
        private static void ApplyFreePlayTexture(GameObject root)
        {
            string freePlayDir = Path.Combine(
                Path.GetDirectoryName(typeof(Core).Assembly.Location) ?? ".",
                "FreePlay");
            string pngPath = Path.Combine(freePlayDir, "cabinet.png");

            if (!File.Exists(pngPath))
            {
                MelonLogger.Msg($"[Core] No cabinet.png at {pngPath}: cabinet keeps GraffitiBallz textures");
                return;
            }

            var tex = new Texture2D(2, 2);
            UnityEngine.ImageConversion.LoadImage(tex, File.ReadAllBytes(pngPath));
            MelonLogger.Msg($"[Core] Loaded cabinet texture: {tex.width}x{tex.height}");

            Transform body = root.transform.Find("ArcadeMachine_GraffitiBallz");
            if (body == null)
            {
                foreach (Transform child in root.transform)
                {
                    if (child.GetComponent<MeshRenderer>() != null) { body = child; break; }
                }
                if (body == null)
                {
                    MelonLogger.Warning("[Core] No suitable child with MeshRenderer found: texture not swapped");
                    return;
                }
            }

            var mr = body.GetComponent<MeshRenderer>();
            if (mr == null || mr.sharedMaterial == null)
            {
                MelonLogger.Warning("[Core] MeshRenderer or sharedMaterial missing on body: texture not swapped");
                return;
            }

            var sourceMaterial = mr.sharedMaterial;
            var freePlayMat = new Material(sourceMaterial.shader);
            freePlayMat.name = "FreePlayCabinetMat";
            freePlayMat.CopyPropertiesFromMaterial(sourceMaterial);
            freePlayMat.mainTexture = tex;
            ApplyFinalCabinetSurface(freePlayMat);
            mr.material = freePlayMat;

            MelonLogger.Msg("[Core] Swapped texture on main cabinet body");
        }

        private static readonly string[] InheritedSurfaceMapProperties =
        {
            "_BumpMap",
            "_NormalMap",
            "_DetailNormalMap",
            "_DetailAlbedoMap",
            "_DetailMask",
            "_OcclusionMap",
            "_MetallicGlossMap",
            "_SpecGlossMap",
            "_RMA",
            "_MaskMap",
            "_EmissionMap",
            "_OverlayMap",
            "_DecalMap",
            "_ParallaxMap",
            "_HeightMap"
        };

        private static Texture2D _genericFlatNormal;
        private static Texture2D _genericRma;
        private static Texture2D _genericMetallicGloss;
        private static Texture2D _genericSpecGloss;
        private static Texture2D _genericWhite;

        private static void ApplyFinalCabinetSurface(Material material)
        {
            if (material == null)
                return;

            // the original RMAE map is the source of the Graffiti Ballz ghost graphic.
            // always remove inherited surface maps, then use only solid, artwork-free fallback maps for the custom cabinet.
            foreach (string propertyName in InheritedSurfaceMapProperties)
                ClearMaterialTexture(material, propertyName);

            ApplyGenericCabinetSurface(material);
            MelonLogger.Msg("[Core] Applied final custom cabinet surface (inherited maps locked out)");
        }

        private static void ClearMaterialTexture(Material material, string propertyName)
        {
            if (material != null && material.HasProperty(propertyName))
                material.SetTexture(propertyName, null);
        }

        private static void ApplyGenericCabinetSurface(Material material)
        {
            if (material == null)
                return;

            // a flat normal is (0.5, 0.5, 1.0):
            //      it adds no fake lettering or bumps but still lets the cabinets actual mesh normals receive lighting
            var flatNormal = GetOrCreateSolidTexture(
                ref _genericFlatNormal,
                "FreePlayGenericFlatNormal",
                new Color32(128, 128, 255, 255));
            SetMaterialTexture(material, "_BumpMap", flatNormal);
            SetMaterialTexture(material, "_NormalMap", flatNormal);

            // neutral packed fallbacks contain no artwork. 
            // these values add back in some smoothness to match vanilla cabinets
            var rma = GetOrCreateSolidTexture(
                ref _genericRma,
                "FreePlayGenericRMA",
                new Color32(77, 0, 255, 0));
            SetMaterialTexture(material, "_RMA", rma);

            // shader binds the source RMAE texture to _MetallicGlossMap where:
            // R=roughness, G=metallic, B=ambient occlusion, A=emission.
            // use a slightly glossy painted-plastic response with low/moderate roughness, a small metallic contribution, full AO, and no emission.
            var metallicGloss = GetOrCreateSolidTexture(
                ref _genericMetallicGloss,
                "FreePlayGenericMetallicGloss",
                new Color32(64, 20, 255, 0));
            SetMaterialTexture(material, "_MetallicGlossMap", metallicGloss);

            var specGloss = GetOrCreateSolidTexture(
                ref _genericSpecGloss,
                "FreePlayGenericSpecGloss",
                new Color32(128, 128, 128, 179));
            SetMaterialTexture(material, "_SpecGlossMap", specGloss);

            var white = GetOrCreateSolidTexture(
                ref _genericWhite,
                "FreePlayGenericWhite",
                new Color32(255, 255, 255, 255));
            SetMaterialTexture(material, "_OcclusionMap", white);

            // support common scalar names if this shader exposes them. 
            // Unsupported properties are simply ignored, so the custom RMA shader remains safe.
            SetMaterialFloat(material, "_BumpScale", 1f);
            SetMaterialFloat(material, "_Metallic", 0.08f);
            SetMaterialFloat(material, "_Smoothness", 0.75f);
            SetMaterialFloat(material, "_Glossiness", 0.75f);
            SetMaterialFloat(material, "_Roughness", 0.25f);
            SetMaterialFloat(material, "_SpecularHighlights", 1f);

            MelonLogger.Msg("[Core] Applied generic flat-normal and moderately-smooth cabinet surface");
        }

        private static void SetMaterialTexture(Material material, string propertyName, Texture texture)
        {
            if (material != null && material.HasProperty(propertyName))
                material.SetTexture(propertyName, texture);
        }

        private static void SetMaterialFloat(Material material, string propertyName, float value)
        {
            if (material != null && material.HasProperty(propertyName))
                material.SetFloat(propertyName, value);
        }

        private static Texture2D GetOrCreateSolidTexture(
            ref Texture2D cached,
            string name,
            Color32 color)
        {
            if (cached != null)
                return cached;

            cached = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            cached.name = name;
            cached.wrapMode = TextureWrapMode.Repeat;
            cached.filterMode = FilterMode.Bilinear;
            cached.hideFlags = HideFlags.DontUnloadUnusedAsset;
            cached.SetPixels32(new[] { color, color, color, color });
            cached.Apply(false, true);
            return cached;
        }

        private static void MakeMaterialInvisible(Material material)
        {
            if (material == null)
                return;

            if (material.HasProperty("_MainTex"))
                material.SetTexture("_MainTex", null);
            if (material.HasProperty("_BaseMap"))
                material.SetTexture("_BaseMap", null);
            if (material.HasProperty("_DetailAlbedoMap"))
                material.SetTexture("_DetailAlbedoMap", null);
            if (material.HasProperty("_EmissionMap"))
                material.SetTexture("_EmissionMap", null);

            if (material.HasProperty("_BaseColor"))
            {
                var color = material.GetColor("_BaseColor");
                color.a = 0f;
                material.SetColor("_BaseColor", color);
            }
            if (material.HasProperty("_Color"))
            {
                var color = material.GetColor("_Color");
                color.a = 0f;
                material.SetColor("_Color", color);
            }
        }

        /// <summary>
        /// The cabinet mesh has a transparent original glass material as a second submesh.
        /// Neutralise only that borrowed glass overlay
        /// </summary>
        private static void NeutraliseBorrowedGlassOverlay(GameObject root)
        {
            if (root == null)
                return;

            try
            {
                Transform body = root.transform.Find("ArcadeMachine_GraffitiBallz");
                var renderers = body != null
                    ? body.GetComponentsInChildren<MeshRenderer>(true)
                    : root.GetComponentsInChildren<MeshRenderer>(true);

                foreach (var renderer in renderers)
                {
                    if (renderer == null)
                        continue;

                    var sharedMaterials = renderer.sharedMaterials;
                    bool hasGlassMaterial = false;
                    for (int i = 0; i < sharedMaterials.Length; i++)
                    {
                        var sharedMaterial = sharedMaterials[i];
                        if (sharedMaterial != null &&
                            sharedMaterial.name.IndexOf("Glass", StringComparison.OrdinalIgnoreCase) >= 0 &&
                            !sharedMaterial.name.StartsWith("FreePlayHiddenBorrowedGlass", StringComparison.OrdinalIgnoreCase))
                        {
                            hasGlassMaterial = true;
                            break;
                        }
                    }

                    if (!hasGlassMaterial)
                        continue;

                    // only instantiate a material array after finding a matching glass slot
                    var materials = renderer.materials;
                    bool changed = false;
                    for (int i = 0; i < materials.Length; i++)
                    {
                        var material = materials[i];
                        if (material == null ||
                            material.name.IndexOf("Glass", StringComparison.OrdinalIgnoreCase) < 0 ||
                            material.name.StartsWith("FreePlayHiddenBorrowedGlass", StringComparison.OrdinalIgnoreCase))
                            continue;

                        try
                        {
                            var replacement = new Material(material);
                            replacement.name = "FreePlayHiddenBorrowedGlass";
                            MakeMaterialInvisible(replacement);
                            materials[i] = replacement;
                            changed = true;

                            MelonLogger.Msg(
                                $"[Core] Neutralised borrowed glass overlay: " +
                                $"renderer={GetRelativeTransformPath(root.transform, renderer.transform)}, slot={i}, " +
                                $"sourceMaterial={material.name}");
                        }
                        catch (Exception ex)
                        {
                            MelonLogger.Warning(
                                $"[Core] Could not neutralise glass material on " +
                                $"{GetRelativeTransformPath(root.transform, renderer.transform)} slot {i}: {ex.Message}");
                        }
                    }

                    if (changed)
                        renderer.materials = materials;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Core] Failed to neutralise borrowed glass overlay: {ex.Message}");
            }
        }

        private static string GetRelativeTransformPath(Transform root, Transform leaf)
        {
            if (root == null || leaf == null)
                return "<null>";

            string path = leaf.name;
            var current = leaf.parent;
            while (current != null && current != root.transform)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }

            return path;
        }
    }
}
