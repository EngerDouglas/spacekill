using UnityEngine;

namespace OrbitRush
{

public enum CoinType { Oro, Platino, Elite }

/// <summary>
/// A coin lying on a planet: spins and bobs, and is pulled to the player when they get close. Each kind is worth a
/// different amount (Oro 10, Platino 50, Elite 200). Models: Resources/Models/Props/Moneda_&lt;Tipo&gt;.
/// </summary>
public class Coin : MonoBehaviour
{
    public CoinType type;
    public int value = 10;
    [Tooltip("Seconds before it disappears (0 = never). Enemy drops expire; coins scattered on the map don't.")]
    public float lifetime;

    public const float VisualScale = 0.55f;          // the models are 0.6 m wide
    private const float MagnetRadius = 6f, CollectRadius = 1.1f;

    public static int ValueOf(CoinType t) => t == CoinType.Oro ? 10 : t == CoinType.Platino ? 50 : 200;

    private Transform _visual;
    private Vector3 _up = Vector3.up;
    private float _age, _seed, _pull;
    private Transform _target;
    private PlayerWallet _wallet;

    /// <summary>Creates a coin at <paramref name="position"/> standing on the surface whose normal is <paramref name="up"/>.</summary>
    public static Coin Spawn(CoinType type, Vector3 position, Vector3 up, Transform parent = null, float lifetime = 0f)
    {
        var go = new GameObject("Coin_" + type);
        if (parent != null) go.transform.SetParent(parent, true);
        go.transform.position = position;
        go.transform.rotation = Quaternion.FromToRotation(Vector3.up, up);

        var coin = go.AddComponent<Coin>();
        coin.type = type;
        coin.value = ValueOf(type);
        coin.lifetime = lifetime;
        return coin;
    }

    void Awake()
    {
        _seed = Random.value * 10f;
        var col = gameObject.AddComponent<SphereCollider>();
        col.isTrigger = true;
        col.radius = 0.9f;
    }

    void Start()
    {
        _up = transform.up;
        var model = Resources.Load<GameObject>("Models/Props/Moneda_" + type);
        if (model != null)
        {
            var visual = Instantiate(model, transform);
            visual.name = "Visual";
            foreach (var c in visual.GetComponentsInChildren<Collider>(true)) Destroy(c);
            _visual = visual.transform;
        }
        else
        {
            var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = "Visual";
            disc.transform.SetParent(transform, false);
            disc.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Destroy(disc.GetComponent<Collider>());
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.color = type == CoinType.Oro ? new Color(1f, 0.7f, 0.15f) : type == CoinType.Platino ? new Color(0.8f, 0.9f, 1f) : new Color(0.6f, 0.4f, 0.95f);
            disc.GetComponent<MeshRenderer>().sharedMaterial = mat;
            _visual = disc.transform;
        }
        _visual.localPosition = new Vector3(0f, 0.5f, 0f);
        _visual.localScale = Vector3.zero;          // pops in
    }

    void Update()
    {
        if (_visual == null) return;
        float dt = Time.deltaTime;
        _age += dt;
        if (lifetime > 0f && _age > lifetime) { Destroy(gameObject); return; }

        // Pop-in, spin about the surface normal, bob; blink for the last seconds of a drop's life
        float pop = Mathf.Clamp01(_age / 0.35f);
        float s = VisualScale * (1f - Mathf.Pow(1f - pop, 3f)) * (type == CoinType.Elite ? 1.2f : 1f);
        _visual.localScale = Vector3.one * s;
        _visual.localPosition = new Vector3(0f, 0.5f + Mathf.Sin(Time.time * 2.4f + _seed) * 0.08f, 0f);
        _visual.Rotate(_up, (type == CoinType.Oro ? 150f : type == CoinType.Platino ? 190f : 240f) * dt, Space.World);
        if (lifetime > 0f && lifetime - _age < 8f) _visual.gameObject.SetActive(Mathf.Repeat(_age * 5f, 1f) > 0.35f);

        // Magnet
        if (_age < 0.6f) return;
        if (_target == null)
        {
            _wallet = PlayerWallet.Local;
            if (_wallet == null) return;
            _target = _wallet.transform;
        }
        Vector3 to = _target.position - transform.position;
        float dist = to.magnitude;
        if (dist < CollectRadius) { Collect(); return; }
        if (dist < MagnetRadius)
        {
            _pull = Mathf.MoveTowards(_pull, 28f, 40f * dt);
            transform.position += to / dist * Mathf.Min(dist, (4f + _pull * (1f - dist / MagnetRadius)) * dt);
        }
        else _pull = 0f;
    }

    private void Collect()
    {
        if (_wallet == null) return;
        _wallet.Add(value);
        HUD.Instance?.ShowCoinGain(value, type);
        Destroy(gameObject);
    }
}
}
