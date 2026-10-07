using UnityEngine;

namespace OrbitRush
{

/// <summary>
/// Teams for the PvP modes. None = free-for-all / unassigned.
/// Pink and Cyan match the Orbit Rush helmet palette.
/// </summary>
public enum Team
{
    None = 0,
    Pink = 1,
    Cyan = 2
}

public static class TeamUtil
{
    public static Color GetColor(Team team)
    {
        switch (team)
        {
            case Team.Pink: return new Color(1f, 0.1f, 0.65f);
            case Team.Cyan: return new Color(0f, 0.85f, 1f);
            default:        return Color.white;
        }
    }

    public static string GetName(Team team)
    {
        switch (team)
        {
            case Team.Pink: return "PINK";
            case Team.Cyan: return "CYAN";
            default:        return "NONE";
        }
    }

    /// <summary>True when both are on the same real team (None is never an ally).</summary>
    public static bool AreAllies(Team a, Team b) => a != Team.None && a == b;
}

/// <summary>
/// Floating colored marker above a player's head so teammates and enemies are
/// readable at a glance. Built from a primitive — no art assets required.
/// </summary>
public static class TeamVisual
{
    const string MarkerName = "TeamMarker";

    public static void Apply(PlayerStats player)
    {
        if (player == null) return;

        var existing = player.transform.Find(MarkerName);
        GameObject marker;
        if (existing != null)
        {
            marker = existing.gameObject;
        }
        else
        {
            marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = MarkerName;

            // Visual only — a collider here would block shots and grounding casts.
            var col = marker.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);

            marker.transform.SetParent(player.transform, false);
            marker.transform.localPosition = new Vector3(0f, 2.4f, 0f);

            // Keep the marker a constant world size even if the player is scaled.
            float s = Mathf.Max(0.0001f, player.transform.lossyScale.y);
            marker.transform.localScale = Vector3.one * (0.3f / s);
        }

        marker.SetActive(player.team != Team.None);

        var rend = marker.GetComponent<Renderer>();
        if (rend == null) return;

        Color c = TeamUtil.GetColor(player.team);
        var block = new MaterialPropertyBlock();
        rend.GetPropertyBlock(block);
        block.SetColor("_BaseColor", c);   // URP
        block.SetColor("_Color", c);       // built-in fallback
        rend.SetPropertyBlock(block);
    }
}
}
