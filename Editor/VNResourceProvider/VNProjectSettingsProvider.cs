using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 项目设置页：Edit → Project Settings → VNovelizer（方案 A：配置数据存于
/// Assets/Resources/VNProjectConfig.asset，Project Settings 窗口作为编辑入口）。
///
/// - UI 入口挂进 Unity Project Settings 窗口（用户不必去 Assets 里找配置资产）；
/// - 数据本体仍是 Assets/Resources/VNProjectConfig.asset（运行时零成本读取通道，
///   Project Settings 只是 UI 入口，不存数据）；
/// - 首次打开自动创建配置资产（用户主动行为）。
///
/// 本版按功能拆分为子页面（左侧导航 + 右侧内容），与 Unity 标准 Project Settings
/// 风格保持一致。子页面归并依据：相关功能的字段集中展示，避免一个长列表无重点。
/// </summary>
internal class VNProjectSettingsProvider : SettingsProvider
{
    private Editor _editor;
    private VNProjectConfig _config;

    private enum SubPage { Resources, Localization, Security, Theater, UIPrefabs }
    private SubPage _currentPage = SubPage.Resources;

    private static readonly string[] PageLabels =
    {
        "资源 / Resources",
        "本地化 / Localization",
        "加密 / Security",
        "剧场 / Theater",
        "UI 模板 / UI Prefabs"
    };

    private VNProjectSettingsProvider(string path, SettingsScope scope = SettingsScope.Project)
        : base(path, scope) { }

    [SettingsProvider]
    public static SettingsProvider CreateProvider()
    {
        var provider = new VNProjectSettingsProvider("Project/VNovelizer")
        {
            label = "VNovelizer",
            keywords = new[] { "VNovelizer", "VN", "Visual Novel", "剧本", "UI 模板" }
        };
        return provider;
    }

    public override void OnActivate(string searchContext, UnityEngine.UIElements.VisualElement rootElement)
    {
        _config = LoadOrCreateConfig();
        if (_config != null)
        {
            _editor = Editor.CreateEditor(_config);
        }
    }

    public override void OnTitleBarGUI()
    {
        if (_config != null)
        {
            GUILayout.Space(4);
            if (GUILayout.Button("在 Project 中定位", EditorStyles.miniButton, GUILayout.Width(110)))
            {
                Selection.activeObject = _config;
                EditorGUIUtility.PingObject(_config);
            }
        }
    }

    public override void OnGUI(string searchContext)
    {
        if (_config == null)
        {
            EditorGUILayout.HelpBox("无法加载配置资产。", MessageType.Error);
            return;
        }

        EditorGUILayout.Space(2);
        EditorGUILayout.BeginHorizontal();

        // 左侧：子页面导航
        DrawSubPageSidebar();

        // 右侧：当前子页面内容
        EditorGUILayout.BeginVertical(GUILayout.MaxWidth(800));
        DrawCurrentSubPage();
        EditorGUILayout.EndVertical();

        EditorGUILayout.EndHorizontal();
    }

    public override void OnDeactivate()
    {
        if (_editor != null)
        {
            UnityEngine.Object.DestroyImmediate(_editor);
            _editor = null;
        }
    }

