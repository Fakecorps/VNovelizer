using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 资源导入与删除服务。
/// 处理文件复制、文件夹递归导入、删除确认等操作。
/// </summary>
public static class ResourceImportService
{
    /// <summary>导入单个文件到目标分类路径（targetSubfolder = 相对类别根的子文件夹，null/空 = 类别根）</summary>
    public static ImportResult ImportSingleFile(ResType type, string srcPath, string targetSubfolder = null,
        Func<string, bool> confirmOverwrite = null)
    {
        try
        {
            if (string.IsNullOrEmpty(srcPath) || !File.Exists(srcPath)) return ImportResult.Failed("源文件不存在");
            if (!IsSupported(type, srcPath)) return ImportResult.Skipped("不支持的文件格式");
            string root = ResourceAssetService.GetPathFromConfig(type);
            if (string.IsNullOrEmpty(root)) return ImportResult.Failed("未配置目标路径");
            string sub = type == ResType.Video ? "" : (targetSubfolder ?? "").Replace('\\', '/').Trim().Trim('/');
            if (sub.Length > 0)
            {
                string invalid = VNAddressablesRegistrar.ValidateLogicalName(sub);
                if (invalid != null) return ImportResult.Failed(invalid);
            }
            string dest = Path.GetFullPath(Path.Combine(root, sub, Path.GetFileName(srcPath)));
            string invalidTarget = ValidateDestination(root, dest);
            if (invalidTarget != null) return ImportResult.Failed(invalidTarget);
            if (string.Equals(Path.GetFullPath(srcPath), dest, StringComparison.OrdinalIgnoreCase))
                return ImportResult.Skipped("文件已在目标位置");
            string conflict = ValidateImportAddresses(type, root, new[] { dest });
            if (conflict != null) return ImportResult.Failed(conflict);
            if (File.Exists(dest))
            {
                string assetPath = ToAssetPath(dest);
                bool replace = confirmOverwrite != null
                    ? confirmOverwrite(assetPath)
                    : EditorUtility.DisplayDialog("文件已存在", $"文件 '{Path.GetFileName(dest)}' 已存在，要覆盖吗？", "覆盖", "跳过");
                if (!replace) return ImportResult.Cancelled();
            }
            CopyAtomically(srcPath, dest);
            return ImportResult.Ok(Path.GetFileName(dest));
        }
        catch (Exception e)
        {
            return ImportResult.Failed(e.Message);
        }
    }

    public static int ImportDirectoryRecursive(ResType type, string srcDir, string destDir)
    {
        return ImportDirectory(type, srcDir, destDir).Imported;
    }

