#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OrbitRush
{

/// <summary>Test hooks used by the self-test tools (development builds only).</summary>
public partial class MeleeCombat
{
    // ── Test hooks (WeaponSelfTest) ───────────────────────────────────────

    public bool DebugReady => _katana != null;
    public bool DebugDrawn => _state != State.Stowed;
    public bool DebugAttached => _attached;
    public void DebugDraw() { if (_state == State.Stowed) BeginDraw(); }
    public void DebugLight() { if (_state == State.Stowed) BeginDraw(); EnterState(State.Ready); StartLight(); }
    public void DebugCharged(float power) { if (_state == State.Stowed) BeginDraw(); EnterState(State.Ready); StartCharged(power); }
    public void DebugDodge() => TryDodge();
}
}
#endif

// partial class: see MeleeCombat.cs / .Katana.cs / .Dev.cs
