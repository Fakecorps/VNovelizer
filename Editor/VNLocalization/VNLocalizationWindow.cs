using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

/// <summary>
/// 【本地化重构 P1】剧情本地化管理器。
///
/// <para>工作流（Docs/VNLocalizationRefactoring.md）：</para>
/// <list type="bullet">
/// <item>Excel 转换 CSV 时自动生成/更新翻译表模板（{script}_{locale}.csv）；</item>
/// <item>翻译团队直接编辑模板文件（ID, Text_Origin, 译文列）；</item>
/// <item>保存即自动聚合为单一 StringTableCollection（本窗口可手动导入兜底）；</item>
/// <item>本窗口用于：查看聚合结果/冲突、配置翻译表目录、按剧本补生成模板、定位翻译表。</item>
/// </list>
///
/// <para>【UI 重构】剧本列表改为卡片式：每个剧本一张卡，卡内每个目标语言一个状态 chip
/// （绿=已有翻译表/点击定位、橙=缺失/点击生成、黄=缺 Locale 资产），状态与操作一目了然。
/// 移除原「生成语言」勾选层——目标语言唯一来源是 Project Settings → VNovelizer 的
/// 语言配置，模板生成幂等安全（译文列永不动），无需二次选择范围。</para>
/// </summary>
public class VNLocalizationWindow : EditorWindow
{
    private Vector2 _scriptScroll;
    private Vector2 _resultScroll;
    private List<string> _scriptNames = new List<string>();

    // 剧本/翻译表搜索过滤（空 = 全部显示）
    private string _search = "";

    // Unity Localization 已创建的 Locale 代码缓存（每帧 OnGUI 开头刷新一次）
    private HashSet<string> _projectLocaleCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    // 搜索框组件：不能在字段初始化器里 new（会在窗口构造时抛 GetPermanentControlID 异常）
    private SearchField _searchField;

    // chip 配色（选中/已有态绿、待办态橙、警告态黄）
    private static readonly Color ChipExisting = new Color(0.36f, 0.72f, 0.42f);
    private static readonly Color ChipMissing = new Color(0.98f, 0.62f, 0.25f);
    private static readonly Color ChipWarn = new Color(0.95f, 0.85f, 0.30f);
    private static readonly Color BadgeComplete = new Color(0.36f, 0.72f, 0.42f);
    private static readonly Color BadgePartial = new Color(0.98f, 0.72f, 0.25f);
    private static readonly Color BadgeEmpty = new Color(0.90f, 0.42f, 0.35f);

    // 完整度徽章样式：miniButton 派生（有背景，GUI.backgroundColor 着色才生效）。
    // 惰性初始化——静态字段初始化器里 new GUIStyle 会在类构造期访问未就绪的 EditorStyles（NRE）
    private static GUIStyle _badgeStyle;
    private static GUIStyle BadgeStyle => _badgeStyle ?? (_badgeStyle = new GUIStyle(EditorStyles.miniButton)
        { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter });

    private void OnEnable()
    {
        _searchField = new SearchField();
    }

    [MenuItem("VNovelizer/Localization/剧情本地化管理器")]
    public static void ShowWindow()
    {
        var wnd = GetWindow<VNLocalizationWindow>("剧情本地化管理器");
        wnd.minSize = new Vector2(660, 420);
    }

