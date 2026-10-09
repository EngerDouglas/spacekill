using OrbitRush;
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Models exported from Blender (any "/Models/" or "/Planets/" folder) lose emission and
/// arrive with whatever default shader the importer picks. This converts their materials
/// to URP Lit and restores the glow, so they look like they do in Blender without
/// hand-fixing each material. Re-import a model (right-click → Reimport) to re-run this.
///
/// Planet materials read their emission / roughness / metallic from
/// Assets/Editor/OrbitRushMaterials.json (exported from the Blender planets file).
/// </summary>
public class OrbitRushModelPostprocessor : AssetPostprocessor
{
    [Serializable] class MatEntry
    {
        public string name;
        public float[] emit;
        public float strength, rough, metal, alpha;
    }
    [Serializable] class MatFile { public MatEntry[] items; }

    private const string MaterialsJson = "Assets/Editor/OrbitRushMaterials.json";
    private static MatFile _cache;

    static MatEntry FindEntry(string materialName)
    {
        if (_cache == null)
        {
            try { _cache = JsonUtility.FromJson<MatFile>(File.ReadAllText(MaterialsJson)); }
            catch (Exception e) { Debug.LogWarning($"[OrbitRush] Could not read {MaterialsJson}: {e.Message}"); }
            if (_cache == null) _cache = new MatFile { items = new MatEntry[0] };
        }
        // Blender adds ".001" to duplicate material names — look the data up under the base name.
        string key = System.Text.RegularExpressions.Regex.Replace(materialName, @"\.\d{3}$", "");
        foreach (var e in _cache.items) if (e.name == key) return e;
        return null;
    }

    // Bump to force a reimport of every model after changing material rules.
    public override uint GetVersion() => 6;

    bool IsPlanet => assetPath.Replace('\\', '/').Contains("/Planets/");
    bool IsWeapon => assetPath.Replace('\\', '/').Contains("/Weapons/");
    bool IsProp => assetPath.Replace('\\', '/').Contains("/Models/Props/");   // vending machine, coins
    bool IsOurModel => assetPath.Replace('\\', '/').Contains("/Models/") || IsPlanet || IsWeapon;

    // Big joined meshes + runtime MeshColliders: keep meshes readable (required for colliders
    // created at runtime in a build), 32-bit indices for >65k verts, and drop Blender's cameras/lights.
    bool IsAnim => assetPath.Contains("/Resources/Anims/");
    bool IsRig => assetPath.EndsWith("Rig.fbx");

    // Mixamo animations → Humanoid clips (retargetable onto any humanoid avatar); the rigged characters get their own Humanoid avatar.
    void OnPreprocessModel()
    {
        if (IsAnim || IsRig)
        {
            var mi = (ModelImporter)assetImporter;
            mi.animationType = ModelImporterAnimationType.Human;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            mi.importAnimation = IsAnim;
            mi.materialImportMode = IsAnim ? ModelImporterMaterialImportMode.None : ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            mi.importCameras = false;
            mi.importLights = false;
            mi.isReadable = false;
            return;
        }
        if (!IsPlanet && !IsWeapon) return;
        var importer = (ModelImporter)assetImporter;
        importer.isReadable = true;   // weapons: the muzzle position is measured from the mesh at runtime
        importer.meshCompression = ModelImporterMeshCompression.Off;
        importer.indexFormat = ModelImporterIndexFormat.UInt32;
        importer.generateSecondaryUV = false;
        importer.importCameras = false;
        importer.importLights = false;
    }

    static readonly string[] LoopingClips = { "Walk", "WalkLoop", "WalkBackPistol", "SwordWalk", "FallIdle", "Falling", "WalkLeftTurn", "RunGun", "RunLeftGun", "RunRightGun", "RunBack", "RifleRun", "PistolRun", "Running", "RunTurnRight", "RunStairs", "CrouchRun", "ZombieRun" };

