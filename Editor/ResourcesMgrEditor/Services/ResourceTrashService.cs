using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 回收站服务（软删除，【子文件夹支持】配套功能）。
///
/// 删除的资产（文件 + .meta）移到项目根的 .VNovelizerTrash/（Assets 外，Unity 不导入），
/// 以 JSON 清单记录原资产路径与原 Addressables 地址：
/// - GUID 不丢（.meta 随行），恢复后引用关系完整；
/// - 恢复 = 移回原路径 + 按原地址重新注册（托管模式）；
/// - 仅手动清空，不自动过期；跨 Unity 重启有效。
///
/// 「删除文件夹」场景中物理不在文件夹内的外部分配条目（拖放分配、文件留原位）：
/// 不删除其物理文件，仅移除分配并记录待恢复地址，恢复时重新分配。
/// </summary>
public static class ResourceTrashService
{
    /// <summary>项目根（Assets 的父目录）</summary>
    private static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName.Replace('\\', '/');

    /// <summary>回收站根目录（Assets 外，Unity 忽略 '.' 开头目录不导入）</summary>
    public static string TrashRoot => ProjectRoot + "/.VNovelizerTrash";

    private static string ManifestPath => TrashRoot + "/manifest.json";

    // ==================== 数据结构 ====================

    [Serializable]
    public class TrashFileRecord
    {
        public string OriginalAssetPath; // 原资产路径（Assets 相对，'/' 分隔）
        public string Address;           // 删除时的 Addressables 地址（未注册/非托管为空——恢复时按此重注册）
    }

    [Serializable]
    public class TrashEntry
    {
        public string Id;              // 时间戳唯一 ID
        public string Title;           // 概要（文件夹名 / 文件名 / "N 个文件"）
        public int Type;               // ResType
        public string CategoryKey;     // 类别资源键（显示用，如 VNovelizerRes/Audio/Voice）
        public string TrashedAt;       // ISO 时间
        public List<TrashFileRecord> Files = new List<TrashFileRecord>();
    }

    [Serializable]
    private class TrashManifest
    {
        public List<TrashEntry> Entries = new List<TrashEntry>();
    }

    // ==================== 查询 ====================

    /// <summary>全部回收站条目（新→旧排序；清单与磁盘目录自愈去孤儿）</summary>
    public static List<TrashEntry> ListEntries()
    {
        var manifest = LoadManifest();
        // 自愈：清理磁盘目录已不存在的条目（被用户手动清理过）
        bool dirty = manifest.Entries.RemoveAll(e => e == null || !Directory.Exists(EntryDir(e.Id))) > 0;
        if (dirty) SaveManifest(manifest);
        return manifest.Entries.OrderByDescending(e => e.TrashedAt).ToList();
    }

    /// <summary>回收站条目总数（侧边栏徽标）</summary>
    public static int Count => LoadManifest().Entries.Count;

    // ==================== 删除（移入回收站） ====================

