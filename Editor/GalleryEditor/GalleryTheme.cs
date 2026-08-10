using UnityEngine;

/// <summary>
/// 画廊编辑器统一视觉令牌（深色专业主题）。
/// </summary>
public static class GalleryTheme
{
    public const string BgPrimary = "#1e1e1e";
    public const string BgSecondary = "#252526";
    public const string BgCard = "#2d2d30";
    public const string BgHover = "#333334";
    public const string Accent = "#4a9eff";
    public const string AccentDim = "#2d5a8e";
    public const string Success = "#4ec9b0";
    public const string Danger = "#f14c4c";
    public const string Warning = "#e2c08d";
    public const string TextPrimary = "#e0e0e0";
    public const string TextSecondary = "#a0a0a0";
    public const string TextMuted = "#6a6a6a";
    public const string Border = "#3f3f46";

    public const string Transparent = "#00000000";

    public static readonly Color Transparent_Color = new Color(0, 0, 0, 0);

    public static Color Hex(string hex)
    {
        Color c;
        ColorUtility.TryParseHtmlString(hex, out c);
        return c;
    }
}