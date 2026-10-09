using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OrbitRush
{

/// <summary>Katana pose, first-person view-model, glow and trail.</summary>
public partial class MeleeCombat
{
    // ══════════════════════════════════════════════════════════════════════
    // Katana placement
    // ══════════════════════════════════════════════════════════════════════

    private void UpdateKatana(float dt)
    {
        // 0 = stowed on the back, 1 = in the hand
        float k;
        switch (_state)
        {
            case State.Stowed: k = 0f; break;
            // The sword leaves the back / returns to it when the hand reaches it (about half way through the clip)
            case State.Drawing: k = Smooth((_stateTime / drawTime - 0.42f) / 0.12f); break;
            default: k = 1f; break;
        }

        Vector3 pos; Quaternion rot;
        if (_attached && _chest != null && _hand != null)
        {
            Vector3 sp = _chest.TransformPoint(_stowLocalPos);
            Quaternion sr = _chest.rotation * _stowLocalRot;
            Vector3 gp = _hand.TransformPoint(_gripLocalPos);
            Quaternion gr = _hand.rotation * _gripLocalRot;
            pos = Vector3.Lerp(sp, gp, k) ; 
            rot = Quaternion.Slerp(sr, gr, k);
        }
        else    // rigid model fallback: fixed poses relative to the player
        {
            Vector3 sp = transform.TransformPoint(StowPos), gp = transform.TransformPoint(ReadyPos);
            Quaternion sr = transform.rotation * BladeRot(StowBlade, 90f), gr = transform.rotation * BladeRot(ReadyBlade, -15f);
            pos = Vector3.Lerp(sp, gp, k);
            rot = Quaternion.Slerp(sr, gr, k);
        }

        // First person: the sword is held in front of the lens; slashes sweep across the view
        if (k > 0.001f && ViewModel.TryGet(_aim, out float fp, out Transform cam))
        {
            Vector3 local = new Vector3(0.34f, -0.34f, 0.95f);          // camera-local units
            float yaw = 0f, roll = 12f, lift = 0f;
            switch (_state)
            {
                case State.Light:
                {
                    float u = Mathf.Clamp01(_stateTime / lightTime);
                    float sweep = Smooth((u - 0.12f) / 0.55f);
                    yaw = Mathf.Lerp(48f, -62f, sweep) * (_side > 0f ? 1f : -1f);
                    roll = Mathf.Lerp(25f, -50f, sweep) * (_side > 0f ? 1f : -1f);
                    local.x = Mathf.Lerp(0.85f, -0.7f, sweep) * (_side > 0f ? 1f : -1f);
                    local.z += 0.25f * Mathf.Sin(sweep * Mathf.PI);
                    break;
                }
                case State.Charging: roll = 55f; local += new Vector3(-0.1f, 0.15f, -0.2f); break;
                case State.Charged:
                {
                    float u = Mathf.Clamp01(_stateTime / chargedTime);
                    yaw = Mathf.Lerp(0f, 360f, Smooth(u)); local.x = 0.15f; local.y = -0.1f;
                    break;
                }
            }
            lift = (1f - k) * -1.1f;
            Vector3 vmPos = cam.TransformPoint(local + new Vector3(0f, lift, 0f));
            Quaternion vmRot = cam.rotation * Quaternion.Euler(0f, yaw, 0f) * BladeRot(new Vector3(-0.30f, 0.62f, 0.72f), roll);
            pos = Vector3.Lerp(pos, vmPos, fp);
            rot = Quaternion.Slerp(rot, vmRot, fp);
        }

        // Charging: a faint tremble
        if (_state == State.Charging) pos += UnityEngine.Random.insideUnitSphere * (0.004f + 0.012f * Mathf.Clamp01(_holdTime / fullChargeTime));

        _katana.SetPositionAndRotation(pos, rot);
    }

    private static float Smooth(float k) => Ease.Smooth(k);

    private void UpdateGlowAndTrail()
    {
        // Blade glow ramps up while charging, flares during the slam, and settles back afterwards.
        float boost = 1f;
        if (_state == State.Charging)
        {
            float charge = Mathf.Clamp01(_holdTime / fullChargeTime);
            boost = 1f + charge * 3.2f + (charge >= 1f ? 0.5f * Mathf.Sin(Time.time * 18f) : 0f);
        }
        else if (_state == State.Charged) boost = 3.5f;
        else if (_state == State.Light) boost = 1.8f;

        if (_glowMaterials != null)
            for (int i = 0; i < _glowMaterials.Length; i++)
                if (_glowMaterials[i] != null) _glowMaterials[i].SetColor("_EmissionColor", _glowBase[i] * boost);

        if (_trail != null) _trail.emitting = _state == State.Light || _state == State.Charged;
    }

    /// <summary>Back to the stowed state (used when the player respawns).</summary>
    public void ResetState()
    {
        _holding = false; _queuedLight = false; _hitDone = false;
        EnterState(State.Stowed);
        BlocksFiring = false;
    }
}
}

// partial class: see MeleeCombat.cs / .Katana.cs / .Dev.cs
