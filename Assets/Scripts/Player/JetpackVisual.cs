using System.Collections.Generic;
using UnityEngine;

namespace OrbitRush
{

/// <summary>
/// The jetpack on the character's back (Resources/Models/OrbitRush_Jetpack): glows while charging, burns twin
/// flames under thrust, leaves a trail in open space and draws the dotted launch trajectory while you aim.
/// </summary>
public class JetpackVisual : MonoBehaviour
{
    public const string ModelPath = "Models/OrbitRush_Jetpack";

    [Header("Fit (player-local metres at the character's scale 1)")]
    [Tooltip("Pack centre relative to the chest bone, behind the back.")]
    public Vector3 backOffset = new Vector3(0f, -0.04f, -0.2f);
    [Tooltip("World width of the pack in metres (the model is ~0.71 m wide natively).")]
    public float worldWidth = 0.36f;

    static readonly Color Cyan = new Color(0f, 0.85f, 1f);
    static readonly Color Pink = new Color(0.98f, 0.02f, 0.62f);

    private PlayerController _controller;
    private PlayerStats _stats;
    private Transform _pack;
    private float _packScale;                    // world scale applied to the model
    private Transform _flameL, _flameR;
    private Light _lightL, _lightR, _chargeLight;
    private TrailRenderer _trailL, _trailR;
    private Material _flameMat, _metalMat, _glowMat;
    private LineRenderer _line;
    private readonly List<Vector3> _path = new List<Vector3>();
    private float _thrust, _flicker;

    public bool Ready => _pack != null;

    public void Init(Animator animator)
    {
        _controller = GetComponent<PlayerController>();
        _stats = GetComponent<PlayerStats>();
        if (animator == null || !animator.isHuman) return;

        var chest = animator.GetBoneTransform(HumanBodyBones.UpperChest);
        if (chest == null) chest = animator.GetBoneTransform(HumanBodyBones.Chest);
        if (chest == null) chest = animator.GetBoneTransform(HumanBodyBones.Spine);
        var prefab = Resources.Load<GameObject>(ModelPath);
        if (chest == null || prefab == null) { Debug.LogWarning("[JetpackVisual] chest bone or model missing."); return; }

        // An unscaled holder (world scale 1) sits on the chest bone, so flames / lights / trails are in real metres whatever
        // scale the rig and the FBX import carry; the mesh is a child that is measured and sized to the wanted width.
        var holder = new GameObject("Jetpack").transform;
        float s = Mathf.Max(0.0001f, transform.lossyScale.x);
        holder.SetPositionAndRotation(chest.position + transform.TransformDirection(backOffset) * s,
                                      transform.rotation * Quaternion.Euler(0f, 180f, 0f));     // control panel facing outward, away from the back
        holder.SetParent(chest, true);
        holder.localScale = Vector3.one / Mathf.Max(0.0001f, chest.lossyScale.x);
        _pack = holder;

        var mesh = Instantiate(prefab, holder);
        mesh.name = "Model";
        mesh.transform.localPosition = Vector3.zero; mesh.transform.localRotation = Quaternion.identity; mesh.transform.localScale = Vector3.one;
        foreach (var c in mesh.GetComponentsInChildren<Collider>(true)) Destroy(c);
        foreach (var l in mesh.GetComponentsInChildren<Light>(true)) Destroy(l);
        foreach (var cam in mesh.GetComponentsInChildren<Camera>(true)) Destroy(cam);

        Bounds raw = MeshBoundsInHolder(mesh, holder);
        if (raw.size.x < 1e-5f) { Debug.LogWarning("[JetpackVisual] model has no measurable mesh."); Destroy(holder.gameObject); _pack = null; return; }
        float f = worldWidth / raw.size.x;
        mesh.transform.localScale = Vector3.one * f;
        Bounds fit = MeshBoundsInHolder(mesh, holder);
        mesh.transform.localPosition = -fit.center;                          // centred on the holder
        fit = MeshBoundsInHolder(mesh, holder);
        _nozzle = new Vector3(fit.size.x * 0.365f, fit.min.y + fit.size.y * 0.05f, fit.center.z);
        _packScale = f;

        BuildMaterials();
        foreach (var r in mesh.GetComponentsInChildren<Renderer>(true))
        {
            r.sharedMaterial = _metalMat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }
        BuildFlames();
        BuildLine();
        if (GetComponent<SonicWarpFx>() == null) gameObject.AddComponent<SonicWarpFx>();
        StartCoroutine(AnnounceEquipped());
    }

    /// <summary>The player always starts the match wearing the jetpack: say so once the game is actually running, with a short flame puff.</summary>
    private System.Collections.IEnumerator AnnounceEquipped()
    {
        while (MainMenu.BlocksStart || HUD.Instance == null) yield return new WaitForSeconds(0.5f);
        yield return new WaitForSeconds(1.5f);
        HUD.Instance.ShowEvent("JETPACK EQUIPADO — MANTÉN CTRL, APUNTA A UN PLANETA Y SUELTA", 5f);
        _thrust = 1f;                                                 // flames flare for a moment
        for (float t = 0f; t < 0.6f; t += Time.deltaTime) { _controllerFlash = true; yield return null; }
        _controllerFlash = false;
    }
    private bool _controllerFlash;

