using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 资源加载与排序服务。
/// 负责从 VNProjectConfig 获取路径、扫描资源、排序。
/// </summary>
public static class ResourceAssetService
{
    /// <summary>获取分类对应的 Asset 路径</summary>
    public static string GetPathFromConfig(ResType type)
    {
        var config = VNProjectConfig.Instance;
        if (config == null) return "";

        if (type == ResType.Video)
            return "Assets/StreamingAssets/" + config.VideoResPath;

        // 用户内容目录：工作区（新项目）或旧版 Assets/Resources/VNovelizerRes（存量项目）
        switch (type)
        {
            case ResType.Background: return VNProjectPaths.ResourceKeyToFolder(config.BackgroundResPath);
            case ResType.BGM: return VNProjectPaths.ResourceKeyToFolder(config.BgmResPath);
            case ResType.SFX: return VNProjectPaths.ResourceKeyToFolder(config.SFXResPath);
            case ResType.Voice: return VNProjectPaths.ResourceKeyToFolder(config.VoiceResPath);
            default: return "";
        }
    }

    /// <summary>
    /// 加载分类下全部资源（双模型数据源，见 Docs/VNResourceProviderRefactoring.md）：
    /// - Addressables 托管模式：枚举 VNovelizer 组内该类别 Label 的条目——资产的物理位置无关紧要，
    ///   逻辑名（Excel 索引名）= 条目地址尾段，由拖放分配指定；
    /// - 文件夹模式（旧版/未初始化 Addressables）：扫描类别文件夹（旧行为），
    ///   逻辑名 = 文件名。
    /// </summary>
    public static List<ResourceItem> LoadAssets(ResType type, string searchKeyword)
    {
        var result = new List<ResourceItem>();
        string kw = (searchKeyword ?? "").ToLower().Trim();

        // 视频始终是 StreamingAssets 原始文件（不经 Addressables）
        if (type == ResType.Video)
        {
            string videoPath = GetPathFromConfig(type);
            if (string.IsNullOrEmpty(videoPath) || !Directory.Exists(videoPath)) return result;

            string[] videoExt = { ".mp4", ".mov", ".webm", ".avi", ".asf", ".wmv" };
            var files = Directory.GetFiles(videoPath, "*.*", SearchOption.TopDirectoryOnly)
                .Where(f => videoExt.Contains(Path.GetExtension(f).ToLower()));
            foreach (var filePath in files)
            {
                string fileName = Path.GetFileNameWithoutExtension(filePath);
                if (!string.IsNullOrEmpty(kw) && !fileName.ToLower().Contains(kw)) continue;
                string assetPath = filePath.Replace('\\', '/');
                var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
                result.Add(BuildItem(asset, fileName, assetPath, filePath));
            }
            return result;
        }

        // Addressables 托管模式：组内条目（物理位置无关）
        string category = VNAddressablesRegistrar.GetCategoryKey(type);
        if (VNAddressablesRegistrar.IsManagedMode && !string.IsNullOrEmpty(category))
        {
            foreach (var entry in VNAddressablesRegistrar.GetCategoryEntries(category))
            {
                string logicalName = GetLogicalName(entry.address, category);
                if (string.IsNullOrEmpty(logicalName)) continue;
                if (!string.IsNullOrEmpty(kw) && !logicalName.ToLower().Contains(kw)) continue;

                string assetPath = entry.AssetPath;
                var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
                if (asset == null) continue;
                string fullPath = Path.Combine(Directory.GetCurrentDirectory(), assetPath);
                var item = BuildItem(asset, Path.GetFileNameWithoutExtension(assetPath), assetPath, fullPath);
                item.LogicalName = logicalName;
                result.Add(item);
            }

            // 合并类别文件夹中"尚未分配"的资产（旧项目残留/外部导入未注册等混合状态），
            // 按 AssetPath 去重：已注册条目优先（用其逻辑名）
            string folderPath = GetPathFromConfig(type);
            if (!string.IsNullOrEmpty(folderPath) && Directory.Exists(folderPath))
            {
                var assignedPaths = new HashSet<string>(result.ConvertAll(i => i.AssetPath), StringComparer.OrdinalIgnoreCase);
                string folderFilter = UIElementBuilder.GetSearchFilter(type);
                foreach (var guid in AssetDatabase.FindAssets(folderFilter, new[] { folderPath }))
                {
                    string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                    if (assignedPaths.Contains(assetPath)) continue;
                    string fileName = Path.GetFileNameWithoutExtension(assetPath);
                    if (!string.IsNullOrEmpty(kw) && !fileName.ToLower().Contains(kw)) continue;
                    var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
                    if (asset == null) continue;
                    string fullPath = Path.Combine(Directory.GetCurrentDirectory(), assetPath);
                    var item = BuildItem(asset, fileName, assetPath, fullPath);
                    item.LogicalName = fileName; // 未分配：逻辑名 = 文件名（文件夹兜底语义）
                    result.Add(item);
                }
            }
            return result;
        }

        // 文件夹模式（旧行为）：扫描类别文件夹
        string path = GetPathFromConfig(type);
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return result;

        string filter = UIElementBuilder.GetSearchFilter(type);
        string[] guids = AssetDatabase.FindAssets(filter, new[] { path });
        foreach (var guid in guids)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            string fileName = Path.GetFileNameWithoutExtension(assetPath);
            if (!string.IsNullOrEmpty(kw) && !fileName.ToLower().Contains(kw)) continue;
            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
            if (asset == null) continue;
            string fullPath = Path.Combine(Directory.GetCurrentDirectory(), assetPath);
            var item = BuildItem(asset, fileName, assetPath, fullPath);
            item.LogicalName = fileName; // 文件夹模式：逻辑名 = 文件名
            result.Add(item);
        }

