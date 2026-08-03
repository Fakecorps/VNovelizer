using System;
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
    /// <summary>导入单个文件到目标分类路径</summary>
    public static ImportResult ImportSingleFile(ResType type, string srcPath)
    {
        string ext = Path.GetExtension(srcPath).ToLower().Replace(".", "");
        string[] allowed = UIElementBuilder.GetExtensionsList(type);
        if (allowed.Length > 0 && !allowed.Contains(ext))
            return ImportResult.Skipped("不支持的文件格式");

        string targetAssetPath = ResourceAssetService.GetPathFromConfig(type);
        if (string.IsNullOrEmpty(targetAssetPath))
            return ImportResult.Failed("未配置目标路径");

        string fileName = Path.GetFileName(srcPath);
        string projectRoot = Path.GetDirectoryName(Application.dataPath);
        string destPath = Path.Combine(projectRoot, targetAssetPath, fileName);

        if (File.Exists(destPath))
        {
            bool replace = EditorUtility.DisplayDialog("文件已存在",
                $"文件 '{fileName}' 已存在，要覆盖吗？", "覆盖", "跳过");
            if (!replace) return ImportResult.Cancelled();
        }

        try
        {
            File.Copy(srcPath, destPath, true);
            return ImportResult.Ok(fileName);
        }
        catch (Exception e)
        {
            return ImportResult.Failed(e.Message);
        }
    }

    /// <summary>递归导入文件夹</summary>
    public static int ImportDirectoryRecursive(ResType type, string srcDir, string destDir)
    {
        int count = 0;
        string[] allowed = UIElementBuilder.GetExtensionsList(type);
        foreach (var file in Directory.GetFiles(srcDir, "*.*", SearchOption.AllDirectories))
        {
            string ext = Path.GetExtension(file).ToLower().Replace(".", "");
            if (allowed.Length > 0 && !allowed.Contains(ext)) continue;
            string rel = Path.GetRelativePath(srcDir, file);
            string dest = Path.Combine(destDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest));
            if (!File.Exists(dest))
            {
                try { File.Copy(file, dest, false); count++; }
                catch (Exception e) { Debug.LogError($"[资源管理器] 复制失败: {e.Message}"); }
            }
        }
        return count;
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
