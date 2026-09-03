using UnityEngine;
using UnityEditor;
using System.IO;
using System.Text;
using System.Collections.Generic;
using ExcelDataReader;
using ClosedXML.Excel;

public class ExcelToCsvConverter : EditorWindow
{
    public static void ConvertAllExcelFiles()
    {
        // 注册编码提供程序（ExcelDataReader 需要）
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

        // 1. 获取全局配置
        VNProjectConfig config = VNProjectConfig.Instance;
        if (config == null)
        {
            Debug.LogError("无法加载 VNProjectConfig，请检查 Resources 文件夹。");
            return;
        }

        // 2. 从配置中获取路径
        string excelFolderPath = config.GetExcelFolderPath();
        string csvOutputPath = config.GetCsvOutputPath();

        // 3. 路径校验
        if (string.IsNullOrEmpty(excelFolderPath) || string.IsNullOrEmpty(csvOutputPath))
        {
            Debug.LogError("路径配置未填写！请检查 Resources/VNProjectConfig 配置文件中的 ExcelSourceFolder 和 CsvOutputFolder。");
            return;
        }

        Debug.Log($"来源路径: {excelFolderPath}");
        Debug.Log($"输出路径: {csvOutputPath}");

        if (!Directory.Exists(excelFolderPath))
        {
            Debug.LogError($"[错误] 找不到源文件夹: {excelFolderPath}");
            return;
        }

        // 如果输出目录不存在，自动创建
        if (!Directory.Exists(csvOutputPath))
        {
            Directory.CreateDirectory(csvOutputPath);
        }

        // 4. 获取所有文件并遍历
        string[] files = Directory.GetFiles(excelFolderPath, "*.*", SearchOption.AllDirectories);
        int updateCount = 0;
        int createCount = 0;

        foreach (string file in files)
        {
            string ext = Path.GetExtension(file).ToLower();
            string fileName = Path.GetFileName(file);

            // 过滤掉临时文件 (~$) 和非 Excel 文件
            if ((ext == ".xlsx" || ext == ".xls") && !fileName.StartsWith("~$"))
            {
                try
                {
                    bool isOverwritten = ConvertFile(file, csvOutputPath);

                    if (isOverwritten) updateCount++;
                    else createCount++;
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"文件 {fileName} 转换失败: {e.Message}\n{e.StackTrace}");
                }
            }
        }

        // 5. 刷新资源
        AssetDatabase.Refresh();

        // 6. 工作区新增/更新的 CSV 自动注册进 Addressables（未初始化 Addressables 的项目自动跳过）
        // 【修复】AssetDatabase.Refresh() 是异步的，立即调用 SyncWorkspace 时新 CSV 的 GUID 可能尚未就绪
        //         改用 delayCall 推迟到导入管线完成后执行，避免 "资产尚未导入，跳过" 导致漏注册
        EditorApplication.delayCall += VNAddressablesRegistrar.SyncWorkspace;