    /// <summary>
    /// 把资产移入回收站（文件 + .meta 连同移动，托管条目先记录地址再移除分配）。
    /// unassignOnly：只移除分配不动物理文件的条目（文件夹删除中的外部分配资产），
    /// 与 assetPaths 合并记录进同一条回收站条目，恢复时一并还原。
    /// 返回 null = 成功，否则为错误信息（失败时已移动的文件自动回滚）。
    /// </summary>
    public static string TrashPaths(List<string> assetPaths, string title, ResType type, string categoryKey,
        List<string> unassignOnly = null)
    {
        if ((assetPaths == null || assetPaths.Count == 0) && (unassignOnly == null || unassignOnly.Count == 0))
            return "没有可删除的文件";

        // 1. 收集与校验
        var physical = (assetPaths ?? new List<string>())
            .Where(p => !string.IsNullOrEmpty(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var unassign = (unassignOnly ?? new List<string>())
            .Where(p => !string.IsNullOrEmpty(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var records = new List<TrashFileRecord>();
        var moved = new List<KeyValuePair<string, string>>(); // 回滚用：trashPath → originalPath

        try
        {
            string id = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            string entryDir = EntryDir(id);
            // 条目目录无条件创建（含纯移除分配记录的条目——无物理文件也要有目录，
            // 否则 ListEntries 的孤儿自愈会把它当作已被手动清理而丢失）
            Directory.CreateDirectory(entryDir);

            // 2. 托管条目：记录地址 → 移除分配
            bool managed = VNAddressablesRegistrar.IsManagedMode;
            foreach (var path in physical.Concat(unassign))
            {
                string address = null;
                if (managed)
                {
                    var settings = UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.Settings;
                    if (settings != null)
                    {
                        string guid = AssetDatabase.AssetPathToGUID(path);
                        var entry = string.IsNullOrEmpty(guid) ? null : settings.FindAssetEntry(guid);
                        if (entry != null && entry.parentGroup != null &&
                            entry.parentGroup.Name == VNResourceKeys.GroupName)
                        {
                            address = entry.address;
                            settings.RemoveAssetEntry(guid);
                        }
                    }
                }
                records.Add(new TrashFileRecord { OriginalAssetPath = path, Address = address });
            }
            if (managed) AssetDatabase.SaveAssets();

            // 3. 物理文件 + .meta 移动到回收站（保留 Assets/ 之后的相对结构）
            foreach (var path in physical)
            {
                string srcFile = Path.GetFullPath(path);
                if (!File.Exists(srcFile)) continue;
                string rel = ToEntryRelative(path);
                string dstFile = Path.Combine(entryDir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dstFile));
                File.Move(srcFile, dstFile);
                moved.Add(new KeyValuePair<string, string>(dstFile, path));

                string srcMeta = srcFile + ".meta";
                if (File.Exists(srcMeta))
                {
                    string dstMeta = dstFile + ".meta";
                    File.Move(srcMeta, dstMeta);
                    moved.Add(new KeyValuePair<string, string>(dstMeta, path + ".meta"));
                }
            }

            // 4. 写清单
            var manifest = LoadManifest();
            manifest.Entries.Add(new TrashEntry
            {
                Id = id,
                Title = title,
                Type = (int)type,
                CategoryKey = categoryKey ?? "",
                TrashedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Files = records,
            });
            SaveManifest(manifest);

            // 5. 刷新（Unity 顺带清理由此变空的物理目录）
            AssetDatabase.Refresh();
            VNAddressablesRegistrar.SyncWorkspace();
            return null;
        }
        catch (Exception e)
        {
            // 回滚已移动的文件
            foreach (var kv in moved)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(kv.Value)));
                    if (!File.Exists(kv.Value)) File.Move(kv.Key, kv.Value);
                }
                catch { /* 回滚尽力而为 */ }
            }
            AssetDatabase.Refresh();
            return $"移入回收站失败：{e.Message}（已回滚）";
        }
    }

    // ==================== 恢复 / 彻底删除 ====================

    /// <summary>恢复条目：移回原路径 + 按原地址重注册。返回 null = 成功。</summary>
    public static string Restore(string id)
    {
        var manifest = LoadManifest();
        var entry = manifest.Entries.FirstOrDefault(e => e.Id == id);
        if (entry == null) return "回收站条目不存在";
        string entryDir = EntryDir(id);

        // 1. 冲突预检：任何一个原位置被占用则整体放弃（避免半恢复状态）
        foreach (var f in entry.Files)
        {
            string srcFile = Path.Combine(entryDir, ToEntryRelative(f.OriginalAssetPath));
            if (!File.Exists(srcFile)) continue; // 纯移除分配条目：无物理文件，无冲突
            if (File.Exists(Path.GetFullPath(f.OriginalAssetPath)))
                return $"原位置已有同名文件：\n{f.OriginalAssetPath}\n\n请先处理冲突后重试。";
        }

        // 2. 移回文件（含 .meta——meta 与文件同目录同名，无需单独记录）；
        //    纯移除分配条目（外部分配资产，物理文件未动）在回收站内无文件，跳过
        foreach (var f in entry.Files)
        {
            string rel = ToEntryRelative(f.OriginalAssetPath);
            string srcFile = Path.Combine(entryDir, rel);
            if (!File.Exists(srcFile)) continue;
            string dstFile = Path.GetFullPath(f.OriginalAssetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(dstFile));
            File.Move(srcFile, dstFile);
            string srcMeta = srcFile + ".meta";
            if (File.Exists(srcMeta)) File.Move(srcMeta, dstFile + ".meta");
        }

        // 3. 刷新 + 按原地址重注册（.meta 随行 GUID 不变，引用关系完整）
        AssetDatabase.Refresh();
        foreach (var f in entry.Files)
        {
            if (!string.IsNullOrEmpty(f.Address) && File.Exists(f.OriginalAssetPath))
                VNAddressablesRegistrar.RegisterAssetAtPath(f.OriginalAssetPath, f.Address);
        }

        // 4. 清理空回收站目录 + 移除清单条目
        TryDeleteEmptyDir(entryDir);
        manifest.Entries.Remove(entry);
        SaveManifest(manifest);
        return null;
    }

    /// <summary>彻底删除单个条目（不可恢复）</summary>
    public static string DeletePermanent(string id)
    {
        var manifest = LoadManifest();
        var entry = manifest.Entries.FirstOrDefault(e => e.Id == id);
        if (entry == null) return "回收站条目不存在";
        try
        {
            string dir = EntryDir(id);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
        catch (Exception e) { return $"删除失败：{e.Message}"; }
        manifest.Entries.Remove(entry);
        SaveManifest(manifest);
        return null;
    }

    /// <summary>清空回收站（不可恢复）。返回 null = 成功。</summary>
    public static string ClearAll()
    {
        try
        {
            if (Directory.Exists(TrashRoot)) Directory.Delete(TrashRoot, true);
            return null;
        }
        catch (Exception e) { return $"清空失败：{e.Message}"; }
    }

    // ==================== 内部工具 ====================

    private static string EntryDir(string id) => TrashRoot + "/" + id;

    /// <summary>原资产路径 → 回收站内相对路径（剥离 "Assets/" 前缀，保留子目录结构）</summary>
    private static string ToEntryRelative(string assetPath)
    {
        string p = assetPath.Replace('\\', '/');
        return p.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ? p.Substring("Assets/".Length) : p;
    }

    private static TrashManifest LoadManifest()
    {
        var manifest = new TrashManifest();
        try
        {
            if (File.Exists(ManifestPath))
                manifest = JsonUtility.FromJson<TrashManifest>(File.ReadAllText(ManifestPath)) ?? new TrashManifest();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[回收站] 清单读取失败，按空清单处理：{e.Message}");
        }
        if (manifest.Entries == null) manifest.Entries = new List<TrashEntry>();
        return manifest;
    }

    private static void SaveManifest(TrashManifest manifest)
    {
        Directory.CreateDirectory(TrashRoot);
        File.WriteAllText(ManifestPath, JsonUtility.ToJson(manifest, true));
    }

    /// <summary>删除目录（仅当其下已无文件时）</summary>
    private static void TryDeleteEmptyDir(string dir)
    {
        try
        {
            if (Directory.Exists(dir) && !Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Any())
                Directory.Delete(dir, true);
        }
        catch { }
    }
}
