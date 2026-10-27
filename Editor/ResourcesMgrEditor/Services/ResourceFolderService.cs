using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 文件夹操作服务（【子文件夹支持】配套功能）。
///
/// 语义：类别文件夹内的"文件夹"= 物理子目录（工作区/旧目录下所见即所得），
/// 新建时写入 _keep.txt 占位防 Unity 刷新时删除空目录（.txt 已被注册器排除、
/// t:AudioClip/t:Sprite 搜索不命中，资源管理器中完全隐形）。
///
/// 双模式适配：
/// - 物理移动（AssetDatabase.MoveAsset，GUID/.meta 全保留）后由
///   VNWorkspaceAssetPostprocessor 自动重扫工作区地址（子目录段随之进入地址）；
/// - 托管模式下"拖放分配"的外部条目（物理不在类别文件夹内）只改地址不动物理文件。
/// </summary>
public static class ResourceFolderService
{
    /// <summary>空文件夹占位文件名（.txt 在注册器 ExcludedExtensions 中，不会被注册/显示）</summary>
    public const string KeepFileName = "_keep.txt";

    // ===================== 新建 =====================

    /// <summary>
    /// 新建子文件夹（parentFolder 相对类别根，"" = 根；name 单层名称）。
    /// 返回 null = 成功，否则为错误信息。
    /// </summary>
    public static string CreateSubfolder(ResType type, string parentFolder, string name)
    {
        string root = ResourceAssetService.GetPathFromConfig(type);
        if (string.IsNullOrEmpty(root)) return "未配置资源路径，请检查 VNProjectConfig";

        name = (name ?? "").Trim().Trim('\\').Replace('\\', '/').Trim('/');
        string invalidError = VNAddressablesRegistrar.ValidateLogicalName(name);
        if (invalidError != null) return invalidError;
        if (name.Contains("/")) return "文件夹名称不能包含 '/'（一次只建一层）";

        string parent = (parentFolder ?? "").Trim().Trim('/');
        string target = string.IsNullOrEmpty(parent) ? root + "/" + name : root + "/" + parent + "/" + name;
        if (AssetDatabase.IsValidFolder(target) || Directory.Exists(Path.GetFullPath(target)))
            return $"同名文件夹已存在：{name}";

        try
        {
            VNProjectPaths.EnsureFolder(target);
            // 占位文件：目录里没有任何可导入资产时 Unity 会在刷新时删除空目录
            File.WriteAllText(Path.GetFullPath(target + "/" + KeepFileName), "");
            AssetDatabase.Refresh();
            Debug.Log($"[资源管理器] 已新建文件夹: {target}");
            return null;
        }
        catch (Exception e)
        {
            return $"创建文件夹失败：{e.Message}";
        }
    }

    // ===================== 重命名 =====================

