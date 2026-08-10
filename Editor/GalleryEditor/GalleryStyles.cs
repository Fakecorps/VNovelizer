using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 通用 UI 样式工具：按钮、Toggle、ObjectField 颜色统一。
/// </summary>
public static class GalleryStyles
{
    public static Color Hex(string hex) => GalleryTheme.Hex(hex);

    public static void ApplyButton(Button btn, string bgHex, bool primary)
    {
        btn.style.backgroundColor = Hex(bgHex);
        btn.style.color = primary ? Color.white : Hex(GalleryTheme.TextPrimary);
        btn.style.borderTopLeftRadius = 4;
        btn.style.borderTopRightRadius = 4;
        btn.style.borderBottomLeftRadius = 4;
        btn.style.borderBottomRightRadius = 4;
        btn.style.borderTopWidth = 0;
        btn.style.borderBottomWidth = 0;
        btn.style.borderLeftWidth = 0;
        btn.style.borderRightWidth = 0;
    }

    public static void ApplyToggle(Toggle t)
    {
        t.style.color = Hex(GalleryTheme.TextPrimary);
    }

    public static void ApplyField(VisualElement field)
    {
        field.style.color = Hex(GalleryTheme.TextPrimary);
    }

    public static VisualElement MakeCard()
    {
        var card = new VisualElement();
        card.style.backgroundColor = Hex(GalleryTheme.BgCard);
        card.style.borderTopLeftRadius = 8;
        card.style.borderTopRightRadius = 8;
        card.style.borderBottomLeftRadius = 8;
        card.style.borderBottomRightRadius = 8;
        card.style.paddingTop = 12;
        card.style.paddingBottom = 12;
        card.style.paddingLeft = 14;
        card.style.paddingRight = 14;
        card.style.marginBottom = 12;
        card.style.borderTopWidth = 1;
        card.style.borderTopColor = Hex(GalleryTheme.Border);
        // 卡片允许收缩，避免内容被撑出窗口
        card.style.flexShrink = 1;
        card.style.minWidth = 0;
        return card;
    }
}