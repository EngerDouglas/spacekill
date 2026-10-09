using UnityEngine;

namespace OrbitRush
{

/// <summary>Shared UGUI building blocks (rects, font, generated radial sprites) used by HUD, MainMenu and SniperScope.</summary>
public static class UiFactory
{
    /// <summary>Regular UI font: Inter (Resources/Fonts) when present, else Unity's built-in font.</summary>
    public static Font LoadFont() => LoadInter("Inter-Regular");

    /// <summary>Light weight (big numbers, timer). Falls back to the regular font.</summary>
    public static Font LoadFontLight() => LoadInter("Inter-Light");

    /// <summary>Semibold weight (labels, names). Falls back to the regular font.</summary>
    public static Font LoadFontSemiBold() => LoadInter("Inter-SemiBold");

    static Font LoadInter(string file)
    {
        var f = Resources.Load<Font>("Fonts/" + file);
        if (f == null) f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (f == null) f = Resources.GetBuiltinResource<Font>("Arial.ttf");
        if (f == null) f = Font.CreateDynamicFontFromOSFont("Arial", 16);
        return f;
    }

    public static RectTransform NewRect(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = LayerMask.NameToLayer("UI");
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return rt;
    }

    /// <param name="alpha">Opacity as a function of normalised distance from the centre (0 = centre, 1 = edge).</param>
    public static Sprite RadialSprite(int size, System.Func<float, float> alpha, string name, bool clampToCircle = true)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = name, wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color32[size * size];
        float c = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                float a = alpha(d);
                if (clampToCircle && d > 1f) a = 0f;
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
            }
        tex.SetPixels32(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }
}
}