    public static ResourceImportSummary ImportDirectory(ResType type, string srcDir, string destDir)
    {
        var result = new ResourceImportSummary();
        try
        {
            string source = Path.GetFullPath(srcDir).TrimEnd(Path.DirectorySeparatorChar);
            string destination = Path.GetFullPath(destDir).TrimEnd(Path.DirectorySeparatorChar);
            string root = ResourceAssetService.GetPathFromConfig(type);
            string invalid = ValidateDestination(root, Path.Combine(destination, "_probe"));
            if (invalid != null) throw new IOException(invalid);
            if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase) ||
                destination.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new IOException("目标目录不能位于所选源目录之内");
            var copies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                if (!IsSupported(type, file)) { result.Unsupported++; continue; }
                string relative = type == ResType.Video ? Path.GetFileName(file) : Path.GetRelativePath(source, file);
                string target = Path.GetFullPath(Path.Combine(destination, relative));
                string logical = relative.Replace('\\', '/');
                if (type != ResType.Video) logical = logical.Substring(0, logical.Length - Path.GetExtension(logical).Length);
                if (copies.ContainsKey(target) || keys.ContainsKey(logical))
                    throw new IOException($"源目录存在重复目标名：{relative}。视频导入采用平铺，请先处理同名文件。");
                copies.Add(target, file);
                keys.Add(logical, target);
            }
            string conflict = ValidateImportAddresses(type, root, copies.Keys);
            if (conflict != null) throw new IOException(conflict);
            foreach (var copy in copies)
            {
                if (File.Exists(copy.Key)) { result.SkippedExisting++; continue; }
                try { CopyAtomically(copy.Value, copy.Key); result.Imported++; }
                catch (Exception e) { result.Errors.Add($"{Path.GetFileName(copy.Value)}：{e.Message}"); }
            }
        }
        catch (Exception e) { result.Errors.Add(e.Message); }
        return result;
    }

    private static bool IsSupported(ResType type, string path)
    {
        string[] allowed = UIElementBuilder.GetExtensionsList(type);
        return allowed.Length == 0 || allowed.Contains(Path.GetExtension(path).TrimStart('.').ToLowerInvariant());
    }

    private static string ValidateDestination(string root, string destination)
    {
        if (string.IsNullOrEmpty(root)) return "未配置目标路径";
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string assets = Path.GetFullPath(Application.dataPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullRoot.StartsWith(assets, StringComparison.OrdinalIgnoreCase)) return "导入目标必须位于项目 Assets 内";
        if (!Path.GetFullPath(destination).StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) return "导入目标超出类别目录";
        return null;
    }

    private static string ValidateImportAddresses(ResType type, string root, IEnumerable<string> destinations)
    {
        if (type == ResType.Video || !VNAddressablesRegistrar.IsManagedMode) return null;
        string category = VNAddressablesRegistrar.GetCategoryKey(type);
        var changes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string dest in destinations)
        {
            string assetPath = ToAssetPath(dest);
            string logical = ResourceAssetService.GetRelativeLogicalName(root, assetPath);
            changes[assetPath] = VNAddressablesRegistrar.ResolveEffectiveResourceKey(assetPath, category + "/" + logical);
        }
        return VNAddressablesRegistrar.ValidateAddressChanges(changes);
    }

    private static string ToAssetPath(string path)
    {
        string full = Path.GetFullPath(path).Replace('\\', '/');
        string root = Application.dataPath.Replace('\\', '/').TrimEnd('/') + "/";
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? "Assets/" + full.Substring(root.Length) : path;
    }

    private static void CopyAtomically(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination));
        string temp = destination + ".vnimport-" + Guid.NewGuid().ToString("N") + "~";
        try
        {
            File.Copy(source, temp, false);
            if (File.Exists(destination)) File.Replace(temp, destination, null);
            else File.Move(temp, destination);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    /// <summary>删除资源文件</summary>
    public static bool DeleteAsset(string assetPath)
    {
        if (EditorUtility.DisplayDialog("删除确认",
            $"确定要删除以下文件吗？\n\n{assetPath}\n\n此操作无法撤销！",
            "删除", "取消"))
        {
            AssetDatabase.DeleteAsset(assetPath);
            return true;
        }
        return false;
    }

    /// <summary>创建文件夹（如果不存在）</summary>
    public static bool CreateFolderIfMissing(string path)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
                AssetDatabase.Refresh();
            }
            return true;
        }
        catch (Exception e)
        {
            EditorUtility.DisplayDialog("错误", $"创建失败：{e.Message}", "确定");
            return false;
        }
    }
}

public class ResourceImportSummary
{
    public int Imported;
    public int SkippedExisting;
    public int Unsupported;
    public readonly List<string> Errors = new List<string>();
    public string Message => $"导入成功：{Imported}，同名跳过：{SkippedExisting}，不支持格式：{Unsupported}，失败：{Errors.Count}" +
        (Errors.Count > 0 ? "\n\n" + string.Join("\n", Errors) : "");
}

/// <summary>导入操作的结果</summary>
public class ImportResult
{
    public bool Success { get; private set; }
    public bool WasSkipped { get; private set; }
    public string FileName { get; private set; }
    public string Error { get; private set; }

    private ImportResult(bool success, bool skipped, string fileName, string error)
    {
        Success = success;
        WasSkipped = skipped;
        FileName = fileName;
        Error = error;
    }

    public static ImportResult Ok(string fileName) => new ImportResult(true, false, fileName, null);
    public static ImportResult Cancelled() => new ImportResult(false, true, null, "用户取消");
    public static ImportResult Skipped(string reason) => new ImportResult(false, true, null, reason);
    public static ImportResult Failed(string error) => new ImportResult(false, false, null, error);
}