    void OnPreprocessAnimation()
    {
        if (!IsAnim) return;
        var mi = (ModelImporter)assetImporter;
        string clipName = Path.GetFileNameWithoutExtension(assetPath);
        var clips = mi.defaultClipAnimations;
        if (clips == null || clips.Length == 0) return;
        var clip = clips[0];
        clip.name = clipName;
        clip.loopTime = Array.IndexOf(LoopingClips, clipName) >= 0;
        clip.loopPose = clip.loopTime;
        // Bake the root so clips play in place (the controller moves the body, not the animation).
        clip.lockRootRotation = true; clip.keepOriginalOrientation = true;
        clip.lockRootHeightY = true; clip.keepOriginalPositionY = true;
        clip.lockRootPositionXZ = true; clip.keepOriginalPositionXZ = true;
        mi.clipAnimations = new[] { clip };
    }

    // HUD panel art (Resources/HudArt): crisp UI sprites, no compression/mips
    void OnPreprocessTexture_HudArt()
    {
        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.filterMode = FilterMode.Bilinear;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.maxTextureSize = 2048;
        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        settings.spritePixelsPerUnit = 100;
        importer.SetTextureSettings(settings);
    }

    // Panoramic sky texture (Resources/Sky): wraps horizontally, no seam, full resolution, good compression.
    void OnPreprocessTexture()
    {
        // Drop-ship PBR maps (Resources/Ship): the normal map is read as a normal map, the data maps are linear
        if (assetPath.Replace('\\', '/').Contains("/Resources/Ship/") || assetPath.Replace('\\', '/').Contains("/Resources/Pod/"))
        {
            var ti = (TextureImporter)assetImporter;
            string file = Path.GetFileNameWithoutExtension(assetPath);
            if (file.EndsWith("Normal")) ti.textureType = TextureImporterType.NormalMap;
            else if (file.EndsWith("MetalSmooth") || file.EndsWith("AO")) ti.sRGBTexture = false;
            if (file == "Explosion") { ti.alphaIsTransparency = true; ti.wrapMode = TextureWrapMode.Clamp; ti.mipmapEnabled = false; ti.maxTextureSize = 2048; ti.textureCompression = TextureImporterCompression.CompressedHQ; return; }
            ti.maxTextureSize = 2048;
            ti.mipmapEnabled = true;
            ti.anisoLevel = 8;
            ti.textureCompression = TextureImporterCompression.Compressed;
            return;
        }
        if (assetPath.Contains("/Resources/HudArt/")) { OnPreprocessTexture_HudArt(); return; }
        if (assetPath.Contains("/Planets/GalaxyTextures/"))
        {
            var ti = (TextureImporter)assetImporter;
            ti.maxTextureSize = 1024;
            ti.mipmapEnabled = true;
            ti.textureCompression = TextureImporterCompression.Compressed;
            return;
        }
        if (!assetPath.Replace('\\', '/').Contains("/Sky/")) return;
        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Default;
        importer.textureShape = TextureImporterShape.Texture2D;
        importer.wrapModeU = TextureWrapMode.Repeat;
        importer.wrapModeV = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Bilinear;
        importer.mipmapEnabled = true;
        importer.maxTextureSize = 4096;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.sRGBTexture = true;
    }

