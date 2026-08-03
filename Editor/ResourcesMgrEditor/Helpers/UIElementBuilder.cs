using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// UI 元素构建辅助工具 - 图标、按钮的便捷构造。
/// 集中处理 Unity 版本差异（同名图标多个 fallback）。
/// </summary>
public static class UIElementBuilder
{
    /// <summary>
    /// 安全获取 Unity 内置图标。按顺序尝试多个名称以兼容不同 Unity 版本。
    /// </summary>
    public static Texture GetIcon(params string[] names)
    {
        foreach (var name in names)
        {
            if (string.IsNullOrEmpty(name)) continue;
            var content = EditorGUIUtility.IconContent(name);
            if (content != null && content.image != null) return content.image;
        }
        return null;
    }

    /// <summary>按资源类型返回对应的 Unity 内置图标</summary>
    public static Texture GetTypeIconTexture(ResType type)
    {
        switch (type)
        {
            case ResType.Background: return GetIcon("Sprite Icon", "Sprite", "d_Sprite", "Image Icon");
            case ResType.Video:      return GetIcon("VideoClip Icon", "VideoClip", "Movie Icon", "d_VideoClip Icon");
            case ResType.BGM:        return GetIcon("AudioClip Icon", "AudioClip", "d_AudioClip Icon");
            case ResType.SFX:        return GetIcon("AudioClip Icon", "AudioClip", "d_AudioClip Icon");
            case ResType.Voice:      return GetIcon("AudioClip Icon", "AudioClip", "d_AudioClip Icon");
            default:                 return GetIcon("DefaultAsset Icon", "DefaultAsset", "d_DefaultAsset Icon");
        }
    }

    /// <summary>分类的中文显示名</summary>
    public static string GetTypeDisplayName(ResType type)
    {
        switch (type)
        {
            case ResType.Background: return "背景";
            case ResType.Video: return "视频";
            case ResType.BGM: return "背景音乐";
            case ResType.SFX: return "音效";
            case ResType.Voice: return "语音";
            default: return type.ToString();
        }
    }

    /// <summary>分类的图标符号（仅用于 ListView 内部 fallback）</summary>
    public static string GetSortModeName(SortMode mode)
    {
        switch (mode)
        {
            case SortMode.NameAsc: return "名称↑";
            case SortMode.NameDesc: return "名称↓";
            case SortMode.DateNewest: return "最新修改";
            case SortMode.DateOldest: return "最早修改";
            default: return "";
        }
    }

    /// <summary>分类支持的文件扩展名描述</summary>
    public static string GetExtensionDescription(ResType type)
    {
        switch (type)
        {
            case ResType.Background: return "png, jpg, jpeg, tga";
            case ResType.Video: return "mp4, mov, webm, avi";
            case ResType.BGM:
            case ResType.SFX:
            case ResType.Voice: return "mp3, wav, ogg, aiff";
            default: return "*";
        }
    }

    /// <summary>分类支持的文件扩展名数组（用于 OpenFilePanel 过滤）</summary>
    public static string[] GetExtensionsList(ResType type)
    {
        switch (type)
        {
            case ResType.Background: return new[] { "png", "jpg", "jpeg", "tga" };
            case ResType.BGM:
            case ResType.SFX:
            case ResType.Voice: return new[] { "mp3", "wav", "ogg", "aiff" };
            case ResType.Video: return new[] { "mp4", "mov", "webm", "avi" };
            default: return System.Array.Empty<string>();
        }
    }

    /// <summary>AssetDatabase 搜索过滤字符串</summary>
    public static string GetSearchFilter(ResType type)
    {
        if (type == ResType.Background) return "t:Sprite";
        if (type == ResType.BGM || type == ResType.SFX || type == ResType.Voice) return "t:AudioClip";
        if (type == ResType.Video) return "";
        return "t:Object";
    }

    /// <summary>创建一个带图标的按钮（避免 emoji 字体问题）</summary>
    public static Button MakeIconButton(Texture icon, string text, System.Action onClick = null)
    {
        var btn = new Button(onClick) { text = "" };
        if (icon != null)
        {
            var img = new Image();
            img.image = icon;
            img.style.width = 14;
            img.style.height = 14;
            img.style.marginRight = 4;
            btn.Add(img);
        }
        if (!string.IsNullOrEmpty(text))
        {
            var lbl = new Label(text);
            btn.Add(lbl);
        }
        return btn;
    }
}