        Debug.Log($"<color=green>转换完成！新建: {createCount}, 更新: {updateCount}</color>");
    }

    /// <summary>
    /// 转换单个文件
    /// </summary>
    /// <param name="filePath">Excel源文件绝对路径</param>
    /// <param name="targetFolder">CSV输出文件夹绝对路径</param>
    /// <returns>是否覆盖了旧文件</returns>
    public static bool ConvertFile(string filePath, string targetFolder)
    {
        // ---- 1. 读取 xlsx 全部行（ExcelDataReader，只读） ----
        List<string[]> allRows = new List<string[]>();
        int maxColumnCount = 0;

        using (var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            using (var reader = ExcelReaderFactory.CreateReader(stream))
            {
                bool firstRow = true;
                while (reader.Read())
                {
                    int fieldCount = reader.FieldCount;
                    string[] row = new string[fieldCount];

                    for (int j = 0; j < fieldCount; j++)
                    {
                        object cellValue = reader.GetValue(j);
                        row[j] = cellValue != null ? cellValue.ToString() : "";
                    }

                    allRows.Add(row);

                    // 第一行用于确定最大列数
                    if (firstRow)
                    {
                        // 检查第一行的实际列数（从右往左找最后一个非空列）
                        for (int i = fieldCount - 1; i >= 0; i--)
                        {
                            if (!string.IsNullOrEmpty(row[i]))
                            {
                                maxColumnCount = i + 1;
                                break;
                            }
                        }
                        if (maxColumnCount == 0)
                        {
                            maxColumnCount = fieldCount; // 如果第一行全空，使用字段数
                        }
                        firstRow = false;
                    }
                }
            }
        }

        if (allRows.Count == 0)
        {
            Debug.LogWarning($"文件 {Path.GetFileName(filePath)} 没有数据");
            return false;
        }

        string fileName = Path.GetFileNameWithoutExtension(filePath);
        string finalPath = Path.Combine(targetFolder, fileName + ".csv");
        bool fileExists = File.Exists(finalPath);

        // ---- 2. 列分工：Command 列三方合并（决策见 VNCommandChainSpec.md §11.5）----
        // 数据列从 xlsx 全量更新；Command 列编辑权归 Unity 图编辑器，做单元格级三方合并：
        //   base = 上次转换时的 Command 快照（sidecar 文件）
        //   · xlsx == base（Excel 未改）→ 保留 CSV 值（图编辑器的修改生效）
        //   · csv  == base（CSV 未改） → 采用 xlsx 值（Excel 旧工作流的修改生效，过渡期零感知）
        //   · 两边都改且不同 → CSV 值（Command 永远以 CSV 为准）+ 冲突警告
        //   · sidecar 不存在（首次转换/升级）→ 全部采用 xlsx 值（与旧版行为一致），并建立快照基准
        int commandColIndex = GetCommandColumnIndex(maxColumnCount);
        string sidecarPath = GetSidecarPath(finalPath);
        bool sidecarExists = File.Exists(sidecarPath);

        Dictionary<string, string> csvCommands = (commandColIndex >= 0 && fileExists)
            ? LoadCommandColumnFromCsv(finalPath, commandColIndex)
            : new Dictionary<string, string>();
        Dictionary<string, string> baseCommands = sidecarExists
            ? LoadCommandSnapshot(sidecarPath)
            : new Dictionary<string, string>();

        var mergedCommands = new Dictionary<string, string>();   // rowId → 合并终值
        var writeBackDiffs = new List<(int excelRow, string value)>(); // 镜像写回差异（Excel 1-based 行号）

        // ---- 3. 生成 CSV ----
        StringBuilder csvContent = new StringBuilder();
        bool isFirstRow = true;

        for (int r = 0; r < allRows.Count; r++)
        {
            string[] row = allRows[r];
            int currentLoopLimit = maxColumnCount;
            string rowId = (row.Length > 0 ? row[0] : "").Trim();

            for (int j = 0; j < currentLoopLimit; j++)
            {
                string cellValueStr = (j < row.Length) ? (row[j] ?? "") : "";

                // 【列分工】Command 列走三方合并（表头行原样保留）
                if (j == commandColIndex && !isFirstRow)
                {
                    string xlsxVal = cellValueStr;
                    string csvVal = csvCommands.TryGetValue(rowId, out var c) ? c : null;
                    string baseVal = baseCommands.TryGetValue(rowId, out var b) ? b : null;

                    string merged = MergeCommandCell(filePath, rowId, xlsxVal, csvVal, baseVal, sidecarExists);
                    cellValueStr = merged;
                    mergedCommands[rowId] = merged;

                    // 镜像写回差异：xlsx 原值 ≠ 合并终值
                    if (merged != xlsxVal)
                        writeBackDiffs.Add((r + 1, merged));
                }

                csvContent.Append(EscapeCsvCell(cellValueStr));

                if (j < currentLoopLimit - 1)
                    csvContent.Append(",");
            }
            csvContent.AppendLine();
            isFirstRow = false;
        }

        // UTF-8 (无BOM) 写入
        File.WriteAllText(finalPath, csvContent.ToString(), new UTF8Encoding(false));

        // ---- 4. 保存新的 base 快照（下次三方合并的基准） ----
        SaveCommandSnapshot(sidecarPath, mergedCommands);

        // ---- 5. 镜像写回：把合并终值写回 xlsx 的 Command 列（ClosedXML） ----
        // 仅在存在差异时物理写入（常态零写入零扰动）；写回后由调用方（AutoExcelConverter）刷新时间戳防循环
        if (writeBackDiffs.Count > 0)
        {
            MirrorCommandsBackToExcel(filePath, writeBackDiffs, commandColIndex + 1);
        }

        return fileExists;
    }

    /// <summary>
    /// 按列数布局确定 Command 列索引（与 ScriptParser 的解析布局一致）：
    /// 14 列新格式 → 12；12 列旧格式 → 10；其余（列数不足）→ -1（无 Command 列，不做列分工）
    /// </summary>
    private static int GetCommandColumnIndex(int columnCount)
    {
        if (columnCount >= 14) return 12;
        if (columnCount >= 12) return 10;
        return -1;
    }

    /// <summary>
    /// 读取现有 CSV 的 Command 列（rowId → command），复用 ScriptParser 的引号感知解析保证行为一致
    /// </summary>
    private static Dictionary<string, string> LoadCommandColumnFromCsv(string csvPath, int commandColIndex)
    {
        var result = new Dictionary<string, string>();
        try
        {
            string content = File.ReadAllText(csvPath, new UTF8Encoding(false));
            string[] lines = ScriptParser.SplitCSVLines(content);

            for (int i = 0; i < lines.Length; i++)
            {
                if (i == 0) continue; // 表头行
                string line = lines[i].Trim();
                if (string.IsNullOrEmpty(line)) continue;

                string[] cols = ScriptParser.SplitCSV(line);
                if (cols.Length == 0) continue;

                string id = cols[0].Trim();
                if (string.IsNullOrEmpty(id)) continue;

                // 按该行列数自适应 Command 位置（兼容新旧布局混存）
                int idx = GetCommandColumnIndex(cols.Length);
                if (idx >= 0 && idx < cols.Length)
                    result[id] = cols[idx];
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[CmdSync] 读取现有 CSV 的 Command 列失败（本次按无基准处理）: {csvPath} — {e.Message}");
        }
        return result;
    }

    /// <summary>
    /// Command 单元格三方合并
    /// </summary>
    private static string MergeCommandCell(string xlsxPath, string rowId, string xlsxVal, string csvVal, string baseVal, bool sidecarExists)
    {
        xlsxVal = xlsxVal ?? "";

        // sidecar 不存在（首次转换/从旧版本升级）：全部采用 xlsx 值，与旧版全量覆盖行为完全一致（零感知升级）
        if (!sidecarExists) return xlsxVal;

        // 新插入的行（快照中无基准）：Excel 是唯一编辑过它的地方
        if (baseVal == null) return xlsxVal;

        // Excel 未改（xlsx == base）→ 保留 CSV 值（图编辑器的修改生效；CSV 无此行则回退 xlsx 值）
        if (xlsxVal == baseVal) return csvVal ?? xlsxVal;

        // Excel 改了；CSV 未改（csv == base 或 CSV 无此行）→ Excel 修改生效（旧工作流兼容）
        if (csvVal == null || csvVal == baseVal) return xlsxVal;

        // 两边都改且不同 → 冲突：Command 永远以 CSV 为准（决策见 VNCommandChainSpec.md §11.4/§11.5）
        Debug.LogWarning($"[CmdSync] 行 {rowId} 的 Command 列在 Excel 与 CSV（图编辑器）两侧均被修改且不一致，已采用 CSV 值。" +
                         $"Excel 侧的修改被忽略：\"{Truncate(xlsxVal, 40)}\" → \"{Truncate(csvVal, 40)}\"（{Path.GetFileName(xlsxPath)}）");
        return csvVal;
    }

    /// <summary>
    /// 镜像写回：把 CSV 的 Command 终值写回 xlsx 对应列（ClosedXML，原地编辑保留用户格式）
    /// </summary>
    private static void MirrorCommandsBackToExcel(string xlsxPath, List<(int excelRow, string value)> diffs, int excelCol)
    {
        if (Path.GetExtension(xlsxPath).ToLower() == ".xls")
        {
            Debug.LogWarning($"[CmdSync] {Path.GetFileName(xlsxPath)} 为 .xls 旧格式，不支持镜像写回（请另存为 .xlsx）；CSV 侧不受影响。");
            return;
        }

        try
        {
            using (var workbook = new XLWorkbook(xlsxPath))
            {
                var ws = workbook.Worksheet(1);
                foreach (var d in diffs)
                    ws.Cell(d.excelRow, excelCol).Value = d.value;
                workbook.Save();
            }
            Debug.Log($"[CmdSync] 已镜像写回 {diffs.Count} 个 Command 单元格到 {Path.GetFileName(xlsxPath)}（Excel 侧视图已同步）");
        }
        catch (IOException)
        {
            Debug.LogWarning($"[CmdSync] 镜像写回失败（{Path.GetFileName(xlsxPath)} 可能被 Excel 占用），下次转换时自动重试。CSV 侧不受影响。");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[CmdSync] 镜像写回异常（{Path.GetFileName(xlsxPath)}）：{e.Message}。CSV 侧不受影响。");
        }
    }

    /// <summary>
    /// 行命令编辑器保存后调用：把该行 Command 终值即时镜像写回对应 xlsx（ClosedXML 原地编辑），
    /// 并同步更新三方合并基准（.csv.cmdmap.json），使下次转换零扰动。
    /// 找不到 xlsx / .xls 旧格式 / 文件被 Excel 占用时仅告警，CSV 侧不受影响（下次转换时自动补写）。
    /// </summary>
    /// <param name="csvPath">已保存的 CSV 路径（用于反推同名 xlsx 与 sidecar 路径）</param>
    /// <param name="rowId">行 ID（CSV 第一列值）</param>
    /// <param name="command">该行 Command 列终值</param>
    public static void MirrorRowCommandBackToExcel(string csvPath, string rowId, string command)
    {
        if (string.IsNullOrEmpty(csvPath)) return;
        rowId = rowId?.Trim() ?? "";
        if (rowId.Length == 0) return;

        string xlsxPath = LocateExcelFile(csvPath);
        if (xlsxPath == null)
        {
            Debug.LogWarning($"[CmdSync] 未找到 {Path.GetFileNameWithoutExtension(csvPath)} 对应的 Excel 文件，" +
                             "Command 已保存到 CSV（Excel 侧将在下次转换时同步）。");
            return;
        }

        if (Path.GetExtension(xlsxPath).ToLower() == ".xls")
        {
            Debug.LogWarning($"[CmdSync] {Path.GetFileName(xlsxPath)} 为 .xls 旧格式，不支持镜像写回（请另存为 .xlsx）；CSV 侧不受影响。");
            return;
        }

        // ---- 1. 读 xlsx：定位 rowId 的行号 + 按布局确定 Command 列（与 ConvertFile 同一规则） ----
        int excelRow = -1;
        int commandColIndex = -1;
        try
        {
            using (var stream = File.Open(xlsxPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = ExcelReaderFactory.CreateReader(stream))
            {
                int r = 0;
                while (reader.Read())
                {
                    r++;
                    if (r == 1)
                        commandColIndex = GetCommandColumnIndex(reader.FieldCount);

                    object idVal = reader.GetValue(0);
                    if (idVal != null && idVal.ToString().Trim() == rowId)
                    {
                        excelRow = r;
                        break;
                    }
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[CmdSync] 读取 {Path.GetFileName(xlsxPath)} 定位行失败：{e.Message}。CSV 侧不受影响。");
            return;
        }

        if (excelRow < 0)
        {
            Debug.LogWarning($"[CmdSync] 在 {Path.GetFileName(xlsxPath)} 中未找到行 {rowId}，跳过镜像写回。CSV 侧不受影响。");
            return;
        }
        if (commandColIndex < 0) return; // 列布局不满足（无 Command 列），静默跳过

        // ---- 2. ClosedXML 原地写回该单元格 ----
        try
        {
            using (var workbook = new XLWorkbook(xlsxPath))
            {
                var ws = workbook.Worksheet(1);
                ws.Cell(excelRow, commandColIndex + 1).Value = command;
                workbook.Save();
            }
            Debug.Log($"[CmdSync] 已镜像写回行 {rowId} 的 Command 到 {Path.GetFileName(xlsxPath)}（Excel 侧视图已同步）");
        }
        catch (IOException)
        {
            Debug.LogWarning($"[CmdSync] 镜像写回失败（{Path.GetFileName(xlsxPath)} 可能被 Excel 占用），下次转换时自动重试。CSV 侧不受影响。");
            return;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[CmdSync] 镜像写回异常（{Path.GetFileName(xlsxPath)}）：{e.Message}。CSV 侧不受影响。");
            return;
        }

        // ---- 3. 同步更新三方合并基准（sidecar）——否则下次转换会误报「两边都改」冲突 ----
        string sidecarPath = GetSidecarPath(csvPath);
        var snapshot = LoadCommandSnapshot(sidecarPath);
        snapshot[rowId] = command ?? "";
        SaveCommandSnapshot(sidecarPath, snapshot);

        // ---- 4. 防死循环：写回更新了 xlsx 修改时间，立即刷新该文件记录 ----
        AutoExcelConverter.RefreshTimestampForFile(xlsxPath);
    }

    /// <summary>
    /// 由 CSV 路径反推同名 Excel 源文件（递归查找，优先 .xlsx）。
    /// 找不到或配置缺失返回 null。
    /// </summary>
    private static string LocateExcelFile(string csvPath)
    {
        if (!VNProjectConfig.TryGetInstance(out VNProjectConfig config)) return null;

        string excelFolder = config.GetExcelFolderPath();
        if (string.IsNullOrEmpty(excelFolder) || !Directory.Exists(excelFolder)) return null;

        string baseName = Path.GetFileNameWithoutExtension(csvPath);
        string fallback = null;
        foreach (string file in Directory.GetFiles(excelFolder, "*.*", SearchOption.AllDirectories))
        {
            string ext = Path.GetExtension(file).ToLower();
            if ((ext != ".xlsx" && ext != ".xls") || Path.GetFileNameWithoutExtension(file) != baseName)
                continue;

            if (ext == ".xlsx") return file; // .xlsx 优先
            fallback = file;
        }
        return fallback;
    }

    // ==================== sidecar 快照（三方合并基准） ====================

    private static string GetSidecarPath(string csvPath) => csvPath + ".cmdmap.json";

    [System.Serializable]
    private class CommandSnapshotData
    {
        public List<RowCommandEntry> rows = new List<RowCommandEntry>();
    }

    [System.Serializable]
    private class RowCommandEntry
    {
        public string id;
        public string cmd;
    }

    private static Dictionary<string, string> LoadCommandSnapshot(string sidecarPath)
    {
        var result = new Dictionary<string, string>();
        try
        {
            if (!File.Exists(sidecarPath)) return result;
            var data = JsonUtility.FromJson<CommandSnapshotData>(File.ReadAllText(sidecarPath, new UTF8Encoding(false)));
            if (data?.rows != null)
            {
                foreach (var entry in data.rows)
                {
                    if (!string.IsNullOrEmpty(entry?.id))
                        result[entry.id] = entry.cmd ?? "";
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[CmdSync] 快照读取失败（按无基准处理，本次采用 xlsx 值）: {sidecarPath} — {e.Message}");
        }
        return result;
    }

    private static void SaveCommandSnapshot(string sidecarPath, Dictionary<string, string> commands)
    {
        try
        {
            var data = new CommandSnapshotData();
            foreach (var kvp in commands)
                data.rows.Add(new RowCommandEntry { id = kvp.Key, cmd = kvp.Value });
            File.WriteAllText(sidecarPath, JsonUtility.ToJson(data), new UTF8Encoding(false));
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[CmdSync] 快照写入失败（下次转换将按无基准处理）: {sidecarPath} — {e.Message}");
        }
    }

    private static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Length <= max ? s : s.Substring(0, max) + "...";
    }

    // 辅助方法：CSV 转义规则
    private static string EscapeCsvCell(string data)
    {
        if (string.IsNullOrEmpty(data)) return "";
        // 如果包含逗号、双引号、换行符，需要加引号包裹
        if (data.Contains(",") || data.Contains("\"") || data.Contains("\r") || data.Contains("\n"))
        {
            // 将内部的双引号转义为两个双引号
            data = data.Replace("\"", "\"\"");
            return "\"" + data + "\"";
        }
        return data;
    }
}