    /// <summary>左侧子页面导航栏</summary>
    private void DrawSubPageSidebar()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(190));
        EditorGUILayout.LabelField("VNovelizer", EditorStyles.boldLabel);
        EditorGUILayout.Space(2);
        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
        for (int i = 0; i < PageLabels.Length; i++)
        {
            bool isActive = (int)_currentPage == i;
            var style = isActive ? EditorStyles.miniButtonMid : EditorStyles.miniButton;
            if (GUILayout.Toggle(isActive, PageLabels[i], style) != isActive)
            {
                _currentPage = (SubPage)i;
            }
        }
        EditorGUILayout.EndVertical();
    }

    /// <summary>右侧子页面内容（按当前 _currentPage 派发到具体子页渲染）</summary>
    private void DrawCurrentSubPage()
    {
        EditorGUI.BeginChangeCheck();

        switch (_currentPage)
        {
            case SubPage.Resources:    DrawResourcesPage();    break;
            case SubPage.Localization: DrawLocalizationPage(); break;
            case SubPage.Security:     DrawSecurityPage();     break;
            case SubPage.Theater:      DrawTheaterPage();      break;
            case SubPage.UIPrefabs:    DrawUIPrefabsPage();    break;
        }

        if (EditorGUI.EndChangeCheck())
        {
            EditorUtility.SetDirty(_config);
            AssetDatabase.SaveAssets();
        }
    }

    // ==================== 子页面渲染 ====================

    /// <summary>资源页：路径前缀（只读）+ Excel/CSV 工作流 + 默认 Sprite</summary>
    private void DrawResourcesPage()
    {
        EditorGUILayout.LabelField("工作流", EditorStyles.boldLabel);
        DrawFields("ExcelSourceFolder", "CsvOutputFolder", "AutoConvertExcel");
        EditorGUILayout.Space(8);

        EditorGUILayout.LabelField("默认 UI 资源", EditorStyles.boldLabel);
        DrawFields("DefaultSpeakerBoxSprite", "DefaultHeadFrameSprite");
        EditorGUILayout.Space(8);

        EditorGUILayout.LabelField("引擎内部地址（只读）", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "VN 引擎内部地址前缀，正常使用无需修改。\n" +
            "如需自定义媒体资源位置，请在资源管理器窗口拖放分配（与路径无关）。",
            MessageType.Info);
        DrawFields(
            "VNScriptResPath", "BackgroundResPath", "VideoResPath", "CharacterResPath",
            "ParticalEffectPath", "AnimationPath",
            "BgmResPath", "SFXResPath", "VoiceResPath");
    }

    /// <summary>本地化页</summary>
    private void DrawLocalizationPage()
    {
        EditorGUILayout.LabelField("剧情本地化", EditorStyles.boldLabel);
        DrawFields("EnableLocalization", "TranslationFolder");

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("语言配置", EditorStyles.boldLabel);

        // 源语言 / 目标语言：直接从 Unity Localization 已创建的 Locale 中选择，
        // 避免手输 locale 代码出错（如 zh_Hans 与标准 zh-Hans 不匹配导致"检测不到"）。
        DrawSourceLocaleField();
        EditorGUILayout.Space(4);
        DrawTargetLocalesField();

        EditorGUILayout.Space(4);
        DrawUiResourceImport(_config);

        EditorGUILayout.Space(4);
        EditorGUILayout.HelpBox(
            "语言候选来自 Edit → Project Settings → Localization 中已创建的 Locale 资产。\n" +
            "如需新语言（如日语 ja），先到 Unity Localization 设置页创建对应 Locale，再回到本页选择/勾选。\n" +
            "建议给每个目标 Locale 资产配置 Fallback Locale 指向源语言——翻译缺失时运行时自动回退原文。",
            MessageType.Info);
    }

    /// <summary>包内 UI 翻译表模板目录（UPM 包内文件系统绝对路径）。</summary>
    private static string PackageUiTemplateFolder => Path.GetFullPath(
        "Packages/com.fakecorps.vnovelizer/Runtime/PackageDefault/VNovelizerRes/UI_Localization");

    /// <summary>
    /// 【P3 UI 独立集合】本地化资源导入区：把包内 UI 翻译表模板复制到项目翻译表目录，
    /// 聚合进 VNUI 集合后，模板 UI 上内置的本地化组件即刻生效。
    /// 已存在同名文件不覆盖（保护用户已填的译文）。
    /// </summary>
    private void DrawUiResourceImport(VNProjectConfig config)
    {
        string targetFolder = Path.GetFullPath(config.GetTranslationFolderPath());
        string templateFolder = PackageUiTemplateFolder;

        string[] templates = Directory.Exists(templateFolder)
            ? Directory.GetFiles(templateFolder, "*.csv")
            : new string[0];
        var missing = templates.Where(f => !File.Exists(Path.Combine(targetFolder, Path.GetFileName(f)))).ToArray();

        using (new EditorGUILayout.HorizontalScope())
        {
            if (templates.Length == 0)
            {
                EditorGUILayout.LabelField("UI 本地化资源：模板缺失", EditorStyles.miniLabel);
            }
            else if (missing.Length == 0)
            {
                // 已全部导入：绿色状态
                var prev = GUI.color;
                GUI.color = new Color(0.35f, 0.75f, 0.4f);
                EditorGUILayout.LabelField($"UI 本地化资源：已导入 {templates.Length} 张（{string.Join("、", templates.Select(Path.GetFileName))}）", EditorStyles.boldLabel);
                GUI.color = prev;
            }
            else
            {
                EditorGUILayout.LabelField($"UI 本地化资源：未导入（缺 {missing.Length} 张）", EditorStyles.boldLabel);
            }

            GUILayout.FlexibleSpace();
            if (GUILayout.Button(new GUIContent("导入本地化资源",
                    "把包内默认 UI 翻译表复制到项目翻译表目录，并聚合进 VNUI 集合。\n已存在的同名文件不会覆盖（保护你已填写的译文）。"),
                    GUILayout.Width(120)))
            {
                ImportUiResources(config, templateFolder, targetFolder);
            }
        }
    }

    private void ImportUiResources(VNProjectConfig config, string templateFolder, string targetFolder)
    {
        if (!Directory.Exists(templateFolder))
        {
            EditorUtility.DisplayDialog("导入本地化资源", "未找到包内 UI 翻译表模板目录。", "确定");
            return;
        }

        var templates = Directory.GetFiles(templateFolder, "*.csv");
        if (templates.Length == 0)
        {
            EditorUtility.DisplayDialog("导入本地化资源", "包内 UI 翻译表模板目录为空。", "确定");
            return;
        }

        Directory.CreateDirectory(targetFolder);
        int copied = 0, skipped = 0;
        foreach (var f in templates)
        {
            string dest = Path.Combine(targetFolder, Path.GetFileName(f));
            if (File.Exists(dest))
            {
                skipped++;
                continue;
            }
            File.Copy(f, dest);
            copied++;
        }

        // 【UI 本地化开箱即送】其余目标语言生成空译文模板：
        // 包内只有 en 现成译文，用户目标语言列表里的其他语言（如 ja）自动补
        // UI_Locale_{locale}.csv（ID + Text_Origin 照抄自包内 en 表，译文列留空待填）。
        int generated = GenerateUiTemplatesForOtherLocales(config, templateFolder, targetFolder);

        AssetDatabase.Refresh();

        // 【UI 本地化开箱即送】Locale 资产自动兜底：缺失的源/目标 Locale 自动创建，
        // 目标 Locale 的 fallback 链自动指向源 Locale（译文缺失时回退中文原文）。
        VNLocaleBootstrapper.EnsureLocalesAndFallbacks(config.SourceLocale, config.GetTargetLocales());

        // 新导入的资源参与聚合（VNUI 集合生成/更新）；全部跳过时也聚合一次以刷新状态
        VNTranslationAggregator.Aggregate();

        string msg = $"导入完成：新增 {copied} 张" +
                     (skipped > 0 ? $"，跳过 {skipped} 张（项目已有同名文件，不覆盖以保护译文）" : "") +
                     (generated > 0 ? $"\n已为目标语言生成 {generated} 张空译文模板（译文待填，缺失时自动回退源语言）" : "") +
                     "\n\nUI 文本已聚合进 VNUI 集合，默认模板 UI 内置的本地化组件即刻生效。";
        EditorUtility.DisplayDialog("导入本地化资源", msg, "确定");
    }

    /// <summary>
    /// 【UI 本地化开箱即送】为包内 en 表之外的目标语言生成空译文模板
    /// （UI_Locale_{locale}.csv：ID + Text_Origin 照抄包内 en 表，译文列留空）。
    /// 同名文件不覆盖（保护用户已填译文）。返回新生成数量。
    /// </summary>
    private static int GenerateUiTemplatesForOtherLocales(VNProjectConfig config, string templateFolder, string targetFolder)
    {
        // 包内 en 表 = ID/Text_Origin 权威来源（取目录里第一张表即可，当前仅 UI_Locale_en.csv）
        string enTemplate = null;
        foreach (var f in Directory.GetFiles(templateFolder, "*.csv"))
        {
            enTemplate = f;
            break;
        }
        if (enTemplate == null || !File.Exists(enTemplate))
            return 0;

        string[] sourceLines;
        try
        {
            sourceLines = File.ReadAllLines(enTemplate);
        }
        catch (System.Exception)
        {
            return 0;
        }
        if (sourceLines.Length < 2)
            return 0;

        var targetLocales = config.GetTargetLocales();
        int generated = 0;
        foreach (var locale in targetLocales)
        {
            if (string.IsNullOrEmpty(locale) || locale.Equals("en", System.StringComparison.OrdinalIgnoreCase))
                continue;

            string dest = Path.Combine(targetFolder, $"UI_Locale_{locale}.csv");
            if (File.Exists(dest))
                continue; // 已有同名文件：不覆盖（保护译文）

            // 表头换成该语言；数据行照抄 ID + Text_Origin（不含 en 译文列）
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"ID,Text_Origin,{locale}");
            for (int i = 1; i < sourceLines.Length; i++)
            {
                if (string.IsNullOrEmpty(sourceLines[i]))
                    continue;
                var cols = ScriptParser.SplitCSV(sourceLines[i]);
                if (cols.Length < 2 || string.IsNullOrEmpty(cols[0]))
                    continue;
                sb.AppendLine(string.Join(",",
                    VNTranslationTemplateGenerator.EscapeCsvField(cols[0].Trim()),
                    VNTranslationTemplateGenerator.EscapeCsvField(cols[1].Trim()),
                    ""));
            }

            File.WriteAllText(dest, sb.ToString(), new System.Text.UTF8Encoding(true));
            generated++;
        }

        return generated;
    }

    /// <summary>源语言：下拉选择 Unity Localization 已创建的 Locale（旧值未匹配时兜底显示 + 警告）。</summary>
    private void DrawSourceLocaleField()
    {
        var locales = GetProjectLocales();
        string current = (_config.SourceLocale ?? "").Trim();

        if (locales.Count == 0)
        {
            _config.SourceLocale = EditorGUILayout.TextField("源语言 (Text_Origin)", current);
            EditorGUILayout.HelpBox(
                "未检测到任何 Locale 资产。请先到 Edit → Project Settings → Localization 创建源语言 Locale" +
                "（如 zh-Hans），创建后回到本页下拉选择。", MessageType.Warning);
            return;
        }

        var displayNames = new List<string>();
        var codes = new List<string>();
        int selected = -1;

        for (int i = 0; i < locales.Count; i++)
        {
            string code = locales[i].Identifier.Code;
            if (string.IsNullOrEmpty(code))
                continue;
            codes.Add(code);
            displayNames.Add(LocaleDisplayName(locales[i]));
            if (selected < 0 && string.Equals(code, current, StringComparison.OrdinalIgnoreCase))
                selected = i;
        }

        bool unmatched = false;
        if (codes.Count == 0)
        {
            // 防御：Locale 列表存在但全部无 code（异常状态），回退手输
            _config.SourceLocale = EditorGUILayout.TextField("源语言 (Text_Origin)", current);
            return;
        }

        if (selected < 0 && current.Length > 0)
        {
            // 旧配置值不在候选列表（Locale 尚未创建，或填错代码如 zh_Hans）：追加一项让其可见并可重选
            codes.Add(current);
            displayNames.Add($"⚠ {current}（未在 Unity Localization 中找到）");
            selected = displayNames.Count - 1;
            unmatched = true;
        }

        int newSelected = EditorGUILayout.Popup("源语言 (Text_Origin)", selected, displayNames.ToArray());
        if (newSelected != selected && newSelected >= 0 && newSelected < codes.Count)
        {
            _config.SourceLocale = codes[newSelected];
            // 源语言不需要翻译表：从目标语言中自动剔除
            RemoveFromTargets(_config.SourceLocale);
        }

        // 仅在配置值尚未修正时提示（选新值后下一帧自动消失）
        if (unmatched && newSelected == selected)
        {
            EditorGUILayout.HelpBox(
                "当前源语言代码未匹配到任何 Locale 资产，聚合导入会被跳过。请从下拉中重新选择。" +
                (current.Contains("_")
                    ? "\n注意：locale 代码使用连字符，应为 zh-Hans 而不是 zh_Hans。"
                    : ""),
                MessageType.Warning);
        }
    }

    /// <summary>目标语言：多选 Unity Localization 已创建的 Locale（源语言除外；旧值未匹配时 ⚠ 兜底）。</summary>
    private void DrawTargetLocalesField()
    {
        var locales = GetProjectLocales();
        string source = (_config.SourceLocale ?? "").Trim();
        var selected = new HashSet<string>(_config.GetTargetLocales(), StringComparer.OrdinalIgnoreCase);

        if (locales.Count == 0)
        {
            _config.TargetLocales = EditorGUILayout.TextField("目标语言列表（逗号分隔）", _config.TargetLocales ?? "");
            return; // 源语言字段已有"未检测到 Locale"提示
        }

        EditorGUILayout.LabelField("目标语言列表（生成翻译模板的语言）", EditorStyles.miniLabel);

        bool changed = false;
        var candidateCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var l in locales)
        {
            string code = l.Identifier.Code;
            if (string.IsNullOrEmpty(code))
                continue;
            if (string.Equals(code, source, StringComparison.OrdinalIgnoreCase))
                continue; // 源语言不需要翻译表

            candidateCodes.Add(code);
            bool on = selected.Contains(code);
            bool newOn = GUILayout.Toggle(on, "  " + LocaleDisplayName(l));
            if (newOn != on)
            {
                if (newOn) selected.Add(code); else selected.Remove(code);
                changed = true;
            }
        }

        // 旧配置值不在候选列表（Locale 尚未创建）：⚠ 显示，取消勾选即从配置移除
        foreach (var stale in selected.Where(c => !candidateCodes.Contains(c)).ToList())
        {
            bool newOn = GUILayout.Toggle(true, $"  ⚠ {stale}（未在 Unity Localization 中找到）");
            if (!newOn)
            {
                selected.Remove(stale);
                changed = true;
            }
        }

        if (candidateCodes.Count == 0)
        {
            EditorGUILayout.HelpBox(
                "除源语言外没有其他 Locale 可选。请先到 Edit → Project Settings → Localization 创建目标语言 Locale（如 en、ja）。",
                MessageType.Info);
        }

        if (changed)
            _config.TargetLocales = string.Join(",", selected);
    }

    /// <summary>Unity Localization 已创建的 Locale 资产列表（空 = 未创建任何 Locale）。</summary>
    private static List<UnityEngine.Localization.Locale> GetProjectLocales()
    {
        var result = new List<UnityEngine.Localization.Locale>();
        try
        {
            var locales = UnityEditor.Localization.LocalizationEditorSettings.GetLocales();
            if (locales != null)
            {
                foreach (var l in locales)
                    if (l != null)
                        result.Add(l);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[VNProjectSettings] 读取 Unity Localization Locale 列表失败：{e.Message}");
        }
        return result;
    }

    /// <summary>Locale 显示名：名称（code），如 简体中文（zh-Hans）。</summary>
    private static string LocaleDisplayName(UnityEngine.Localization.Locale locale)
    {
        string code = locale.Identifier.Code;
        return string.IsNullOrEmpty(locale.LocaleName) ? code : $"{locale.LocaleName}（{code}）";
    }

    /// <summary>从目标语言列表中剔除指定 locale（源语言变更时联动）。</summary>
    private void RemoveFromTargets(string localeCode)
    {
        if (string.IsNullOrEmpty(localeCode) || string.IsNullOrEmpty(_config.TargetLocales))
            return;
        var keep = _config.GetTargetLocales()
            .Where(c => !string.Equals(c, localeCode, StringComparison.OrdinalIgnoreCase));
        _config.TargetLocales = string.Join(",", keep);
    }

    /// <summary>加密页</summary>
    private void DrawSecurityPage()
    {
        EditorGUILayout.LabelField("AES 存档加密", EditorStyles.boldLabel);
        DrawFields("UseAES");

        if (!_config.UseAES)
        {
            EditorGUILayout.HelpBox("开发期建议关闭；发布前开启并配置密钥。", MessageType.Info);
            return;
        }

        DrawFields("Key", "IV");

        EditorGUILayout.Space(4);
        if (GUILayout.Button("生成随机密钥与偏移向量"))
        {
            _config.GenerateRandomKey();
            EditorUtility.SetDirty(_config);
            AssetDatabase.SaveAssets();
        }
    }

    /// <summary>剧场页</summary>
    private void DrawTheaterPage()
    {
        EditorGUILayout.LabelField("剧场相机", EditorStyles.boldLabel);
        DrawFields("CustomSceneCameraPrefab");
    }

    /// <summary>UI 模板页：所有 Override_* 字段 + 模板创建按钮（复用 VNProjectConfigEditor 完整 UI）</summary>
    private void DrawUIPrefabsPage()
    {
        EditorGUILayout.LabelField("UI 模板覆写", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "留空 = 使用包内默认模板（无需任何配置）。\n" +
            "要自定义某个 UI，点击下方按钮从模板创建副本（位置自选）。",
            MessageType.None);

        // 复用自定义 Inspector 的"从模板创建"按钮（一致入口）
        if (GUILayout.Button("从模板创建自定义 UI…", GUILayout.Height(28)))
        {
            VNUIPrefabTemplateCreator.ShowTemplateMenu(_config);
        }
        EditorGUILayout.Space(4);

        // 全部 Override_* 字段：复用 Editor 的批量渲染（不重复硬编码）
        if (_editor != null)
        {
            var so = new SerializedObject(_config);
            so.Update();
            var iter = so.GetIterator();
            iter.NextVisible(true);
            while (iter.NextVisible(false))
            {
                if (!iter.name.StartsWith("Override_")) continue;
                EditorGUILayout.PropertyField(iter, true);
            }
            so.ApplyModifiedProperties();
        }
    }

    /// <summary>通过 SerializedObject 按字段名渲染（保持只读与可编辑语义一致）</summary>
    private void DrawFields(params string[] fieldNames)
    {
        if (_editor == null) return;
        var so = new SerializedObject(_config);
        so.Update();
        foreach (var name in fieldNames)
        {
            var prop = so.FindProperty(name);
            if (prop == null) continue;

            // 【UI 修复】Alchemy 的 LabelText 不继承 Unity PropertyAttribute，PropertyField 不会应用它
            //（此前显示英文原始字段名如 "Source Locale"，易与 Unity Localization 设置页混淆）
            var label = GetFieldLabel(name);
            if (label != null)
                EditorGUILayout.PropertyField(prop, label, true);
            else
                EditorGUILayout.PropertyField(prop, true);
        }
        so.ApplyModifiedProperties();
    }

    /// <summary>读取字段的 Alchemy LabelText 特性文本作为显示名；无特性返回 null（用默认字段名）。</summary>
    private static GUIContent GetFieldLabel(string fieldName)
    {
        var field = typeof(VNProjectConfig).GetField(fieldName,
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic);
        if (field == null) return null;

        foreach (var a in field.GetCustomAttributes(true))
        {
            if (a is Alchemy.Inspector.LabelTextAttribute lt && !string.IsNullOrEmpty(lt.Text))
                return new GUIContent(lt.Text);
        }
        return null;
    }

    /// <summary>加载配置资产；不存在则创建（首次打开设置页 = 用户主动创建配置）</summary>
    private static VNProjectConfig LoadOrCreateConfig()
    {
        VNProjectConfig.TryGetInstance(out var config);
        if (config != null) return config;

        string folder = "Assets/Resources";
        string path = folder + "/VNProjectConfig.asset";
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", "Resources");

        config = ScriptableObject.CreateInstance<VNProjectConfig>();
        AssetDatabase.CreateAsset(config, path);
        AssetDatabase.SaveAssets();
        Debug.Log("[VNProjectSettings] 已创建配置资产: " + path);

        // 顺带为剧本工作流填默认文件夹引用（文件夹存在时）
        var excelFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(VNProjectPaths.ExcelFolder);
        var csvFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(VNProjectPaths.ScriptsFolder);
        if (excelFolder != null) config.ExcelSourceFolder = excelFolder;
        if (csvFolder != null) config.CsvOutputFolder = csvFolder;
        EditorUtility.SetDirty(config);
        AssetDatabase.SaveAssets();

        return config;
    }
}
