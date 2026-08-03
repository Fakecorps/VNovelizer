using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 资源管理器全局样式常量（颜色、间距、尺寸等）。
/// 集中管理便于统一调整视觉风格。
/// </summary>
public static class ResourceStyles
{
    // ===================== 颜色 =====================
    public static readonly Color Bg = new(0.18f, 0.18f, 0.18f);
    public static readonly Color Sidebar = new(0.16f, 0.16f, 0.16f);
    public static readonly Color Toolbar = new(0.22f, 0.22f, 0.22f);
    public static readonly Color Card = new(0.27f, 0.27f, 0.27f);
    public static readonly Color CardHover = new(0.34f, 0.34f, 0.34f);
    public static readonly Color CardSelected = new(0.20f, 0.40f, 0.65f);
    public static readonly Color CardBorder = new(0.10f, 0.10f, 0.10f);
    public static readonly Color TextPrimary = new(0.86f, 0.86f, 0.86f);
    public static readonly Color TextSecondary = new(0.62f, 0.62f, 0.62f);
    public static readonly Color Accent = new(0.20f, 0.55f, 0.85f);
    public static readonly Color AccentSuccess = new(0.25f, 0.60f, 0.30f);
    public static readonly Color StatusBar = new(0.14f, 0.14f, 0.14f);
    public static readonly Color ActiveItem = new(0.24f, 0.42f, 0.62f);
    public static readonly Color DangerNormal = new(0.55f, 0.20f, 0.20f);
    public static readonly Color DangerHover = new(0.75f, 0.25f, 0.25f);
    public static readonly Color TransparentBlack = new(0, 0, 0, 0);

    // ===================== 尺寸 =====================
    public const float DefaultCardSize = 120f;
    public const float MinCardSize = 100f;
    public const float MaxCardSize = 160f;
    public const float ToolbarHeight = 32f;
    public const float StatusBarHeight = 22f;
    public const float SidebarMinWidth = 200f;
    public const float CardRadius = 6f;
    public const float ButtonRadius = 4f;
    public const float SearchHeight = 22f;

    // ===================== 列表列宽百分比 =====================
    public const float ColPct_Name = 30f;
    public const float ColPct_Type = 12f;
    public const float ColPct_Path = 38f;
    public const float ColPct_Size = 10f;
    public const float ColPct_Op = 10f;

    // ===================== 通用样式方法 =====================

    /// <summary>设置四边边框</summary>
    public static void SetBorder(VisualElement el, Color color, float width)
    {
        el.style.borderTopColor = color;
        el.style.borderBottomColor = color;
        el.style.borderLeftColor = color;
        el.style.borderRightColor = color;
        el.style.borderTopWidth = width;
        el.style.borderBottomWidth = width;
        el.style.borderLeftWidth = width;
        el.style.borderRightWidth = width;
    }

    /// <summary>设置四边圆角</summary>
    public static void SetRadius(VisualElement el, float r)
    {
        el.style.borderTopLeftRadius = r;
        el.style.borderTopRightRadius = r;
        el.style.borderBottomLeftRadius = r;
        el.style.borderBottomRightRadius = r;
    }

    /// <summary>为按钮添加悬停变色</summary>
    public static void AddHover(Button btn, Color normalColor, Color hoverColor)
    {
        btn.RegisterCallback<MouseEnterEvent>(_ => btn.style.backgroundColor = hoverColor);
        btn.RegisterCallback<MouseLeaveEvent>(_ => btn.style.backgroundColor = normalColor);
    }

    /// <summary>主样式按钮（带图标的主操作）</summary>
    public static void StylePrimary(Button btn, Color accent)
    {
        btn.style.backgroundColor = accent;
        btn.style.color = Color.white;
        btn.style.paddingTop = 4;
        btn.style.paddingBottom = 4;
        btn.style.paddingLeft = 12;
        btn.style.paddingRight = 12;
        SetRadius(btn, ButtonRadius);
        SetBorder(btn, new Color(0, 0, 0, 0.3f), 1);
        btn.style.unityFontStyleAndWeight = FontStyle.Bold;
        AddHover(btn, accent, accent * 1.2f);
    }

    /// <summary>普通按钮样式</summary>
    public static void StyleNormal(Button btn)
    {
        var normal = new Color(0.30f, 0.30f, 0.30f);
        btn.style.backgroundColor = normal;
        btn.style.color = TextPrimary;
        btn.style.paddingTop = 4;
        btn.style.paddingBottom = 4;
        btn.style.paddingLeft = 10;
        btn.style.paddingRight = 10;
        SetRadius(btn, ButtonRadius);
        SetBorder(btn, CardBorder, 1);
        AddHover(btn, normal, new Color(0.40f, 0.40f, 0.40f));
    }

    /// <summary>图标按钮（无文字的小按钮）</summary>
    public static void StyleIcon(Button btn, bool active)
    {
        btn.style.width = 28;
        btn.style.height = 22;
        btn.style.fontSize = 14;
        var activeColor = new Color(0.30f, 0.45f, 0.65f);
        var normalColor = new Color(0.25f, 0.25f, 0.25f);
        btn.style.backgroundColor = active ? activeColor : normalColor;
        btn.style.color = Color.white;
        SetRadius(btn, 3);
        SetBorder(btn, CardBorder, 1);
        if (!active) AddHover(btn, normalColor, new Color(0.35f, 0.35f, 0.35f));
    }

    /// <summary>弹性间隔</summary>
    public static VisualElement MakeSpacer(bool flex = true)
    {
        var sp = new VisualElement();
        if (flex) sp.style.flexGrow = 1;
        else sp.style.width = 10;
        sp.style.backgroundColor = Color.clear;
        return sp;
    }
}
