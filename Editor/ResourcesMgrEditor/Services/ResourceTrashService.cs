using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
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

    public enum TrashKind { Legacy = 0, Physical = 1, UnassignOnly = 2 }

    [Serializable]
    public class TrashFileRecord
    {
        public string OriginalAssetPath;
        public string Address;
        public TrashKind Kind;
        public bool IsDirectory;
        public string Guid;
        public bool AssignmentRecorded;
        public bool HadAssignment;
        public string OriginalGroupGuid;
        public string OriginalGroupName;
        public bool PreserveManualAddress;
        public bool AutomaticRegistrationSuppressed;
        public List<string> Labels = new List<string>();
    }

    [Serializable]
    public class TrashEntry
    {
        public int SchemaVersion;
        public string Id;
        public string Title;
        public int Type;
        public string CategoryKey;
        public string TrashedAt;
        public string State;
        public string LastFailure;
        public long UpdatedAtTicks;
        public List<TrashFileRecord> Files = new List<TrashFileRecord>();
    }

    [Serializable]
    private class TrashManifest
    {
        public int SchemaVersion;
        public List<TrashEntry> Entries;
    }

    private const int CurrentSchema = 2;
    private const string EntryMetadataName = "entry.json";
    public static string LastError { get; private set; }
    private static bool _transactionInProgress;

    // ==================== 查询 ====================

    /// <summary>纯查询；损坏索引显式报错，丢失载荷的条目仍保留供修复。</summary>
    public static List<TrashEntry> ListEntries()
    {
        return LoadManifest().Entries.OrderByDescending(e => e.TrashedAt).ToList();
    }

    public static int Count => LoadManifest().Entries.Count;

    // ==================== 删除（移入回收站） ====================

    /// <summary>
    /// 把资产移入回收站（文件 + .meta 连同移动，托管条目先记录地址再移除分配）。
    /// unassignOnly：只移除分配不动物理文件的条目（文件夹删除中的外部分配资产），
    /// 与 assetPaths 合并记录进同一条回收站条目，恢复时一并还原。
    /// 返回 null = 成功，否则为错误信息（失败时已移动的文件自动回滚）。
    /// </summary>
    public static string TrashPaths(List<string> assetPaths, string title, ResType type, string categoryKey,
        List<string> unassignOnly = null, IEnumerable<string> folderPaths = null)
    {
        TrashManifest manifest = null;
        TrashManifest before = null;
        TrashEntry entry = null;
        var undo = new List<Action>();
        bool journalWritten = false;
        bool entered = false;
        try
        {
            BeginTransaction();
            entered = true;
            manifest = LoadManifest();
            before = CloneManifest(manifest);
            var paths = new HashSet<string>(NormalizePaths(assetPaths), StringComparer.OrdinalIgnoreCase);
            var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var unassign = new HashSet<string>(NormalizePaths(unassignOnly), StringComparer.OrdinalIgnoreCase);
            foreach (string root in NormalizePaths(folderPaths)) CollectDirectoryTree(root, paths, directories);
            foreach (string path in paths.Where(IsPackagePath)) unassign.Add(path);
            foreach (string path in unassign)
                if (directories.Contains(path) || paths.Contains(path) && directories.Any(d => path.StartsWith(d + "/", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("文件夹内物理资产不能同时仅撤分配：" + path);
            var physical = paths.Where(p => !IsPackagePath(p) && !unassign.Contains(p)).Concat(directories).ToList();
            var all = physical.Concat(unassign).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (all.Count == 0) throw new InvalidOperationException("没有可删除的文件或文件夹");

            var settings = AddressableAssetSettingsDefaultObject.GetSettings(false);
            var records = new List<TrashFileRecord>();
            foreach (string path in all)
            {
                string file = FullAssetPath(path);
                bool isDirectory = directories.Contains(path) || unassign.Contains(path) && Directory.Exists(file);
                RequirePair(file, path, isDirectory);
                string guid = ReadMetaGuid(file + ".meta");
                VerifyAssetGuid(path, guid);
                var record = CaptureAssignment(path, guid, settings, isDirectory);
                record.Kind = unassign.Contains(path) ? TrashKind.UnassignOnly : TrashKind.Physical;
                if (record.Kind == TrashKind.UnassignOnly && !record.HadAssignment)
                    throw new InvalidOperationException("仅撤分配的资产没有可撤销的注册：" + path);
                records.Add(record);
            }
            ValidateRecordSet(records);
            ValidateAddressOccupancy(records, settings);
            VNAddressablesRegistrar.SuppressAutomaticRegistrationForCurrentImportBatch(records.Select(r => r.Guid));
            entry = new TrashEntry
            {
                SchemaVersion = CurrentSchema,
                Id = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + System.Guid.NewGuid().ToString("N"),
                Title = title ?? "资源",
                Type = (int)type,
                CategoryKey = categoryKey ?? "",
                TrashedAt = DateTime.UtcNow.ToString("o"),
                State = "Prepared",
                Files = records
            };
            foreach (var record in records.Where(r => r.Kind == TrashKind.Physical))
                RequireVacant(PayloadPath(entry, record), record.OriginalAssetPath);
            SaveEntry(entry);
            journalWritten = true;

            foreach (var record in records)
            {
                var captured = record;
                undo.Add(() => SetAutomaticRegistrationSuppression(captured, captured.AutomaticRegistrationSuppressed));
                undo.Add(() => RestoreAssignment(captured, false));
                if (record.Kind == TrashKind.UnassignOnly) SetAutomaticRegistrationSuppression(record, true);
                if (record.HadAssignment) RemoveAssignment(record, settings);
            }
            AssetDatabase.SaveAssets();
            foreach (var record in PhysicalDeleteOrder(records))
            {
                string source = FullAssetPath(record.OriginalAssetPath);
                string destination = PayloadPath(entry, record);
                MoveJournaled(source, destination, undo, record.IsDirectory);
                MoveJournaled(source + ".meta", destination + ".meta", undo);
            }
            RefreshChangedAssets(records);
            foreach (var record in records)
            {
                VerifyUnassigned(record, settings);
                bool expectedSuppression = record.Kind == TrashKind.UnassignOnly || record.AutomaticRegistrationSuppressed;
                if (ReadAutomaticRegistrationSuppression(record.Guid) != expectedSuppression)
                    throw new InvalidOperationException("撤分配自动登记抑制未实际生效：" + record.OriginalAssetPath);
            }
            entry.State = "Active";
            SaveEntry(entry);
            manifest.Entries.Add(entry);
            SaveManifest(manifest);
            VNResourceService.Reset();
            return null;
        }
        catch (Exception error)
        {
            return FailTransaction("移入回收站", error, entry, before, undo, journalWritten, false);
        }
        finally
        {
            if (entered) EndTransaction();
        }
    }

    // ==================== 恢复 / 彻底删除 ====================

    /// <summary>全批恢复；载荷、GUID、路径和地址任一冲突均阻止整批操作。</summary>
    public static string Restore(string id)
    {
        TrashManifest before = null;
        TrashEntry entry = null;
        var undo = new List<Action>();
        bool journalWritten = false;
        bool entered = false;
        try
        {
            BeginTransaction();
            entered = true;
            var manifest = LoadManifest();
            before = CloneManifest(manifest);
            entry = FindEntry(manifest, id);
            PrepareLegacyRecords(entry);
            ValidateRestore(entry);
            var settings = AddressableAssetSettingsDefaultObject.GetSettings(false);
            var previousAssignments = entry.Files.Select(r => CaptureAssignment(r.OriginalAssetPath, r.Guid, settings, r.IsDirectory)).ToList();
            VNAddressablesRegistrar.SuppressAutomaticRegistrationForCurrentImportBatch(entry.Files.Select(r => r.Guid));
            entry.State = "Restoring";
            SaveEntry(entry);
            journalWritten = true;

            foreach (var record in PhysicalRestoreOrder(entry.Files))
            {
                string source = PayloadPath(entry, record);
                string destination = FullAssetPath(record.OriginalAssetPath);
                MoveJournaled(source, destination, undo, record.IsDirectory, requireExistingParent: true);
                MoveJournaled(source + ".meta", destination + ".meta", undo, requireExistingParent: true);
            }
            foreach (var previous in previousAssignments)
            {
                var captured = previous;
                undo.Add(() => SetAutomaticRegistrationSuppression(captured, captured.AutomaticRegistrationSuppressed));
                undo.Add(() => RestoreAssignment(captured, false));
            }
            RefreshChangedAssets(entry.Files);
            foreach (var record in entry.Files) VerifyAssetGuid(record.OriginalAssetPath, record.Guid);
            var changes = entry.Files.Where(r => r.HadAssignment && !r.IsDirectory && r.OriginalGroupName == VNResourceKeys.GroupName)
                .ToDictionary(r => r.OriginalAssetPath, r => r.Address, StringComparer.OrdinalIgnoreCase);
            string validation = VNAddressablesRegistrar.ValidateAddressChanges(changes);
            if (validation != null) throw new InvalidOperationException(validation);
            foreach (var record in entry.Files) RestoreAssignment(record, true);
            AssetDatabase.SaveAssets();
            entry.State = "Restored";
            SaveEntry(entry);
            manifest.Entries.Remove(entry);
            SaveManifest(manifest);
            VNResourceService.Reset();
            return null;
        }
        catch (Exception error)
        {
            return FailTransaction("恢复", error, entry, before, undo, journalWritten, true);
        }
        finally
        {
            if (entered) EndTransaction();
        }
    }

    public static string DeletePermanent(string id)
    {
        return PurgeEntries(id, false);
    }

    /// <summary>只清理清单中明确记录的条目，不删除整个根目录或未索引目录。</summary>
    public static string ClearAll()
    {
        return PurgeEntries(null, true);
    }

    /// <summary>显式使用备份修复主清单；保留原损坏文件，不自动执行。</summary>
    public static string RecoverManifest()
    {
        bool entered = false;
        try
        {
            BeginTransaction();
            entered = true;
            var recovered = ReadManifest(ManifestPath + ".bak");
            PreserveCurrentManifest();
            SaveManifest(recovered, true);
            LastError = null;
            return null;
        }
        catch (Exception e) { return SetError("恢复清单失败：" + e.Message); }
        finally { if (entered) EndTransaction(); }
    }

    /// <summary>显式从每条entry.json重建索引；不推测未知载荷，不清理孤儿。</summary>
    public static string RepairIndex()
    {
        bool entered = false;
        try
        {
            BeginTransaction();
            entered = true;
            TrashManifest repaired;
            try { repaired = LoadManifest(); }
            catch (Exception) { repaired = NewManifest(); }
            var entries = repaired.Entries.ToDictionary(e => e.Id, StringComparer.Ordinal);
            bool foundMetadata = false;
            if (Directory.Exists(TrashRoot))
            {
                foreach (string directory in Directory.GetDirectories(TrashRoot))
                {
                    EnsureNotReparsePoint(directory);
                    var entry = ReadRecoverableEntry(directory);
                    if (entry == null)
                    {
                        if (!entries.ContainsKey(Path.GetFileName(directory)))
                            throw new InvalidDataException("未索引目录缺少entry.json，无法安全推断：" + directory);
                        continue;
                    }
                    if (!string.Equals(Path.GetFileName(directory), entry.Id, StringComparison.Ordinal))
                        throw new InvalidDataException("entry.json的ID与目录不一致：" + directory);
                    foundMetadata = true;
                    if (IsTerminal(entry.State)) entries.Remove(entry.Id);
                    else entries[entry.Id] = entry;
                }
            }
            if (!foundMetadata && repaired.Entries.Count == 0)
                throw new InvalidDataException("没有可用条目元数据；不会以空清单覆盖原索引");
            repaired.Entries = entries.Values.OrderBy(e => e.TrashedAt).ToList();
            ValidateManifest(repaired);
            PreserveCurrentManifest();
            SaveManifest(repaired, true);
            LastError = null;
            return null;
        }
        catch (Exception e) { return SetError("修复索引失败：" + e.Message); }
        finally { if (entered) EndTransaction(); }
    }

    // ==================== 内部工具 ====================

    public static bool IsTransactionInProgress => _transactionInProgress;

    private static void BeginTransaction()
    {
        if (Application.isPlaying) throw new InvalidOperationException("运行模式不能修改回收站");
        if (_transactionInProgress) throw new InvalidOperationException("已有回收站事务正在执行");
        EnsureNotReparsePoint(TrashRoot);
        _transactionInProgress = true;
        LastError = null;
    }

    private static void EndTransaction() { _transactionInProgress = false; }

    private static string SetError(string error)
    {
        LastError = error;
        Debug.LogWarning("[回收站] " + error);
        return error;
    }

    private static TrashManifest NewManifest()
    {
        return new TrashManifest { SchemaVersion = CurrentSchema, Entries = new List<TrashEntry>() };
    }

    private static TrashManifest CloneManifest(TrashManifest manifest)
    {
        return JsonUtility.FromJson<TrashManifest>(JsonUtility.ToJson(manifest));
    }

    private static TrashManifest LoadManifest()
    {
        try
        {
            EnsureNotReparsePoint(TrashRoot);
            if (File.Exists(ManifestPath))
            {
                var manifest = ReadManifest(ManifestPath);
                LastError = null;
                return manifest;
            }
            if (Directory.Exists(TrashRoot) && Directory.EnumerateFileSystemEntries(TrashRoot).Any())
                throw new InvalidDataException("主清单缺失但回收站仍有数据；请显式恢复清单或修复索引");
            LastError = null;
            return NewManifest();
        }
        catch (Exception e)
        {
            LastError = "清单读取失败，不会按空清单处理：" + e.Message;
            throw new InvalidDataException(LastError, e);
        }
    }

    private static TrashManifest ReadManifest(string path)
    {
        string json = ReadJsonObject(path);
        if (!Regex.IsMatch(json, "\\\"Entries\\\"\\s*:"))
            throw new InvalidDataException("缺少Entries字段：" + path);
        var manifest = JsonUtility.FromJson<TrashManifest>(json);
        ValidateManifest(manifest);
        return manifest;
    }

    private static string ReadJsonObject(string path)
    {
        EnsureNotReparsePoint(path);
        string json = File.ReadAllText(path, Encoding.UTF8).Trim().TrimStart('\uFEFF');
        if (!json.StartsWith("{", StringComparison.Ordinal) || !json.EndsWith("}", StringComparison.Ordinal))
            throw new InvalidDataException("JSON不完整：" + path);
        return json;
    }

    private static void ValidateManifest(TrashManifest manifest)
    {
        if (manifest == null || manifest.Entries == null)
            throw new InvalidDataException("清单Entries为空或JSON无效");
        if (manifest.SchemaVersion < 0 || manifest.SchemaVersion > CurrentSchema)
            throw new InvalidDataException("不支持的回收站清单版本");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in manifest.Entries)
        {
            ValidateEntry(entry);
            if (!ids.Add(entry.Id)) throw new InvalidDataException("清单条目ID重复：" + entry.Id);
        }
    }

    private static void ValidateEntry(TrashEntry entry)
    {
        if (entry == null || !IsSafeId(entry.Id) || entry.Files == null || entry.Files.Count == 0)
            throw new InvalidDataException("回收站条目缺少ID或文件记录");
        if (entry.SchemaVersion < 0 || entry.SchemaVersion > CurrentSchema)
            throw new InvalidDataException("不支持的条目版本：" + entry.Id);
        ValidateRecordSet(entry.Files);
        if (entry.SchemaVersion >= CurrentSchema && entry.Files.Any(r => !r.AssignmentRecorded || r.Kind == TrashKind.Legacy))
            throw new InvalidDataException("新版本条目缺少原注册状态或删除类型：" + entry.Id);
    }

    private static void ValidateRecordSet(List<TrashFileRecord> records)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var guids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var addresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in records)
        {
            if (record == null) throw new InvalidDataException("文件记录为空");
            string path = NormalizeAssetPath(record.OriginalAssetPath);
            if (!string.Equals(path, record.OriginalAssetPath, StringComparison.Ordinal))
                throw new InvalidDataException("文件记录路径未规范化：" + record.OriginalAssetPath);
            if (!paths.Add(path)) throw new InvalidDataException("文件记录路径重复：" + path);
            if ((int)record.Kind < (int)TrashKind.Legacy || (int)record.Kind > (int)TrashKind.UnassignOnly)
                throw new InvalidDataException("未知删除类型：" + path);
            if (!string.IsNullOrEmpty(record.Guid))
            {
                if (!Regex.IsMatch(record.Guid, "^[a-fA-F0-9]{32}$") || !guids.Add(record.Guid))
                    throw new InvalidDataException("GUID无效或重复：" + path);
            }
            else if (record.AssignmentRecorded) throw new InvalidDataException("缺少原GUID：" + path);
            if (record.HadAssignment && (string.IsNullOrEmpty(record.Address) || string.IsNullOrEmpty(record.OriginalGroupName)))
                throw new InvalidDataException("原注册地址或分组缺失：" + path);
            if (!string.IsNullOrEmpty(record.Address) && !addresses.Add(record.Address))
                throw new InvalidDataException("同批原地址重复：" + record.Address);
            if (record.Kind == TrashKind.Physical && IsPackagePath(path))
                throw new InvalidDataException("Packages资产不能物理移动：" + path);
        }
    }

    private static bool IsSafeId(string id)
    {
        return !string.IsNullOrEmpty(id) && Regex.IsMatch(id, "^[A-Za-z0-9][A-Za-z0-9_-]{0,127}$");
    }

    private static string EntryDir(string id)
    {
        if (!IsSafeId(id)) throw new InvalidDataException("无效的回收站条目ID");
        string directory = Path.Combine(TrashRoot, id);
        EnsureNotReparsePoint(directory);
        return directory;
    }

    private static string PayloadPath(TrashEntry entry, TrashFileRecord record)
    {
        string path = NormalizeAssetPath(record.OriginalAssetPath);
        if (IsPackagePath(path)) throw new InvalidDataException("包资产没有回收站物理载荷");
        string root = entry.SchemaVersion >= CurrentSchema ? Path.Combine(EntryDir(entry.Id), "payload") : EntryDir(entry.Id);
        string payload = record.IsDirectory
            ? Path.Combine(EntryDir(entry.Id), "directory-payload", record.Guid)
            : Path.Combine(root, path.Substring("Assets/".Length));
        EnsureNotReparsePoint(payload);
        return payload;
    }

    private static List<string> NormalizePaths(IEnumerable<string> paths)
    {
        return (paths ?? Enumerable.Empty<string>()).Select(NormalizeAssetPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void CollectDirectoryTree(string root, HashSet<string> files, HashSet<string> directories)
    {
        if (IsPackagePath(root)) throw new InvalidOperationException("Packages文件夹不能物理删除：" + root);
        string source = FullAssetPath(root);
        RequirePair(source, root, true);
        if (!directories.Add(root)) return;
        foreach (string file in Directory.GetFiles(source))
        {
            EnsureNotReparsePoint(file);
            if (file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
            {
                string asset = file.Substring(0, file.Length - ".meta".Length);
                if (!File.Exists(asset) && !Directory.Exists(asset))
                    throw new InvalidDataException("文件夹内存在孤立.meta，未修改任何资源：" + file);
                continue;
            }
            files.Add(NormalizeAssetPath(file));
        }
        foreach (string directory in Directory.GetDirectories(source))
            CollectDirectoryTree(NormalizeAssetPath(directory), files, directories);
    }

    private static IEnumerable<TrashFileRecord> PhysicalDeleteOrder(IEnumerable<TrashFileRecord> records)
    {
        return records.Where(r => r.Kind == TrashKind.Physical).OrderBy(r => r.IsDirectory ? 1 : 0)
            .ThenByDescending(r => r.OriginalAssetPath.Count(c => c == '/'));
    }

    private static IEnumerable<TrashFileRecord> PhysicalRestoreOrder(IEnumerable<TrashFileRecord> records)
    {
        return records.Where(r => r.Kind == TrashKind.Physical).OrderBy(r => r.IsDirectory ? 0 : 1)
            .ThenBy(r => r.OriginalAssetPath.Count(c => c == '/'));
    }

    private static string NormalizeAssetPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidDataException("资产路径为空");
        string normalized = path.Replace('\\', '/');
        if (Path.IsPathRooted(normalized))
        {
            string root = ProjectRoot + "/";
            normalized = Path.GetFullPath(normalized).Replace('\\', '/');
            if (!normalized.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("资产路径不在项目中：" + path);
            normalized = normalized.Substring(root.Length);
        }
        if (!normalized.StartsWith("Assets/", StringComparison.Ordinal) && !IsPackagePath(normalized))
            throw new InvalidDataException("仅支持Assets或Packages资产：" + path);
        if (normalized.Split('/').Any(p => p.Length == 0 || p == "." || p == ".." || p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            || normalized.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("无效的资产路径：" + path);
        return normalized;
    }

    private static bool IsPackagePath(string path)
    {
        return path != null && path.StartsWith("Packages/", StringComparison.Ordinal);
    }

    private static string FullAssetPath(string assetPath)
    {
        string path = NormalizeAssetPath(assetPath);
        if (IsPackagePath(path))
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(path);
            if (package == null || string.IsNullOrEmpty(package.resolvedPath))
                throw new InvalidDataException("无法定位包资产源：" + path);
            string prefix = "Packages/" + package.name + "/";
            if (!path.StartsWith(prefix, StringComparison.Ordinal)) throw new InvalidDataException("包资产路径不匹配");
            return Path.GetFullPath(Path.Combine(package.resolvedPath, path.Substring(prefix.Length)));
        }
        string full = Path.GetFullPath(Path.Combine(ProjectRoot, path));
        EnsureNotReparsePoint(full);
        return full;
    }

    private static void EnsureNotReparsePoint(string path)
    {
        string current = Path.GetFullPath(path);
        string stop = ProjectRoot.TrimEnd('/', '\\');
        while (!string.IsNullOrEmpty(current) && !string.Equals(current, stop, StringComparison.OrdinalIgnoreCase))
        {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("不允许沿符号链接或目录联接修改资产：" + current);
            current = Path.GetDirectoryName(current);
        }
    }

    private static void RequirePair(string file, string display, bool isDirectory = false)
    {
        if (!(isDirectory ? Directory.Exists(file) : File.Exists(file)) || !File.Exists(file + ".meta"))
            throw new FileNotFoundException("资产文件/目录或.meta缺失：" + display);
        EnsureNotReparsePoint(file);
        EnsureNotReparsePoint(file + ".meta");
    }

    private static void RequireVacant(string file, string display)
    {
        if (File.Exists(file) || Directory.Exists(file) || File.Exists(file + ".meta") || Directory.Exists(file + ".meta"))
            throw new IOException("目标文件或.meta已被占用：" + display);
        string parent = Path.GetDirectoryName(file);
        while (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
        {
            if (File.Exists(parent)) throw new IOException("目标父路径是文件：" + parent);
            parent = Path.GetDirectoryName(parent);
        }
    }

    private static string ReadMetaGuid(string meta)
    {
        string text = File.ReadAllText(meta);
        var match = Regex.Match(text, "(?m)^guid:\\s*([a-fA-F0-9]{32})\\s*$");
        if (!match.Success) throw new InvalidDataException(".meta没有有效GUID：" + meta);
        return match.Groups[1].Value.ToLowerInvariant();
    }

    private static void VerifyAssetGuid(string assetPath, string guid)
    {
        string actual = AssetDatabase.AssetPathToGUID(assetPath);
        if (!string.Equals(actual, guid, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("资产GUID已改变或尚未导入：" + assetPath);
        string mapped = AssetDatabase.GUIDToAssetPath(guid).Replace('\\', '/');
        if (!string.Equals(mapped, assetPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("GUID已被其他路径占用：" + guid + " → " + mapped);
    }

    private static void SaveManifest(TrashManifest manifest, bool explicitRepair = false)
    {
        ValidateManifest(manifest);
        if (!explicitRepair && File.Exists(ManifestPath)) ReadManifest(ManifestPath);
        manifest.SchemaVersion = CurrentSchema;
        AtomicWrite(ManifestPath, JsonUtility.ToJson(manifest, true));
    }

    private static void SaveEntry(TrashEntry entry)
    {
        ValidateEntry(entry);
        entry.UpdatedAtTicks = DateTime.UtcNow.Ticks;
        AtomicWrite(Path.Combine(EntryDir(entry.Id), EntryMetadataName), JsonUtility.ToJson(entry, true));
    }

    private static TrashEntry ReadEntry(string path)
    {
        var entry = JsonUtility.FromJson<TrashEntry>(ReadJsonObject(path));
        ValidateEntry(entry);
        return entry;
    }

    private static TrashEntry ReadRecoverableEntry(string directory)
    {
        var paths = Directory.GetFiles(directory, "recovery-*.json").ToList();
        string primary = Path.Combine(directory, EntryMetadataName);
        if (File.Exists(primary)) paths.Add(primary);
        if (File.Exists(primary + ".tmp")) paths.Add(primary + ".tmp");
        if (paths.Count == 0) return null;
        var valid = new List<TrashEntry>();
        var failures = new List<string>();
        foreach (string path in paths)
        {
            try { valid.Add(ReadEntry(path)); }
            catch (Exception e) { failures.Add(path + "：" + e.Message); }
        }
        if (valid.Count == 0) throw new InvalidDataException("条目恢复元数据全部损坏：" + string.Join("；", failures));
        if (valid.Any(e => e.Id != Path.GetFileName(directory)))
            throw new InvalidDataException("恢复元数据ID与目录不一致：" + directory);
        var latest = valid.OrderByDescending(e => e.UpdatedAtTicks).First();
        if (failures.Count > 0)
        {
            latest.State = "Failed";
            latest.LastFailure = "部分元数据损坏，请核查原文件：" + string.Join("；", failures);
        }
        return latest;
    }

    private static void SaveRecoveryEvidence(TrashEntry entry, List<string> failures)
    {
        entry.State = "Failed";
        entry.LastFailure = (entry.LastFailure ?? "") + "；" + string.Join("；", failures);
        entry.UpdatedAtTicks = DateTime.UtcNow.Ticks;
        string path = Path.Combine(EntryDir(entry.Id), "recovery-" + entry.UpdatedAtTicks + "-" + System.Guid.NewGuid().ToString("N") + ".json");
        try { AtomicWrite(path, JsonUtility.ToJson(entry, true)); }
        catch (Exception e) { failures.Add("独立恢复快照也写入失败：" + e.Message); }
    }

    private static void AtomicWrite(string path, string text)
    {
        EnsureNotReparsePoint(path);
        string temporary = path + ".tmp";
        EnsureNotReparsePoint(temporary);
        EnsureNotReparsePoint(path + ".bak");
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        if (File.Exists(temporary))
            throw new IOException("存在未提交的临时清单，请先检查并显式修复：" + temporary);
        byte[] bytes = new UTF8Encoding(false).GetBytes(text);
        using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush(true);
        }
        if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
        else File.Move(temporary, path);
    }

    private static void PreserveCurrentManifest()
    {
        if (File.Exists(ManifestPath))
            File.Copy(ManifestPath, ManifestPath + ".preserved-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + System.Guid.NewGuid().ToString("N"));
        if (File.Exists(ManifestPath + ".bak"))
            File.Copy(ManifestPath + ".bak", ManifestPath + ".backup-preserved-" + System.Guid.NewGuid().ToString("N"));
        if (File.Exists(ManifestPath + ".tmp"))
            File.Move(ManifestPath + ".tmp", ManifestPath + ".pending-preserved-" + System.Guid.NewGuid().ToString("N"));
    }

    private static bool IsTerminal(string state)
    {
        return state == "Restored" || state == "Deleted" || state == "RolledBack";
    }

    private static TrashEntry FindEntry(TrashManifest manifest, string id)
    {
        if (!IsSafeId(id)) throw new InvalidDataException("无效的回收站条目ID");
        var entry = manifest.Entries.FirstOrDefault(e => e.Id == id);
        if (entry == null) throw new InvalidOperationException("回收站条目不存在");
        if (IsTerminal(entry.State)) throw new InvalidOperationException("条目已结束；请显式修复索引");
        return entry;
    }

    private static TrashFileRecord CaptureAssignment(string path, string guid, AddressableAssetSettings settings, bool isDirectory = false)
    {
        var registered = settings == null ? null : settings.FindAssetEntry(guid);
        if (registered != null && (registered.parentGroup == null || registered.parentGroup.ReadOnly || registered.ReadOnly))
            throw new InvalidOperationException("资产注册为只读或没有有效分组：" + path);
        return new TrashFileRecord
        {
            OriginalAssetPath = path,
            Guid = guid,
            IsDirectory = isDirectory,
            AssignmentRecorded = true,
            HadAssignment = registered != null,
            Address = registered == null ? null : registered.address,
            OriginalGroupGuid = registered == null ? null : registered.parentGroup.Guid,
            OriginalGroupName = registered == null ? null : registered.parentGroup.Name,
            PreserveManualAddress = registered != null && registered.labels.Contains(VNAddressablesRegistrar.ManualAddressOverrideLabel),
            AutomaticRegistrationSuppressed = ReadAutomaticRegistrationSuppression(guid),
            Labels = registered == null ? new List<string>() : registered.labels.OrderBy(l => l).ToList()
        };
    }

    private static AddressableAssetGroup OriginalGroup(TrashFileRecord record, AddressableAssetSettings settings)
    {
        if (settings == null) throw new InvalidOperationException("原Addressables配置已丢失，不能静默跳过注册恢复");
        var group = !string.IsNullOrEmpty(record.OriginalGroupGuid)
            ? settings.FindGroup(g => g.Guid == record.OriginalGroupGuid)
            : settings.FindGroup(record.OriginalGroupName);
        if (group == null || group.ReadOnly)
            throw new InvalidOperationException("原注册分组不存在或为只读：" + record.OriginalGroupName);
        return group;
    }

    private static void ValidateAddressOccupancy(List<TrashFileRecord> records, AddressableAssetSettings settings)
    {
        if (records.Any(r => r.HadAssignment) && settings == null)
            throw new InvalidOperationException("恢复需要原Addressables配置");
        if (settings == null) return;
        foreach (var record in records.Where(r => r.HadAssignment))
        {
            OriginalGroup(record, settings);
            foreach (var group in settings.groups.Where(g => g != null))
                if (group.entries.Any(e => string.Equals(e.address, record.Address, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(e.guid, record.Guid, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("原地址已被其他资产占用：" + record.Address);
        }
    }

    private static void RemoveAssignment(TrashFileRecord record, AddressableAssetSettings settings)
    {
        if (settings == null || !settings.RemoveAssetEntry(record.Guid))
            throw new InvalidOperationException("撤销注册失败：" + record.OriginalAssetPath);
        VerifyUnassigned(record, settings);
    }

    private static void VerifyUnassigned(TrashFileRecord record, AddressableAssetSettings settings)
    {
        if (settings != null && settings.FindAssetEntry(record.Guid) != null)
            throw new InvalidOperationException("资产仍被注册，不能报告删除成功：" + record.OriginalAssetPath);
    }

    private static void RestoreAssignment(TrashFileRecord record, bool checkedRegistration)
    {
        var settings = AddressableAssetSettingsDefaultObject.GetSettings(false);
        if (!record.HadAssignment)
        {
            if (settings != null && settings.FindAssetEntry(record.Guid) != null)
            {
                if (!settings.RemoveAssetEntry(record.Guid)) throw new InvalidOperationException("无法回滚新增注册：" + record.OriginalAssetPath);
                VerifyUnassigned(record, settings);
            }
            SetAutomaticRegistrationSuppression(record, record.AutomaticRegistrationSuppressed);
            return;
        }
        string source = FullAssetPath(record.OriginalAssetPath);
        RequirePair(source, record.OriginalAssetPath, record.IsDirectory);
        if (!string.Equals(ReadMetaGuid(source + ".meta"), record.Guid, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("注册恢复时源资产已被替换：" + record.OriginalAssetPath);
        if (AssetDatabase.AssetPathToGUID(record.OriginalAssetPath) != record.Guid)
            AssetDatabase.ImportAsset(record.OriginalAssetPath, ImportAssetOptions.ForceSynchronousImport);
        VerifyAssetGuid(record.OriginalAssetPath, record.Guid);
        ValidateAddressOccupancy(new List<TrashFileRecord> { record }, settings);
        var group = OriginalGroup(record, settings);
        if (checkedRegistration && !record.IsDirectory && group.Name == VNResourceKeys.GroupName)
        {
            string error = VNAddressablesRegistrar.RegisterAssetAtPathChecked(record.OriginalAssetPath, record.Address, record.PreserveManualAddress);
            if (error != null) throw new InvalidOperationException(error);
        }
        var restored = settings.CreateOrMoveEntry(record.Guid, group);
        if (restored == null) throw new InvalidOperationException("创建原注册条目失败：" + record.OriginalAssetPath);
        restored.SetAddress(record.Address);
        foreach (string label in restored.labels.ToList()) restored.SetLabel(label, false);
        foreach (string label in record.Labels ?? new List<string>()) restored.SetLabel(label, true, true);
        if (restored.address != record.Address || restored.parentGroup.Guid != group.Guid
            || !restored.labels.SetEquals(record.Labels ?? new List<string>()))
            throw new InvalidOperationException("注册恢复校验失败：" + record.OriginalAssetPath);
        AssetDatabase.SaveAssets();
        SetAutomaticRegistrationSuppression(record, record.AutomaticRegistrationSuppressed);
    }

    private static bool ReadAutomaticRegistrationSuppression(string guid)
    {
        string error = VNAddressablesRegistrar.GetAutomaticRegistrationSuppressedChecked(guid, out bool suppressed);
        if (error != null) throw new InvalidOperationException(error);
        return suppressed;
    }

    private static void SetAutomaticRegistrationSuppression(TrashFileRecord record, bool suppressed)
    {
        string error = VNAddressablesRegistrar.SetAutomaticRegistrationSuppressedChecked(record.Guid, suppressed);
        if (error != null) throw new InvalidOperationException(error);
        if (ReadAutomaticRegistrationSuppression(record.Guid) != suppressed)
            throw new InvalidOperationException("持久自动登记抑制校验失败：" + record.OriginalAssetPath);
    }

    private static void PrepareLegacyRecords(TrashEntry entry)
    {
        foreach (var record in entry.Files)
        {
            if (record.Kind != TrashKind.Legacy) continue;
            if (IsPackagePath(record.OriginalAssetPath))
                throw new InvalidDataException("旧条目没有GUID和Kind，不能安全判断包资产身份：" + record.OriginalAssetPath);
            string payload = PayloadPath(entry, record);
            string metadata = Path.Combine(EntryDir(entry.Id), EntryMetadataName);
            if (new[] { metadata, metadata + ".tmp", metadata + ".bak" }
                .Any(p => string.Equals(p, payload, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("旧载荷与恢复元数据名称冲突，请手动迁移该条目：" + record.OriginalAssetPath);
            if (!File.Exists(payload) || !File.Exists(payload + ".meta"))
                throw new InvalidDataException("旧条目载荷不完整或仅撤分配但未保存GUID，不能安全恢复：" + record.OriginalAssetPath);
            record.Kind = TrashKind.Physical;
            record.Guid = ReadMetaGuid(payload + ".meta");
            record.AssignmentRecorded = true;
            record.HadAssignment = !string.IsNullOrEmpty(record.Address);
            record.OriginalGroupName = record.HadAssignment ? VNResourceKeys.GroupName : null;
            record.PreserveManualAddress = record.HadAssignment;
            record.Labels = new List<string>();
            if (record.HadAssignment)
            {
                string category = entry.CategoryKey;
                if (string.IsNullOrEmpty(category) && Enum.IsDefined(typeof(ResType), entry.Type))
                    category = VNAddressablesRegistrar.GetCategoryKey((ResType)entry.Type);
                if (string.IsNullOrEmpty(category) || !record.Address.StartsWith(category + "/", StringComparison.Ordinal))
                    throw new InvalidDataException("旧条目的类别信息不足，不能安全恢复标签：" + record.OriginalAssetPath);
                string label = VNResourceKeys.CategoryToLabel(category);
                if (!string.IsNullOrEmpty(label)) record.Labels.Add(label);
                record.Labels.Add(VNAddressablesRegistrar.ManualAddressOverrideLabel);
            }
        }
        ValidateRecordSet(entry.Files);
    }

    private static void ValidateRestore(TrashEntry entry)
    {
        var settings = AddressableAssetSettingsDefaultObject.GetSettings(false);
        var plannedDirectories = new HashSet<string>(entry.Files.Where(r => r.Kind == TrashKind.Physical && r.IsDirectory)
            .Select(r => FullAssetPath(r.OriginalAssetPath)), StringComparer.OrdinalIgnoreCase);
        foreach (var record in entry.Files.Where(r => r.Kind == TrashKind.Physical))
        {
            string parent = Path.GetDirectoryName(FullAssetPath(record.OriginalAssetPath));
            while (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
            {
                if (!plannedDirectories.Contains(parent))
                    throw new DirectoryNotFoundException("恢复祖先目录缺失且不在本批目录记录中，请先处理：" + parent);
                parent = Path.GetDirectoryName(parent);
            }
        }
        foreach (var record in entry.Files)
        {
            string source = FullAssetPath(record.OriginalAssetPath);
            if (record.Kind == TrashKind.Physical)
            {
                string payload = PayloadPath(entry, record);
                RequirePair(payload, record.OriginalAssetPath, record.IsDirectory);
                if (record.IsDirectory && Directory.EnumerateFileSystemEntries(payload).Any())
                    throw new IOException("目录载荷中存在未知内容，不会自动移回：" + payload);
                if (!string.Equals(ReadMetaGuid(payload + ".meta"), record.Guid, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("回收站载荷GUID不符：" + record.OriginalAssetPath);
                RequireVacant(source, record.OriginalAssetPath);
                string mapped = AssetDatabase.GUIDToAssetPath(record.Guid);
                if (!string.IsNullOrEmpty(mapped) && (File.Exists(FullAssetPath(mapped)) || Directory.Exists(FullAssetPath(mapped))))
                    throw new InvalidDataException("原GUID已被其他现存资产占用：" + record.Guid);
            }
            else
            {
                RequirePair(source, record.OriginalAssetPath, record.IsDirectory);
                if (!string.Equals(ReadMetaGuid(source + ".meta"), record.Guid, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("仅撤分配的源资产已被替换：" + record.OriginalAssetPath);
                VerifyAssetGuid(record.OriginalAssetPath, record.Guid);
                var current = settings == null ? null : settings.FindAssetEntry(record.Guid);
                if (current != null && (current.address != record.Address || current.parentGroup == null
                    || current.parentGroup.Guid != OriginalGroup(record, settings).Guid))
                    throw new InvalidOperationException("源资产已被重新分配，不能覆盖当前分配：" + record.OriginalAssetPath);
            }
        }
        ValidateAddressOccupancy(entry.Files, settings);
    }

    private static void RefreshChangedAssets(IEnumerable<TrashFileRecord> records)
    {
        var physical = records.Where(r => r.Kind == TrashKind.Physical).ToList();
        if (physical.Count == 0) return;
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        foreach (var record in physical.OrderBy(r => r.IsDirectory ? 0 : 1).ThenBy(r => r.OriginalAssetPath.Count(c => c == '/')))
            if (record.IsDirectory ? Directory.Exists(FullAssetPath(record.OriginalAssetPath)) : File.Exists(FullAssetPath(record.OriginalAssetPath)))
                AssetDatabase.ImportAsset(record.OriginalAssetPath, ImportAssetOptions.ForceSynchronousImport);
    }

    private static void MoveJournaled(string source, string destination, List<Action> undo, bool isDirectory = false,
        bool requireExistingParent = false)
    {
        if (!(isDirectory ? Directory.Exists(source) : File.Exists(source))) throw new FileNotFoundException("移动源缺失", source);
        if (File.Exists(destination) || Directory.Exists(destination)) throw new IOException("移动目标已被占用：" + destination);
        EnsureNotReparsePoint(source);
        EnsureNotReparsePoint(destination);
        if (isDirectory && Directory.EnumerateFileSystemEntries(source).Any())
            throw new IOException("目录仍有未移入本批次的内容：" + source);
        string destinationParent = Path.GetDirectoryName(destination);
        if (requireExistingParent)
        {
            if (!Directory.Exists(destinationParent))
                throw new DirectoryNotFoundException("恢复父目录已丢失，不会隐式创建：" + destinationParent);
        }
        else Directory.CreateDirectory(destinationParent);
        if (isDirectory) Directory.Move(source, destination);
        else File.Move(source, destination);
        undo.Add(() =>
        {
            if (!(isDirectory ? Directory.Exists(destination) : File.Exists(destination))) throw new FileNotFoundException("回滚源缺失", destination);
            if (File.Exists(source) || Directory.Exists(source)) throw new IOException("回滚目标被占用：" + source);
            if (isDirectory && Directory.EnumerateFileSystemEntries(destination).Any())
                throw new IOException("回滚目录中有额外内容，不会移动未知资产：" + destination);
            string sourceParent = Path.GetDirectoryName(source);
            if (!Directory.Exists(sourceParent))
                throw new DirectoryNotFoundException("回滚父目录丢失，不会创建未记录的祖先：" + sourceParent);
            if (isDirectory) Directory.Move(destination, source);
            else File.Move(destination, source);
        });
    }

    private static string FailTransaction(string operation, Exception error, TrashEntry entry, TrashManifest before,
        List<Action> undo, bool journalWritten, bool restoring)
    {
        var failures = new List<string>();
        for (int i = undo.Count - 1; i >= 0; i--)
        {
            try { undo[i](); }
            catch (Exception e) { failures.Add(e.Message); }
        }
        if (journalWritten && entry != null)
        {
            try { RefreshChangedAssets(entry.Files); AssetDatabase.SaveAssets(); }
            catch (Exception e) { failures.Add(e.Message); }
            entry.State = failures.Count == 0 ? (restoring ? "Active" : "RolledBack") : "Failed";
            entry.LastFailure = operation + "失败：" + error.Message;
            try { SaveEntry(entry); }
            catch (Exception e) { failures.Add("恢复元数据写入失败：" + e.Message); }
            if (before != null)
            {
                if (failures.Count > 0)
                {
                    entry.State = "Failed";
                    before.Entries.RemoveAll(e => e.Id == entry.Id);
                    before.Entries.Add(entry);
                }
                try { SaveManifest(before); }
                catch (Exception e) { failures.Add("清单回滚失败：" + e.Message); }
            }
            if (failures.Count > 0) SaveRecoveryEvidence(entry, failures);
        }
        VNResourceService.Reset();
        string status = undo.Count == 0 && !journalWritten ? "未修改资源" : failures.Count == 0 ? "事务已回滚" : "回滚未完整完成：" + string.Join("；", failures);
        return SetError(operation + "失败：" + error.Message + "\n" + status + (entry == null ? "" : "\n恢复记录：" + Path.Combine(EntryDir(entry.Id), EntryMetadataName)));
    }

    private static void ValidatePayloadIsolation(List<KeyValuePair<string, string>> payloads)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var payload in payloads)
        {
            foreach (string rawPath in new[] { payload.Key, payload.Key + ".meta", payload.Value, payload.Value + ".meta" })
            {
                string path = Path.GetFullPath(rawPath);
                if (!paths.Add(path)) throw new InvalidDataException("回收站载荷或暂存路径重叠：" + path);
            }
        }
        foreach (string path in paths)
        {
            string parent = Path.GetDirectoryName(path);
            while (!string.IsNullOrEmpty(parent))
            {
                if (paths.Contains(parent)) throw new InvalidDataException("回收站载荷存在父子路径重叠：" + path);
                parent = Path.GetDirectoryName(parent);
            }
        }
    }

    private static string PurgeEntries(string id, bool clearAll)
    {
        bool entered = false;
        bool journalWritten = false;
        TrashManifest before = null;
        List<TrashEntry> entries = null;
        var undo = new List<Action>();
        var deleted = new List<string>();
        try
        {
            BeginTransaction();
            entered = true;
            var manifest = LoadManifest();
            before = CloneManifest(manifest);
            entries = clearAll ? manifest.Entries.ToList() : new List<TrashEntry> { FindEntry(manifest, id) };
            var payloads = new List<KeyValuePair<string, string>>();
            var directoryPayloads = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries)
            {
                PrepareLegacyRecords(entry);
                foreach (var record in entry.Files)
                {
                    string source = record.Kind == TrashKind.Physical ? PayloadPath(entry, record) : FullAssetPath(record.OriginalAssetPath);
                    RequirePair(source, record.OriginalAssetPath, record.IsDirectory);
                    if (!string.Equals(ReadMetaGuid(source + ".meta"), record.Guid, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("永久删除预检发现资产身份不符：" + record.OriginalAssetPath);
                    if (record.Kind != TrashKind.Physical) continue;
                    string staged = record.IsDirectory
                        ? Path.Combine(EntryDir(entry.Id), ".purge", "directory", record.Guid)
                        : Path.Combine(EntryDir(entry.Id), ".purge", "file", record.OriginalAssetPath.Substring("Assets/".Length));
                    RequireVacant(staged, staged);
                    if (record.IsDirectory && Directory.EnumerateFileSystemEntries(source).Any())
                        throw new IOException("目录载荷有未知内容，不会递归永久删除：" + source);
                    foreach (string file in record.IsDirectory ? new[] { source + ".meta" } : new[] { source, source + ".meta" })
                    {
                        if ((File.GetAttributes(file) & FileAttributes.ReadOnly) != 0) throw new IOException("载荷为只读：" + file);
                        using (new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None)) { }
                    }
                    if (record.IsDirectory) directoryPayloads.Add(staged);
                    payloads.Add(new KeyValuePair<string, string>(source, staged));
                }
            }
            if (entries.Count == 0) return null;
            ValidatePayloadIsolation(payloads);
            foreach (var entry in entries)
            {
                entry.State = "Purging";
                SaveEntry(entry);
                journalWritten = true;
            }
            foreach (var payload in payloads)
            {
                MoveJournaled(payload.Key, payload.Value, undo, directoryPayloads.Contains(payload.Value));
                MoveJournaled(payload.Key + ".meta", payload.Value + ".meta", undo);
            }
            foreach (var payload in payloads)
            {
                if (directoryPayloads.Contains(payload.Value)) Directory.Delete(payload.Value, false);
                else File.Delete(payload.Value);
                deleted.Add(payload.Value);
                File.Delete(payload.Value + ".meta");
                deleted.Add(payload.Value + ".meta");
            }
            foreach (var entry in entries)
            {
                entry.State = "Deleted";
                SaveEntry(entry);
                manifest.Entries.Remove(entry);
            }
            SaveManifest(manifest);
            LastError = null;
            return null;
        }
        catch (Exception error)
        {
            var failures = new List<string>();
            for (int i = undo.Count - 1; i >= 0; i--)
            {
                try { undo[i](); }
                catch (Exception e) { failures.Add(e.Message); }
            }
            if (journalWritten && entries != null)
            {
                foreach (var entry in entries)
                {
                    entry.State = deleted.Count == 0 && failures.Count == 0 ? "Active" : "FailedPermanentDelete";
                    entry.LastFailure = error.Message + "；已永久删除文件数：" + deleted.Count;
                    try { SaveEntry(entry); }
                    catch (Exception e) { failures.Add(e.Message); }
                    if (before != null)
                    {
                        before.Entries.RemoveAll(e => e.Id == entry.Id);
                        before.Entries.Add(entry);
                    }
                }
                try { SaveManifest(before); }
                catch (Exception e) { failures.Add("清单回滚失败：" + e.Message); }
                if (failures.Count > 0 || deleted.Count > 0)
                    foreach (var entry in entries) SaveRecoveryEvidence(entry, failures);
            }
            string status = deleted.Count > 0 ? "已有" + deleted.Count + "个文件不可逆删除；保留条目和剩余载荷，不能宣称完整回滚。"
                : failures.Count == 0 ? "尚未永久删除载荷；暂存操作已回滚。" : "暂存回滚未完成。";
            return SetError((clearAll ? "清空" : "永久删除") + "失败：" + error.Message + "\n" + status
                + (failures.Count == 0 ? "" : "\n" + string.Join("；", failures)) + "\n恢复信息：" + TrashRoot);
        }
        finally { if (entered) EndTransaction(); }
    }
}
