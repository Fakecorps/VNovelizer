using UnityEngine;
using UnityEditor;
using System.IO;
using System.Text;
using ExcelDataReader;

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
        Debug.Log($"<color=green>转换完成！新建: {createCount}, 更新: {updateCount}</color>");
    }

    /// <summary>
    /// 转换单个文件
    /// </summary>
    /// <param name="filePath">Excel源文件绝对路径</param>
    /// <param name="targetFolder">CSV输出文件夹绝对路径</param>
    /// <returns>是否覆盖了旧文件</returns>
    private static bool ConvertFile(string filePath, string targetFolder)
    {
        using (var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            using (var reader = ExcelReaderFactory.CreateReader(stream))
            {
                StringBuilder csvContent = new StringBuilder();
                int maxColumnCount = 0;
                bool isFirstRow = true;
                System.Collections.Generic.List<string[]> allRows = new System.Collections.Generic.List<string[]>();

                // 读取所有行
                while (reader.Read())
                {
                    int fieldCount = reader.FieldCount;
                    string[] row = new string[fieldCount];

                    // 读取当前行的所有列
                    for (int j = 0; j < fieldCount; j++)
                    {
                        object cellValue = reader.GetValue(j);
                        row[j] = cellValue != null ? cellValue.ToString() : "";
                    }

                    allRows.Add(row);

                    // 第一行用于确定最大列数
                    if (isFirstRow)
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
                        isFirstRow = false;
                    }
                }

                if (allRows.Count == 0)
                {
                    Debug.LogWarning($"文件 {Path.GetFileName(filePath)} 没有数据");
                    return false;
                }

                // 将所有行转换为 CSV 格式
                foreach (string[] row in allRows)
                {
                    // 确保每一行的数据列数与表头对齐
                    int currentLoopLimit = maxColumnCount;

                    for (int j = 0; j < currentLoopLimit; j++)
                    {
                        string cellValueStr = "";

                        // 获取单元格值
                        if (j < row.Length)
                        {
                            cellValueStr = row[j] ?? "";
                        }

                        // 转义 CSV 特殊字符
                        csvContent.Append(EscapeCsvCell(cellValueStr));

                        // 添加逗号 (最后一列除外)
                        if (j < currentLoopLimit - 1)
                            csvContent.Append(",");
                    }
                    csvContent.AppendLine();
                }

                // 使用传入的 targetFolder 拼接路径
                string fileName = Path.GetFileNameWithoutExtension(filePath);
                string finalPath = Path.Combine(targetFolder, fileName + ".csv");

                bool fileExists = File.Exists(finalPath);

                // 使用 UTF-8 (无BOM) 编码写入
                File.WriteAllText(finalPath, csvContent.ToString(), new UTF8Encoding(false));

                return fileExists;
            }
        }
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
