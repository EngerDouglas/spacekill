using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace OrbitRush
{

/// <summary>
/// Blurs the 3D world behind overlay screens (pause, death, scoreboard). It drives a global URP volume with a
/// Gaussian depth-of-field set to blur everything; the UI canvases are Screen Space Overlay, so they stay sharp.
/// Several screens can ask at once: it stays blurred while any of them wants it. Runs on unscaled time (works while paused).
/// </summary>
public class ScreenBlur : MonoBehaviour
{
    private static ScreenBlur _instance;
    private readonly HashSet<object> _requests = new HashSet<object>();
    private Volume _volume;
    private DepthOfField _dof;
    private float _weight;

    // Scope variant: everything beyond ~1 m is blurred, but things right in front of the camera (the scope) stay sharp
    private readonly HashSet<object> _scopeRequests = new HashSet<object>();
    private Volume _scopeVolume;
    private float _scopeWeight;

    // Strong variant (weapon wheel): a Bokeh depth-of-field with a long lens, which blurs far more than the Gaussian's 1.5 cap
    private readonly HashSet<object> _strongRequests = new HashSet<object>();
    private Volume _strongVolume;
    private float _strongWeight;

    /// <summary>Ask for the extra-strong blur (true) or release it (false). Ask for the normal blur as well as a fallback.</summary>
    public static void RequestStrong(object owner, bool on)
    {
        var b = Get();
        if (on) b._strongRequests.Add(owner); else b._strongRequests.Remove(owner);
    }

    public static void RequestScope(object owner, bool on)
    {
        var b = Get();
        if (on) b._scopeRequests.Add(owner); else b._scopeRequests.Remove(owner);
    }

    private static ScreenBlur Get()
    {
        if (_instance != null) return _instance;
        var go = new GameObject("ScreenBlur");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<ScreenBlur>();
        return _instance;
    }

    /// <summary>Ask for the blur (true) or release the request (false). `owner` identifies the screen asking.</summary>
    public static void Request(object owner, bool on)
    {
        var b = Get();
        if (on) b._requests.Add(owner); else b._requests.Remove(owner);
    }

    /// <summary>Drops every request and the blur at once (scene reloads).</summary>
    public static void Clear()
    {
        if (_instance == null) return;
        _instance._requests.Clear();
        _instance._scopeRequests.Clear();
        _instance._strongRequests.Clear();
        _instance._strongWeight = 0f;
        _instance._scopeWeight = 0f;
        _instance._weight = 0f;
        _instance.Apply();
    }

    void Awake()
    {
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        _dof = profile.Add<DepthOfField>(true);
        _dof.mode.Override(DepthOfFieldMode.Gaussian);
        _dof.gaussianStart.Override(0f);          // everything from the camera onwards is out of focus...
        _dof.gaussianEnd.Override(0.01f);         // ...immediately
        _dof.gaussianMaxRadius.Override(1.5f);
        _dof.highQualitySampling.Override(true);

        _volume = gameObject.AddComponent<Volume>();
        _volume.isGlobal = true;
        _volume.priority = 100f;
        _volume.profile = profile;
        _volume.weight = 0f;

        // Scope volume
        var sp = ScriptableObject.CreateInstance<VolumeProfile>();
        var sdof = sp.Add<DepthOfField>(true);
        sdof.mode.Override(DepthOfFieldMode.Gaussian);
        sdof.gaussianStart.Override(0.95f);
        sdof.gaussianEnd.Override(1.25f);
        sdof.gaussianMaxRadius.Override(1.5f);
        sdof.highQualitySampling.Override(true);
        var sgo = new GameObject("ScopeBlurVolume");
        sgo.transform.SetParent(transform, false);
        _scopeVolume = sgo.AddComponent<Volume>();
        _scopeVolume.isGlobal = true; _scopeVolume.priority = 90f; _scopeVolume.profile = sp; _scopeVolume.weight = 0f;

        // Strong volume: Bokeh with a 300 mm lens at f/1 focused 0.5 m away puts everything beyond arm's length well out of focus
        var strong = ScriptableObject.CreateInstance<VolumeProfile>();
        var bdof = strong.Add<DepthOfField>(true);
        bdof.mode.Override(DepthOfFieldMode.Bokeh);
        bdof.focusDistance.Override(0.5f);
        bdof.focalLength.Override(300f);
        bdof.aperture.Override(1f);
        var bgo = new GameObject("StrongBlurVolume");
        bgo.transform.SetParent(transform, false);
        _strongVolume = bgo.AddComponent<Volume>();
        _strongVolume.isGlobal = true; _strongVolume.priority = 110f; _strongVolume.profile = strong; _strongVolume.weight = 0f;

        SceneManager.sceneLoaded += (s, m) => Clear();
    }

    void Update()
    {
        float target = _requests.Count > 0 ? 1f : 0f;
        _weight = Mathf.MoveTowards(_weight, target, Time.unscaledDeltaTime * 3.2f);
        _scopeWeight = Mathf.MoveTowards(_scopeWeight, _scopeRequests.Count > 0 ? 1f : 0f, Time.unscaledDeltaTime * 5f);
        _strongWeight = Mathf.MoveTowards(_strongWeight, _strongRequests.Count > 0 ? 1f : 0f, Time.unscaledDeltaTime * 4f);
        Apply();
    }

    private void Apply()
    {
        if (_volume == null) return;
        _volume.weight = Mathf.SmoothStep(0f, 1f, _weight);
        _volume.enabled = _weight > 0.001f;
        if (_strongVolume != null)
        {
            _strongVolume.weight = Mathf.SmoothStep(0f, 1f, _strongWeight);
            _strongVolume.enabled = _strongWeight > 0.001f;
        }
        if (_scopeVolume != null)
        {
            _scopeVolume.weight = Mathf.SmoothStep(0f, 1f, _scopeWeight);
            _scopeVolume.enabled = _scopeWeight > 0.001f;
        }
    }
}
}
