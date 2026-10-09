using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace OrbitRush
{

/// <summary>
/// Step 3: Helldivers-style drop. The destroyer in orbit shows its pod, counts down and launches it; the camera chases the pod
/// through the atmosphere (fire, speed lines, heat haze) and ends on the impact: shock rings, dust and the pod hatch opening.
/// </summary>
public partial class DropSelector
{
    private RectTransform _dropUi;
    private GameObject _pod;
    private Vector3 _finalPos;
    private Quaternion _finalRot;
    private Text _dropCount, _dropAlt, _dropVel, _dropMsg;
    private Image _heat, _flash;

    private readonly List<Transform> _flames = new List<Transform>();
    private Transform _shipRoot;
    private Light _shipLight, _camLight;
    private Vector3 _bayInside, _bayHang;         // pod centre (ship-local) while stowed in the hull / hanging out of the open bay
    private Transform _doorL, _doorR;
    private Light _bayLight;
    private Material _bayGlow;
    private const float ShipScale = 3.4f;         // the Blender ship is 22 m long: ×3.4 → ~75 m

    private const float PodHalfNose = 2.0f;       // pod centre → ground when it stands in the crater

    private class Dust { public Transform t; public Vector3 vel; public float age, life, size; }

    private IEnumerator DropPhase()
    {
        Vector3 up = _targetUp.normalized;
        Vector3 P = _target;
        Vector3 T = Vector3.ProjectOnPlane(Vector3.forward, up);
        if (T.sqrMagnitude < 0.05f) T = Vector3.ProjectOnPlane(Vector3.right, up);
        T.Normalize();
        Vector3 side = Vector3.Cross(up, T).normalized;

        float H = Mathf.Max(420f, _planet.radius * 1.6f);
        Vector3 end = P + up * PodHalfNose;
        Vector3 A = P + up * H + T * 40f;                 // where the pod hangs under the ship
        Vector3 B = P + up * (H * 0.55f);                 // bends the path so it comes down vertically

        LightFor(_planet);
        BuildDropUi();
        var ship = BuildShip(A, T, up);                   // hangs the ship over the pod anchor
        Vector3 S = _shipRoot.position;                   // the ship's centre
        _pod = BuildPod();
        _pod.transform.SetParent(_world, false);
        var fire = _pod.transform.Find("Fire");
        var core = _pod.transform.Find("FireCore");
        var door = _pod.transform.Find("DoorPivot");
        fire.gameObject.SetActive(false); core.gameObject.SetActive(false);
        var fireLight = new GameObject("FireLight").AddComponent<Light>();
        fireLight.transform.SetParent(_pod.transform, false);
        fireLight.transform.localPosition = new Vector3(0f, 4f, 0f);
        fireLight.type = LightType.Point; fireLight.color = new Color(1f, 0.55f, 0.2f); fireLight.range = 60f; fireLight.intensity = 0f;
        fireLight.shadows = LightShadows.None;

        // Speed lines along the path
        var streaks = new GameObject("Streaks").transform;
        streaks.SetParent(_world, false);
        for (int i = 0; i < 46; i++)
        {
            float e = Random.Range(0.15f, 0.98f);
            Vector3 pos = PathPos(A, B, end, e);
            Vector3 dir = PathVel(A, B, end, e).normalized;
            Vector2 o = Random.insideUnitCircle * Random.Range(4f, 22f);
            Vector3 c = pos + side * o.x + T * o.y;
            float len = Random.Range(22f, 60f);
            var lr = NewLine(streaks, "Streak", Color.Lerp(White, Orange, Random.value) * new Color(1f, 1f, 1f, 0.55f), Random.Range(0.08f, 0.2f));
            lr.SetPosition(0, c - dir * len * 0.5f);
            lr.SetPosition(1, c + dir * len * 0.5f);
        }

        _cam.fieldOfView = 55f;
        _cam.nearClipPlane = 0.1f;
        _cam.clearFlags = RenderSettings.skybox != null ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
        _cam.enabled = true;
        _camLight = new GameObject("CamFill").AddComponent<Light>();
        _camLight.transform.SetParent(_cam.transform, false);
        _camLight.type = LightType.Point; _camLight.range = 160f; _camLight.intensity = 5f;
        _camLight.color = new Color(0.8f, 0.92f, 1f); _camLight.shadows = LightShadows.None;
        _pod.transform.position = A;
        _pod.transform.rotation = Quaternion.FromToRotation(Vector3.up, up);
        Vector3 camStart = S + side * 62f + T * 100f - up * 8f;
        Vector3 camCtrl = S + side * 95f - T * 20f - up * 40f;
        Vector3 camEnd = A + side * 13f - T * 4f - up * 1.5f;
        SetCam(camStart, S, up, up);
        Cursor.visible = false;
        yield return FadeTo(0f, 0.6f);

        // ── 1. The pod hangs under the ship, countdown ───────────────────
        const float hang = 6.4f;
        float t = 0f;
        int lastNum = -1;
        _dropMsg.text = "";
        _pod.transform.position = _shipRoot.TransformPoint(_bayInside);          // stowed in the hull
        _pod.transform.rotation = Quaternion.FromToRotation(Vector3.up, up);
        while (t < hang)
        {
            float dt = Time.unscaledDeltaTime; t += dt;
            float s = Mathf.SmoothStep(0f, 1f, t / hang);
            AnimateShip(S, T, up, t, 0f);

            // 1.2–2.0 s the hatch slides open, 2.0–3.4 s the pod is lowered out of it, then it hangs there
            SetBay(Mathf.Clamp01((t - 1.2f) / 0.8f));
            float lower = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 2.0f) / 1.4f));
            Vector3 hangPos = Vector3.Lerp(_bayInside, _bayHang, lower);
            _pod.transform.position = _shipRoot.TransformPoint(hangPos) + Random.insideUnitSphere * 0.03f * Mathf.Clamp01((t - 3.4f) / 0.5f);
            _pod.transform.rotation = _shipRoot.rotation * Quaternion.FromToRotation(Vector3.up, Vector3.up);
            if (_bayGlow != null) _bayGlow.SetColor("_EmissionColor", Cyan * (2.2f + 1.2f * Mathf.Sin(t * 6f)));
            _dropMsg.text = t < 1.2f ? "DESTRUCTOR EN ÓRBITA" : (t < 2.0f ? "ABRIENDO COMPUERTA DE CARGA" : (t < 3.4f ? "BAJANDO CÁPSULA DE DESPLIEGUE" : "CÁPSULA ARMADA"));

            // the camera sweeps around the wing and ends up under the belly, next to the pod
            Vector3 camPos = Bezier(camStart, camCtrl, camEnd, s);
            Vector3 look = Vector3.Lerp(S, _pod.transform.position, Mathf.SmoothStep(0f, 1f, s * 1.2f));
            SetCam(camPos, look, up, up);
            _cam.fieldOfView = Mathf.Lerp(52f, 62f, s);

            int num = t < 3.4f ? -1 : (t < 6.1f ? 3 - (int)((t - 3.4f) / 0.9f) : 0);
            if (num != lastNum) { lastNum = num; _punch = 1f; }
            _punch = Mathf.MoveTowards(_punch, 0f, dt * 3.5f);
            _dropCount.text = num > 0 ? num.ToString() : (num == 0 ? "¡YA!" : "");
            _dropCount.rectTransform.localScale = Vector3.one * (1f + 0.4f * _punch * _punch);
            _dropCount.color = new Color(1f, 1f, 1f, num >= 0 ? 0.55f + 0.45f * _punch : 0f);
            _dropAlt.text = Mathf.RoundToInt(Vector3.Dot(A - P, up)).ToString("0000") + " M";
            _dropVel.text = "0000 KM/H";
            yield return null;
        }
        _dropCount.text = "";
        _flash.color = new Color(1f, 1f, 1f, 0.45f);

        // ── 2. Fall ──────────────────────────────────────────────────────
        const float fall = 5.4f;
        t = 0f;
        float spin = 0f, shakeAmp;
        fire.gameObject.SetActive(true); core.gameObject.SetActive(true);
        Vector3 ground = P + side * 22f - T * 6f + up * 2.5f;
        Vector3 groundLook = P + up * 5f;
        while (t < fall)
        {
            float dt = Time.unscaledDeltaTime; t += dt;
            float u = Mathf.Clamp01(t / fall);
            float e = Mathf.Pow(u, 1.9f);
            Vector3 pos = PathPos(A, B, end, e);
            Vector3 vel = PathVel(A, B, end, e);
            Vector3 vdir = vel.sqrMagnitude > 0.01f ? vel.normalized : -up;
            float speed = vel.magnitude * 1.9f * Mathf.Pow(Mathf.Max(u, 0.0001f), 0.9f) / fall;
            spin += 160f * u * dt;
            AnimateShip(S, T, up, hang + t, t);
            _camLight.intensity = Mathf.Lerp(5f, 1.5f, u);
            _pod.transform.position = pos;
            _pod.transform.rotation = Quaternion.FromToRotation(Vector3.up, -vdir) * Quaternion.AngleAxis(spin, Vector3.up);

            float k = Mathf.Clamp01(u * 2.5f);
            float flick = 0.88f + 0.24f * Random.value;
            fire.localScale = _fireScale * k * flick;
            core.localScale = _coreScale * k * (0.85f + 0.3f * Random.value);
            fireLight.intensity = 9f * k * flick;
            if (_podMat != null) _podMat.SetColor("_EmissionColor", new Color(1f, 0.4f, 0.1f) * (2.2f * u * flick));

            float w = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.72f, 0.97f, u));
            Vector3 chasePos = pos - vdir * 11f + side * 7f;
            Vector3 chaseLook = pos + vdir * 4f;
            shakeAmp = (0.03f + 0.3f * u * u) * (1f - 0.7f * w);
            Vector3 camPos = Vector3.Lerp(chasePos, ground, w) + Random.insideUnitSphere * shakeAmp;
            Vector3 look = Vector3.Lerp(chaseLook, groundLook, w);
            SetCam(camPos, look, Vector3.Slerp(T, up, w), up);
            _cam.fieldOfView = Mathf.Lerp(Mathf.Lerp(62f, 82f, u), 50f, w);

            _heat.color = new Color(1f, 0.45f, 0.1f, Mathf.Clamp01(u * 1.7f) * 0.7f * (1f - Mathf.InverseLerp(0.93f, 1f, u)));
            _flash.color = new Color(1f, 1f, 1f, Mathf.MoveTowards(_flash.color.a, 0f, dt * 2f));
            _dropAlt.text = Mathf.Max(0, Mathf.RoundToInt(Vector3.Dot(pos - P, up) - PodHalfNose)).ToString("0000") + " M";
            _dropVel.text = Mathf.RoundToInt(speed * 3.6f).ToString("0000") + " KM/H";
            _dropMsg.text = u < 0.2f ? "SALIDA DE ÓRBITA" : (u < 0.88f ? "ENTRADA ATMOSFÉRICA" : "PREPARA EL IMPACTO");
            yield return null;
        }

        // ── 3. Impact ────────────────────────────────────────────────────
        _pod.transform.position = end;
        _pod.transform.rotation = Quaternion.FromToRotation(Vector3.up, up);
        fire.gameObject.SetActive(false); core.gameObject.SetActive(false);
        if (_podMat != null) _podMat.SetColor("_EmissionColor", new Color(1f, 0.35f, 0.08f) * 0.8f);
        _heat.color = new Color(1f, 0.45f, 0.1f, 0f);
        _flash.color = new Color(1f, 1f, 1f, 1f);
        _dropAlt.text = "0000 M"; _dropVel.text = "0000 KM/H";
        _dropMsg.text = "IMPACTO CONFIRMADO";
        var boom = new GameObject("ImpactLight").AddComponent<Light>();
        boom.transform.SetParent(_world, false);
        boom.transform.position = P + up * 5f;
        boom.type = LightType.Point; boom.color = new Color(1f, 0.7f, 0.4f); boom.range = 90f; boom.shadows = LightShadows.None;

        var dust = new List<Dust>();
        var dustMat = Lit(DustColor(), Color.black, 0f, 0.1f);
        for (int i = 0; i < 38; i++)
        {
            var d = new Dust { age = 0f, life = Random.Range(1.1f, 2.0f), size = Random.Range(1.2f, 3.2f) };
            var go = Prim(PrimitiveType.Sphere, _world, P + up * 0.5f, Vector3.one * 0.1f, dustMat, "Dust");
            d.t = go.transform;
            Vector2 c = Random.insideUnitCircle.normalized * Random.Range(5f, 22f);
            d.vel = side * c.x + T * c.y + up * Random.Range(1.5f, 9f);
            dust.Add(d);
        }
        var rings = new[]
        {
            NewLine(_world, "Ring0", new Color(0.8f, 0.95f, 1f, 1f), 1f, 64, true),
            NewLine(_world, "Ring1", new Color(Cyan.r, Cyan.g, Cyan.b, 1f), 0.7f, 64, true),
        };

        _camLight.intensity = 1.5f;
        const float after = 2.1f;
        t = 0f;
        bool doorOpen = false;
        while (t < after)
        {
            float dt = Time.unscaledDeltaTime; t += dt;
            float u = t / after;
            shakeAmp = 1.1f * Mathf.Pow(1f - Mathf.Clamp01(t / 1.1f), 2f);
            Vector3 camPos = Vector3.Lerp(ground, ground - side * 3f + up * 0.5f, u) + Random.insideUnitSphere * shakeAmp;
            SetCam(camPos, groundLook, up, up);
            _cam.fieldOfView = Mathf.Lerp(50f, 44f, u);
            _flash.color = new Color(1f, 1f, 1f, Mathf.MoveTowards(_flash.color.a, 0f, dt * 2.2f));
            boom.intensity = 40f * Mathf.Pow(1f - Mathf.Clamp01(t / 0.9f), 2f);

            foreach (var d in dust)
            {
                d.age += dt;
                d.vel += -up * 9f * dt;
                d.vel *= 1f - 1.3f * dt;
                d.t.position += d.vel * dt;
                float a = Mathf.Clamp01(d.age / d.life);
                d.t.localScale = Vector3.one * d.size * (0.3f + a * 1.2f) * (1f - a * a);
            }
            for (int i = 0; i < rings.Length; i++)
            {
                float age = t - i * 0.2f;
                bool on = age > 0f && age < 1.4f;
                rings[i].enabled = on;
                if (!on) continue;
                float rad = 2f + age * 34f;
                float fade = 1f - age / 1.4f;
                rings[i].startWidth = rings[i].endWidth = 0.9f * fade + 0.1f;
                var rc = rings[i].startColor; rc.a = fade; rings[i].startColor = rings[i].endColor = rc;
                Vector3 centre = P + up * 0.25f;
                for (int j = 0; j < 64; j++)
                {
                    float ang = j / 64f * Mathf.PI * 2f;
                    rings[i].SetPosition(j, centre + (side * Mathf.Cos(ang) + T * Mathf.Sin(ang)) * rad);
                }
            }
            if (t > 0.75f)
            {
                if (!doorOpen) { doorOpen = true; _dropMsg.text = "COMPUERTA ABIERTA   ·   BUENA SUERTE, SOLDADO"; }
                float o = Mathf.SmoothStep(0f, 1f, (t - 0.75f) / 0.5f);
                door.localRotation = Quaternion.Euler(0f, -110f * o, 0f);
            }
            yield return null;
        }

        // ── 4. Hand the player over ──────────────────────────────────────
        ComputeFinal(P, up, side, T);
        yield return FadeTo(1f, 0.4f);
        if (ship != null) Destroy(ship);
    }

    private Color DustColor()
    {
        switch (_planet.biome)
        {
            case PlanetGravity.BiomeType.Desert: return new Color(0.72f, 0.58f, 0.38f);
            case PlanetGravity.BiomeType.Forest: return new Color(0.28f, 0.27f, 0.2f);
            case PlanetGravity.BiomeType.Ice:    return new Color(0.85f, 0.9f, 0.95f);
            default:                             return new Color(0.4f, 0.38f, 0.36f);
        }
    }

    private float _punch;

    private void SetCam(Vector3 pos, Vector3 lookAt, Vector3 upHint, Vector3 fallbackUp)
    {
        _cam.transform.position = pos;
        Vector3 f = lookAt - pos;
        if (f.sqrMagnitude < 0.0001f) return;
        Vector3 hint = upHint.sqrMagnitude > 0.0001f ? upHint : fallbackUp;
        if (Mathf.Abs(Vector3.Dot(f.normalized, hint.normalized)) > 0.995f) hint = Vector3.Cross(f, fallbackUp).sqrMagnitude > 0.001f ? Vector3.Cross(f, fallbackUp) : Vector3.right;
        _cam.transform.rotation = Quaternion.LookRotation(f, hint);
    }

    private static Vector3 PathPos(Vector3 a, Vector3 b, Vector3 c, float e)
    {
        float q = 1f - e;
        return q * q * a + 2f * q * e * b + e * e * c;
    }

    private static Vector3 PathVel(Vector3 a, Vector3 b, Vector3 c, float e) => 2f * (1f - e) * (b - a) + 2f * e * (c - b);

    // ── Final spot for the player ─────────────────────────────────────────

    private void ComputeFinal(Vector3 P, Vector3 up, Vector3 side, Vector3 T)
    {
        Vector3 centre = _planet.transform.position;
        float start = Random.value * Mathf.PI * 2f;
        for (int i = 0; i < 10; i++)
        {
            float a = start + i * Mathf.PI * 0.2f;
            Vector3 want = P + (side * Mathf.Cos(a) + T * Mathf.Sin(a)) * 3.6f;
            Vector3 dir = (want - centre).normalized;
            Vector3 pos = _planet.GetSurfacePoint(dir) + dir;
            if (!SpotClear(pos, dir)) continue;
            _finalPos = pos;
            _finalRot = Quaternion.LookRotation(Vector3.ProjectOnPlane(pos - P, dir).normalized, dir);
            return;
        }
        _finalPos = P + up * 1.2f + side * 3.6f;
        _finalRot = Quaternion.LookRotation(side, up);
    }

    private bool SpotClear(Vector3 pos, Vector3 up)
    {
        foreach (var h in Physics.OverlapSphere(pos + up * 1.1f, 0.8f))
        {
            if (h.isTrigger || _planet.IsGround(h)) continue;
            if (_player != null && h.transform.IsChildOf(_player)) continue;
            return false;
        }
        return true;
    }

    // ── Models ────────────────────────────────────────────────────────────

    private static Material _shipMat;

    /// <summary>PBR material of the Blender ship (Resources/Ship): colour, normal, metal/smoothness, occlusion and emission maps.</summary>
    private static Material ShipMaterial()
    {
        if (_shipMat != null) return _shipMat;
        var sh = Shader.Find("Universal Render Pipeline/Lit");
        var baseTex = Resources.Load<Texture2D>("Ship/Ship_Base");
        if (sh == null || baseTex == null) return null;
        var m = new Material(sh) { name = "Ship" };
        m.SetTexture("_BaseMap", baseTex); m.SetColor("_BaseColor", Color.white);
        var n = Resources.Load<Texture2D>("Ship/Ship_Normal");
        if (n != null) { m.SetTexture("_BumpMap", n); m.SetFloat("_BumpScale", 1f); m.EnableKeyword("_NORMALMAP"); }
        var ms = Resources.Load<Texture2D>("Ship/Ship_MetalSmooth");
        if (ms != null) { m.SetTexture("_MetallicGlossMap", ms); m.SetFloat("_Metallic", 1f); m.SetFloat("_Smoothness", 1f); m.EnableKeyword("_METALLICSPECGLOSSMAP"); }
        var ao = Resources.Load<Texture2D>("Ship/Ship_AO");
        if (ao != null) { m.SetTexture("_OcclusionMap", ao); m.SetFloat("_OcclusionStrength", 1f); m.EnableKeyword("_OCCLUSIONMAP"); }
        var em = Resources.Load<Texture2D>("Ship/Ship_Emission");
        if (em != null) { m.SetTexture("_EmissionMap", em); m.SetColor("_EmissionColor", Color.white * 3f); m.EnableKeyword("_EMISSION"); }
        _shipMat = m;
        return m;
    }

    /// <summary>The drop ship (Resources/Ship/Ship.fbx, nose along +Z). Falls back to a block-built destroyer if the model is missing.</summary>
    private GameObject BuildShip(Vector3 anchor, Vector3 forward, Vector3 up)
    {
        _flames.Clear();
        var prefab = Resources.Load<GameObject>("Ship/Ship");
        var mat = prefab != null ? ShipMaterial() : null;
        if (prefab == null || mat == null)
        {
            Debug.LogWarning($"[DropSelector] Ship model/textures not found (model {(prefab != null)}, material {(mat != null)}): using the block-built ship.");
            return BuildPrimitiveShip(anchor, forward, up);
        }
        Debug.Log("[DropSelector] Textured ship loaded.");

        // Cargo bay in the flat belly (measured on the model): pod stowed inside the hull, lowered out through the hatch
        float K = ShipScale;
        float bellyY = -0.88f * K, bayZ = -3.0f * K;
        _bayHang = new Vector3(0f, bellyY - 5.0f, bayZ);
        _bayInside = new Vector3(0f, bellyY + 2.4f, bayZ);
        var root = new GameObject("Destroyer").transform;
        root.SetParent(_world, false);
        root.position = anchor - forward * _bayHang.z - up * _bayHang.y;
        root.rotation = Quaternion.LookRotation(forward, up);
        _shipRoot = root;

        var model = Instantiate(prefab, root);
        model.name = "ShipModel";
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;
        model.transform.localScale = Vector3.one * ShipScale;
        foreach (var c in model.GetComponentsInChildren<Collider>()) Destroy(c);
        foreach (var r in model.GetComponentsInChildren<Renderer>())
        {
            var mats = new Material[Mathf.Max(1, r.sharedMaterials.Length)];
            for (int i = 0; i < mats.Length; i++) mats[i] = mat;
            r.sharedMaterials = mats;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        BuildBay(root, bellyY, bayZ);

        // Engine flames behind the two nozzles (nozzle positions measured on the model)
        var outer = Lit(new Color(0.2f, 0.8f, 1f), new Color(0.25f, 0.85f, 1f) * 6f, 0f, 0f);
        var core = Lit(Color.white, new Color(0.8f, 0.97f, 1f) * 10f, 0f, 0f);
        for (int sgn = -1; sgn <= 1; sgn += 2)
        {
            var f = new GameObject("Flame").transform;
            f.SetParent(root, false);
            f.localPosition = new Vector3(sgn * 4.13f, -0.04f, -9.55f) * ShipScale;
            Prim(PrimitiveType.Sphere, f, new Vector3(0f, 0f, -9f), new Vector3(5.6f, 5.6f, 18f), outer, "FlameOuter");
            Prim(PrimitiveType.Sphere, f, new Vector3(0f, 0f, -5f), new Vector3(2.8f, 2.8f, 10f), core, "FlameCore");
            _flames.Add(f);
        }
        _shipLight = new GameObject("EngineLight").AddComponent<Light>();
        _shipLight.transform.SetParent(root, false);
        _shipLight.transform.localPosition = new Vector3(0f, 0f, -14f * ShipScale);
        _shipLight.type = LightType.Point; _shipLight.color = new Color(0.4f, 0.85f, 1f);
        _shipLight.range = 120f; _shipLight.intensity = 10f; _shipLight.shadows = LightShadows.None;
        return root.gameObject;
    }

    /// <summary>A hatch on the belly: dark interior, glowing frame and two plates that slide apart (the hull mesh is solid, so the bay is drawn just under it).</summary>
    private void BuildBay(Transform root, float bellyY, float bayZ)
    {
        const float W = 3.4f, L = 5.8f;
        var inner = Lit(new Color(0.01f, 0.012f, 0.02f), new Color(0.05f, 0.25f, 0.35f), 0f, 0.2f);
        var plate = Lit(new Color(0.11f, 0.13f, 0.17f), Color.black, 0.9f, 0.5f);
        _bayGlow = Lit(new Color(0.1f, 0.3f, 0.4f), Cyan * 3f, 0f, 0.5f);

        Prim(PrimitiveType.Cube, root, new Vector3(0f, bellyY - 0.03f, bayZ), new Vector3(W, 0.05f, L), inner, "BayInterior");
        // frame around the opening
        Prim(PrimitiveType.Cube, root, new Vector3(-W * 0.5f - 0.09f, bellyY - 0.1f, bayZ), new Vector3(0.14f, 0.1f, L + 0.4f), _bayGlow, "BayFrameL");
        Prim(PrimitiveType.Cube, root, new Vector3(W * 0.5f + 0.09f, bellyY - 0.1f, bayZ), new Vector3(0.14f, 0.1f, L + 0.4f), _bayGlow, "BayFrameR");
        Prim(PrimitiveType.Cube, root, new Vector3(0f, bellyY - 0.1f, bayZ + L * 0.5f + 0.09f), new Vector3(W + 0.4f, 0.1f, 0.14f), _bayGlow, "BayFrameF");
        Prim(PrimitiveType.Cube, root, new Vector3(0f, bellyY - 0.1f, bayZ - L * 0.5f - 0.09f), new Vector3(W + 0.4f, 0.1f, 0.14f), _bayGlow, "BayFrameB");

        _doorL = new GameObject("DoorL").transform; _doorL.SetParent(root, false);
        _doorR = new GameObject("DoorR").transform; _doorR.SetParent(root, false);
        _doorL.localPosition = new Vector3(0f, bellyY - 0.14f, bayZ);
        _doorR.localPosition = new Vector3(0f, bellyY - 0.14f, bayZ);
        Prim(PrimitiveType.Cube, _doorL, new Vector3(-W * 0.25f, 0f, 0f), new Vector3(W * 0.5f, 0.18f, L), plate, "PlateL");
        Prim(PrimitiveType.Cube, _doorL, new Vector3(-0.04f, -0.1f, 0f), new Vector3(0.08f, 0.04f, L), _bayGlow, "EdgeL");
        Prim(PrimitiveType.Cube, _doorR, new Vector3(W * 0.25f, 0f, 0f), new Vector3(W * 0.5f, 0.18f, L), plate, "PlateR");
        Prim(PrimitiveType.Cube, _doorR, new Vector3(0.04f, -0.1f, 0f), new Vector3(0.08f, 0.04f, L), _bayGlow, "EdgeR");

        _bayLight = new GameObject("BayLight").AddComponent<Light>();
        _bayLight.transform.SetParent(root, false);
        _bayLight.transform.localPosition = new Vector3(0f, bellyY - 1.2f, bayZ);
        _bayLight.type = LightType.Point; _bayLight.color = new Color(0.4f, 0.85f, 1f);
        _bayLight.range = 28f; _bayLight.intensity = 0f; _bayLight.shadows = LightShadows.None;
    }

    /// <summary>0 = closed, 1 = open: the plates slide sideways into the hull.</summary>
    private void SetBay(float open)
    {
        if (_doorL == null) return;
        float x = 1.8f * Mathf.SmoothStep(0f, 1f, open);
        _doorL.localPosition = new Vector3(-x, _doorL.localPosition.y, _doorL.localPosition.z);
        _doorR.localPosition = new Vector3(x, _doorR.localPosition.y, _doorR.localPosition.z);
        if (_bayLight != null) _bayLight.intensity = 14f * Mathf.SmoothStep(0f, 1f, open);
    }

    private static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, float t)
    {
        float q = 1f - t;
        return q * q * a + 2f * q * t * b + t * t * c;
    }

    /// <summary>Hover bob and roll while it waits; after the launch (<paramref name="leave"/> seconds) it pitches up and burns away.</summary>
    private void AnimateShip(Vector3 centre, Vector3 T, Vector3 up, float time, float leave)
    {
        if (_shipRoot == null) return;
        Vector3 pos = centre + up * (Mathf.Sin(time * 1.3f) * 0.8f) + (T * 14f + up * 9f) * (leave * leave);
        float tilt = Mathf.Clamp01(leave * 0.12f) * 0.3f;
        float roll = 2.5f * Mathf.Sin(time * 0.9f);
        _shipRoot.position = pos;
        _shipRoot.rotation = Quaternion.LookRotation(Vector3.Slerp(T, up, tilt), up) * Quaternion.AngleAxis(roll, Vector3.forward);

        float boost = 1f + Mathf.Min(leave, 1f) * 1.2f;
        foreach (var f in _flames) f.localScale = new Vector3(1f, 1f, boost * (0.9f + 0.2f * Random.value));
        if (_shipLight != null) _shipLight.intensity = 10f * boost;
    }

    private GameObject BuildPrimitiveShip(Vector3 anchor, Vector3 forward, Vector3 up)
    {
        _bayHang = new Vector3(0f, -10.2f, 10f);
        _bayInside = new Vector3(0f, -4f, 10f);
        Vector3 pos = anchor - forward * _bayHang.z - up * _bayHang.y;
        var root = new GameObject("Destroyer").transform;
        root.SetParent(_world, false);
        root.position = pos;
        root.rotation = Quaternion.LookRotation(forward, up);
        _shipRoot = root;

        var hull = Lit(new Color(0.13f, 0.15f, 0.2f), Color.black, 0.85f, 0.55f);
        var plate = Lit(new Color(0.07f, 0.08f, 0.11f), Color.black, 0.9f, 0.4f);
        var glowC = Lit(new Color(0.1f, 0.3f, 0.4f), Cyan * 3.2f, 0f, 0.5f);
        var glowP = Lit(new Color(0.4f, 0.05f, 0.25f), new Color(1f, 0.1f, 0.6f) * 3f, 0f, 0.5f);
        var engine = Lit(new Color(0.6f, 0.9f, 1f), new Color(0.5f, 0.9f, 1f) * 8f, 0f, 0.5f);

        Prim(PrimitiveType.Cube, root, Vector3.zero, new Vector3(30f, 10f, 140f), hull);
        Prim(PrimitiveType.Cube, root, new Vector3(0f, -1f, 82f), new Vector3(16f, 6f, 34f), hull);
        Prim(PrimitiveType.Cube, root, new Vector3(0f, 8f, -32f), new Vector3(12f, 8f, 30f), plate);
        Prim(PrimitiveType.Cube, root, new Vector3(0f, 14f, -36f), new Vector3(6f, 3f, 14f), glowC);
        for (int s = -1; s <= 1; s += 2)
        {
            var wing = Prim(PrimitiveType.Cube, root, new Vector3(s * 40f, -1f, -26f), new Vector3(56f, 2.5f, 46f), plate);
            wing.transform.localRotation = Quaternion.Euler(0f, s * 12f, s * -4f);
            Prim(PrimitiveType.Cube, root, new Vector3(s * 15.2f, 2f, -5f), new Vector3(0.8f, 1f, 120f), glowC);
            Prim(PrimitiveType.Cube, root, new Vector3(s * 66f, -1f, -34f), new Vector3(1f, 2.7f, 30f), glowP);
            Prim(PrimitiveType.Sphere, root, new Vector3(s * 9f, 0f, -72f), new Vector3(9f, 9f, 4f), engine);
        }
        Prim(PrimitiveType.Sphere, root, new Vector3(0f, 3f, -73f), new Vector3(10f, 10f, 4f), engine);
        Prim(PrimitiveType.Cube, root, new Vector3(0f, -6f, 10f), new Vector3(11f, 3f, 16f), plate);
        Prim(PrimitiveType.Cube, root, new Vector3(0f, -7.6f, 10f), new Vector3(8f, 0.4f, 12f), glowP);
        return root.gameObject;
    }

    private const float PodScale = 3.6f;          // the 1.07 m barrel becomes a ~3.9 m pod
    private Material _podMat;
    private Vector3 _fireScale = new Vector3(2.6f, 7f, 2.6f), _coreScale = new Vector3(1.3f, 4f, 1.3f);   // full-size flame (ellipsoid fallback)

    private static Material _podTemplate;

    private static Material PodMaterial()
    {
        if (_podTemplate != null) return _podTemplate;
        var sh = Shader.Find("Universal Render Pipeline/Lit");
        var albedo = Resources.Load<Texture2D>("Pod/Pod_Albedo");
        if (sh == null || albedo == null) return null;
        var m = new Material(sh) { name = "Pod" };
        m.SetTexture("_BaseMap", albedo); m.SetColor("_BaseColor", Color.white);
        var n = Resources.Load<Texture2D>("Pod/Pod_Normal");
        if (n != null) { m.SetTexture("_BumpMap", n); m.EnableKeyword("_NORMALMAP"); }
        var ms = Resources.Load<Texture2D>("Pod/Pod_MetalSmooth");
        if (ms != null) { m.SetTexture("_MetallicGlossMap", ms); m.SetFloat("_Metallic", 1f); m.SetFloat("_Smoothness", 1f); m.EnableKeyword("_METALLICSPECGLOSSMAP"); }
        var ao = Resources.Load<Texture2D>("Pod/Pod_AO");
        if (ao != null) { m.SetTexture("_OcclusionMap", ao); m.EnableKeyword("_OCCLUSIONMAP"); }
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", Color.black);
        _podTemplate = m;
        return m;
    }

    /// <summary>The textured sci-fi barrel (Resources/Pod/Pod.fbx, "prop1") as the pod body. False when the model is missing.</summary>
    private bool TryBuildBarrelPod(Transform root, out float top)
    {
        top = PodHalfNose;
        var prefab = Resources.Load<GameObject>("Pod/Pod");
        var template = prefab != null ? PodMaterial() : null;
        if (prefab == null || template == null) return false;

        var inst = Instantiate(prefab, root);
        inst.name = "PodModel";
        // The file holds three barrels; keep only prop1
        Transform keep = null;
        foreach (var t in inst.GetComponentsInChildren<Transform>(true)) if (t.name == "prop1") { keep = t; break; }
        if (keep == null) { Destroy(inst); return false; }
        foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
            if (!r.transform.IsChildOf(keep)) r.gameObject.SetActive(false);
        foreach (var c in inst.GetComponentsInChildren<Collider>(true)) Destroy(c);

        _podMat = new Material(template);
        foreach (var r in keep.GetComponentsInChildren<Renderer>(true))
        {
            var mats = new Material[Mathf.Max(1, r.sharedMaterials.Length)];
            for (int i = 0; i < mats.Length; i++) mats[i] = _podMat;
            r.sharedMaterials = mats;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }
        // The model's pivot is at its base: centre it on the pod's origin
        inst.transform.localScale = Vector3.one * PodScale;
        inst.transform.localPosition = new Vector3(0f, -0.5365f * PodScale, 0f);
        top = 0.5365f * PodScale;
        return true;
    }

    private static Material _fireOuter, _fireInner;

    /// <summary>
    /// The flame mesh from Fire.obj behind the pod, with the additive FireJet shader (outer orange shell + smaller white-hot core).
    /// Replaces the ellipsoid fallback. False when the model or the shader is missing.
    /// </summary>
    private bool TryBuildFlame(Transform root, float top, ref GameObject fire, ref GameObject core)
    {
        var prefab = Resources.Load<GameObject>("Pod/Fire");
        var sh = Shader.Find("OrbitRush/FireJet");
        if (prefab == null || sh == null) return false;
        if (_fireOuter == null) { _fireOuter = new Material(sh) { name = "FireOuter" }; _fireOuter.SetFloat("_Intensity", 2.4f); }
        if (_fireInner == null)
        {
            _fireInner = new Material(sh) { name = "FireInner" };
            _fireInner.SetFloat("_Intensity", 3.2f);
            _fireInner.SetColor("_Tint", new Color(1f, 0.92f, 0.75f, 1f));
        }

        fire.name = "_old"; core.name = "_old";          // Destroy is deferred: make sure Find("Fire") later returns the new ones
        fire.SetActive(false); core.SetActive(false);
        Destroy(fire); Destroy(core);
        fire = MakeFlame(root, "Fire", prefab, _fireOuter, top - 0.15f);
        core = MakeFlame(root, "FireCore", prefab, _fireInner, top - 0.1f);
        _fireScale = new Vector3(1.35f, 1.5f, 1.35f);
        _coreScale = new Vector3(0.8f, 1.0f, 0.8f);
        return true;
    }

    private static GameObject MakeFlame(Transform root, string name, GameObject prefab, Material mat, float y)
    {
        var pivot = new GameObject(name).transform;
        pivot.SetParent(root, false);
        pivot.localPosition = new Vector3(0f, y, 0f);
        var mesh = Instantiate(prefab, pivot);
        mesh.name = "Mesh";
        // The mesh's base sits at y = 0.52 and is off-centre (z 0.15): put its base on the pivot, centred
        mesh.transform.localPosition = new Vector3(0f, -0.52f, -0.15f);
        foreach (var c in mesh.GetComponentsInChildren<Collider>(true)) Destroy(c);
        foreach (var r in mesh.GetComponentsInChildren<Renderer>(true))
        {
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }
        return pivot.gameObject;
    }

    private GameObject BuildPod()
    {
        var root = new GameObject("DropPod").transform;
        var body = Lit(new Color(0.16f, 0.18f, 0.22f), Color.black, 0.9f, 0.55f);
        var dark = Lit(new Color(0.06f, 0.07f, 0.09f), Color.black, 0.9f, 0.4f);
        var band = Lit(new Color(0.4f, 0.05f, 0.25f), new Color(1f, 0.1f, 0.6f) * 3f, 0f, 0.5f);
        var fireMat = Lit(new Color(1f, 0.5f, 0.1f), new Color(1f, 0.45f, 0.08f) * 5f, 0f, 0f);
        var coreMat = Lit(new Color(1f, 0.95f, 0.8f), new Color(1f, 0.95f, 0.8f) * 9f, 0f, 0f);

        float top = 2.1f;
        bool model = TryBuildBarrelPod(root, out top);
        if (!model)
        {
        Prim(PrimitiveType.Capsule, root, Vector3.zero, new Vector3(1.7f, 1.9f, 1.7f), body, "Body");
        Prim(PrimitiveType.Sphere, root, new Vector3(0f, -1.85f, 0f), new Vector3(1.6f, 1.5f, 1.6f), dark, "Nose");
        Prim(PrimitiveType.Cylinder, root, new Vector3(0f, 2.05f, 0f), new Vector3(1.1f, 0.22f, 1.1f), dark, "Engine");
        Prim(PrimitiveType.Cylinder, root, new Vector3(0f, 0.45f, 0f), new Vector3(1.75f, 0.05f, 1.75f), band, "Band");
        for (int i = 0; i < 4; i++)
        {
            var fin = Prim(PrimitiveType.Cube, root, Vector3.zero, new Vector3(1.4f, 1.5f, 0.1f), dark, "Fin");
            fin.transform.localRotation = Quaternion.Euler(0f, i * 90f, 0f);
            fin.transform.localPosition = Quaternion.Euler(0f, i * 90f, 0f) * new Vector3(0.95f, 0.75f, 0f);
        }
        }
        var fire = Prim(PrimitiveType.Sphere, root, new Vector3(0f, 5.5f, 0f), new Vector3(2.6f, 7f, 2.6f), fireMat, "Fire");
        var core = Prim(PrimitiveType.Sphere, root, new Vector3(0f, 4f, 0f), new Vector3(1.3f, 4f, 1.3f), coreMat, "FireCore");
        _fireScale = new Vector3(2.6f, 7f, 2.6f); _coreScale = new Vector3(1.3f, 4f, 1.3f);
        bool flameMesh = TryBuildFlame(root, top, ref fire, ref core);

        // Hatch: a panel on a hinge at the side, opens when the pod has landed
        var pivot = new GameObject("DoorPivot").transform;
        pivot.SetParent(root, false);
        pivot.localPosition = new Vector3(0.84f, 0f, 0.55f);
        if (!model) Prim(PrimitiveType.Cube, pivot, new Vector3(0.02f, 0f, -0.55f), new Vector3(0.1f, 2.6f, 1.1f), dark, "Door");

        // The fire is positioned relative to the engine: keep the pivot at the engine end so it grows away from the pod
        if (!flameMesh)
        {
            fire.transform.localPosition = new Vector3(0f, top + 3.5f, 0f);
            core.transform.localPosition = new Vector3(0f, top + 2f, 0f);
        }
        return root.gameObject;
    }

    // ── UI ────────────────────────────────────────────────────────────────

    private void BuildDropUi()
    {
        _dropUi = Stretch("Drop", transform);
        _dropUi.SetAsFirstSibling();

        _heat = FullImage("Heat", _dropUi, new Color(1f, 0.45f, 0.1f, 0f));
        _heat.sprite = VignetteSprite();
        var vig = FullImage("Vignette", _dropUi, new Color(0f, 0f, 0f, 0.55f));
        vig.sprite = VignetteSprite();

        // Letterbox bars
        var top = UiFactory.NewRect("BarTop", _dropUi, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 84f));
        top.anchorMin = new Vector2(0f, 1f); top.anchorMax = new Vector2(1f, 1f); top.sizeDelta = new Vector2(0f, 84f);
        top.gameObject.AddComponent<Image>().color = Color.black;
        var bot = UiFactory.NewRect("BarBottom", _dropUi, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(0f, 84f));
        bot.anchorMin = new Vector2(0f, 0f); bot.anchorMax = new Vector2(1f, 0f); bot.sizeDelta = new Vector2(0f, 84f);
        bot.gameObject.AddComponent<Image>().color = Color.black;

        var info = Describe(_planet);
        float lat = Mathf.Asin(Mathf.Clamp(_targetUp.y, -1f, 1f)) * Mathf.Rad2Deg;
        float lon = Mathf.Atan2(_targetUp.z, _targetUp.x) * Mathf.Rad2Deg;
        Txt(_dropUi, "//  DESTRUCTOR  \"ORBIT RUSH\"", 18, Cyan, TextAnchor.UpperLeft, TL, new Vector2(96f, -118f), new Vector2(900f, 26f));
        Txt(_dropUi, info.name, 46, White, TextAnchor.UpperLeft, TL, new Vector2(96f, -146f), new Vector2(900f, 60f), true, FontStyle.BoldAndItalic);
        Txt(_dropUi, $"LAT {Mathf.Abs(lat):0.0}° {(lat >= 0f ? "N" : "S")}     LON {Mathf.Abs(lon):0.0}° {(lon >= 0f ? "E" : "O")}", 18, Soft, TextAnchor.UpperLeft, TL, new Vector2(96f, -208f), new Vector2(900f, 26f));

        var bl = new Vector2(0f, 0f);
        Txt(_dropUi, "ALTITUD", 14, Soft, TextAnchor.LowerLeft, bl, new Vector2(96f, 214f), new Vector2(300f, 22f)).rectTransform.pivot = bl;
        _dropAlt = Txt(_dropUi, "0000 M", 58, White, TextAnchor.LowerLeft, bl, new Vector2(96f, 150f), new Vector2(500f, 70f), true, FontStyle.BoldAndItalic);
        _dropAlt.rectTransform.pivot = bl;
        Txt(_dropUi, "VELOCIDAD", 14, Soft, TextAnchor.LowerLeft, bl, new Vector2(96f, 126f), new Vector2(300f, 22f)).rectTransform.pivot = bl;
        _dropVel = Txt(_dropUi, "0000 KM/H", 34, Cyan, TextAnchor.LowerLeft, bl, new Vector2(96f, 98f), new Vector2(500f, 44f), true, FontStyle.BoldAndItalic);
        _dropVel.rectTransform.pivot = bl;

        _dropCount = Txt(_dropUi, "", 240, White, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.56f), Vector2.zero, new Vector2(900f, 300f), true, FontStyle.BoldAndItalic);
        _dropMsg = Txt(_dropUi, "", 26, Cyan, TextAnchor.MiddleCenter, new Vector2(0.5f, 0f), new Vector2(0f, 130f), new Vector2(1400f, 40f), true, FontStyle.BoldAndItalic);

        _flash = FullImage("Flash", _dropUi, new Color(1f, 1f, 1f, 0f));
        _fade.transform.SetAsLastSibling();
    }
}
}