    /// <summary>
    /// 重命名文件夹：物理目录改名（GUID 保留 + Postprocessor 自动重扫地址）+
    /// 托管模式下仍挂旧前缀的外部分配条目（拖放分配、物理不在类别文件夹内）批量改地址。
    /// 返回 null = 成功。
    /// </summary>
    public static string RenameFolder(ResType type, string folderPath, string newName)
    {
        string root = ResourceAssetService.GetPathFromConfig(type);
        if (string.IsNullOrEmpty(root)) return "未配置资源路径，请检查 VNProjectConfig";

        if (Application.isPlaying || type == ResType.Video) return "当前不能重命名资源文件夹";
        string folder = NormalizeFolder(folderPath);
        if (folder.Length == 0) return "不能重命名类别根目录";
        string invalidError = VNAddressablesRegistrar.ValidateLogicalName(folder);
        if (invalidError != null) return invalidError;
        newName = NormalizeFolder(newName);
        invalidError = VNAddressablesRegistrar.ValidateLogicalName(newName);
        if (invalidError != null) return invalidError;
        if (newName.Contains("/")) return "文件夹名称不能包含 '/'（一次只改一层）";
        int lastSlash = folder.LastIndexOf('/');
        string parent = lastSlash < 0 ? "" : folder.Substring(0, lastSlash);
        string renamedFolder = parent.Length == 0 ? newName : parent + "/" + newName;
        if (folder == renamedFolder) return null;
        string src = root + "/" + folder;
        string dst = root + "/" + renamedFolder;
        if (Directory.Exists(dst) || File.Exists(dst) || File.Exists(dst + ".meta")
            || ResourceAssetService.ListSubfolders(type, parent).Any(f =>
                string.Equals(f.Path, renamedFolder, StringComparison.OrdinalIgnoreCase)))
            return $"目标文件夹已存在：{newName}";
        try
        {
            string category = VNAddressablesRegistrar.GetCategoryKey(type);
            if (string.IsNullOrEmpty(category)) return "未配置资源类别";
            string oldPrefix = category + "/" + folder + "/";
            string newPrefix = category + "/" + renamedFolder + "/";
            var plans = new List<AddressPlan>();
            var physicalMoves = new List<KeyValuePair<string, string>>();
            var settings = UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.Settings;
            if (AssetDatabase.IsValidFolder(src))
            {
                string moveError = AssetDatabase.ValidateMoveAsset(src, dst);
                if (!string.IsNullOrEmpty(moveError)) return $"无法重命名物理文件夹：{moveError}";
                physicalMoves.Add(new KeyValuePair<string, string>(src, dst));
                foreach (string guid in AssetDatabase.FindAssets("", new[] { src }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (AssetDatabase.IsValidFolder(path) || IsExcludedFile(path)) continue;
                    var entry = settings != null ? settings.FindAssetEntry(guid) : null;
                    if (entry != null && entry.parentGroup != null && entry.parentGroup.Name != VNResourceKeys.GroupName) continue;
                    string destination = dst + path.Substring(src.Length);
                    string key = category + "/" + ResourceAssetService.GetRelativeLogicalName(root, destination);
                    bool manual = VNAddressablesRegistrar.HasManualAddressOverride(path);
                    if (manual && entry != null)
                        key = entry.address.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase)
                            ? newPrefix + entry.address.Substring(oldPrefix.Length) : entry.address;
                    plans.Add(CapturePlan(path, destination, key, manual));
                }
            }
            if (VNAddressablesRegistrar.IsManagedMode)
            {
                foreach (var entry in VNAddressablesRegistrar.GetCategoryEntries(category))
                {
                    if (string.IsNullOrEmpty(entry.address) || !entry.address.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                    if (plans.Any(p => p.Guid == entry.guid)) continue;
                    if (entry.AssetPath.StartsWith(src + "/", StringComparison.OrdinalIgnoreCase)) continue;
                    plans.Add(CapturePlan(entry.AssetPath, entry.AssetPath,
                        newPrefix + entry.address.Substring(oldPrefix.Length), true));
                }
            }
            if (physicalMoves.Count == 0 && plans.Count == 0) return "文件夹不存在";
            string error = ValidatePlans(type, plans);
            return error ?? ExecutePlans(plans, physicalMoves);
        }
        catch (Exception e) { return $"重命名文件夹失败：{e.Message}"; }
    }

    // ===================== 删除（进回收站） =====================

    /// <summary>
    /// 删除文件夹：物理文件（含 _keep.txt 与未注册文件）全部移入回收站；
    /// 托管模式下物理不在文件夹内、但地址挂在文件夹前缀下的外部分配条目仅移除分配
    /// （物理文件保留，随回收站条目记录地址，恢复时可还原分配）。
    /// 返回 (error, trashedPhysicalCount, unassignedCount)；error 非 null = 失败。
    /// </summary>
    public static string DeleteFolder(ResType type, string folderPath, out int physicalCount, out int unassignCount)
    {
        physicalCount = 0;
        unassignCount = 0;
        string root = ResourceAssetService.GetPathFromConfig(type);
        if (string.IsNullOrEmpty(root)) return "未配置资源路径，请检查 VNProjectConfig";

        if (Application.isPlaying || type == ResType.Video) return "当前不能删除资源文件夹";
        string folder = NormalizeFolder(folderPath);
        if (folder.Length == 0) return "不能删除类别根目录";
        string invalidError = VNAddressablesRegistrar.ValidateLogicalName(folder);
        if (invalidError != null) return invalidError;
        string dir = root + "/" + folder;

        // 1) 物理文件收集（含未注册文件与占位文件；排除 .meta——meta 随主文件一起移动）
        var physicalPaths = new List<string>();
        var directoryPaths = new List<string>();
        if (Directory.Exists(Path.GetFullPath(dir)))
        {
            directoryPaths.Add(dir);
            foreach (string file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
            {
                if (file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                physicalPaths.Add(ToAssetPath(file));
            }
        }

        // 2) 外部分配条目收集（地址前缀匹配 + 物理不在该文件夹下）
        var unassignOnly = new List<string>();
        string category = VNAddressablesRegistrar.GetCategoryKey(type);
        if (VNAddressablesRegistrar.IsManagedMode && !string.IsNullOrEmpty(category))
        {
            string prefix = category + "/" + folder + "/";
            foreach (var entry in VNAddressablesRegistrar.GetCategoryEntries(category))
            {
                if (entry.address == null || !entry.address.StartsWith(prefix, StringComparison.Ordinal)) continue;
                if (entry.AssetPath != null &&
                    entry.AssetPath.StartsWith(dir + "/", StringComparison.OrdinalIgnoreCase)) continue; // 物理在文件夹内（已被收集）
                unassignOnly.Add(entry.AssetPath);
            }
        }

        if (physicalPaths.Count == 0 && unassignOnly.Count == 0 && directoryPaths.Count == 0)
            return "文件夹不存在";

        // 3) 目录结构与每级 meta 同文件、外部分配快照一并进入回收站。
        string title = $"{folder}（{physicalPaths.Count} 个文件" +
                       (unassignOnly.Count > 0 ? $" + {unassignOnly.Count} 个外部分配" : "") + "）";
        string error = ResourceTrashService.TrashPaths(physicalPaths, title, type, category, unassignOnly, directoryPaths);
        if (error != null) return error;

        physicalCount = physicalPaths.Count;
        unassignCount = unassignOnly.Count;
        return null;
    }

    // ===================== 移动文件 =====================

    /// <summary>
    /// 把条目移动到文件夹（targetFolder 相对类别根，"" = 移回类别根）：
    /// - 物理文件在类别文件夹内 → AssetDatabase.MoveAsset 物理移动（地址由 Postprocessor 自动重派生）；
    /// - 外部分配条目（拖放分配，物理不在类别文件夹内）→ 只改地址（逻辑名前缀替换）。
    /// 返回 null = 成功。
    /// </summary>
    public static string MoveItemToFolder(ResType type, ResourceItem item, string targetFolder)
    {
        return MoveItemsToFolder(new List<ResourceItem> { item }, type, targetFolder);
    }

    /// <summary>全批纯预检：物理目标、逻辑同键（含不同扩展名）、全局地址占用。</summary>
    public static string ValidateMoveItems(List<ResourceItem> items, ResType type, string targetFolder)
    {
        try { return BuildMovePlans(items, type, targetFolder, out _); }
        catch (Exception e) { return $"移动预检失败：{e.Message}"; }
    }

    /// <summary>全批事务；任一物理移动或注册失败均恢复原路径、地址及全部标签。</summary>
    public static string MoveItemsToFolder(List<ResourceItem> items, ResType type, string targetFolder)
    {
        try
        {
            string error = BuildMovePlans(items, type, targetFolder, out var plans);
            if (error != null) return error;
            var moves = plans.Where(p => !string.Equals(p.Source, p.Target, StringComparison.OrdinalIgnoreCase))
                .Select(p => new KeyValuePair<string, string>(p.Source, p.Target)).ToList();
            return ExecutePlans(plans, moves);
        }
        catch (Exception e) { return $"移动预检失败：{e.Message}"; }
    }

    private static string BuildMovePlans(List<ResourceItem> items, ResType type, string targetFolder, out List<AddressPlan> plans)
    {
        plans = new List<AddressPlan>();
        if (Application.isPlaying || type == ResType.Video) return "当前不能移动资源文件";
        if (items == null || items.Count == 0) return "请选择要移动的资源";
        string root = ResourceAssetService.GetPathFromConfig(type);
        string category = VNAddressablesRegistrar.GetCategoryKey(type);
        if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(category)) return "未配置资源路径或类别";
        string target = NormalizeFolder(targetFolder);
        if (target.Length > 0)
        {
            string error = VNAddressablesRegistrar.ValidateLogicalName(target);
            if (error != null) return error;
        }
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var settings = UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.Settings;
        foreach (var item in items)
        {
            if (string.IsNullOrEmpty(item.AssetPath)) return "无效的资产路径";
            string path = item.AssetPath.Replace('\\', '/');
            if (AssetDatabase.IsValidFolder(path)) return "请选择文件而非文件夹";
            string guid = AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrEmpty(guid)) return $"资产尚未导入：{path}";
            if (!seen.Add(guid)) continue;
            var entry = settings != null ? settings.FindAssetEntry(guid) : null;
            if (entry != null && entry.parentGroup != null && entry.parentGroup.Name != VNResourceKeys.GroupName)
                return $"资产已在其他 Addressables 组中：{path}";
            bool physical = path.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase);
            if (!physical && !VNAddressablesRegistrar.IsManagedMode) return "外部分配资源只能在托管模式下移动";
            string logicalName = !string.IsNullOrEmpty(item.LogicalName) ? item.LogicalName : item.Name;
            if (string.IsNullOrEmpty(logicalName)) logicalName = Path.GetFileNameWithoutExtension(path);
            string tail = Path.GetFileName(logicalName.Replace('\\', '/'));
            string newLogicalName = target.Length == 0 ? tail : target + "/" + tail;
            string destination = physical ? root + "/" + (target.Length == 0 ? "" : target + "/") + Path.GetFileName(path) : path;
            if (!string.Equals(path, destination, StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(destination) || Directory.Exists(destination) || File.Exists(destination + ".meta"))
                    return $"目标已有同名资产：{destination}";
                string moveError = AssetDatabase.ValidateMoveAsset(path, destination);
                string destinationParent = Path.GetDirectoryName(destination)?.Replace('\\', '/');
                for (string dir = destinationParent; !string.IsNullOrEmpty(dir) && !AssetDatabase.IsValidFolder(dir);
                     dir = Path.GetDirectoryName(dir)?.Replace('\\', '/'))
                    if (File.Exists(dir) || File.Exists(dir + ".meta")) return $"目标目录路径被文件或残留 meta 占用：{dir}";
                if (!string.IsNullOrEmpty(moveError) && AssetDatabase.IsValidFolder(destinationParent))
                    return $"无法移动：{moveError}";
            }
            bool manual = !physical || VNAddressablesRegistrar.HasManualAddressOverride(path)
                || !string.Equals(newLogicalName, ResourceAssetService.GetRelativeLogicalName(root, destination), StringComparison.Ordinal);
            plans.Add(CapturePlan(path, destination, category + "/" + newLogicalName, manual));
        }
        return ValidatePlans(type, plans);
    }

