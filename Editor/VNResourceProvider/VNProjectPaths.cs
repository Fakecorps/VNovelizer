using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 编辑器端用户内容目录解析：新项目工作区 Assets/VNovelizer/ 与旧版 Assets/Resources/VNovelizerRes/ 的统一切换。
///
/// 规则（见 Docs/VNResourceProviderRefactoring.md）：
/// - 旧目录存在 → 沿用旧目录（存量项目平滑兼容，运行时经 Resources 兜底继续可用）；
/// - 旧目录不存在 → 使用工作区（新项目默认，不往 Assets/Resources 塞内容）。
///
/// 工作区下的相对路径与资源键的映射：Assets/VNovelizer/{相对路径} ↔ 资源键 "VNovelizerRes/{相对路径}"。
/// </summary>
public static class VNProjectPaths
{
    /// <summary>新项目用户工作区根目录（用户自己放角色/背景/剧本等内容的推荐位置）</summary>
    public const string WorkspaceRoot = "Assets/VNovelizer";

    /// <summary>旧版用户内容根目录（初始化向导历史行为：复制包内默认内容到 Assets/Resources）</summary>
    public const string LegacyRoot = "Assets/Resources/VNovelizerRes";

    /// <summary>是否处于旧版兼容模式（旧目录存在）</summary>
    public static bool IsLegacyMode => Directory.Exists(LegacyRoot);

    /// <summary>用户内容根目录（旧目录存在时沿用，否则为工作区）</summary>
    public static string ContentRoot => IsLegacyMode ? LegacyRoot : WorkspaceRoot;

    /// <summary>工作区（或旧目录）下的类别文件夹。category 可含子路径，如 "Audio/Music/BGM"。</summary>
    public static string CategoryFolder(string category) => ContentRoot + "/" + category;

    // ---- 常用类别快捷属性 ----
    public static string CharactersFolder => CategoryFolder("Characters");
    public static string BackgroundsFolder => CategoryFolder("Backgrounds");
    public static string ScriptsFolder => CategoryFolder("VNScripts");
    public static string ExcelFolder => CategoryFolder("ExcelVNScripts");
    public static string BgmFolder => CategoryFolder("Audio/Music/BGM");
    public static string SfxFolder => CategoryFolder("Audio/SFX");
    public static string VoiceFolder => CategoryFolder("Audio/Voice");
    public static string ParticalFolder => CategoryFolder("VFX/Partical");
    public static string AnimationFolder => CategoryFolder("VFX/Animation");

    /// <summary>画廊数据容器文件夹（mode: "CG" / "Music" / "Scene"）</summary>
    public static string GalleryFolder(string mode) => CategoryFolder("GalleryContent/" + mode);

    /// <summary>
    /// 资源键 → 用户内容资产文件夹。
    /// 例："VNovelizerRes/GalleryContent/CG" 在旧模式下 → "Assets/Resources/VNovelizerRes/GalleryContent/CG"（与旧行为一致）；
    /// 在工作区模式下 → "Assets/VNovelizer/GalleryContent/CG"。
    /// </summary>
    public static string ResourceKeyToFolder(string resourceKey)
    {
        if (string.IsNullOrEmpty(resourceKey)) return ContentRoot;
        string prefix = VNResourceKeys.RootPrefix + "/";
        if (resourceKey.StartsWith(prefix))
            return ContentRoot + resourceKey.Substring(VNResourceKeys.RootPrefix.Length);
        return ContentRoot + "/" + resourceKey;
    }

    /// <summary>确保用户工作区目录结构存在（幂等；仅创建缺失文件夹，不写入任何文件）</summary>
    public static void EnsureWorkspaceFolders()
    {
        // 按父级在前的顺序声明，逐级创建
        string[] folders =
        {
            WorkspaceRoot,
            WorkspaceRoot + "/Backgrounds",
            WorkspaceRoot + "/Characters",
            WorkspaceRoot + "/VNScripts",
            WorkspaceRoot + "/ExcelVNScripts",
            WorkspaceRoot + "/Audio",
            WorkspaceRoot + "/Audio/Music",
            WorkspaceRoot + "/Audio/Music/BGM",
            WorkspaceRoot + "/Audio/SFX",
            WorkspaceRoot + "/Audio/Voice",
            WorkspaceRoot + "/VFX",
            WorkspaceRoot + "/VFX/Partical",
            WorkspaceRoot + "/VFX/Animation",
            WorkspaceRoot + "/GalleryContent",
            WorkspaceRoot + "/GalleryContent/CG",
            WorkspaceRoot + "/GalleryContent/Music",
            WorkspaceRoot + "/GalleryContent/Scene",
        };

        bool created = false;
        foreach (string folder in folders)
        {
            int lastSlash = folder.LastIndexOf('/');
            string parent = folder.Substring(0, lastSlash);
            string leaf = folder.Substring(lastSlash + 1);
            if (AssetDatabase.IsValidFolder(folder)) continue;
            if (!AssetDatabase.IsValidFolder(parent))
            {
                // 理论上不会发生（父级在前），防御性兜底
                Directory.CreateDirectory(folder);
                created = true;
                continue;
            }
            AssetDatabase.CreateFolder(parent, leaf);
            created = true;
        }
        if (created) AssetDatabase.Refresh();
    }

    /// <summary>确保某个 Assets 内文件夹存在（逐级创建；路径分隔符用 '/'）</summary>
    public static void EnsureFolder(string assetFolderPath)
    {
        if (string.IsNullOrEmpty(assetFolderPath) || AssetDatabase.IsValidFolder(assetFolderPath)) return;

        string[] segments = assetFolderPath.Split('/');
        // segments[0] == "Assets"
        string current = "Assets";
        for (int i = 1; i < segments.Length; i++)
        {
            string next = current + "/" + segments[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, segments[i]);
            current = next;
        }
    }
}
