using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace OrbitRush
{

/// <summary>
/// Hover / keyboard-focus look for one text menu entry: the label eases from its resting colour to the accent colour, slides a
/// few pixels right, and a thin accent bar grows on its left edge. Runs on unscaled time so it works while the game is paused.
/// Clicking is handled by a Button on the same object; this only does the visuals and reports hovering.
/// </summary>
public class MenuItemFx : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public Text label;
    public Image bar;
    public Color normal = new Color(1f, 1f, 1f, 0.63f);
    public Color active = new Color32(0x4F, 0xD6, 0xFF, 255);
    public System.Action onClick;

    /// <summary>Raised when the pointer enters an entry, so the menu can move its keyboard focus there.</summary>
    public static event System.Action<MenuItemFx> Hovered;

    private bool _hover, _focused;
    private float _k;
    private Vector2 _labelBase;
    private bool _hasBase;

    public void SetFocus(bool focused) => _focused = focused;
    public void Activate() => onClick?.Invoke();

    public void OnPointerEnter(PointerEventData e) { _hover = true; Hovered?.Invoke(this); }
    public void OnPointerExit(PointerEventData e) => _hover = false;

    void Update()
    {
        if (label == null) return;
        if (!_hasBase) { _labelBase = label.rectTransform.anchoredPosition; _hasBase = true; }

        _k = Mathf.MoveTowards(_k, (_hover || _focused) ? 1f : 0f, Time.unscaledDeltaTime * 9f);
        float e = UiAnim.EaseOutCubic(_k);
        label.color = Color.Lerp(normal, active, e);
        label.rectTransform.anchoredPosition = _labelBase + new Vector2(10f * e, 0f);
        if (bar != null)
        {
            var c = active; c.a = e;
            bar.color = c;
            bar.rectTransform.sizeDelta = new Vector2(3f, Mathf.Lerp(8f, 30f, e));
        }
    }
}
}