    // ===================== 内部工具 ====================

    private sealed class AddressPlan
    {
        public string Source, Target, Guid, Key, OldAddress;
        public bool Manual, OldSuppressed;
        public HashSet<string> OldLabels;
    }

    private static AddressPlan CapturePlan(string source, string target, string key, bool manual)
    {
        string guid = AssetDatabase.AssetPathToGUID(source);
        var settings = UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.Settings;
        var entry = settings != null ? settings.FindAssetEntry(guid) : null;
        string suppressionError = VNAddressablesRegistrar.GetAutomaticRegistrationSuppressedChecked(guid, out bool suppressed);
        if (suppressionError != null) throw new InvalidOperationException(suppressionError);
        return new AddressPlan
        {
            Source = source, Target = target, Guid = guid, Key = key, Manual = manual,
            OldSuppressed = suppressed,
            OldAddress = entry?.address,
            OldLabels = entry != null ? new HashSet<string>(entry.labels) : null,
        };
    }

    private static string ValidatePlans(ResType type, List<AddressPlan> plans)
    {
        var changes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var destinations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var plan in plans)
        {
            if (string.IsNullOrEmpty(plan.Guid)) return $"资产尚未导入：{plan.Source}";
            if (destinations.TryGetValue(plan.Target, out var owner) && owner != plan.Guid)
                return $"批内物理目标冲突：{plan.Target}";
            destinations[plan.Target] = plan.Guid;
            if (!plan.OldSuppressed) changes[plan.Source] = plan.Key;
        }
        string category = VNAddressablesRegistrar.GetCategoryKey(type);
        var settings = UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.Settings;
        foreach (var item in ResourceAssetService.LoadAssets(type, null))
        {
            if (changes.ContainsKey(item.AssetPath)) continue;
            string guid = AssetDatabase.AssetPathToGUID(item.AssetPath);
            if (settings != null && settings.FindAssetEntry(guid) != null) continue;
            changes[item.AssetPath] = category + "/" + (item.LogicalName ?? item.Name);
        }
        return VNAddressablesRegistrar.ValidateAddressChanges(changes);
    }

    private static string ExecutePlans(List<AddressPlan> plans, List<KeyValuePair<string, string>> physicalMoves)
    {
        var moved = new List<KeyValuePair<string, string>>();
        var createdFolders = new List<string>();
        bool managed = VNAddressablesRegistrar.IsManagedMode;
        VNAddressablesRegistrar.SuppressAutomaticRegistrationForCurrentImportBatch(plans.Select(p => p.Guid));
        try
        {
            foreach (var move in physicalMoves)
            {
                string parent = Path.GetDirectoryName(move.Value)?.Replace('\\', '/');
                var missing = new List<string>();
                for (string dir = parent; !string.IsNullOrEmpty(dir) && !AssetDatabase.IsValidFolder(dir);
                     dir = Path.GetDirectoryName(dir)?.Replace('\\', '/'))
                    missing.Add(dir);
                missing.Reverse();
                createdFolders.AddRange(missing);
                VNProjectPaths.EnsureFolder(parent);
                string error = AssetDatabase.MoveAsset(move.Key, move.Value);
                if (!string.IsNullOrEmpty(error)) throw new InvalidOperationException(error);
                moved.Add(move);
            }
            if (managed)
            {
                foreach (var plan in plans)
                {
                    string currentPath = AssetDatabase.GUIDToAssetPath(plan.Guid);
                    if (!string.Equals(currentPath, plan.Target, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException($"移动后的 GUID 路径不匹配：{plan.Source}");
                    if (plan.OldSuppressed) continue;
                    string error = VNAddressablesRegistrar.RegisterAssetAtPathChecked(currentPath, plan.Key, plan.Manual);
                    if (error != null) throw new InvalidOperationException(error);
                }
            }
            AssetDatabase.SaveAssets();
            VNResourceService.Reset();
            return null;
        }
        catch (Exception e)
        {
            var rollbackErrors = new List<string>();
            for (int i = moved.Count - 1; i >= 0; i--)
            {
                try
                {
                    string error = AssetDatabase.MoveAsset(moved[i].Value, moved[i].Key);
                    if (!string.IsNullOrEmpty(error)) rollbackErrors.Add(error);
                }
                catch (Exception rollback) { rollbackErrors.Add(rollback.Message); }
            }
            if (managed)
            {
                var settings = UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.Settings;
                foreach (var plan in plans)
                {
                    try
                    {
                        string suppressionError = VNAddressablesRegistrar.SetAutomaticRegistrationSuppressedChecked(plan.Guid, plan.OldSuppressed);
                        if (suppressionError != null) throw new InvalidOperationException(suppressionError);
                        var entry = settings.FindAssetEntry(plan.Guid);
                        if (plan.OldLabels == null)
                        {
                            if (entry != null) settings.RemoveAssetEntry(plan.Guid);
                            continue;
                        }
                        if (entry == null) throw new InvalidOperationException($"回滚条目缺失：{plan.Source}");
                        entry.SetAddress(plan.OldAddress);
                        foreach (string label in entry.labels.ToList()) entry.SetLabel(label, false);
                        foreach (string label in plan.OldLabels) entry.SetLabel(label, true, true);
                    }
                    catch (Exception rollback) { rollbackErrors.Add(rollback.Message); }
                }
            }
            for (int i = createdFolders.Count - 1; i >= 0; i--)
            {
                try
                {
                    string dir = createdFolders[i];
                    if (Directory.Exists(dir) && Directory.GetFileSystemEntries(dir).Length == 0
                        && !AssetDatabase.DeleteAsset(dir)) rollbackErrors.Add($"无法清理新建空目录：{dir}");
                }
                catch (Exception rollback) { rollbackErrors.Add(rollback.Message); }
            }
            try { AssetDatabase.SaveAssets(); VNResourceService.Reset(); }
            catch (Exception rollback) { rollbackErrors.Add(rollback.Message); }
            return $"操作失败：{e.Message}" + (rollbackErrors.Count == 0 ? "（已回滚）"
                : "；回滚未完成，请检查：" + string.Join("；", rollbackErrors));
        }
    }

    private static bool IsExcludedFile(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext == ".xlsx" || ext == ".md" || ext == ".txt" || ext == ".meta";
    }

    private static string NormalizeFolder(string folder)
    {
        return (folder ?? "").Trim().Replace('\\', '/').Trim('/');
    }

    /// <summary>系统绝对路径 → Assets 相对路径（'/' 分隔）</summary>
    private static string ToAssetPath(string absolutePath)
    {
        string unified = absolutePath.Replace('\\', '/');
        string assetsRoot = Application.dataPath.Replace('\\', '/').TrimEnd('/') + "/";
        return unified.StartsWith(assetsRoot, StringComparison.OrdinalIgnoreCase)
            ? "Assets/" + unified.Substring(assetsRoot.Length)
            : unified;
    }
}
