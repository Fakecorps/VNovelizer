using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;

/// <summary>
/// 编辑器端按资源键定位资产：与运行时 VNResourceService 相同的键空间，返回资产本体/资产路径。
/// 查找顺序：
/// 1. Addressables：VNovelizer 组内地址匹配（地址 = 资源键，含包内默认资产）；
/// 2. 旧版 Assets/Resources 探测（按常见扩展名）；
/// 3. 包内默认资源目录探测（Runtime/PackageDefault）。
/// 供画廊编辑器等编辑器窗口使用——它们需要在非运行模式下找到与运行时同键的资产。
/// </summary>
public static class VNEditorResourceResolver
{
    /// <summary>资源键 → 资产本体（找不到返回 null）</summary>
    public static T LoadByKey<T>(string resourceKey) where T : Object
    {
        if (string.IsNullOrEmpty(resourceKey)) return null;

        string assetPath = KeyToAssetPath(resourceKey);
        if (!string.IsNullOrEmpty(assetPath))
            return AssetDatabase.LoadAssetAtPath<T>(assetPath);

        return null;
    }

    /// <summary>资源键 → 资产路径（找不到返回 null）</summary>
    public static string KeyToAssetPath(string resourceKey)
    {
        if (string.IsNullOrEmpty(resourceKey)) return null;

        // 1) Addressables 组内地址匹配
        string path = FindPathViaAddressables(resourceKey);
        if (!string.IsNullOrEmpty(path)) return path;

        // 2) 旧版 Assets/Resources 探测
        path = ProbeAssetPath("Assets/Resources/" + resourceKey);
        if (!string.IsNullOrEmpty(path)) return path;

        // 3) 包内默认资源探测
        var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(VNEditorResourceResolver).Assembly);
        if (packageInfo != null && !string.IsNullOrEmpty(packageInfo.name))
            path = ProbeAssetPath($"Packages/{packageInfo.name}/Runtime/PackageDefault/{resourceKey}");

        return path;
    }

    /// <summary>Addressables 组内按地址查找（VNovelizer 组；未初始化 Addressables 返回 null）</summary>
    private static string FindPathViaAddressables(string resourceKey)
    {
        // Settings 属性为纯加载（不创建资产文件）：未初始化的项目返回 null
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) return null;

        var group = settings.FindGroup(VNResourceKeys.GroupName);
        if (group == null) return null;

        foreach (var entry in group.entries)
        {
            if (entry != null && entry.address == resourceKey)
                return entry.AssetPath;
        }
        return null;
    }

    /// <summary>按常见扩展名探测资产路径（不含扩展名的键 → 实际文件）</summary>
    private static string ProbeAssetPath(string keyWithoutExtension)
    {
        string[] extensions =
        {
            ".asset", ".prefab", ".png", ".jpg", ".jpeg", ".mp3", ".wav", ".ogg",
            ".mat", ".csv", ".bytes", ".ttf", ".otf", ".controller", ".unity",
        };
        foreach (string ext in extensions)
        {
            string candidate = keyWithoutExtension + ext;
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