        return result;
    }

    /// <summary>地址 → 逻辑名（剥掉类别前缀；格式不符返回 null——非本注册器托管的条目）</summary>
    private static string GetLogicalName(string address, string category)
    {
        if (string.IsNullOrEmpty(address)) return null;
        string prefix = category + "/";
        if (!address.StartsWith(prefix, StringComparison.Ordinal)) return null;
        string name = address.Substring(prefix.Length);
        return string.IsNullOrEmpty(name) ? null : name;
    }

    private static ResourceItem BuildItem(UnityEngine.Object asset, string name, string assetPath, string fullPath)
    {
        long size = 0;
        DateTime modified = DateTime.MinValue;
        try
        {
            if (File.Exists(fullPath))
            {
                var fi = new FileInfo(fullPath);
                size = fi.Length;
                modified = fi.LastWriteTime;
            }
        }
        catch { }

        return new ResourceItem
        {
            Asset = asset,
            Name = name,
            AssetPath = assetPath,
            FullPath = fullPath,
            FileSize = size,
            LastModified = modified
        };
    }

    /// <summary>排序资源列表（按逻辑名——剧本作者视角的名字）</summary>
    public static List<ResourceItem> Sort(List<ResourceItem> items, SortMode mode)
    {
        switch (mode)
        {
            case SortMode.NameAsc:
                return items.OrderBy(i => i.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
            case SortMode.NameDesc:
                return items.OrderByDescending(i => i.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
            case SortMode.DateNewest:
                return items.OrderByDescending(i => i.LastModified).ToList();
            case SortMode.DateOldest:
                return items.OrderBy(i => i.LastModified).ToList();
            default:
                return items;
        }
    }

    /// <summary>统计指定分类的资源数量（与 LoadAssets 同一双模型：组条目 ∪ 未分配文件夹资产，按路径去重）</summary>
    public static int CountAssets(ResType type)
    {
        if (type == ResType.Video)
        {
            string videoPath = GetPathFromConfig(type);
            if (string.IsNullOrEmpty(videoPath) || !Directory.Exists(videoPath)) return 0;
            string[] ext = { ".mp4", ".mov", ".webm", ".avi", ".asf", ".wmv" };
            return Directory.GetFiles(videoPath, "*.*", SearchOption.TopDirectoryOnly)
                .Count(f => ext.Contains(Path.GetExtension(f).ToLower()));
        }

        string category = VNAddressablesRegistrar.GetCategoryKey(type);
        if (VNAddressablesRegistrar.IsManagedMode && !string.IsNullOrEmpty(category))
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in VNAddressablesRegistrar.GetCategoryEntries(category))
                paths.Add(entry.AssetPath);

            string folderPath = GetPathFromConfig(type);
            if (!string.IsNullOrEmpty(folderPath) && Directory.Exists(folderPath))
            {
                foreach (var guid in AssetDatabase.FindAssets(UIElementBuilder.GetSearchFilter(type), new[] { folderPath }))
                    paths.Add(AssetDatabase.GUIDToAssetPath(guid));
            }
            return paths.Count;
        }

        // 文件夹模式
        string path = GetPathFromConfig(type);
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return 0;
        string filter = UIElementBuilder.GetSearchFilter(type);
        return AssetDatabase.FindAssets(filter, new[] { path }).Length;
    }
}
