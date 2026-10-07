using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace OrbitRush
{

/// <summary>
/// The sniper's 3D scope view. While scoped the camera slips up to eye level and the picture splits in two:
///   • outside the scope, the normal world — blurred (a near-sharp depth-of-field volume keeps the scope itself crisp);
///   • inside the scope, a second camera (the zoom) rendered into a texture on a lens disc;
///   • a real 3D scope ring (dark metal with neon rims) and the gun's ribbed rail in front of the camera;
///   • a stylised neon reticle on top that turns red when an enemy is under it.
/// All geometry is generated in code and lives on layer 31 so the zoom camera doesn't render it.
/// </summary>
public class SniperScope : MonoBehaviour
{
    private const int ScopeLayer = 31;
    private const float LensDistance = 0.45f;       // metres in front of the camera
    private const float LensRadius = 0.17f;

    private Camera _main, _zoomCam;
    private RenderTexture _rt;
    private Transform _holder;
    private Material _lensMat;
    private Canvas _canvas;
    private RectTransform _reticle;
    private CanvasGroup _reticleGroup;
    private Image[] _tintParts;
    private Text _zoomLabel;
    private float _shown;
    private bool _built;

    static readonly Color Cyan = new Color(0.00f, 0.85f, 1.00f);
    static readonly Color Pink = new Color(0.98f, 0.02f, 0.62f);
    static readonly Color Red = new Color(1f, 0.15f, 0.2f);

    // ══════════════════════════════════════════════════════════════════════

    public void Init(Camera main)
    {
        _main = main;
        if (_built) return;
        _built = true;

        BuildZoomCamera();
        BuildGeometry();
        BuildReticle();
        SetVisible(false);
    }

    /// <summary>t: 0..1 progress of the scope transition. zoomFov: the field of view inside the scope.</summary>
    public void SetState(float t, float zoomFov, bool aimingAtEnemy)
    {
        if (!_built || _main == null) return;

        // The scope furniture only appears once the camera has nearly arrived at eye level
        float visual = Mathf.Clamp01((t - 0.55f) / 0.4f);
        _shown = Mathf.MoveTowards(_shown, visual, Time.unscaledDeltaTime * 6f);
        bool on = _shown > 0.001f;
        SetVisible(on);
        if (!on) return;

        // Slides in from slightly closer / larger, like raising the rifle to the eye
        float e = UiAnim.EaseOutBack(_shown);
        float s = Mathf.LerpUnclamped(1.35f, 1f, e);
        _holder.localScale = Vector3.one * (s / Mathf.Max(0.01f, _main.transform.lossyScale.x));

        _zoomCam.fieldOfView = zoomFov;
        _zoomCam.aspect = 1f;

        _reticleGroup.alpha = Mathf.Clamp01((_shown - 0.5f) * 2f);
        Color tint = aimingAtEnemy ? Red : Cyan;
        foreach (var img in _tintParts) { var c = tint; c.a = img.color.a; img.color = c; }
        _zoomLabel.text = $"ZOOM {_main.fieldOfView / Mathf.Max(1f, zoomFov):0.0}x";
    }

    private void SetVisible(bool on)
    {
        if (_holder != null && _holder.gameObject.activeSelf != on) _holder.gameObject.SetActive(on);
        if (_zoomCam != null && _zoomCam.enabled != on) _zoomCam.enabled = on;
        if (_canvas != null && _canvas.gameObject.activeSelf != on) _canvas.gameObject.SetActive(on);
    }

    void OnDestroy()
    {
        if (_rt != null) { _rt.Release(); Destroy(_rt); }
    }

    // ══════════════════════════════════════════════════════════════════════
    // Zoom camera (the picture inside the scope)
    // ══════════════════════════════════════════════════════════════════════

    private void BuildZoomCamera()
    {
        _rt = new RenderTexture(1024, 1024, 24, RenderTextureFormat.ARGB32) { name = "SniperScopeRT", antiAliasing = 1 };
        _rt.Create();

        var go = new GameObject("SniperZoomCamera");
        go.transform.SetParent(_main.transform, false);
        _zoomCam = go.AddComponent<Camera>();
        _zoomCam.CopyFrom(_main);
        _zoomCam.targetTexture = _rt;
        _zoomCam.depth = _main.depth - 5f;
        _zoomCam.cullingMask = _main.cullingMask & ~(1 << ScopeLayer);   // never draws the scope itself
        _zoomCam.nearClipPlane = 0.1f;
        _zoomCam.enabled = false;

        var data = go.GetComponent<UniversalAdditionalCameraData>();
        if (data == null) data = go.AddComponent<UniversalAdditionalCameraData>();
        data.renderPostProcessing = false;       // stays sharp: the scope-blur volume must not touch it
        data.antialiasing = AntialiasingMode.None;
        if (go.GetComponent<AudioListener>() != null) Destroy(go.GetComponent<AudioListener>());
    }

    // ══════════════════════════════════════════════════════════════════════
    // 3D parts
    // ══════════════════════════════════════════════════════════════════════

    private void BuildGeometry()
    {
        var holder = new GameObject("SniperScopeModel");
        holder.transform.SetParent(_main.transform, false);
        holder.transform.localPosition = Vector3.zero;
        holder.transform.localRotation = Quaternion.identity;
        _holder = holder.transform;

        var lit = Shader.Find("Universal Render Pipeline/Lit");
        var unlit = Shader.Find("Universal Render Pipeline/Unlit");

        Material metal = new Material(lit);
        metal.SetColor("_BaseColor", new Color(0.07f, 0.075f, 0.09f));
        metal.SetFloat("_Metallic", 0.85f); metal.SetFloat("_Smoothness", 0.55f); metal.SetFloat("_Cull", 0f);

        Material rim(Color c, float glow)
        {
            var m = new Material(lit);
            m.SetColor("_BaseColor", c * 0.4f);
            m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", c * glow); m.SetFloat("_Cull", 0f);
            return m;
        }
        Material cyanRim = rim(Cyan, 3.2f), pinkRim = rim(Pink, 2.6f);

        _lensMat = new Material(unlit);
        _lensMat.SetTexture("_BaseMap", _rt);
        _lensMat.SetColor("_BaseColor", Color.white);

        // Lens: a disc showing the zoom camera
        AddPart("Lens", Disc(LensRadius * 1.02f, 64), _lensMat, new Vector3(0f, 0f, LensDistance + 0.012f));

        // The scope: a thick dark outer ring with a bevel, a bright inner rim and a thin accent
        AddPart("RingOuter", Torus(LensRadius + 0.034f, 0.034f, 72, 18), metal, new Vector3(0f, 0f, LensDistance));
        AddPart("RingCollar", Torus(LensRadius + 0.010f, 0.016f, 72, 12), metal, new Vector3(0f, 0f, LensDistance - 0.024f));
        AddPart("RimCyan", Torus(LensRadius + 0.003f, 0.0036f, 72, 8), cyanRim, new Vector3(0f, 0f, LensDistance - 0.006f));
        AddPart("RimPink", Torus(LensRadius + 0.052f, 0.0032f, 72, 8), pinkRim, new Vector3(0f, 0f, LensDistance - 0.03f));

        // Four little scope-ring screws / marks
        for (int i = 0; i < 4; i++)
        {
            float a = (i * 90f + 45f) * Mathf.Deg2Rad;
            var p = Cube(new Vector3(0.010f, 0.010f, 0.010f), cyanRim, new Vector3(Mathf.Cos(a) * (LensRadius + 0.052f), Mathf.Sin(a) * (LensRadius + 0.052f), LensDistance - 0.036f));
            p.transform.SetParent(_holder, false);
        }

        // The gun's rail, ribbed, running away from the camera at the bottom of the view
        float railY = -0.185f;
        var rail = Cube(new Vector3(0.17f, 0.030f, 0.62f), metal, new Vector3(0f, railY, 0.46f));
        rail.transform.SetParent(_holder, false);
        for (int i = 0; i < 9; i++)
        {
            float z = 0.18f + i * 0.058f;
            var rib = Cube(new Vector3(0.19f, 0.014f, 0.016f), i % 3 == 0 ? cyanRim : metal, new Vector3(0f, railY + 0.020f, z));
            rib.transform.SetParent(_holder, false);
        }

        SetLayerRecursive(_holder.gameObject, ScopeLayer);
    }

    private GameObject AddPart(string name, Mesh mesh, Material mat, Vector3 pos)
    {
        var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(_holder, false);
        go.transform.localPosition = pos;
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        return go;
    }

    private GameObject Cube(Vector3 size, Material mat, Vector3 pos)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(go.GetComponent<Collider>());
        go.transform.localScale = size;
        go.transform.localPosition = pos;
        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        return go;
    }

    private static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform t in go.transform) SetLayerRecursive(t.gameObject, layer);
    }

    private static Mesh Disc(float radius, int segments)
    {
        var verts = new Vector3[segments + 1]; var uv = new Vector2[segments + 1]; var tris = new int[segments * 3];
        for (int i = 0; i < segments; i++)
        {
            float a = i * Mathf.PI * 2f / segments;
            float c = Mathf.Cos(a), s = Mathf.Sin(a);
            verts[i + 1] = new Vector3(c * radius, s * radius, 0f);
            uv[i + 1] = new Vector2(0.5f + c * 0.5f, 0.5f + s * 0.5f);
        }
        verts[0] = Vector3.zero; uv[0] = new Vector2(0.5f, 0.5f);
        for (int i = 0; i < segments; i++)
        {
            tris[i * 3] = 0; tris[i * 3 + 1] = (i + 1) % segments + 1; tris[i * 3 + 2] = i + 1;     // faces the camera (-z)
        }
        var m = new Mesh { name = "ScopeDisc", vertices = verts, uv = uv, triangles = tris };
        m.RecalculateNormals(); m.RecalculateBounds();
        return m;
    }

    private static Mesh Torus(float major, float minor, int segs, int sides)
    {
        var verts = new Vector3[(segs + 1) * (sides + 1)];
        var norms = new Vector3[verts.Length];
        var tris = new int[segs * sides * 6];
        int vi = 0;
        for (int i = 0; i <= segs; i++)
        {
            float a = i * Mathf.PI * 2f / segs;
            Vector3 center = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * major;
            for (int j = 0; j <= sides; j++)
            {
                float b = j * Mathf.PI * 2f / sides;
                Vector3 n = new Vector3(Mathf.Cos(a) * Mathf.Cos(b), Mathf.Sin(a) * Mathf.Cos(b), Mathf.Sin(b));
                verts[vi] = center + n * minor; norms[vi] = n; vi++;
            }
        }
        int ti = 0;
        for (int i = 0; i < segs; i++)
            for (int j = 0; j < sides; j++)
            {
                int a = i * (sides + 1) + j, b = (i + 1) * (sides + 1) + j;
                tris[ti++] = a; tris[ti++] = a + 1; tris[ti++] = b;
                tris[ti++] = b; tris[ti++] = a + 1; tris[ti++] = b + 1;
            }
        var m = new Mesh { name = "ScopeTorus", vertices = verts, normals = norms, triangles = tris };
        m.RecalculateBounds();
        return m;
    }

    // ══════════════════════════════════════════════════════════════════════
    // Reticle (UI on top of the lens)
    // ══════════════════════════════════════════════════════════════════════

    private void BuildReticle()
    {
        var go = new GameObject("SniperReticleCanvas");
        go.transform.SetParent(transform, false);
        _canvas = go.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 14;
        var cs = go.AddComponent<CanvasScaler>();
        cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        cs.referenceResolution = new Vector2(1920, 1080);
        cs.matchWidthOrHeight = 0.5f;

        _reticle = NewRect("Reticle", go.transform, Vector2.zero, new Vector2(1000f, 1000f));
        _reticleGroup = _reticle.gameObject.AddComponent<CanvasGroup>();
        _reticleGroup.blocksRaycasts = false;

        var tinted = new System.Collections.Generic.List<Image>();
        float R = LensRadius * 2164f;               // lens radius in canvas units at the reference 1080p (see LensDistance)

        // Vignette: the picture darkens toward the lens edge
        var vig = Part("Vignette", _reticle, Vector2.zero, new Vector2(R * 2.1f, R * 2.1f), Color.white);
        vig.sprite = VignetteSprite();
        vig.color = new Color(0f, 0.02f, 0.05f, 0.85f);

        // Cross hairs with a gap in the middle, fat posts at the ends
        float gap = 34f;
        foreach (int sign in new[] { -1, 1 })
        {
            tinted.Add(Part("H" + sign, _reticle, new Vector2(sign * (gap + (R - 120f - gap) * 0.5f), 0f), new Vector2(R - 120f - gap, 2.2f), Cyan));
            tinted.Add(Part("V" + sign, _reticle, new Vector2(0f, sign * (gap + (R - 120f - gap) * 0.5f)), new Vector2(2.2f, R - 120f - gap), Cyan));
            tinted.Add(Part("PostH" + sign, _reticle, new Vector2(sign * (R - 55f), 0f), new Vector2(110f, 8f), Cyan));
            tinted.Add(Part("PostV" + sign, _reticle, new Vector2(0f, sign * (R - 55f)), new Vector2(8f, 110f), Cyan));
        }

        // Mil-dots
        foreach (int sign in new[] { -1, 1 })
            for (int i = 1; i <= 4; i++)
            {
                float d = gap + i * 52f;
                tinted.Add(Part($"DotH{sign}_{i}", _reticle, new Vector2(sign * d, 0f), new Vector2(7f, 7f), Cyan));
                tinted.Add(Part($"DotV{sign}_{i}", _reticle, new Vector2(0f, sign * d), new Vector2(7f, 7f), Cyan));
            }

        // Centre: ring + hot-pink dot, outer thin ring with four notches
        var ringSprite = RingSprite();
        var cr = Part("CenterRing", _reticle, Vector2.zero, new Vector2(46f, 46f), Cyan); cr.sprite = ringSprite; tinted.Add(cr);
        var dot = Part("CenterDot", _reticle, Vector2.zero, new Vector2(7f, 7f), Pink); dot.sprite = DotSprite();
        var outer = Part("OuterRing", _reticle, Vector2.zero, new Vector2(R * 2f - 4f, R * 2f - 4f), Cyan); outer.sprite = ringSprite;
        outer.color = new Color(Cyan.r, Cyan.g, Cyan.b, 0.55f); tinted.Add(outer);
        for (int i = 0; i < 4; i++)
        {
            var notch = Part("Notch" + i, _reticle, Vector2.zero, new Vector2(12f, 30f), Pink);
            float a = i * 90f + 45f;
            notch.rectTransform.anchoredPosition = Quaternion.Euler(0, 0, a) * new Vector2(0f, R - 8f);
            notch.rectTransform.localRotation = Quaternion.Euler(0, 0, a);
        }

        // Zoom read-out under the lens
        var txt = new GameObject("Zoom", typeof(RectTransform)).AddComponent<Text>();
        txt.rectTransform.SetParent(_reticle, false);
        txt.rectTransform.anchoredPosition = new Vector2(0f, -R + 46f);
        txt.rectTransform.sizeDelta = new Vector2(400f, 40f);
        txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txt.fontSize = 24; txt.fontStyle = FontStyle.BoldAndItalic; txt.alignment = TextAnchor.MiddleCenter;
        txt.color = new Color(Cyan.r, Cyan.g, Cyan.b, 0.9f); txt.raycastTarget = false;
        _zoomLabel = txt;

        _tintParts = tinted.ToArray();
    }

    private static RectTransform NewRect(string name, Transform parent, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        return rt;
    }

    private static Image Part(string name, Transform parent, Vector2 pos, Vector2 size, Color color)
    {
        var rt = NewRect(name, parent, pos, size);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = color; img.raycastTarget = false;
        return img;
    }

    // ── generated sprites ─────────────────────────────────────────────────

    private static Sprite _ring, _dot, _vignette;

    private static Sprite Radial(int n, System.Func<float, float> alpha, string name)
        => UiFactory.RadialSprite(n, alpha, name, clampToCircle: false);

    private static Sprite RingSprite()
    {
        if (_ring == null) _ring = Radial(256, d => Mathf.Clamp01(1f - Mathf.Abs(d - 0.97f) / 0.025f), "ScopeRing");
        return _ring;
    }
    private static Sprite DotSprite()
    {
        if (_dot == null) _dot = Radial(64, d => Mathf.Clamp01((1f - d) * 8f), "ScopeDot");
        return _dot;
    }
    private static Sprite VignetteSprite()
    {
        // transparent in the middle, darker toward the lens edge, nothing outside the lens
        if (_vignette == null) _vignette = Radial(256, d => d > 1f ? 0f : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.62f, 0.98f, d)), "ScopeVignette");
        return _vignette;
    }
}
}
