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

    /// <summary>加载指定路径下的所有资源（不依赖路径是否已存在）</summary>
    public static List<ResourceItem> LoadAssets(ResType type, string searchKeyword)
    {
        var result = new List<ResourceItem>();
        string path = GetPathFromConfig(type);
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return result;

        string kw = (searchKeyword ?? "").ToLower().Trim();

        if (type == ResType.Video)
        {
            // 视频是 StreamingAssets 中的原始文件
            string[] videoExt = { ".mp4", ".mov", ".webm", ".avi", ".asf", ".wmv" };
            var files = Directory.GetFiles(path, "*.*", SearchOption.TopDirectoryOnly)
                .Where(f => videoExt.Contains(Path.GetExtension(f).ToLower()));
            foreach (var filePath in files)
            {
                string fileName = Path.GetFileNameWithoutExtension(filePath);
                if (!string.IsNullOrEmpty(kw) && !fileName.ToLower().Contains(kw)) continue;
                string assetPath = filePath.Replace('\\', '/');
                var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
                result.Add(BuildItem(asset, fileName, assetPath, filePath));
            }
        }
        else
        {
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
                result.Add(BuildItem(asset, fileName, assetPath, fullPath));
            }
        }

        return result;
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

    /// <summary>排序资源列表</summary>
    public static List<ResourceItem> Sort(List<ResourceItem> items, SortMode mode)
    {
        switch (mode)
        {
            case SortMode.NameAsc:
                return items.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
            case SortMode.NameDesc:
                return items.OrderByDescending(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
            case SortMode.DateNewest:
                return items.OrderByDescending(i => i.LastModified).ToList();
            case SortMode.DateOldest:
                return items.OrderBy(i => i.LastModified).ToList();
            default:
                return items;
        }
    }

    /// <summary>统计指定分类的资源数量</summary>
    public static int CountAssets(ResType type)
    {
        string path = GetPathFromConfig(type);
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return 0;

        if (type == ResType.Video)
        {
            string[] ext = { ".mp4", ".mov", ".webm", ".avi", ".asf", ".wmv" };
            return Directory.GetFiles(path, "*.*", SearchOption.TopDirectoryOnly)
                .Count(f => ext.Contains(Path.GetExtension(f).ToLower()));
        }
        else
        {
            string filter = UIElementBuilder.GetSearchFilter(type);
            return AssetDatabase.FindAssets(filter, new[] { path }).Length;
        }
    }
}
