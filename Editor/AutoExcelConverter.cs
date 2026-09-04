using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;

/// <summary>
/// 自动 Excel → CSV 转换器。
/// 在 EditorApplication.update 中每 2 秒检查 Excel 文件夹中的文件修改时间，
/// 发现被修改的文件时静默转换为 CSV。
/// 当用户从 Excel 切回 Unity 时，update 恢复执行，立即检测到变化并转换。
/// 可在 VNProjectConfig.AutoConvertExcel 中关闭。
/// </summary>
[InitializeOnLoad]
public static class AutoExcelConverter
{
    /// <summary>记录每个 Excel 文件的上次修改时间，用于检测变化</summary>
    private static readonly Dictionary<string, long> _lastWriteTicks = new Dictionary<string, long>();

    /// <summary>上次检查的时间（EditorApplication.timeSinceStartup）</summary>
    private static double _lastCheckTime = 0;

    /// <summary>
    /// 轮询挂起截止时间（EditorApplication.timeSinceStartup）。
    /// 行演出编辑器保存 CSV + 镜像写回 xlsx 期间挂起，避免轮询读到半写文件。
    /// </summary>
    private static double _suspendUntil = 0;

    /// <summary>检查间隔（秒）</summary>
    private const double CheckInterval = 2.0;

    /// <summary>是否已完成首次扫描</summary>
    private static bool _firstScanDone = false;

    static AutoExcelConverter()
    {
        EditorApplication.update += Update;
    }

    private static void Update()
    {
        // 首次运行：扫描并记录所有 Excel 文件时间戳（不触发转换）
        if (!_firstScanDone)
        {
            _firstScanDone = true;
            ScanAndRecordTimestamps();
            _lastCheckTime = EditorApplication.timeSinceStartup;
            return;
        }

        // 挂起期间不检查（编辑器正在写 CSV / 镜像写回 xlsx）
        double now = EditorApplication.timeSinceStartup;
        if (now < _suspendUntil) return;

        // 每 CheckInterval 秒检查一次
        if (now - _lastCheckTime < CheckInterval) return;
        _lastCheckTime = now;

        TryConvertModifiedExcelFiles();
    }

    private static void TryConvertModifiedExcelFiles()
    {
        // 静默探测：刚安装插件（未跑初始化向导）时配置不存在是合法状态，不刷错误日志
        if (!VNProjectConfig.TryGetInstance(out VNProjectConfig config)) return;
        if (!config.AutoConvertExcel) return;

        string excelFolderPath = config.GetExcelFolderPath();
        string csvOutputPath = config.GetCsvOutputPath();

        if (string.IsNullOrEmpty(excelFolderPath) || string.IsNullOrEmpty(csvOutputPath))
            return;

        string absExcelPath = Path.GetFullPath(excelFolderPath);
        if (!Directory.Exists(absExcelPath)) return;

        // 扫描所有 Excel 文件
        string[] files = Directory.GetFiles(absExcelPath, "*.*", SearchOption.AllDirectories);
        List<string> modifiedFiles = new List<string>();

        foreach (string file in files)
        {
            string ext = Path.GetExtension(file).ToLower();
            string fileName = Path.GetFileName(file);

            if ((ext != ".xlsx" && ext != ".xls") || fileName.StartsWith("~$"))
                continue;

            long currentTicks = File.GetLastWriteTime(file).Ticks;

            if (!_lastWriteTicks.TryGetValue(file, out long lastTicks))
            {
                // 新文件，记录但不触发转换
                _lastWriteTicks[file] = currentTicks;
                continue;
            }

            if (currentTicks > lastTicks)
            {
                modifiedFiles.Add(file);
                _lastWriteTicks[file] = currentTicks;
            }
        }

        if (modifiedFiles.Count == 0) return;

        // 静默转换被修改的文件
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        int successCount = 0;
        int failCount = 0;

        foreach (string file in modifiedFiles)
        {
            try
            {
                ExcelToCsvConverter.ConvertFile(file, csvOutputPath);

                // 【防循环】镜像写回可能更新了 xlsx 的修改时间，重新记录最新时间戳，
                // 避免下一轮轮询误判"xlsx 又被修改"而再次触发转换（死循环）。
                _lastWriteTicks[file] = File.GetLastWriteTime(file).Ticks;

                successCount++;
                Debug.Log($"[AutoConvert] 已转换: {Path.GetFileNameWithoutExtension(file)}.csv");
            }
            catch (System.Exception e)
            {
                failCount++;
                Debug.LogWarning($"[AutoConvert] 转换失败: {Path.GetFileName(file)} — {e.Message}");
            }
        }

        if (successCount > 0)
        {
            AssetDatabase.Refresh();
            // 工作区新增/更新的 CSV 自动注册进 Addressables（未初始化的项目自动跳过）
            // 【修复】AssetDatabase.Refresh() 是异步的，需用 delayCall 推迟到导入管线完成后执行，
            //         避免 GUID 尚未就绪时 "资产尚未导入，跳过" 导致漏注册
            EditorApplication.delayCall += VNAddressablesRegistrar.SyncWorkspace;
            Debug.Log($"<color=green>[AutoConvert] 自动转换完成: {successCount} 个文件" +
                      (failCount > 0 ? $", 失败 {failCount} 个" : "") + "</color>");
        }
    }

