using UnityEngine;
using UnityEngine.SceneManagement;

namespace OrbitRush
{

/// <summary>
/// Galaxy sky: replaces the plain background with a panoramic star field and nebula
/// (Resources/Sky/Galaxy.jpg, rendered in Blender in the game's pink/cyan palette) that
/// turns very slowly.
///
/// Installs itself on every scene load that contains a GameManager — no scene edits needed.
/// To remove it, delete this file or set <see cref="Enabled"/> to false.
/// </summary>
public class GalaxyBackdrop : MonoBehaviour
{
    public static bool Enabled = true;

    private const string TexturePath = "Sky/Galaxy";
    private const string ObjectName = "GalaxyBackdrop";

    [Tooltip("Degrees per second the sky turns.")]
    public float rotationSpeed = 0.6f;
    [Range(0.2f, 2f)] public float exposure = 1.0f;

    private Material _skyMaterial;
    private float _rotation;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!Enabled) return;
        if (Object.FindFirstObjectByType<GameManager>() == null) return;
        if (GameObject.Find(ObjectName) != null) return;

        new GameObject(ObjectName).AddComponent<GalaxyBackdrop>();
    }

    void Start()
    {
        var texture = Resources.Load<Texture2D>(TexturePath);
        var shader = Shader.Find("Skybox/Panoramic");
        if (texture == null || shader == null)
        {
            Debug.LogWarning($"[GalaxyBackdrop] Missing {(texture == null ? "texture Resources/" + TexturePath : "shader Skybox/Panoramic")} — keeping the default background.");
            Destroy(gameObject);
            return;
        }

        _skyMaterial = new Material(shader) { name = "GalaxySky" };
        _skyMaterial.SetTexture("_MainTex", texture);
        _skyMaterial.SetFloat("_Exposure", exposure);
        _skyMaterial.SetColor("_Tint", Color.white);

        RenderSettings.skybox = _skyMaterial;

        // The camera must be set to draw the skybox (the scene's camera was using a solid colour).
        foreach (var cam in Camera.allCameras)
            if (cam.cameraType == CameraType.Game) cam.clearFlags = CameraClearFlags.Skybox;

        // A touch of ambient light from the sky so shaded sides pick up the nebula colours.
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        // Neutral dark blue-grey (the sky itself is almost black; a violet ambient made everything purple).
        RenderSettings.ambientSkyColor = new Color(0.17f, 0.20f, 0.28f);
        RenderSettings.ambientEquatorColor = new Color(0.12f, 0.14f, 0.20f);
        RenderSettings.ambientGroundColor = new Color(0.06f, 0.07f, 0.10f);
    }

    void Update()
    {
        if (_skyMaterial == null) return;
        // Unscaled so the sky keeps turning behind the (paused) main menu.
        _rotation = (_rotation + rotationSpeed * Time.unscaledDeltaTime) % 360f;
        _skyMaterial.SetFloat("_Rotation", _rotation);
    }

    void OnDestroy()
    {
        if (_skyMaterial != null) Destroy(_skyMaterial);
    }
}
}