    private Vector3 _nozzle;

    /// <summary>Bounds of every mesh under <paramref name="root"/>, expressed in the holder's local space (metres, holder scale is 1).</summary>
    private static Bounds MeshBoundsInHolder(GameObject root, Transform holder)
    {
        bool any = false; Bounds b = new Bounds(Vector3.zero, Vector3.zero);
        foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            var mb = mf.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 local = holder.InverseTransformPoint(mf.transform.TransformPoint(corner));
                if (!any) { b = new Bounds(local, Vector3.zero); any = true; } else b.Encapsulate(local);
            }
        }
        foreach (var sk in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var wb = sk.bounds;
            if (!any) { b = new Bounds(holder.InverseTransformPoint(wb.center), Vector3.zero); any = true; }
            b.Encapsulate(holder.InverseTransformPoint(wb.min)); b.Encapsulate(holder.InverseTransformPoint(wb.max));
        }
        return b;
    }

    // ── Construction ──────────────────────────────────────────────────────

    private void BuildMaterials()
    {
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        _metalMat = new Material(lit) { name = "Jetpack_Metal" };
        _metalMat.SetColor("_BaseColor", new Color(0.13f, 0.14f, 0.18f));
        _metalMat.SetFloat("_Metallic", 0.85f);
        _metalMat.SetFloat("_Smoothness", 0.55f);
        _metalMat.EnableKeyword("_EMISSION");
        _metalMat.SetColor("_EmissionColor", Color.black);

        var unlit = Shader.Find("Universal Render Pipeline/Unlit");
        _flameMat = new Material(unlit) { name = "Jetpack_Flame" };
        _flameMat.SetColor("_BaseColor", Color.white);
        _flameMat.SetFloat("_Surface", 1f);                 // transparent
        _flameMat.SetFloat("_Blend", 1f);                   // additive
        _flameMat.SetOverrideTag("RenderType", "Transparent");
        _flameMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        _flameMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
        _flameMat.SetInt("_ZWrite", 0);
        _flameMat.renderQueue = 3000;
        _flameMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        _flameMat.SetColor("_BaseColor", new Color(0.3f, 0.9f, 1f, 0.9f));

        _glowMat = _flameMat;
    }

    private Transform MakeFlame(string name, float side)
    {
        var holder = new GameObject(name).transform;
        holder.SetParent(_pack, false);
        holder.localPosition = new Vector3(side * _nozzle.x, _nozzle.y, _nozzle.z);

        var cone = new GameObject("Cone", typeof(MeshFilter), typeof(MeshRenderer));
        cone.transform.SetParent(holder, false);
        cone.GetComponent<MeshFilter>().sharedMesh = ConeMesh(0.085f, 0.55f, 14);
        var mr = cone.GetComponent<MeshRenderer>();
        mr.sharedMaterial = _flameMat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        var lightGo = new GameObject("Light");
        lightGo.transform.SetParent(holder, false);
        lightGo.transform.localPosition = new Vector3(0f, -0.25f, 0f);
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = Cyan;
        light.range = 6f;
        light.intensity = 0f;
        light.shadows = LightShadows.None;
        if (side < 0) _lightL = light; else _lightR = light;

        var trailGo = new GameObject("Trail");
        trailGo.transform.SetParent(holder, false);
        trailGo.transform.localPosition = new Vector3(0f, -0.1f, 0f);
        var trail = trailGo.AddComponent<TrailRenderer>();
        trail.time = 0.9f;
        trail.minVertexDistance = 0.15f;
        trail.widthMultiplier = 0.07f;
        trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
        trail.material = new Material(Shader.Find("Sprites/Default"));
        var grad = new Gradient();
        grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Cyan, 0.3f), new GradientColorKey(Pink, 1f) },
                     new[] { new GradientAlphaKey(0.55f, 0f), new GradientAlphaKey(0f, 1f) });
        trail.colorGradient = grad;
        trail.emitting = false;
        trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        if (side < 0) _trailL = trail; else _trailR = trail;

        holder.gameObject.SetActive(true);
        cone.SetActive(false);
        return cone.transform;
    }

    private void BuildFlames()
    {
        _flameL = MakeFlame("NozzleL", -1f);
        _flameR = MakeFlame("NozzleR", 1f);

        var cl = new GameObject("ChargeGlow");
        cl.transform.SetParent(_pack, false);
        _chargeLight = cl.AddComponent<Light>();
        _chargeLight.type = LightType.Point;
        _chargeLight.color = Pink;
        _chargeLight.range = 4f;
        _chargeLight.intensity = 0f;
        _chargeLight.shadows = LightShadows.None;
    }

    private void BuildLine()
    {
        var go = new GameObject("JetpackTrajectory");
        go.transform.SetParent(null);
        _line = go.AddComponent<LineRenderer>();
        _line.useWorldSpace = true;
        _line.textureMode = LineTextureMode.Tile;
        _line.alignment = LineAlignment.View;
        _line.widthMultiplier = 0.07f;
        _line.numCapVertices = 2;
        _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _line.receiveShadows = false;

        var tex = new Texture2D(16, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
        for (int i = 0; i < 16; i++) tex.SetPixel(i, 0, i < 9 ? Color.white : new Color(1, 1, 1, 0));
        tex.Apply();
        var mat = new Material(Shader.Find("Sprites/Default")) { mainTexture = tex };
        _line.material = mat;
        _line.enabled = false;
    }

    private static Mesh ConeMesh(float radius, float length, int segs)
    {
        var verts = new List<Vector3> { Vector3.zero, new Vector3(0f, -length, 0f) };
        var tris = new List<int>();
        var cols = new List<Color>();
        for (int i = 0; i < segs; i++)
        {
            float a = i * Mathf.PI * 2f / segs;
            verts.Add(new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius));
        }
        for (int i = 0; i < segs; i++)
        {
            int a = 2 + i, b = 2 + (i + 1) % segs;
            tris.AddRange(new[] { 0, b, a });          // top cap (faces up)
            tris.AddRange(new[] { 1, a, b });          // outer cone to the tip
        }
        var m = new Mesh { name = "FlameCone" };
        m.SetVertices(verts); m.SetTriangles(tris, 0);
        var uv = new Vector2[verts.Count];
        uv[1] = new Vector2(0.5f, 1f);
        m.uv = uv;
        m.RecalculateNormals(); m.RecalculateBounds();
        return m;
    }

    // ── Per-frame ─────────────────────────────────────────────────────────

    void LateUpdate()
    {
        if (_pack == null || _controller == null) return;

        bool alive = _stats == null || _stats.IsAlive;
        var phase = _controller.Phase;
        float charge = phase == JetpackPhase.Charging ? _controller.ChargeAmount : 0f;
        float target = (_controller.IsJetpacking || _controllerFlash) ? 1f : 0f;
        if (phase == JetpackPhase.Charging) target = Mathf.Max(target, 0.18f + charge * 0.3f);      // pilot flames while charging
        _thrust = Mathf.MoveTowards(_thrust, alive ? target : 0f, Time.deltaTime * 6f);

        _flicker = Mathf.PerlinNoise(Time.time * 28f, 0.3f);
        float len = _thrust * (0.85f + 0.3f * _flicker);
        bool burning = _thrust > 0.02f;
        foreach (var flame in new[] { _flameL, _flameR })
        {
            if (flame == null) continue;
            flame.gameObject.SetActive(burning);
            flame.localScale = new Vector3(0.7f + 0.5f * _thrust, Mathf.Max(0.05f, len * 1.6f), 0.7f + 0.5f * _thrust);
        }
        float lightI = _thrust * (2.5f + 1.5f * _flicker);
        if (_lightL != null) _lightL.intensity = lightI;
        if (_lightR != null) _lightR.intensity = lightI;

        // Charging: the whole pack glows (rivals see you are about to flee)
        float pulse = charge > 0f ? 0.7f + 0.3f * Mathf.Sin(Time.time * (8f + 14f * charge)) : 0f;
        if (_chargeLight != null) _chargeLight.intensity = charge * 3.5f * pulse;
        _metalMat.SetColor("_EmissionColor", Color.Lerp(Color.black, Pink * 1.4f, charge * pulse));

        // Space trail while flying
        bool trailOn = alive && _controller.IsFlying;
        if (_trailL != null) _trailL.emitting = trailOn;
        if (_trailR != null) _trailR.emitting = trailOn;

        UpdateTrajectory(phase, charge);
    }

    private void UpdateTrajectory(JetpackPhase phase, float charge)
    {
        if (_line == null) return;
        bool show = phase == JetpackPhase.Charging && _controller.Phase == JetpackPhase.Charging;
        _line.enabled = show;
        if (!show) return;

        bool hits = _controller.PredictLaunch(_path, Mathf.Max(0.35f, charge));
        // Thin out the point list: one point every 3rd step is plenty for a smooth arc
        var pts = new List<Vector3>();
        Vector3 camPos = Camera.main != null ? Camera.main.transform.position : transform.position;
        for (int i = 0; i < _path.Count; i++)
            if (Vector3.Distance(_path[i], camPos) > 2.5f) pts.Add(_path[i]);       // nothing inside the camera
        if (pts.Count < 2) { _line.enabled = false; return; }
        _line.positionCount = pts.Count;
        _line.SetPositions(pts.ToArray());
        Color c = hits || _controller.AimTarget != null ? Pink : Cyan;
        _line.startColor = c; _line.endColor = new Color(c.r, c.g, c.b, 0.15f);
        float length = 0f;
        for (int i = 1; i < pts.Count; i++) length += Vector3.Distance(pts[i - 1], pts[i]);
        _line.material.mainTextureScale = new Vector2(length / 1.2f, 1f);
        _line.material.mainTextureOffset = new Vector2(-Time.time * 1.5f, 0f);
    }

    void OnDestroy()
    {
        if (_line != null) Destroy(_line.gameObject);
    }

    void OnDisable() { if (_line != null) _line.enabled = false; }
}
}
