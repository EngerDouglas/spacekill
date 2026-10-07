using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace OrbitRush
{

/// <summary>
/// Plays the Mixamo Humanoid clips (Resources/Anims) on a rigged character through a PlayableGraph, so no
/// AnimatorController asset is needed (works in builds). Two layers:
///   • base   — locomotion / air / landing / melee / death, cross-faded
///   • upper  — arms + torso only (AvatarMask): the rifle aim pose and the firing kick, over the legs' walk cycle
/// Drivers (PlayerAnimDriver, EnemyAnimDriver) just set targets every frame.
/// </summary>
public class CharacterAnimator : MonoBehaviour
{
    public const string Idle = "Idle", Walk = "Walk", Run = "Run", Back = "Back", BackRun = "BackRun",
        StrafeLeft = "StrafeLeft", StrafeRight = "StrafeRight", Sprint = "Sprint", UnarmedRun = "UnarmedRun",
        PistolRun = "PistolRun", CrouchRun = "CrouchRun", ZombieRun = "ZombieRun", Climb = "Climb", Stop = "Stop",
        SwordWalk = "SwordWalk", SwordIdle = "SwordIdle", SwordStrafeL = "SwordStrafeL", SwordStrafeR = "SwordStrafeR",
        Air = "Air", Fall = "Fall", Flying = "Flying", Landing = "Landing", Jump = "Jump", PistolJump = "PistolJump", SwordJump = "SwordJump",
        Gunplay = "Gunplay", Draw = "Draw", SwordDeath = "SwordDeath", RifleJump = "RifleJump", Sheathe = "Sheathe", SwordLight = "SwordLight", SwordCharged = "SwordCharged", Dodge = "Dodge", Death = "Death";
    public const string Aim = "Aim", Fire = "Fire", AimPistol = "AimPistol", FirePistol = "FirePistol";

    private class Entry
    {
        public string key; public AnimationClipPlayable playable; public float weight, target = 0f;
        public float fade = 8f;       // weight units per second
        public float length; public bool frozen;
    }

    private PlayableGraph _graph;
    private AnimationMixerPlayable _baseMixer, _upperMixer;
    private AnimationLayerMixerPlayable _layers;
    private readonly Dictionary<string, Entry> _base = new Dictionary<string, Entry>();
    private readonly Dictionary<string, Entry> _upper = new Dictionary<string, Entry>();
    private float _upperWeight, _upperTarget;
    private bool _ready;

    public bool Ready => _ready;
    public Animator Animator { get; private set; }

    // key → (resource file, frozen first-frame pose?)
    private static readonly (string key, string file, bool frozen)[] BaseClips =
    {
        (Idle, "FiringRifle", true), (Walk, "WalkLoop", false), (Run, "Walk", false), (Back, "WalkBackPistol", false),
        (BackRun, "RunBack", false), (StrafeLeft, "WalkLeftTurn", false), (StrafeRight, "RunRightGun", false),
        (Sprint, "RifleRun", false), (UnarmedRun, "Running", false), (PistolRun, "PistolRun", false),
        (CrouchRun, "CrouchRun", false), (ZombieRun, "ZombieRun", false), (Climb, "RunStairs", false), (Stop, "RifleStop", false),
        (SwordWalk, "SwordWalk", false), (SwordIdle, "SwordWalk", true),
        (SwordStrafeL, "WalkLeftTurn", false), (SwordStrafeR, "RunTurnRight", false),
        (Air, "FallIdle", false), (Fall, "Falling", false), (Flying, "Flying", false), (Landing, "FallLanding", false),
        (Jump, "PistolJump", false), (SwordJump, "SwordJump", false),
        (SwordLight, "SwordTurn180", false), (SwordCharged, "SwordJumpAttack", false), (Dodge, "RollToRun", false),
        (Death, "DeathBack", false), (Gunplay, "Gunplay", false), (Draw, "DrawSword", false), (SwordDeath, "SwordDeath", false), (RifleJump, "RifleJump", false), (Sheathe, "DrawSword", false),
    };
    private static readonly (string key, string file, bool frozen)[] UpperClips =
    {
        (Aim, "FiringRifle", true), (Fire, "FiringRifle", false),
        (AimPistol, "ShootPistol", true), (FirePistol, "ShootPistol", false),
    };

    private static readonly Dictionary<string, AnimationClip> ClipCache = new Dictionary<string, AnimationClip>();

    public static AnimationClip LoadClip(string file)
    {
        if (ClipCache.TryGetValue(file, out var c) && c != null) return c;
        foreach (var clip in Resources.LoadAll<AnimationClip>(ResourcePaths.AnimsFolder + file))
            if (!clip.name.StartsWith("__preview__")) { ClipCache[file] = clip; return clip; }
        return null;
    }

    public bool Setup(Animator animator)
    {
        Animator = animator;
        if (animator == null || animator.avatar == null || !animator.avatar.isValid || !animator.avatar.isHuman)
        {
            Debug.LogWarning($"[CharacterAnimator] {name}: no valid humanoid avatar (avatar={(animator != null ? animator.avatar : null)}).");
            return false;
        }
        animator.runtimeAnimatorController = null;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        _graph = PlayableGraph.Create("CharAnim_" + name);
        _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        var output = AnimationPlayableOutput.Create(_graph, "Anim", animator);

        _baseMixer = BuildMixer(BaseClips, _base);
        _upperMixer = BuildMixer(UpperClips, _upper);

        _layers = AnimationLayerMixerPlayable.Create(_graph, 2);
        _graph.Connect(_baseMixer, 0, _layers, 0);
        _graph.Connect(_upperMixer, 0, _layers, 1);
        _layers.SetInputWeight(0, 1f);
        _layers.SetInputWeight(1, 0f);
        _layers.SetLayerMaskFromAvatarMask(1, UpperBodyMask());

        output.SetSourcePlayable(_layers);
        _graph.Play();
        _ready = true;
        SetBase(Idle, 1f, true);
        return true;
    }

    private AnimationMixerPlayable BuildMixer((string key, string file, bool frozen)[] defs, Dictionary<string, Entry> into)
    {
        var list = new List<(string, AnimationClip, bool)>();
        foreach (var d in defs)
        {
            var clip = LoadClip(d.file);
            if (clip == null) { Debug.LogWarning($"[CharacterAnimator] missing clip Anims/{d.file}"); continue; }
            list.Add((d.key, clip, d.frozen));
        }
        var mixer = AnimationMixerPlayable.Create(_graph, list.Count);
        for (int i = 0; i < list.Count; i++)
        {
            var (key, clip, frozen) = list[i];
            var p = AnimationClipPlayable.Create(_graph, clip);
            p.SetApplyFootIK(false);
            if (frozen) { p.SetSpeed(0); p.SetTime(0.02); }
            _graph.Connect(p, 0, mixer, i);
            mixer.SetInputWeight(i, 0f);
            into[key] = new Entry { key = key, playable = p, length = clip.length, frozen = frozen };
            into[key].weight = 0f;
            _inputIndex[(mixer, key)] = i;
        }
        return mixer;
    }

    private readonly Dictionary<(AnimationMixerPlayable, string), int> _inputIndex = new Dictionary<(AnimationMixerPlayable, string), int>();

    private static AvatarMask UpperBodyMask()
    {
        var mask = new AvatarMask();
        foreach (AvatarMaskBodyPart part in System.Enum.GetValues(typeof(AvatarMaskBodyPart)))
        {
            if (part == AvatarMaskBodyPart.LastBodyPart) continue;
            bool on = part == AvatarMaskBodyPart.Body || part == AvatarMaskBodyPart.Head
                   || part == AvatarMaskBodyPart.LeftArm || part == AvatarMaskBodyPart.RightArm
                   || part == AvatarMaskBodyPart.LeftFingers || part == AvatarMaskBodyPart.RightFingers;
            mask.SetHumanoidBodyPartActive(part, on);
        }
        return mask;
    }

    // ── API for drivers ───────────────────────────────────────────────────

    /// <summary>Sets the target weight of a base-layer state. restart = rewind the clip to frame 0 when it starts.</summary>
    public void SetBase(string key, float weight, bool restart = false)
    {
        if (!_ready || !_base.TryGetValue(key, out var e)) return;
        if (restart) { e.playable.SetTime(e.frozen ? 0.02 : 0.0); }
        e.target = weight;
    }

    public void ClearBaseTargetsExcept(params string[] keep)
    {
        foreach (var e in _base.Values)
        {
            bool k = false;
            foreach (var s in keep) if (s == e.key) k = true;
            if (!k) e.target = 0f;
        }
    }

    public void SetBaseSpeed(string key, float speed)
    {
        if (_ready && _base.TryGetValue(key, out var e) && !e.frozen) e.playable.SetSpeed(speed);
    }

    public void SetBaseFade(string key, float fadePerSecond)
    {
        if (_ready && _base.TryGetValue(key, out var e)) e.fade = fadePerSecond;
    }

    public float BaseWeight(string key) => _base.TryGetValue(key, out var e) ? e.weight : 0f;
    public float ClipLength(string key) => _base.TryGetValue(key, out var e) ? e.length : 0f;

    /// <summary>Plays a base-layer one-shot from its start over `duration` seconds (the clip is sped up/slowed to fit).</summary>
    public void PlayOneShot(string key, float duration)
    {
        if (!_ready || !_base.TryGetValue(key, out var e)) return;
        float rate = duration > 0.01f ? e.length / duration : 1f;
        bool reverse = key == Sheathe;       // putting the sword away = the draw clip played backwards
        e.playable.SetTime(reverse ? e.length : 0.0);
        e.playable.SetSpeed(reverse ? -rate : rate);
        e.weight = Mathf.Max(e.weight, 0.001f);
        e.target = 1f;
        e.fade = 14f;
    }

    public void SetUpperTarget(float weight) => _upperTarget = weight;

    private bool _pistol;
    /// <summary>Pistol-class weapons aim and fire with the one-handed pistol clips, everything else with the rifle ones.</summary>
    public void SetPistolStance(bool pistol) => _pistol = pistol;

    public void FireKick()
    {
        if (!_ready || !_upper.TryGetValue(_pistol ? FirePistol : Fire, out var e)) return;
        e.playable.SetTime(0.0);
        e.playable.SetSpeed(1.4);
        e.weight = 1f; e.target = 0f; e.fade = 3.2f;
    }

    // ── Per-frame blending ────────────────────────────────────────────────

    void LateUpdate()
    {
        if (!_ready) return;
        float dt = Time.deltaTime;
        ApplyWeights(_base, _baseMixer, dt, true);
        // Upper layer: aim pose weight follows the driver; the firing kick fades by itself.
        if (_upper.TryGetValue(Aim, out var aim)) aim.target = (_upperTarget > 0.01f && !_pistol) ? 1f : 0f;
        if (_upper.TryGetValue(AimPistol, out var aimP)) aimP.target = (_upperTarget > 0.01f && _pistol) ? 1f : 0f;
        ApplyWeights(_upper, _upperMixer, dt, true);
        _upperWeight = Mathf.MoveTowards(_upperWeight, _upperTarget, 7f * dt);
        _layers.SetInputWeight(1, _upperWeight);
    }

    private void ApplyWeights(Dictionary<string, Entry> set, AnimationMixerPlayable mixer, float dt, bool normalise)
    {
        float sum = 0f;
        foreach (var e in set.Values)
        {
            e.weight = Mathf.MoveTowards(e.weight, e.target, e.fade * dt);
            sum += e.weight;
        }
        foreach (var e in set.Values)
        {
            float w = e.weight;
            if (normalise && sum > 1f) w /= sum;
            else if (normalise && sum < 0.001f && e.key == Idle) w = 1f;
            int idx = _inputIndex[(mixer, e.key)];
            mixer.SetInputWeight(idx, w);
        }
    }

    void OnDestroy()
    {
        if (_graph.IsValid()) _graph.Destroy();
    }
}
}
