using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class VNLocalizationSyncUtility
{
    private const string DefaultCollectionAssetDir = "Assets/Localization/VNovelizerStringTables";

    public static string GetCollectionNameForScript(string scriptName)
    {
        string prefix = "VNScript_";
        if (VNProjectConfig.Instance != null && !string.IsNullOrEmpty(VNProjectConfig.Instance.ScriptTablePrefix))
        {
            prefix = VNProjectConfig.Instance.ScriptTablePrefix;
        }
        return prefix + scriptName;
    }

    public static bool EnsureScriptCollection(string scriptName, out UnityEngine.Object collectionObj, out string error)
    {
        collectionObj = null;
        error = null;

#if VN_LOCALIZATION
        if (string.IsNullOrEmpty(scriptName))
        {
            error = "scriptName 不能为空。";
            return false;
        }
        string collectionName = GetCollectionNameForScript(scriptName);

        // 查找已有剧本 collection
        var collections = UnityEditor.Localization.LocalizationEditorSettings.GetStringTableCollections();
        if (collections != null)
        {
            var existing = collections.FirstOrDefault(c => c.TableCollectionName == collectionName);
            if (existing != null)
            {
                collectionObj = existing;
                return true;
            }
        }

        // 创建剧本 collection（使用当前项目已配置的 Locales）
        try
        {
            var created = UnityEditor.Localization.LocalizationEditorSettings.CreateStringTableCollection(collectionName, DefaultCollectionAssetDir);
            collectionObj = created;
            return created != null;
        }
        catch (Exception e)
        {
            error = $"创建 StringTableCollection 失败：{e.Message}";
            return false;
        }
#else
        error = "当前未启用 VN_LOCALIZATION（请先在项目安装 Unity Localization 包）。";
        return false;
#endif
    }

    public static List<string> GetAllScriptCollectionNames()
    {
        List<string> names = new List<string>();
#if VN_LOCALIZATION
        string prefix = "VNScript_";
        if (VNProjectConfig.Instance != null && !string.IsNullOrEmpty(VNProjectConfig.Instance.ScriptTablePrefix))
        {
            prefix = VNProjectConfig.Instance.ScriptTablePrefix;
        }

        var collections = UnityEditor.Localization.LocalizationEditorSettings.GetStringTableCollections();
        if (collections != null)
        {
            names = collections
                .Where(c => c != null && !string.IsNullOrEmpty(c.TableCollectionName) && c.TableCollectionName.StartsWith(prefix, StringComparison.Ordinal))
                .Select(c => c.TableCollectionName)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
#endif
        return names;
    }

    public static string ExtractScriptNameFromCollection(string collectionName)
    {
        if (string.IsNullOrEmpty(collectionName))
            return "";

        string prefix = "VNScript_";
        if (VNProjectConfig.Instance != null && !string.IsNullOrEmpty(VNProjectConfig.Instance.ScriptTablePrefix))
        {
            prefix = VNProjectConfig.Instance.ScriptTablePrefix;
        }

        return collectionName.StartsWith(prefix, StringComparison.Ordinal)
            ? collectionName.Substring(prefix.Length)
            : collectionName;
    }

    public static bool DeleteScriptCollection(string scriptName, out string error)
    {
        error = null;
        if (string.IsNullOrEmpty(scriptName))
        {
            error = "scriptName 不能为空。";
            return false;
        }
        string collectionName = GetCollectionNameForScript(scriptName);
        return DeleteCollectionByName(collectionName, out error);
    }

    public static bool DeleteCollectionByName(string collectionName, out string error)
    {
        error = null;
#if VN_LOCALIZATION
        if (string.IsNullOrEmpty(collectionName))
        {
            error = "collectionName 不能为空。";
            return false;
        }

        var collections = UnityEditor.Localization.LocalizationEditorSettings.GetStringTableCollections();
        if (collections == null)
        {
            error = "找不到本地化 Collection 列表。";
            return false;
        }

        var collection = collections.FirstOrDefault(c => c != null && c.TableCollectionName == collectionName);
        if (collection == null)
        {
            error = $"未找到 Collection: {collectionName}";
            return false;
        }

        HashSet<string> pathsToDelete = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1) Collection 资产自身
        string collectionPath = AssetDatabase.GetAssetPath(collection);
        if (!string.IsNullOrEmpty(collectionPath))
            pathsToDelete.Add(collectionPath);

        // 2) SharedData（反射读取，避免版本差异）
        try
        {
            var sharedDataProp = collection.GetType().GetProperty("SharedData");
            if (sharedDataProp != null)
            {
                var sharedData = sharedDataProp.GetValue(collection) as UnityEngine.Object;
                if (sharedData != null)
                {
                    string sharedPath = AssetDatabase.GetAssetPath(sharedData);
                    if (!string.IsNullOrEmpty(sharedPath))
                        pathsToDelete.Add(sharedPath);
                }
            }
        }
        catch { }

        // 3) StringTables（反射读取，避免版本差异）
        try
        {
            var tablesProp = collection.GetType().GetProperty("StringTables");
            if (tablesProp != null)
            {
                var enumerable = tablesProp.GetValue(collection) as System.Collections.IEnumerable;
                if (enumerable != null)
                {
                    foreach (var t in enumerable)
                    {
                        var obj = t as UnityEngine.Object;
                        if (obj == null) continue;
                        string p = AssetDatabase.GetAssetPath(obj);
                        if (!string.IsNullOrEmpty(p))
                            pathsToDelete.Add(p);
                    }
                }
            }
        }
        catch { }

        bool deletedAny = false;
        foreach (var path in pathsToDelete)
        {
            if (AssetDatabase.DeleteAsset(path))
                deletedAny = true;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (!deletedAny)
        {
            error = $"删除失败：{collectionName}（未删除任何资产）。";
            return false;
        }

        return true;
#else
        error = "当前未启用 VN_LOCALIZATION（请先在项目安装 Unity Localization 包）。";
        return false;
#endif
    }

    public static bool TryValidateScriptCsvIds(string scriptName, out string error)
    {
        error = null;

        var config = VNProjectConfig.Instance;
        if (config == null)
        {
            error = "未找到 VNProjectConfig。";
            return false;
        }

        string csvFolderPath = config.GetCsvOutputPath();
        if (string.IsNullOrEmpty(csvFolderPath))
        {
            error = "VNProjectConfig 的 CsvOutputFolder 未配置。";
            return false;
        }

        string csvPath = Path.Combine(csvFolderPath, scriptName + ".csv");
        if (!File.Exists(csvPath))
        {
            error = $"CSV 不存在：{csvPath}";
            return false;
        }

        var text = File.ReadAllText(csvPath);
        var lines = SplitCsvLines(text);
        if (lines.Length <= 1)
        {
            error = "CSV 行数不足，无法校验。";
            return false;
        }

        // 表头跳过：第 0 行
        var header = SplitCsvLine(lines[0]);
        int idCol = FindColumnIndex(header, new[] { "ID", "编号", "行号" });
        if (idCol < 0) idCol = 0;

        HashSet<string> seen = new HashSet<string>();
        List<string> duplicates = new List<string>();

        for (int i = 1; i < lines.Length; i++)
        {
            var cols = SplitCsvLine(lines[i]);
            if (cols.Length <= idCol) continue;

            string id = (cols[idCol] ?? "").Trim();
            if (string.IsNullOrEmpty(id)) continue;

            // 避免把表头当作数据（少数情况下表头可能出现在非首行）
            if (id.Equals("ID", StringComparison.OrdinalIgnoreCase) ||
                id.Contains("编号", StringComparison.OrdinalIgnoreCase) ||
                id.Contains("行号", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!seen.Add(id))
                duplicates.Add(id);
        }

        if (duplicates.Count > 0)
        {
            error = $"检测到重复 ID：{string.Join(",", duplicates.Distinct().Take(20))}" + (duplicates.Count > 20 ? "..." : "");
            return false;
        }

        return true;
    }

    public static bool TrySyncKeysFromCsv(string scriptName, bool fillDefaultLocaleFromCsv, out string error)
    {
        error = null;

        var config = VNProjectConfig.Instance;
        if (config == null)
        {
            error = "未找到 VNProjectConfig。";
            return false;
        }

        // 1) 确保当前剧本 collection 存在
        if (!EnsureScriptCollection(scriptName, out _, out var ensureError))
        {
            error = ensureError;
            return false;
        }

        // 2) 找 CSV
        string csvFolderPath = config.GetCsvOutputPath();
        if (string.IsNullOrEmpty(csvFolderPath))
        {
            error = "VNProjectConfig 的 CsvOutputFolder 未配置。";
            return false;
        }

        string csvPath = Path.Combine(csvFolderPath, scriptName + ".csv");
        if (!File.Exists(csvPath))
        {
            error = $"CSV 不存在：{csvPath}（如果刚新建剧本，可能还没点“转换”，属于正常情况）";
            return false;
        }

        var csvFileText = File.ReadAllText(csvPath);
        var lines = SplitCsvLines(csvFileText);
        if (lines.Length <= 1)
        {
            error = "CSV 行数不足，无法同步。";
            return false;
        }

        // 表头：第 0 行
        var header = SplitCsvLine(lines[0]);
        int idCol = FindColumnIndex(header, new[] { "ID", "编号", "行号" });
        int speakerCol = FindColumnIndex(header, new[] { "Speaker", "说话人" });
        int textCol = FindColumnIndex(header, new[] { "Text", "文本", "台词" });

        // 找不到就用 ScriptParser 的默认列序（保底兼容）
        if (idCol < 0) idCol = 0;
        if (speakerCol < 0) speakerCol = 1;
        if (textCol < 0) textCol = 6;

#if VN_LOCALIZATION
        // 3) 写入默认 locale（使用当前 SelectedLocale）
        // Editor 下 SelectedLocale 可能为 null（初始化未完成或尚未成功选中）
        var initOp = UnityEngine.Localization.Settings.LocalizationSettings.InitializationOperation;
        if (initOp.IsValid() && !initOp.IsDone)
        {
            initOp.WaitForCompletion();
        }

        var locale = UnityEngine.Localization.Settings.LocalizationSettings.SelectedLocale;
        if (locale == null)
        {
            // 1) 优先 Project Locale
            locale = UnityEngine.Localization.Settings.LocalizationSettings.ProjectLocale;
        }
        if (locale == null)
        {
            // 2) 再尝试 AvailableLocales 的第一个
            var available = UnityEngine.Localization.Settings.LocalizationSettings.AvailableLocales;
            if (available != null && available.Locales != null)
            {
                locale = available.Locales.FirstOrDefault();
            }
        }
        if (locale == null)
        {
            // 3) 最后兜底：Editor 配置的 Locales
            var editorLocales = UnityEditor.Localization.LocalizationEditorSettings.GetLocales();
            if (editorLocales != null)
            {
                locale = editorLocales.FirstOrDefault();
            }
        }

        // 如果兜底选到了 locale，写回 SelectedLocale，便于后续工具与运行时一致
        if (locale != null && UnityEngine.Localization.Settings.LocalizationSettings.SelectedLocale == null)
        {
            // 注意：不同版本 LocalizationSettings.SetSelectedLocale 的静态/实例签名可能不同
            if (UnityEngine.Localization.Settings.LocalizationSettings.Instance != null)
            {
                UnityEngine.Localization.Settings.LocalizationSettings.Instance.SetSelectedLocale(locale);
            }
        }

        if (locale == null)
        {
            error = "无法确定当前 Locale（SelectedLocale/ProjectLocale/AvailableLocales 都为空）。请先在 Project Settings -> Localization 配置 Locales。";
            return false;
        }

        var collectionName = GetCollectionNameForScript(scriptName);
        var tables = UnityEditor.Localization.LocalizationEditorSettings.GetStringTableCollections();
        var collection = tables?.FirstOrDefault(c => c.TableCollectionName == collectionName);
        if (collection == null)
        {
            error = $"找不到 StringTableCollection：{collectionName}";
            return false;
        }

        var tableObj = collection.GetTable(locale.Identifier);
        var stringTable = tableObj as UnityEngine.Localization.Tables.StringTable;
        if (stringTable == null)
        {
            error = "默认 locale 的 StringTable 获取失败。";
            return false;
        }

        int syncedCount = 0;

        // 4) 跳过表头，从第 1 行开始
        for (int i = 1; i < lines.Length; i++)
        {
            var cols = SplitCsvLine(lines[i]);
            if (cols.Length <= Math.Max(idCol, Math.Max(speakerCol, textCol))) continue;

            string id = (cols[idCol] ?? "").Trim();
            if (string.IsNullOrEmpty(id)) continue;

            // 避免表头被当成数据
            if (id.Equals("ID", StringComparison.OrdinalIgnoreCase) ||
                id.Contains("编号", StringComparison.OrdinalIgnoreCase) ||
                id.Contains("行号", StringComparison.OrdinalIgnoreCase))
                continue;

            // Text：总是生成 key，并在默认语言 value 为空时填 CSV
            string csvTextValue = (cols[textCol] ?? "").Trim();
            string textKey = $"text.{id}";
            syncedCount += SyncEntryIfNeeded(stringTable, textKey, csvTextValue, fillDefaultLocaleFromCsv);

            // Speaker：Speaker 非空才生成/填充
            string csvSpeaker = (cols[speakerCol] ?? "").Trim();
            if (!string.IsNullOrEmpty(csvSpeaker))
            {
                string speakerKey = $"speaker.{id}";
                syncedCount += SyncEntryIfNeeded(stringTable, speakerKey, csvSpeaker, fillDefaultLocaleFromCsv);
            }
        }

        UnityEditor.EditorUtility.SetDirty(stringTable);
        UnityEditor.EditorUtility.SetDirty(stringTable.SharedData);
        UnityEditor.AssetDatabase.SaveAssets();

        Debug.Log($"[VNLocalizationSyncUtility] 同步完成：{scriptName}，处理条目数: {syncedCount}");
        return true;
#else
        error = "未启用 VN_LOCALIZATION，无法同步。";
        return false;
#endif
    }

    #if VN_LOCALIZATION
    private static int SyncEntryIfNeeded(UnityEngine.Localization.Tables.StringTable stringTable, string key, string csvValue, bool fillDefaultLocaleFromCsv)
    {
        if (stringTable == null) return 0;
        if (string.IsNullOrEmpty(key)) return 0;

        // 未填充场景：只保证 entry 存在
        var entry = stringTable.GetEntry(key);
        if (entry == null)
        {
            // AddEntry 会写入 SharedTableData，使 entry 在共享 collection 下可用
            // 即使 value 为空，也允许后续手动翻译
            stringTable.AddEntry(key, fillDefaultLocaleFromCsv ? csvValue : "");
            return 1;
        }

        if (fillDefaultLocaleFromCsv)
        {
            // 只在 entry.Value 为空时覆盖，避免覆盖已有翻译
            if (string.IsNullOrEmpty(entry.Value))
            {
                entry.Value = csvValue;
                return 1;
            }
        }

        return 0;
    }
    #else
    private static int SyncEntryIfNeeded(object stringTable, string key, string csvValue, bool fillDefaultLocaleFromCsv) => 0;
    #endif

    // -------------------- CSV Parser（Editor 侧简化版）--------------------

    private static string[] SplitCsvLines(string csvContent)
    {
        List<string> lines = new List<string>();
        bool inQuotes = false;
        System.Text.StringBuilder currentLine = new System.Text.StringBuilder();

        for (int i = 0; i < csvContent.Length; i++)
        {
            char c = csvContent[i];
            char nextChar = (i + 1 < csvContent.Length) ? csvContent[i + 1] : '\0';

            if (c == '"')
            {
                if (inQuotes && nextChar == '"')
                {
                    currentLine.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                    currentLine.Append(c);
                }
            }
            else if ((c == '\n' || c == '\r') && !inQuotes)
            {
                if (c == '\r' && nextChar == '\n')
                    i++;

                if (currentLine.Length > 0)
                {
                    lines.Add(currentLine.ToString());
                    currentLine.Clear();
                }
            }
            else
            {
                currentLine.Append(c);
            }
        }

        if (currentLine.Length > 0)
            lines.Add(currentLine.ToString());

        return lines.ToArray();
    }

    private static string[] SplitCsvLine(string line)
    {
        List<string> fields = new List<string>();
        bool inQuotes = false;
        System.Text.StringBuilder currentField = new System.Text.StringBuilder();

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            char nextChar = (i + 1 < line.Length) ? line[i + 1] : '\0';

            if (c == '"')
            {
                if (inQuotes && nextChar == '"')
                {
                    currentField.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                fields.Add(currentField.ToString());
                currentField.Clear();
            }
            else
            {
                currentField.Append(c);
            }
        }

        fields.Add(currentField.ToString());
        return fields.ToArray();
    }

    private static int FindColumnIndex(string[] headerCols, string[] matchSubStrings)
    {
        if (headerCols == null || headerCols.Length == 0) return -1;

        for (int i = 0; i < headerCols.Length; i++)
        {
            string h = (headerCols[i] ?? "").Trim();
            if (string.IsNullOrEmpty(h)) continue;

            foreach (var m in matchSubStrings)
            {
                if (h.IndexOf(m, StringComparison.OrdinalIgnoreCase) >= 0)
                    return i;
            }
        }

        return -1;
    }
}