    private void OnGUI()
    {
        var config = VNProjectConfig.Instance;
        if (config == null)
        {
            EditorGUILayout.HelpBox("未找到 VNProjectConfig。请先打开 Edit → Project Settings → VNovelizer。", MessageType.Warning);
            return;
        }

        if (!config.EnableLocalization)
        {
            EditorGUILayout.LabelField("VNovelizer 剧情本地化管理器", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("本地化未启用。请在 Edit → Project Settings → VNovelizer 勾选「启用剧情本地化」。", MessageType.Warning);
            return;
        }

        RefreshProjectLocaleCodes();

        DrawHeader(config);
        EditorGUILayout.Space(4);
        DrawPathField(config);
        EditorGUILayout.Space(4);
        DrawToolbar(config);
        EditorGUILayout.Space(4);
        DrawAggregateResult();
        EditorGUILayout.Space(6);
        DrawScriptList(config);
    }

    /// <summary>刷新 Unity Localization 已创建 Locale 代码缓存（用于 ⚠ 缺 Locale 诊断）。</summary>
    private void RefreshProjectLocaleCodes()
    {
        _projectLocaleCodes.Clear();
        try
        {
            var locales = UnityEditor.Localization.LocalizationEditorSettings.GetLocales();
            if (locales == null)
                return;
            foreach (var l in locales)
            {
                if (l == null || string.IsNullOrEmpty(l.Identifier.Code))
                    continue;
                _projectLocaleCodes.Add(l.Identifier.Code);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[VNLocalizationWindow] 读取 Unity Localization Locale 列表失败：{e.Message}");
        }
    }

    /// <summary>配置语言是否已创建 Locale 资产（下划线/连字符归一化 + 忽略大小写，与聚合器规则一致）。</summary>
    private bool HasLocaleAsset(string localeCode)
    {
        string normalized = (localeCode ?? "").Replace('_', '-');
        foreach (var code in _projectLocaleCodes)
        {
            if (string.Equals(code.Replace('_', '-'), normalized, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private void DrawHeader(VNProjectConfig config)
    {
        EditorGUILayout.LabelField("VNovelizer 剧情本地化管理器", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            $"源语言：{config.SourceLocale}（原文）    目标语言：{(string.IsNullOrEmpty(config.TargetLocales) ? "（未配置，去 Project Settings → VNovelizer → 本地化 勾选）" : config.TargetLocales)}",
            EditorStyles.miniLabel);
    }

    /// <summary>翻译表目录编辑行：可直接输入 Assets 相对路径，或弹系统文件夹选择框。</summary>
    private void DrawPathField(VNProjectConfig config)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("翻译表目录", GUILayout.Width(66));

            string newPath = EditorGUILayout.DelayedTextField(config.TranslationFolder ?? "");
            if (newPath != (config.TranslationFolder ?? ""))
                ApplyTranslationFolder(config, newPath);

            if (GUILayout.Button("选择…", GUILayout.Width(46)))
            {
                string picked = EditorUtility.OpenFolderPanel(
                    "选择翻译表目录", Path.GetFullPath(config.GetTranslationFolderPath()), "");
                if (!string.IsNullOrEmpty(picked))
                    ApplyTranslationFolder(config, ToAssetsRelativePath(picked));
            }
        }
    }

    /// <summary>应用新的翻译表目录（校验为 Assets 相对路径并保存配置资产）。</summary>
    private void ApplyTranslationFolder(VNProjectConfig config, string newPath)
    {
        string normalized = (newPath ?? "").Trim().Replace('\\', '/').TrimEnd('/');
        if (string.IsNullOrEmpty(normalized))
            return;

        if (!normalized.Equals("Assets", StringComparison.OrdinalIgnoreCase) &&
            !normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
        {
            EditorUtility.DisplayDialog("无效路径",
                "翻译表目录必须是工程内 Assets 下的相对路径（如 Assets/Localization/Translations）。", "确定");
            return;
        }

        if (normalized == config.TranslationFolder)
            return;

        config.TranslationFolder = normalized;
        SaveConfigAsset(config);
    }

    /// <summary>绝对路径 → "Assets/..." 相对路径；不在工程 Assets 下返回 null。</summary>
    private static string ToAssetsRelativePath(string absolutePath)
    {
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string full = Path.GetFullPath(absolutePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (!full.StartsWith(projectRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            !full.StartsWith(projectRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return null;

        string rel = full.Substring(projectRoot.Length + 1).Replace('\\', '/');
        return rel.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ? rel : null;
    }

    /// <summary>持久化配置资产（临时实例无法保存时提示先创建配置）。</summary>
    private static void SaveConfigAsset(VNProjectConfig config)
    {
        if (AssetDatabase.Contains(config))
        {
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssetIfDirty(config);
        }
        else
        {
            Debug.LogWarning("[VNLocalizationWindow] 当前使用内置临时配置（尚未创建 VNProjectConfig 资产），目录修改无法保存。" +
                             "请先打开 Edit → Project Settings → VNovelizer 创建配置。");
        }
    }

    // ==================== 概览与工具栏 ====================

    /// <summary>全局概览统计。</summary>
    private (int scripts, int total, int existing, int localeMissing) ComputeOverview(VNProjectConfig config)
    {
        var targets = config.GetTargetLocales();
        int existing = 0, localeMissing = 0;
        string folder = Path.GetFullPath(config.GetTranslationFolderPath());

        foreach (var scriptName in _scriptNames)
        {
            foreach (var t in targets)
            {
                if (File.Exists(Path.Combine(folder, $"{scriptName}_{t}.csv")))
                    existing++;
                else if (!HasLocaleAsset(t))
                    localeMissing++;
            }
        }
        return (_scriptNames.Count, _scriptNames.Count * targets.Length, existing, localeMissing);
    }

    private void DrawToolbar(VNProjectConfig config)
    {
        // 概览条：剧本数 + 翻译表完整度（着色）+ 缺 Locale 警告
        var (scripts, total, existing, localeMissing) = ComputeOverview(config);
        using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField($"{scripts} 个剧本", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("翻译表", GUILayout.Width(40));

            float ratio = total > 0 ? (float)existing / total : 1f;
            var prev = GUI.backgroundColor;
            GUI.backgroundColor = ratio >= 1f ? BadgeComplete : (existing == 0 ? BadgeEmpty : BadgePartial);
            GUILayout.Label($"  {existing} / {total}  ", BadgeStyle, GUILayout.Height(16));
            GUI.backgroundColor = prev;

            if (localeMissing > 0)
                EditorGUILayout.LabelField($"⚠ {localeMissing} 张缺失对应语言尚未创建 Locale 资产", EditorStyles.miniLabel);

            GUILayout.FlexibleSpace();
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("立即导入翻译表", GUILayout.Height(30)))
            {
                VNTranslationImportPostprocessor.ImportNow();
                Repaint();
            }

            if (GUILayout.Button(new GUIContent("补全全部缺失模板",
                    "为所有剧本生成尚不存在的目标语言翻译表模板（幂等安全：已有模板与译文不受影响）"),
                    GUILayout.Height(30)))
            {
                GenerateAllMissing(config);
            }

            if (GUILayout.Button("打开翻译表目录", GUILayout.Height(30)))
            {
                string folder = Path.GetFullPath(config.GetTranslationFolderPath());
                Directory.CreateDirectory(folder);
                EditorUtility.RevealInFinder(folder);
            }
        }
    }

    // ==================== 聚合结果 ====================

    private void DrawAggregateResult()
    {
        var result = VNTranslationAggregator.LastResult;
        if (result == null)
        {
            EditorGUILayout.HelpBox("尚未执行过导入。保存翻译表后自动导入，或点击「立即导入翻译表」。", MessageType.None);
            return;
        }

        if (!result.Success)
        {
            EditorGUILayout.HelpBox("最近导入失败。", MessageType.Error);
        }
        else
        {
            var counts = new List<string>();
            foreach (var kv in result.EntryCounts)
                counts.Add($"{kv.Key}: {kv.Value} 条");
            EditorGUILayout.HelpBox(
                "最近导入成功。" + (counts.Count > 0 ? "\n写入：" + string.Join("，", counts) : ""),
                MessageType.Info);
        }

        if (result.Conflicts.Count > 0)
        {
            EditorGUILayout.LabelField($"⚠ key 冲突 {result.Conflicts.Count} 处（后加载的表覆盖先加载的）", EditorStyles.boldLabel);
            _resultScroll = EditorGUILayout.BeginScrollView(_resultScroll, GUILayout.Height(90));
            foreach (var c in result.Conflicts)
            {
                EditorGUILayout.HelpBox(
                    $"[{c.Locale}] {c.Key}\n  {Path.GetFileName(c.FileA)} ↔ {Path.GetFileName(c.FileB)}",
                    MessageType.Warning);
            }
            EditorGUILayout.EndScrollView();
        }

        if (result.OrphanSkipped.Count > 0)
        {
            EditorGUILayout.LabelField($"已跳过 {result.OrphanSkipped.Count} 个失效 key（未导入）", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "这些翻译行对应的剧本行已删除/改动（如行 ID 或命令变更），译文仍保留在翻译 CSV 中" +
                "（模板末尾），请人工把译文迁移到新 key 后删除旧行。",
                MessageType.Info);
            _resultScroll = EditorGUILayout.BeginScrollView(_resultScroll, GUILayout.Height(70));
            foreach (var s in result.OrphanSkipped)
                EditorGUILayout.LabelField("· " + s, EditorStyles.miniLabel);
            EditorGUILayout.EndScrollView();
        }

        if (result.Warnings.Count > 0)
        {
            EditorGUILayout.LabelField("警告", EditorStyles.boldLabel);
            _resultScroll = EditorGUILayout.BeginScrollView(_resultScroll, GUILayout.Height(70));
            foreach (var w in result.Warnings)
                EditorGUILayout.LabelField("· " + w, EditorStyles.wordWrappedLabel);
            EditorGUILayout.EndScrollView();
        }
    }

    // ==================== 剧本卡片列表 ====================

    private void DrawScriptList(VNProjectConfig config)
    {
        RefreshScriptNames(config);

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("剧本与翻译表", EditorStyles.boldLabel, GUILayout.Width(90));
            _search = _searchField.OnGUI(_search ?? "", GUILayout.ExpandWidth(true));
        }

        if (_scriptNames.Count == 0)
        {
            EditorGUILayout.HelpBox("CSV 输出目录下暂无剧本。请先在剧本管理器创建并转换。", MessageType.None);
            return;
        }

        var shown = FilterScripts(config);
        if (shown.Count == 0)
        {
            EditorGUILayout.HelpBox($"没有匹配「{_search}」的剧本或翻译表。", MessageType.None);
            return;
        }

        EditorGUILayout.LabelField($"显示 {shown.Count} / {_scriptNames.Count} 个剧本", EditorStyles.miniLabel);

        _scriptScroll = EditorGUILayout.BeginScrollView(_scriptScroll);
        foreach (var scriptName in shown)
            DrawScriptCard(config, scriptName);
        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space(2);
        EditorGUILayout.HelpBox(
            "✓ 绿色 = 已有翻译表（点击定位文件）      ＋ 橙色 = 缺失（点击立即生成）      ⚠ 黄色 = 缺少 Locale 资产（可先生成模板填翻译，导入前需在 Project Settings → Localization 创建同名 Locale）",
            MessageType.None);
    }

    /// <summary>单剧本卡片：头行（剧本名 + 完整度徽章 + 批量操作）+ 语言 chip 行（状态即操作）。</summary>
    private void DrawScriptCard(VNProjectConfig config, string scriptName)
    {
        var targets = config.GetTargetLocales();
        string folder = Path.GetFullPath(config.GetTranslationFolderPath());
        string csvPath = Path.Combine(Path.GetFullPath(config.GetCsvOutputPath()), scriptName + ".csv");

        int existing = 0;
        var missing = new List<string>();
        foreach (var t in targets)
        {
            if (File.Exists(Path.Combine(folder, $"{scriptName}_{t}.csv")))
                existing++;
            else
                missing.Add(t);
        }

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            // ---- 头行 ----
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(scriptName, EditorStyles.boldLabel, GUILayout.MinWidth(140));

                float ratio = targets.Length > 0 ? (float)existing / targets.Length : 1f;
                var prev = GUI.backgroundColor;
                GUI.backgroundColor = ratio >= 1f ? BadgeComplete : (existing == 0 ? BadgeEmpty : BadgePartial);
                GUILayout.Label($" {existing}/{targets.Length} 张翻译表 ", BadgeStyle, GUILayout.Height(16));
                GUI.backgroundColor = prev;

                GUILayout.FlexibleSpace();

                using (new EditorGUI.DisabledScope(missing.Count == 0))
                {
                    if (GUILayout.Button(new GUIContent("生成缺失",
                            missing.Count > 0 ? $"为该剧本生成缺失的 {missing.Count} 张模板（{string.Join(", ", missing)}）" : "无缺失模板"),
                            GUILayout.Width(76)))
                    {
                        GenerateMissingForScript(config, scriptName, missing);
                    }
                }

                if (GUILayout.Button("定位 CSV", GUILayout.Width(66)))
                {
                    if (File.Exists(csvPath))
                        EditorUtility.RevealInFinder(csvPath);
                    else
                        EditorUtility.DisplayDialog("提示", $"剧本 CSV 不存在：{csvPath}\n请先在剧本管理器点击「转换」。", "确定");
                }
            }

            // ---- 语言 chip 行 ----
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(14);
                foreach (var t in targets)
                    DrawLocaleChip(config, scriptName, t, folder);
                GUILayout.FlexibleSpace();
            }
        }
    }

    /// <summary>
    /// 单语言 chip：状态即操作——
    /// 绿「✓ locale」已有翻译表（点击定位文件）；橙「＋ locale」缺失（点击生成）；
    /// 黄「⚠ locale」缺失且该语言未创建 Locale 资产（仍可生成模板，导入前需补 Locale）。
    /// </summary>
    private void DrawLocaleChip(VNProjectConfig config, string scriptName, string locale, string folder)
    {
        string path = Path.Combine(folder, $"{scriptName}_{locale}.csv");
        bool has = File.Exists(path);
        bool noLocaleAsset = !HasLocaleAsset(locale);

        string chipText = has ? $"✓ {locale}" : (noLocaleAsset ? $"⚠ {locale}" : $"＋ {locale}");
        string tooltip = has
            ? $"已有翻译表：\n{path}\n\n点击定位文件"
            : noLocaleAsset
                ? $"缺失翻译表：\n{path}\n\n点击生成模板。\n⚠ 该语言尚未创建 Locale 资产——填好译文后，需到 Project Settings → Localization 创建同名 Locale 才能导入聚合。"
                : $"缺失翻译表：\n{path}\n\n点击生成模板（新增行自动补入，已有译文永不动）";

        var prev = GUI.backgroundColor;
        GUI.backgroundColor = has ? ChipExisting : (noLocaleAsset ? ChipWarn : ChipMissing);
        if (GUILayout.Button(new GUIContent(chipText, tooltip), EditorStyles.miniButton, GUILayout.MinWidth(64)))
        {
            if (has)
            {
                EditorUtility.RevealInFinder(path);
            }
            else
            {
                string generated = GenerateSingle(config, scriptName, locale);
                if (!string.IsNullOrEmpty(generated))
                {
                    VNTranslationAggregator.Aggregate();
                    Repaint();
                }
            }
        }
        GUI.backgroundColor = prev;
    }

    // ==================== 模板生成 ====================

    /// <summary>为单剧本生成单语言模板；剧本 CSV 不存在时提示。返回生成路径（失败 null）。</summary>
    private static string GenerateSingle(VNProjectConfig config, string scriptName, string locale)
    {
        string csvPath = Path.Combine(Path.GetFullPath(config.GetCsvOutputPath()), scriptName + ".csv");
        if (!File.Exists(csvPath))
        {
            EditorUtility.DisplayDialog("提示", $"剧本 CSV 不存在：{csvPath}\n请先在剧本管理器点击「转换」。", "确定");
            return null;
        }

        try
        {
            string csvText = File.ReadAllText(csvPath);
            string path = VNTranslationTemplateGenerator.GenerateTemplate(scriptName, locale, csvText);
            if (string.IsNullOrEmpty(path))
                EditorUtility.DisplayDialog("未生成", $"剧本 {scriptName} 无可本地化条目，跳过模板生成。", "确定");
            return path;
        }
        catch (Exception e)
        {
            EditorUtility.DisplayDialog("失败", $"模板生成失败：{e.Message}", "确定");
            return null;
        }
    }

    /// <summary>为单剧本生成全部缺失模板并聚合。</summary>
    private void GenerateMissingForScript(VNProjectConfig config, string scriptName, List<string> missing)
    {
        int count = 0;
        foreach (var locale in missing)
        {
            if (!string.IsNullOrEmpty(GenerateSingle(config, scriptName, locale)))
                count++;
        }

        if (count > 0)
            VNTranslationAggregator.Aggregate();

        Repaint();
    }

    /// <summary>为全部剧本补全全部缺失模板（只生成不存在的文件，幂等安全）并聚合。</summary>
    private void GenerateAllMissing(VNProjectConfig config)
    {
        RefreshScriptNames(config);
        var targets = config.GetTargetLocales();
        if (targets.Length == 0)
        {
            EditorUtility.DisplayDialog("提示",
                "未配置目标语言。请到 Edit → Project Settings → VNovelizer → 本地化 勾选目标语言。", "确定");
            return;
        }

        string folder = Path.GetFullPath(config.GetTranslationFolderPath());
        string csvFolder = Path.GetFullPath(config.GetCsvOutputPath());

        int count = 0;
        try
        {
            for (int i = 0; i < _scriptNames.Count; i++)
            {
                string scriptName = _scriptNames[i];
                string csvPath = Path.Combine(csvFolder, scriptName + ".csv");
                if (!File.Exists(csvPath))
                    continue;

                string csvText = File.ReadAllText(csvPath);
                foreach (var locale in targets)
                {
                    if (File.Exists(Path.Combine(folder, $"{scriptName}_{locale}.csv")))
                        continue; // 只补缺失

                    if (EditorUtility.DisplayCancelableProgressBar(
                            "生成翻译表模板", $"{scriptName} → {locale}", (float)i / Math.Max(1, _scriptNames.Count)))
                        break; // 用户取消：跳出当前剧本的语言循环

                    string path = VNTranslationTemplateGenerator.GenerateTemplate(scriptName, locale, csvText);
                    if (!string.IsNullOrEmpty(path))
                        count++;
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        VNTranslationAggregator.Aggregate();
        Repaint();

        EditorUtility.DisplayDialog("完成", $"已补全 {count} 张缺失模板，并已执行导入。", "确定");
    }

    // ==================== 剧本枚举/过滤 ====================

    /// <summary>按搜索词过滤剧本：匹配剧本名，或已有翻译表文件名（scriptName_locale.csv）。</summary>
    private List<string> FilterScripts(VNProjectConfig config)
    {
        string search = (_search ?? "").Trim();
        if (search.Length == 0)
            return new List<string>(_scriptNames);

        string folder = Path.GetFullPath(config.GetTranslationFolderPath());
        var result = new List<string>();
        foreach (var scriptName in _scriptNames)
        {
            if (scriptName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                result.Add(scriptName);
                continue;
            }

            foreach (var locale in config.GetTargetLocales())
            {
                string fileName = $"{scriptName}_{locale}.csv";
                if (fileName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 &&
                    File.Exists(Path.Combine(folder, fileName)))
                {
                    result.Add(scriptName);
                    break;
                }
            }
        }
        return result;
    }

    private void RefreshScriptNames(VNProjectConfig config)
    {
        _scriptNames.Clear();
        string csvFolder = config.GetCsvOutputPath();
        if (string.IsNullOrEmpty(csvFolder))
            return;

        string full = Path.GetFullPath(csvFolder);
        if (!Directory.Exists(full))
            return;

        foreach (var f in Directory.GetFiles(full, "*.csv"))
            _scriptNames.Add(Path.GetFileNameWithoutExtension(f));
        _scriptNames.Sort(StringComparer.OrdinalIgnoreCase);
    }
}