    void OnPostprocessMaterial(Material material)
    {
        if (!IsOurModel) return;

        var lit = Shader.Find("Universal Render Pipeline/Lit");
        if (lit == null) return;

        // Read the imported color before the shader (and its property names) change.
        Color color = Color.white;
        if (material.HasProperty("_BaseColor")) color = material.GetColor("_BaseColor");
        else if (material.HasProperty("_Color")) color = material.GetColor("_Color");

        Texture mainTex = material.mainTexture;     // keep the albedo map across the shader swap
        if (material.shader != lit) material.shader = lit;
        material.SetColor("_BaseColor", color);
        if (mainTex != null) { material.SetTexture("_BaseMap", mainTex); material.SetColor("_BaseColor", Color.white); }

        string n = material.name;

        // Character materials (hand-tuned)
        if (n.Contains("M_Neon_Pink"))      SetGlow(material, new Color(1f, 0.05f, 0.55f), 2.2f);
        else if (n.Contains("M_Neon_Cyan")) SetGlow(material, new Color(0f, 0.9f, 1f), 3f);
        else if (n.Contains("M_HUD_White")) SetGlow(material, new Color(0.85f, 0.9f, 1f), 1.2f);
        else if (n.Contains("M_Enemy_Glow")) SetGlow(material, new Color(1f, 0.1f, 0.04f), 3.5f);

        // Soldier / Drone enemy models (M_En_*)
        else if (n.Contains("M_En_Pink"))  SetGlow(material, new Color(1f, 0.05f, 0.55f), 3f);
        else if (n.Contains("M_En_Cyan"))  SetGlow(material, new Color(0f, 0.9f, 1f), 3f);

        // Drone DL-3 (generic Blender material names, so keyed on the asset)
        if (assetPath.Contains("OrbitRush_DroneDL3"))
        {
            if (n.StartsWith("emision")) SetGlow(material, color, 3.2f);
            else if (n == "glass" || n == "sklo_" || n == "mgla")
            {
                material.SetFloat("_Smoothness", 0.92f); material.SetFloat("_Metallic", 0.2f);
                SetTransparent(material, 0.45f);
            }
            else { material.SetFloat("_Metallic", 0.35f); material.SetFloat("_Smoothness", 0.55f); }
        }

        // Enemy models from the asset packs (generic "Material" names): the FBX carries a white emission colour
        // but Unity drops the emission map, so the whole body glowed solid white. Keep emission only with a map.
        if (assetPath.Replace('\\', '/').Contains("/Models/Enemies/"))
        {
            var emap = material.HasProperty("_EmissionMap") ? material.GetTexture("_EmissionMap") : null;
            if (emap == null)
            {
                material.SetColor("_EmissionColor", Color.black);
                material.DisableKeyword("_EMISSION");
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            }
        }

        if (n.Contains("Visor") || n.Contains("M_En_Glass"))
        {
            material.SetFloat("_Smoothness", 0.95f);
            material.SetFloat("_Metallic", 0.6f);
        }

        // Planet / weapon materials (data from Blender)
        var entry = FindEntry(n);
        if (entry != null && (IsPlanet || IsWeapon || IsProp))
        {
            material.SetFloat("_Smoothness", 1f - Mathf.Clamp01(entry.rough));
            material.SetFloat("_Metallic", Mathf.Clamp01(entry.metal));

            if (entry.strength > 0f && entry.emit != null && entry.emit.Length >= 3)
            {
                // Blender strengths (up to 18) are far brighter than URP wants; scale into a sane HDR range.
                float intensity = entry.strength >= 1f ? Mathf.Clamp(entry.strength * 0.35f, 1f, 5f) : entry.strength;
                SetGlow(material, new Color(entry.emit[0], entry.emit[1], entry.emit[2]), intensity);
            }

            // Glass / smoke / fireball materials carry alpha < 1.
            if (entry.alpha < 0.99f) SetTransparent(material, entry.alpha);
        }
    }

    /// <summary>Configures a URP Lit material as alpha-blended with the given opacity.</summary>
    static void SetTransparent(Material m, float alpha)
    {
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", 0f);
        m.SetOverrideTag("RenderType", "Transparent");
        m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetInt("_ZWrite", 0);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        Color c = m.GetColor("_BaseColor");
        c.a = alpha;
        m.SetColor("_BaseColor", c);
    }

    static void SetGlow(Material m, Color color, float intensity)
    {
        m.EnableKeyword("_EMISSION");
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
        m.SetColor("_EmissionColor", color * intensity);
    }
}