    /// <summary>
    /// 首次扫描：记录所有 Excel 文件的当前修改时间，不触发转换。
    /// </summary>
    private static void ScanAndRecordTimestamps()
    {
        _lastWriteTicks.Clear();

        VNProjectConfig config = VNProjectConfig.Instance;
        if (config == null) return;

        string excelFolderPath = config.GetExcelFolderPath();
        if (string.IsNullOrEmpty(excelFolderPath)) return;

        string absExcelPath = Path.GetFullPath(excelFolderPath);
        if (!Directory.Exists(absExcelPath)) return;

        string[] files = Directory.GetFiles(absExcelPath, "*.*", SearchOption.AllDirectories);
        foreach (string file in files)
        {
            string ext = Path.GetExtension(file).ToLower();
            string fileName = Path.GetFileName(file);
            if ((ext == ".xlsx" || ext == ".xls") && !fileName.StartsWith("~$"))
            {
                _lastWriteTicks[file] = File.GetLastWriteTime(file).Ticks;
            }
        }
    }

    /// <summary>
    /// 重置所有文件的修改时间记录。
    /// 供 ScriptManagerWindow 在手动转换后调用，避免重复转换。
    /// </summary>
    public static void RefreshAllFileTimestamps()
    {
        ScanAndRecordTimestamps();
    }

    /// <summary>
    /// 挂起自动转换轮询（秒）。行演出编辑器写 CSV 并镜像写回 xlsx 期间调用，
    /// 与「临时文件 + File.Replace」原子写构成双保险，避免轮询读到半写文件
    /// （见 VNCommandChainSpec.md §11.5 保存竞态防护）。
    /// </summary>
    public static void SuspendForSeconds(double seconds)
    {
        _suspendUntil = EditorApplication.timeSinceStartup + seconds;
    }

    /// <summary>
    /// 刷新单个文件的修改时间记录：镜像写回 xlsx 属于程序性写入，会更新其修改时间，
    /// 调用本方法避免下一轮轮询把它误判为「用户在 Excel 中的修改」而重复转换（防死循环）。
    /// 只刷新指定文件，不影响其他文件尚未处理的真实修改（与 <see cref="RefreshAllFileTimestamps"/> 区分）。
    /// </summary>
    public static void RefreshTimestampForFile(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return;
        // 统一键格式：轮询扫描（TryConvertModifiedExcelFiles）用 Path.GetFullPath 的
        // 绝对路径做字典键，而镜像写回方传入的是 Assets/... 相对路径——不规范化则
        // 刷新写入另一个键，防死循环失效，下次轮询会把程序性写回误判为「用户在
        // Excel 中的修改」而触发一次多余转换。
        filePath = Path.GetFullPath(filePath);
        _lastWriteTicks[filePath] = File.GetLastWriteTime(filePath).Ticks;
    }
}
