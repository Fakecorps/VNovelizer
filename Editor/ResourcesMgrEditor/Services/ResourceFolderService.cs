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

        string folder = (folderPath ?? "").Trim().Trim('/');
        if (folder.Length == 0) return "不能重命名类别根目录";

        newName = (newName ?? "").Trim().Trim('\\').Replace('\\', '/').Trim('/');
        string invalidError = VNAddressablesRegistrar.ValidateLogicalName(newName);
        if (invalidError != null) return invalidError;
        if (newName.Contains("/")) return "文件夹名称不能包含 '/'（一次只改一层）";

        int lastSlash = folder.LastIndexOf('/');
        string parent = lastSlash <= 0 ? "" : folder.Substring(0, lastSlash);
        string src = string.IsNullOrEmpty(parent) ? root + "/" + folder : root + "/" + parent + "/" + folder;
        string dst = string.IsNullOrEmpty(parent) ? root + "/" + newName : root + "/" + parent + "/" + newName;
        if (Directory.Exists(Path.GetFullPath(dst))) return $"目标文件夹已存在：{newName}";

        try
        {
            // 1) 物理目录改名（GUID/.meta 全保留；工作区内的条目地址由 Postprocessor 自动重派生）
            if (AssetDatabase.IsValidFolder(src))
            {
                string moveError = AssetDatabase.MoveAsset(src, dst);
                if (!string.IsNullOrEmpty(moveError)) return $"物理目录改名失败：{moveError}";
                AssetDatabase.Refresh();
                VNAddressablesRegistrar.SyncWorkspace();
            }

            // 2) 托管模式：仍挂旧地址前缀的条目 = 外部分配资产（物理不在类别文件夹内）→ 批量改地址
            if (VNAddressablesRegistrar.IsManagedMode)
            {
                string category = VNAddressablesRegistrar.GetCategoryKey(type);
                if (!string.IsNullOrEmpty(category))
                {
                    string oldPrefix = category + "/" + folder + "/";
                    string newPrefix = (string.IsNullOrEmpty(parent) ? newName : parent + "/" + newName) + "/";
                    var entries = VNAddressablesRegistrar.GetCategoryEntries(category).ToList();
                    foreach (var entry in entries)
                    {
                        if (entry.address == null || !entry.address.StartsWith(oldPrefix, StringComparison.Ordinal)) continue;
                        string tail = entry.address.Substring(oldPrefix.Length);
                        string renameError = VNAddressablesRegistrar.RenameAssignment(entry.AssetPath, newPrefix + tail);
                        if (renameError != null)
                            Debug.LogWarning($"[资源管理器] 外部分配条目改地址失败: {entry.AssetPath} → {renameError}");
                    }
                }
            }

            Debug.Log($"[资源管理器] 文件夹已重命名: {folder} → {newName}");
            return null;
        }
        catch (Exception e)
        {
            return $"重命名文件夹失败：{e.Message}";
        }
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

        string folder = (folderPath ?? "").Trim().Trim('/');
        if (folder.Length == 0) return "不能删除类别根目录";
        string dir = root + "/" + folder;

        // 1) 物理文件收集（含未注册文件与占位文件；排除 .meta——meta 随主文件一起移动）
        var physicalPaths = new List<string>();
        if (Directory.Exists(Path.GetFullPath(dir)))
        {
            foreach (string file in Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories))
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

        if (physicalPaths.Count == 0 && unassignOnly.Count == 0)
            return "文件夹不存在或为空";

        // 3) 移入回收站（物理文件 + 纯移除分配记录进同一条目）
        string title = $"{folder}（{physicalPaths.Count} 个文件" +
                       (unassignOnly.Count > 0 ? $" + {unassignOnly.Count} 个外部分配" : "") + "）";
        string error = ResourceTrashService.TrashPaths(physicalPaths, title, type, category, unassignOnly);
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
        string root = ResourceAssetService.GetPathFromConfig(type);
        if (string.IsNullOrEmpty(root)) return "未配置资源路径，请检查 VNProjectConfig";

        string target = (targetFolder ?? "").Trim().Trim('/').Replace('\\', '/').Trim();
        string fileName = Path.GetFileName(item.AssetPath ?? "");
        if (string.IsNullOrEmpty(fileName)) return "无效的资产路径";

        bool physicallyInCategory = item.AssetPath != null &&
            item.AssetPath.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase);

        if (physicallyInCategory)
        {
            string dst = string.IsNullOrEmpty(target) ? root + "/" + fileName : root + "/" + target + "/" + fileName;
            if (string.Equals(item.AssetPath, dst, StringComparison.OrdinalIgnoreCase)) return null; // 已在目标位置
            if (File.Exists(Path.GetFullPath(dst))) return $"目标文件夹已有同名文件：{fileName}";

            string dstParent = dst.Substring(0, dst.LastIndexOf('/'));
            VNProjectPaths.EnsureFolder(dstParent);

            string moveError = AssetDatabase.MoveAsset(item.AssetPath, dst);
            if (!string.IsNullOrEmpty(moveError)) return $"移动失败：{moveError}";
            AssetDatabase.Refresh();
            // 立即重扫工作区地址（不等 Postprocessor 的 delayCall——否则移动后短暂显示在旧文件夹）
            VNAddressablesRegistrar.SyncWorkspace();
            return null;
        }

        // 外部分配条目：只改地址（物理文件不动）
        if (!VNAddressablesRegistrar.IsManagedMode) return "该资源物理不在类别文件夹内，且当前非托管模式";
        string logicalName = string.IsNullOrEmpty(item.LogicalName) ? item.Name : item.LogicalName;
        string tail = logicalName != null && logicalName.Contains("/")
            ? logicalName.Substring(logicalName.LastIndexOf('/') + 1)
            : fileName;
        string newLogicalName = string.IsNullOrEmpty(target) ? tail : target + "/" + tail;
        return VNAddressablesRegistrar.RenameAssignment(item.AssetPath, newLogicalName);
    }

    // ===================== 内部工具 ====================

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
