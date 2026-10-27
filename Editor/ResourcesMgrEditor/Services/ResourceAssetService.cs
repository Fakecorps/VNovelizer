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
            var settings = UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.Settings;
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
                string folderFilter = UIElementBuilder.GetSearchFilter(type);
                foreach (var guid in AssetDatabase.FindAssets(folderFilter, new[] { folderPath }))
                {
                    string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                    if (settings.FindAssetEntry(guid) != null || VNAddressablesRegistrar.IsAutomaticRegistrationSuppressed(guid)) continue;
                    // 【子文件夹支持】未分配资产的逻辑名 = 相对类别根的路径（去扩展名），
                    // 与 Excel 中应填写的名字一致（旧逻辑只取裸文件名，会丢掉子目录段）
                    string logicalName = GetRelativeLogicalName(folderPath, assetPath);
                    if (!string.IsNullOrEmpty(kw) && !logicalName.ToLower().Contains(kw)) continue;
                    var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
                    if (asset == null) continue;
                    string fullPath = Path.Combine(Directory.GetCurrentDirectory(), assetPath);
                    var item = BuildItem(asset, Path.GetFileNameWithoutExtension(assetPath), assetPath, fullPath);
                    item.LogicalName = logicalName;
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
            // 【子文件夹支持】文件夹模式逻辑名同样改为相对路径（旧逻辑只取裸文件名）
            string logicalName = GetRelativeLogicalName(path, assetPath);
            if (!string.IsNullOrEmpty(kw) && !logicalName.ToLower().Contains(kw)) continue;
            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
            if (asset == null) continue;
            string fullPath = Path.Combine(Directory.GetCurrentDirectory(), assetPath);
            var item = BuildItem(asset, Path.GetFileNameWithoutExtension(assetPath), assetPath, fullPath);
            item.LogicalName = logicalName; // 文件夹模式：逻辑名 = 相对路径
            result.Add(item);
        }

        return result;
    }

    /// <summary>
    /// 类别文件夹内的资产路径 → 相对类别根的逻辑名（去扩展名、'/' 分隔）。
    /// 资产不在该文件夹下时回退裸文件名（防御，理论上不会发生——调用方均以该文件夹为扫描根）。
    /// </summary>
    public static string GetRelativeLogicalName(string categoryFolderPath, string assetPath)
    {
        assetPath = assetPath?.Replace('\\', '/');
        string fileName = Path.GetFileNameWithoutExtension(assetPath);
        if (string.IsNullOrEmpty(categoryFolderPath)) return fileName;
        string prefix = categoryFolderPath.Replace('\\', '/').TrimEnd('/') + "/";
        if (assetPath != null && assetPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            string rel = assetPath.Substring(prefix.Length).TrimEnd('/');
            string extension = Path.GetExtension(rel);
            return rel.Substring(0, rel.Length - extension.Length);
        }
        return fileName;
    }

    /// <summary>
    /// 【子文件夹支持】枚举当前文件夹下的直接子文件夹（逻辑名派生 ∪ 物理子目录）：
    /// - 逻辑名派生：托管条目 / 文件夹兜底条目的逻辑名按 '/' 拆层——
    ///   覆盖"拖放分配到子文件夹逻辑名"的外部资产（物理位置无关）；
    /// - 物理子目录：覆盖空文件夹（只含 _keep.txt 等被排除文件的目录，无条目可派生）。
    /// 返回的 ResourceFolderInfo 已按名称排序。
    /// </summary>
    public static List<ResourceFolderInfo> ListSubfolders(ResType type, string currentFolder)
    {
        var result = new List<ResourceFolderInfo>();
        if (type == ResType.Video) return result;
        string folder = (currentFolder ?? "").Trim().Trim('/').Replace('\\', '/').Trim('/');
        string prefix = folder.Length == 0 ? "" : folder + "/";
        var subfolders = new Dictionary<string, ResourceFolderInfo>(StringComparer.OrdinalIgnoreCase);
        var childNames = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        // 1) 从全部条目的逻辑名派生（含托管 + 文件夹兜底双模式）
        foreach (var item in LoadAssets(type, null))
        {
            string ln = item.LogicalName ?? item.Name;
            if (string.IsNullOrEmpty(ln)) continue;
            if (!ln.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            string rest = ln.Substring(prefix.Length);
            int slash = rest.IndexOf('/');
            if (slash <= 0) continue; // 当前层文件（slash == -1）或异常空段（slash == 0）

            string name = rest.Substring(0, slash);
            if (!subfolders.TryGetValue(name, out var info))
            {
                info = new ResourceFolderInfo
                {
                    Path = prefix + name,
                    Name = name,
                    ItemCount = 0,
                    SubfolderCount = 0,
                };
                subfolders.Add(name, info);
            }
            string after = rest.Substring(slash + 1);
            int childSlash = after.IndexOf('/');
            if (childSlash > 0)
            {
                if (!childNames.TryGetValue(name, out var children))
                    childNames[name] = children = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                children.Add(after.Substring(0, childSlash));
            }
            else if (after.Length > 0) info.ItemCount++;
        }

        // 2) 物理子目录（覆盖空文件夹；与逻辑名派生去重合并）
        string root = GetPathFromConfig(type);
        if (!string.IsNullOrEmpty(root))
        {
            string dir = folder.Length == 0 ? root : root + "/" + folder;
            if (Directory.Exists(dir))
            {
                foreach (var sub in Directory.GetDirectories(dir))
                {
                    string name = Path.GetFileName(sub);
                    if (!subfolders.TryGetValue(name, out var info))
                    {
                        info = new ResourceFolderInfo { Path = prefix + name, Name = name };
                        subfolders.Add(name, info);
                    }
                    if (!childNames.TryGetValue(name, out var children))
                        childNames[name] = children = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (string child in Directory.GetDirectories(sub))
                        children.Add(Path.GetFileName(child));
                }
            }
        }

        foreach (var pair in subfolders)
        {
            pair.Value.SubfolderCount = childNames.TryGetValue(pair.Key, out var directChildren) ? directChildren.Count : 0;
        }
        result.AddRange(subfolders.Values);
        result.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));
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
        var settings = VNAddressablesRegistrar.IsManagedMode
            ? UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.Settings : null;
        var guids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (settings != null && !string.IsNullOrEmpty(category))
        {
            foreach (var entry in VNAddressablesRegistrar.GetCategoryEntries(category))
            {
                if (string.IsNullOrEmpty(GetLogicalName(entry.address, category))) continue;
                if (AssetDatabase.GetMainAssetTypeAtPath(entry.AssetPath) == null) continue;
                guids.Add(entry.guid);
            }
        }
        string folder = GetPathFromConfig(type);
        if (!string.IsNullOrEmpty(folder) && AssetDatabase.IsValidFolder(folder))
        {
            foreach (string guid in AssetDatabase.FindAssets(UIElementBuilder.GetSearchFilter(type), new[] { folder }))
            {
                if (settings != null && (settings.FindAssetEntry(guid) != null
                    || VNAddressablesRegistrar.IsAutomaticRegistrationSuppressed(guid))) continue;
                guids.Add(guid);
            }
        }
        return guids.Count;
    }
}
