using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using ProcessEx = System.Diagnostics.Process;

/// <summary>
/// 【本地化重构 P1 · UI 重构 2026-10】剧情本地化管理器。
///
/// <para>与其他管理器（角色/资源/画廊）统一的 UIToolkit 深色分栏风格：</para>
/// <list type="bullet">
/// <item>标题栏：窗口名 + 英文副标题 + 刷新 / 立即导入；</item>
/// <item>左侧语言侧栏：全部剧本 + 各语言（已有表数/剧本总数，缺 Locale 资产标 "!"）+
///     旧版扁平表（检测到时提供一键迁移）+ 底部翻译表目录；</item>
/// <item>右侧内容区：全部剧本（卡片 + 语言 chip）/ 单语言详情（每张表已译进度 + 定位/打开 /
///     缺失模板生成）/ 迁移视图三态切换；</item>
/// <item>底部：最近导入结果折叠面板 + 状态栏概览。</item>
/// </list>
///
/// <para>【按语言文件夹分类】翻译表新结构 <c>{TranslationFolder}/{locale}/{script}.csv</c>
/// （UI 表为 <c>{locale}/UI.csv</c>）；旧扁平表自动检测 + 一键迁移（MoveAsset 保 GUID），
/// 聚合器递归扫描保证新旧共存过渡零破坏。详见 <see cref="VNTranslationTablePaths"/>。</para>
///
/// <para>工作流（Docs/VNLocalizationRefactoring.md）：Excel 转 CSV 自动生成/更新翻译表模板；
/// 翻译团队直接编辑 CSV；保存即自动聚合为 StringTableCollection；本窗口用于总览/补模板/迁移/诊断。</para>
/// </summary>
public class VNLocalizationWindow : EditorWindow
{
    // 侧栏分类键（语言代码本身也是分类键）
    private const string CategoryAll = "__all__";
    private const string CategoryLegacy = "__legacy__";

    // ==================== 状态 ====================

    private string _selectedCategory = CategoryAll;
    private string _search = "";
    private bool _resultExpanded;

    // ==================== 数据缓存（RefreshData 重建） ====================

    private sealed class ScriptRow
    {
        public string Name;
        public string CsvFullPath;
        /// <summary>locale → 翻译表 Assets 相对路径（null = 缺失；旧扁平路径也算已有）</summary>
        public readonly Dictionary<string, string> TablePaths =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class LocaleRow
    {
        public string Code;
        public int ScriptTableCount;
        public int ScriptTotal;
        public bool HasLocaleAsset;
    }

    private readonly List<ScriptRow> _scripts = new List<ScriptRow>();
    private readonly List<LocaleRow> _locales = new List<LocaleRow>();
    private List<VNTranslationTablePaths.LegacyFileInfo> _legacyFiles =
        new List<VNTranslationTablePaths.LegacyFileInfo>();
    private readonly HashSet<string> _projectLocaleCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private string[] _targetLocales = Array.Empty<string>();
    private bool _localizationEnabled;

    // ==================== UI 引用 ====================

    private VisualElement _sidebarList;
    private VisualElement _contentRoot;
    private Label _resultSummaryLabel;
    private VisualElement _resultDetail;
    private Label _statusLabel;
    private ToolbarSearchField _searchField;

    // ==================== 生命周期 ====================

    // 菜单入口统一注册在 VNLocalizationMenu（路径常量 + 未启用本地化时灰显 validate）
    public static void ShowWindow()
    {
        var wnd = GetWindow<VNLocalizationWindow>();
        wnd.titleContent = new GUIContent("剧情本地化管理器");
        wnd.minSize = new Vector2(1000, 600);
    }

    private void OnEnable()
    {
        EditorApplication.projectChanged += OnProjectChanged;
    }

    private void OnDisable()
    {
        EditorApplication.projectChanged -= OnProjectChanged;
    }

    private void OnProjectChanged()
    {
        if (_contentRoot == null)
            return;
        RefreshData();
        RebuildAll();
    }

    public void CreateGUI()
    {
        RefreshData();

        var root = rootVisualElement;
        root.Clear();
        root.style.backgroundColor = GalleryTheme.Hex(GalleryTheme.BgPrimary);
        root.style.flexDirection = FlexDirection.Column;

        BuildTitleBar(root);

        var splitView = new TwoPaneSplitView(0, 232, TwoPaneSplitViewOrientation.Horizontal);
        splitView.style.flexGrow = 1;
        splitView.style.minWidth = 0;
        splitView.style.minHeight = 0;

        splitView.Add(BuildSidebar());

        var contentWrapper = new VisualElement();
        contentWrapper.style.flexDirection = FlexDirection.Column;
        contentWrapper.style.flexGrow = 1;
        contentWrapper.style.minWidth = 0;

        BuildToolbar(contentWrapper);

        _contentRoot = new VisualElement();
        _contentRoot.style.flexGrow = 1;
        _contentRoot.style.minHeight = 0;
        _contentRoot.style.overflow = Overflow.Hidden;
        contentWrapper.Add(_contentRoot);

        contentWrapper.Add(BuildResultSection());

        splitView.Add(contentWrapper);
        root.Add(splitView);

        BuildStatusBar(root);

        RebuildAll();
    }

    // ==================== 数据刷新 ====================

    private void RefreshData()
    {
        _scripts.Clear();
        _locales.Clear();

        var config = VNProjectConfig.Instance;
        _localizationEnabled = config != null && config.EnableLocalization;
        _targetLocales = config?.GetTargetLocales() ?? Array.Empty<string>();
        if (config == null || !_localizationEnabled)
        {
            _legacyFiles = new List<VNTranslationTablePaths.LegacyFileInfo>();
            return;
        }

        // 1) 剧本列表（CSV 输出目录）
        string csvFolder = config.GetCsvOutputPath();
        if (!string.IsNullOrEmpty(csvFolder))
        {
            string full = Path.GetFullPath(csvFolder);
            if (Directory.Exists(full))
            {
                foreach (var f in Directory.GetFiles(full, "*.csv"))
                {
                    _scripts.Add(new ScriptRow
                    {
                        Name = Path.GetFileNameWithoutExtension(f),
                        CsvFullPath = f,
                    });
                }
                _scripts.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            }
        }

        // 2) 语言清单 = 配置目标语言（顺序优先）∪ 翻译目录下实际语言文件夹
        var codes = new List<string>(_targetLocales);
        foreach (var folder in VNTranslationTablePaths.DiscoverLocaleFolders(config))
        {
            if (!codes.Any(c => string.Equals(c, folder, StringComparison.OrdinalIgnoreCase)))
                codes.Add(folder);
        }

        // 3) 项目 Locale 资产缓存（缺 Locale 诊断）
        RefreshProjectLocaleCodes();

        // 4) 每剧本 × 每语言：翻译表位置（新结构优先，旧扁平兜底）
        foreach (var s in _scripts)
        {
            foreach (var code in codes)
                s.TablePaths[code] = VNTranslationTablePaths.FindTableAssetPath(config, s.Name, code);
        }

        // 5) 语言行统计
        foreach (var code in codes)
        {
            _locales.Add(new LocaleRow
            {
                Code = code,
                ScriptTotal = _scripts.Count,
                ScriptTableCount = _scripts.Count(s =>
                    s.TablePaths.TryGetValue(code, out var p) && p != null),
                HasLocaleAsset = HasLocaleAsset(code),
            });
        }

        // 6) 旧扁平表检测（已知语言 = 配置 + 语言文件夹 + 项目 Locale + 源语言）
        var known = new List<string>(codes);
        if (!string.IsNullOrEmpty(config.SourceLocale))
            known.Add(config.SourceLocale);
        known.AddRange(_projectLocaleCodes);
        _legacyFiles = VNTranslationTablePaths.FindLegacyFlatTables(config,
            known.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>刷新 Unity Localization 已创建 Locale 代码缓存。</summary>
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

    /// <summary>语言是否已创建 Locale 资产（下划线/连字符归一化 + 忽略大小写）。</summary>
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

    // ==================== 骨架构建 ====================

    private void BuildTitleBar(VisualElement root)
    {
        var titleBar = new VisualElement();
        titleBar.style.flexDirection = FlexDirection.Row;
        titleBar.style.height = 48;
        titleBar.style.backgroundColor = GalleryTheme.Hex(GalleryTheme.BgSecondary);
        titleBar.style.alignItems = Align.Center;
        titleBar.style.paddingLeft = 16;
        titleBar.style.paddingRight = 12;
        titleBar.style.borderBottomWidth = 1;
        titleBar.style.borderBottomColor = GalleryTheme.Hex(GalleryTheme.Border);
        root.Add(titleBar);

        var icon = new Label("\u25C8")
        {
            style =
            {
                fontSize = 16,
                color = GalleryTheme.Hex(GalleryTheme.Accent),
                marginRight = 8
            }
        };
        titleBar.Add(icon);

        var title = new Label("剧情本地化管理器")
        {
            style =
            {
                fontSize = 16,
                unityFontStyleAndWeight = FontStyle.Bold,
                color = GalleryTheme.Hex(GalleryTheme.TextPrimary),
                marginRight = 24
            }
        };
        titleBar.Add(title);

        var subtitle = new Label("Story Localization")
        {
            style =
            {
                fontSize = 11,
                color = GalleryTheme.Hex(GalleryTheme.TextMuted)
            }
        };
        titleBar.Add(subtitle);

        titleBar.Add(new VisualElement { style = { flexGrow = 1 } });

        var refreshBtn = new Button(() =>
        {
            RefreshData();
            RebuildAll();
        })
        { text = "\u21BB 刷新" };
        GalleryStyles.ApplyButton(refreshBtn, GalleryTheme.BgCard, false);
        AddButtonFeedback(refreshBtn, GalleryTheme.Hex(GalleryTheme.BgCard));
        refreshBtn.style.height = 30;
        refreshBtn.style.marginRight = 8;
        titleBar.Add(refreshBtn);

        var importBtn = new Button(AggregateNow) { text = "\u21BB 立即导入翻译表" };
        GalleryStyles.ApplyButton(importBtn, GalleryTheme.Accent, true);
        AddButtonFeedback(importBtn, GalleryTheme.Hex(GalleryTheme.Accent));
        importBtn.style.height = 30;
        importBtn.style.marginRight = 8;
        titleBar.Add(importBtn);

        var settingsBtn = new Button(() =>
            SettingsService.OpenProjectSettings("Project/VNovelizer"))
        { text = "语言设置" };
        GalleryStyles.ApplyButton(settingsBtn, GalleryTheme.BgCard, false);
        AddButtonFeedback(settingsBtn, GalleryTheme.Hex(GalleryTheme.BgCard));
        settingsBtn.style.height = 30;
        titleBar.Add(settingsBtn);
    }

    private VisualElement BuildSidebar()
    {
        var sidebar = new VisualElement();
        sidebar.style.backgroundColor = GalleryTheme.Hex(GalleryTheme.BgSecondary);
        sidebar.style.borderRightWidth = 1;
        sidebar.style.borderRightColor = GalleryTheme.Hex(GalleryTheme.Border);
        sidebar.style.minWidth = 200;

        var title = new Label("语言");
        title.style.fontSize = 13;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.color = GalleryTheme.Hex(GalleryTheme.TextPrimary);
        title.style.marginLeft = 12;
        title.style.marginTop = 10;
        title.style.marginBottom = 8;
        sidebar.Add(title);

        var scroll = new ScrollView(ScrollViewMode.Vertical);
        scroll.style.flexGrow = 1;
        scroll.style.flexShrink = 1;
        scroll.style.borderTopWidth = 0;
        scroll.style.borderBottomWidth = 0;
        scroll.style.borderLeftWidth = 0;
        scroll.style.borderRightWidth = 0;
        _sidebarList = new VisualElement();
        scroll.Add(_sidebarList);
        sidebar.Add(scroll);

        BuildSidebarFooter(sidebar);
        return sidebar;
    }

    /// <summary>侧栏底部：翻译表目录显示与修改。</summary>
    private void BuildSidebarFooter(VisualElement sidebar)
    {
        var footer = new VisualElement();
        footer.style.borderTopWidth = 1;
        footer.style.borderTopColor = GalleryTheme.Hex(GalleryTheme.Border);
        footer.style.paddingTop = 8;
        footer.style.paddingBottom = 8;
        footer.style.paddingLeft = 12;
        footer.style.paddingRight = 10;

        var caption = new Label("翻译表目录");
        caption.style.fontSize = 11;
        caption.style.unityFontStyleAndWeight = FontStyle.Bold;
        caption.style.color = GalleryTheme.Hex(GalleryTheme.TextSecondary);
        caption.style.marginBottom = 4;
        footer.Add(caption);

        var pathLabel = new Label(VNProjectConfig.Instance != null
            ? VNProjectConfig.Instance.GetTranslationFolderPath()
            : "-");
        pathLabel.name = "translation-folder-label";
        pathLabel.style.fontSize = 10;
        pathLabel.style.color = GalleryTheme.Hex(GalleryTheme.TextMuted);
        pathLabel.style.whiteSpace = WhiteSpace.Normal;
        pathLabel.style.marginBottom = 6;
        pathLabel.style.unityTextAlign = TextAnchor.UpperLeft;
        footer.Add(pathLabel);

        var btnRow = new VisualElement();
        btnRow.style.flexDirection = FlexDirection.Row;
        btnRow.style.justifyContent = Justify.SpaceBetween;

        var pickBtn = new Button(() => PickTranslationFolder(pathLabel)) { text = "选择…" };
        StyleMiniButton(pickBtn, GalleryTheme.BgCard, GalleryTheme.TextPrimary);
        pickBtn.style.flexGrow = 1;
        pickBtn.style.marginRight = 6;
        btnRow.Add(pickBtn);

        var openBtn = new Button(() =>
        {
            var config = VNProjectConfig.Instance;
            if (config == null) return;
            string folder = Path.GetFullPath(config.GetTranslationFolderPath());
            Directory.CreateDirectory(folder);
            EditorUtility.RevealInFinder(folder);
        })
        { text = "打开" };
        StyleMiniButton(openBtn, GalleryTheme.BgCard, GalleryTheme.TextPrimary);
        openBtn.style.flexGrow = 1;
        btnRow.Add(openBtn);

        footer.Add(btnRow);
        sidebar.Add(footer);
    }

    private void BuildToolbar(VisualElement parent)
    {
        var toolbar = new VisualElement();
        toolbar.style.flexDirection = FlexDirection.Row;
        toolbar.style.alignItems = Align.Center;
        toolbar.style.backgroundColor = GalleryTheme.Hex(GalleryTheme.BgSecondary);
        toolbar.style.paddingTop = 6;
        toolbar.style.paddingBottom = 6;
        toolbar.style.paddingLeft = 10;
        toolbar.style.paddingRight = 10;
        toolbar.style.borderBottomWidth = 1;
        toolbar.style.borderBottomColor = GalleryTheme.Hex(GalleryTheme.Border);

        _searchField = new ToolbarSearchField
        {
            style =
            {
                flexGrow = 1,
                flexShrink = 1,
                minWidth = 120,
                marginRight = 8
            }
        };
        _searchField.RegisterValueChangedCallback(evt =>
        {
            _search = evt.newValue ?? "";
            RebuildContent();
        });
        toolbar.Add(_searchField);

        var fillBtn = new Button(() => GenerateAllMissing())
        { text = "补全缺失模板", tooltip = "为所有剧本生成尚不存在的目标语言翻译表模板（幂等安全：已有模板与译文不受影响）" };
        GalleryStyles.ApplyButton(fillBtn, GalleryTheme.BgCard, false);
        AddButtonFeedback(fillBtn, GalleryTheme.Hex(GalleryTheme.BgCard));
        fillBtn.style.height = 26;
        fillBtn.style.marginRight = 8;
        toolbar.Add(fillBtn);

        var openBtn = new Button(() =>
        {
            var config = VNProjectConfig.Instance;
            if (config == null) return;
            string folder = Path.GetFullPath(config.GetTranslationFolderPath());
            Directory.CreateDirectory(folder);
            EditorUtility.RevealInFinder(folder);
        })
        { text = "打开翻译表目录" };
        GalleryStyles.ApplyButton(openBtn, GalleryTheme.BgCard, false);
        AddButtonFeedback(openBtn, GalleryTheme.Hex(GalleryTheme.BgCard));
        openBtn.style.height = 26;
        toolbar.Add(openBtn);

        parent.Add(toolbar);
    }

    private VisualElement BuildResultSection()
    {
        var section = new VisualElement();
        section.style.backgroundColor = GalleryTheme.Hex(GalleryTheme.BgSecondary);
        section.style.borderTopWidth = 1;
        section.style.borderTopColor = GalleryTheme.Hex(GalleryTheme.Border);

        var headerRow = new VisualElement();
        headerRow.style.flexDirection = FlexDirection.Row;
        headerRow.style.alignItems = Align.Center;
        headerRow.style.paddingLeft = 10;
        headerRow.style.paddingRight = 10;

        var foldout = new Foldout { text = "最近导入结果", value = _resultExpanded };
        foldout.style.flexGrow = 1;
        foldout.style.marginTop = 0;
        foldout.style.marginBottom = 0;
        foldout.style.color = GalleryTheme.Hex(GalleryTheme.TextPrimary);
        foldout.style.unityFontStyleAndWeight = FontStyle.Bold;
        foldout.RegisterValueChangedCallback(v =>
        {
            _resultExpanded = v.newValue;
            _resultDetail.style.display = _resultExpanded ? DisplayStyle.Flex : DisplayStyle.None;
        });
        headerRow.Add(foldout);

        _resultSummaryLabel = new Label("");
        _resultSummaryLabel.style.fontSize = 11;
        _resultSummaryLabel.style.color = GalleryTheme.Hex(GalleryTheme.TextMuted);
        _resultSummaryLabel.style.marginRight = 4;
        headerRow.Add(_resultSummaryLabel);

        section.Add(headerRow);

        _resultDetail = new VisualElement();
        _resultDetail.style.display = _resultExpanded ? DisplayStyle.Flex : DisplayStyle.None;
        _resultDetail.style.paddingLeft = 30;
        _resultDetail.style.paddingRight = 14;
        _resultDetail.style.paddingBottom = 10;
        _resultDetail.style.maxHeight = 180;
        section.Add(_resultDetail);

        return section;
    }

    private void BuildStatusBar(VisualElement root)
    {
        var bar = new VisualElement();
        bar.style.flexDirection = FlexDirection.Row;
        bar.style.alignItems = Align.Center;
        bar.style.height = 24;
        bar.style.backgroundColor = GalleryTheme.Hex(GalleryTheme.BgPrimary);
        bar.style.borderTopWidth = 1;
        bar.style.borderTopColor = GalleryTheme.Hex(GalleryTheme.Border);
        bar.style.paddingLeft = 12;

        _statusLabel = new Label("");
        _statusLabel.style.fontSize = 11;
        _statusLabel.style.color = GalleryTheme.Hex(GalleryTheme.TextMuted);
        bar.Add(_statusLabel);

        root.Add(bar);
    }

    // ==================== 重建 ====================

    private void RebuildAll()
    {
        ValidateSelectedCategory();
        RebuildSidebar();
        RebuildContent();
        RefreshResultSection();
        RefreshStatusBar();
    }

    private void RebuildSidebar()
    {
        _sidebarList.Clear();

        if (!_localizationEnabled)
        {
            var hint = new Label("本地化未启用");
            hint.style.fontSize = 11;
            hint.style.color = GalleryTheme.Hex(GalleryTheme.TextMuted);
            hint.style.marginLeft = 12;
            _sidebarList.Add(hint);
            return;
        }

        // 全部剧本
        _sidebarList.Add(MakeSidebarItem(
            CategoryAll, "全部剧本", _scripts.Count.ToString(),
            GalleryTheme.Hex(GalleryTheme.TextMuted)));

        // 各语言
        foreach (var locale in _locales)
        {
            _sidebarList.Add(MakeSidebarItem(
                locale.Code,
                $"{locale.Code}  {SettingsPanel.GetLocaleDisplayName(locale.Code)}",
                $"{locale.ScriptTableCount}/{locale.ScriptTotal}",
                locale.HasLocaleAsset
                    ? GalleryTheme.Hex(GalleryTheme.TextMuted)
                    : GalleryTheme.Hex(GalleryTheme.Warning),
                warn: !locale.HasLocaleAsset,
                warnTooltip: "该语言尚未创建 Locale 资产——填好译文后需到 Project Settings → Localization 创建同名 Locale 才能导入聚合"));
        }

        // 旧版扁平表（检测到才显示）
        if (_legacyFiles.Count > 0)
        {
            var divider = new VisualElement();
            divider.style.height = 1;
            divider.style.backgroundColor = GalleryTheme.Hex(GalleryTheme.Border);
            divider.style.marginTop = 8;
            divider.style.marginBottom = 8;
            divider.style.marginLeft = 12;
            divider.style.marginRight = 12;
            _sidebarList.Add(divider);

            _sidebarList.Add(MakeSidebarItem(
                CategoryLegacy, "旧版扁平表", _legacyFiles.Count.ToString(),
                GalleryTheme.Hex(GalleryTheme.Warning)));
        }
    }

    private VisualElement MakeSidebarItem(string key, string title, string badge, Color badgeColor,
        bool warn = false, string warnTooltip = null)
    {
        var item = new VisualElement();
        item.style.flexDirection = FlexDirection.Row;
        item.style.alignItems = Align.Center;
        item.style.height = 28;
        item.style.paddingLeft = 12;
        item.style.paddingRight = 8;
        item.style.marginLeft = 4;
        item.style.marginRight = 4;
        ResourceStyles.SetRadius(item, 4);

        bool selected = string.Equals(_selectedCategory, key, StringComparison.Ordinal);

        // 选中竖条（左侧强调色，选中态一眼可辨）
        if (selected)
        {
            var indicator = new VisualElement();
            indicator.style.width = 3;
            indicator.style.height = 18;
            indicator.style.backgroundColor = GalleryTheme.Hex(GalleryTheme.Accent);
            indicator.style.marginRight = 8;
            indicator.style.borderTopLeftRadius = 2;
            indicator.style.borderBottomLeftRadius = 2;
            item.Add(indicator);
        }

        var name = new Label(title);
        name.style.flexGrow = 1;
        name.style.flexShrink = 1;
        name.style.minWidth = 0;
        name.style.color = selected
            ? GalleryTheme.Hex(GalleryTheme.TextPrimary)
            : GalleryTheme.Hex(GalleryTheme.TextSecondary);
        name.style.fontSize = 12;
        name.style.unityTextAlign = TextAnchor.MiddleLeft;
        item.Add(name);

        if (warn)
        {
            var warnLabel = new Label("!");
            warnLabel.style.fontSize = 11;
            warnLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            warnLabel.style.color = GalleryTheme.Hex(GalleryTheme.Warning);
            warnLabel.style.marginRight = 6;
            warnLabel.tooltip = warnTooltip ?? "缺少 Locale 资产";
            item.Add(warnLabel);
        }

        var count = new Label(badge);
        count.style.fontSize = 11;
        count.style.color = badgeColor;
        count.style.paddingLeft = 6;
        count.style.paddingRight = 6;
        count.style.paddingTop = 1;
        count.style.paddingBottom = 1;
        count.style.backgroundColor = new Color(0, 0, 0, 0.25f);
        ResourceStyles.SetRadius(count, 8);
        item.Add(count);

        // 三态反馈：选中基色 AccentDim / 未选中透明；悬停提亮；按下变暗
        Color normal = selected
            ? GalleryTheme.Hex(GalleryTheme.AccentDim)
            : GalleryTheme.Transparent_Color;
        Color hover = selected
            ? Color.Lerp(GalleryTheme.Hex(GalleryTheme.AccentDim), Color.white, 0.18f)
            : GalleryTheme.Hex(GalleryTheme.BgHover);
        Color pressed = Color.Lerp(normal, Color.black, 0.30f);

        bool hovering = false, pressing = false;
        void UpdateBg()
        {
            item.style.backgroundColor = pressing ? pressed : (hovering ? hover : normal);
        }
        UpdateBg();

        item.RegisterCallback<ClickEvent>(_ =>
        {
            if (string.Equals(_selectedCategory, key, StringComparison.Ordinal))
                return;
            _selectedCategory = key;
            RebuildSidebar();
            RebuildContent();
        });

        item.RegisterCallback<MouseEnterEvent>(_ => { hovering = true; UpdateBg(); });
        item.RegisterCallback<MouseLeaveEvent>(_ => { hovering = false; pressing = false; UpdateBg(); });
        item.RegisterCallback<PointerDownEvent>(_ => { pressing = true; UpdateBg(); });
        item.RegisterCallback<PointerUpEvent>(_ => { pressing = false; UpdateBg(); });

        return item;
    }

    private void RebuildContent()
    {
        _contentRoot.Clear();

        if (!_localizationEnabled)
        {
            _contentRoot.Add(BuildDisabledView());
            return;
        }

        ValidateSelectedCategory();

        if (_selectedCategory == CategoryLegacy)
            _contentRoot.Add(BuildLegacyView());
        else if (_selectedCategory == CategoryAll)
            _contentRoot.Add(BuildAllScriptsView());
        else
            _contentRoot.Add(BuildLanguageView(_selectedCategory));
    }

    /// <summary>分类失效兜底：选中语言已不在清单（配置变化/文件夹消失）或旧表已迁移完 → 回全部剧本。</summary>
    private void ValidateSelectedCategory()
    {
        if (_selectedCategory == CategoryAll)
            return;
        if (_selectedCategory == CategoryLegacy)
        {
            if (_legacyFiles.Count == 0)
                _selectedCategory = CategoryAll;
            return;
        }
        if (!_locales.Any(l => string.Equals(l.Code, _selectedCategory, StringComparison.OrdinalIgnoreCase)))
            _selectedCategory = CategoryAll;
    }

    // ==================== 视图：未启用引导 ====================

    private VisualElement BuildDisabledView()
    {
        var container = MakeScrollContent();

        var card = GalleryStyles.MakeCard();
        card.style.alignItems = Align.Center;
        card.style.justifyContent = Justify.Center;
        card.style.flexGrow = 1;

        var title = new Label(VNProjectConfig.Instance == null
            ? "未找到 VNProjectConfig"
            : "剧情本地化未启用");
        title.style.fontSize = 16;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.color = GalleryTheme.Hex(GalleryTheme.TextPrimary);
        title.style.marginBottom = 10;
        card.Add(title);

        var desc = new Label(VNProjectConfig.Instance == null
            ? "请先打开 Edit → Project Settings → VNovelizer 创建项目配置。"
            : "启用后可按语言生成翻译表模板、聚合为 StringTableCollection，并支持多语言切换。\n请在设置页勾选「启用剧情本地化」。");
        desc.style.fontSize = 12;
        desc.style.color = GalleryTheme.Hex(GalleryTheme.TextSecondary);
        desc.style.whiteSpace = WhiteSpace.Normal;
        desc.style.unityTextAlign = TextAnchor.MiddleCenter;
        desc.style.marginBottom = 16;
        card.Add(desc);

        var btn = new Button(() => SettingsService.OpenProjectSettings("Project/VNovelizer"))
        { text = "前往设置页" };
        GalleryStyles.ApplyButton(btn, GalleryTheme.Accent, true);
        AddButtonFeedback(btn, GalleryTheme.Hex(GalleryTheme.Accent));
        btn.style.height = 32;
        btn.style.width = 140;
        card.Add(btn);

        container.Add(card);
        return container;
    }

    // ==================== 视图：全部剧本（卡片） ====================

    private VisualElement BuildAllScriptsView()
    {
        var container = MakeScrollContent();

        if (_targetLocales.Length == 0)
        {
            container.Add(MakeHintBox(
                "未配置目标语言。请在 Project Settings → VNovelizer → 本地化 勾选目标语言（候选来自 Localization 的 Locale 资产）。",
                GalleryTheme.Hex(GalleryTheme.Warning)));
        }

        if (_scripts.Count == 0)
        {
            container.Add(MakeHintBox(
                "CSV 输出目录下暂无剧本。请先在剧本管理器创建并转换。", GalleryTheme.Hex(GalleryTheme.TextMuted)));
            return container;
        }

        var shown = FilterScripts();
        if (shown.Count == 0)
        {
            container.Add(MakeHintBox($"没有匹配「{_search}」的剧本或翻译表。", GalleryTheme.Hex(GalleryTheme.TextMuted)));
            return container;
        }

        var countLabel = new Label($"显示 {shown.Count} / {_scripts.Count} 个剧本");
        countLabel.style.fontSize = 11;
        countLabel.style.color = GalleryTheme.Hex(GalleryTheme.TextMuted);
        countLabel.style.marginLeft = 4;
        countLabel.style.marginBottom = 8;
        container.Add(countLabel);

        foreach (var script in shown)
            container.Add(MakeScriptCard(script));

        return container;
    }

    /// <summary>按搜索词过滤：匹配剧本名或已有翻译表文件名。</summary>
    private List<ScriptRow> FilterScripts()
    {
        string search = (_search ?? "").Trim();
        if (search.Length == 0)
            return new List<ScriptRow>(_scripts);

        return _scripts.Where(s =>
        {
            if (s.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return s.TablePaths.Values.Any(p =>
                p != null &&
                Path.GetFileName(p).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0);
        }).ToList();
    }

    private VisualElement MakeScriptCard(ScriptRow script)
    {
        var card = GalleryStyles.MakeCard();

        // ---- 头行：剧本名 + 完整度徽章 + 操作 ----
        var header = new VisualElement();
        header.style.flexDirection = FlexDirection.Row;
        header.style.alignItems = Align.Center;
        header.style.marginBottom = 8;
        card.Add(header);

        var dot = new Label("\u25A0");
        dot.style.fontSize = 10;
        dot.style.color = GalleryTheme.Hex(GalleryTheme.Accent);
        dot.style.marginRight = 8;
        header.Add(dot);

        var nameLabel = new Label(script.Name);
        nameLabel.style.fontSize = 13;
        nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        nameLabel.style.color = GalleryTheme.Hex(GalleryTheme.TextPrimary);
        nameLabel.style.marginRight = 12;
        header.Add(nameLabel);

        var missing = _targetLocales
            .Where(t => script.TablePaths.TryGetValue(t, out var p) && p == null)
            .ToList();

        if (_targetLocales.Length > 0)
        {
            int existing = _targetLocales.Length - missing.Count;
            float ratio = (float)existing / _targetLocales.Length;
            var badge = MakeBadge(
                $"{existing}/{_targetLocales.Length}",
                ratio >= 1f ? GalleryTheme.Hex(GalleryTheme.Success)
                    : existing == 0 ? GalleryTheme.Hex(GalleryTheme.Danger)
                    : GalleryTheme.Hex(GalleryTheme.Warning));
            header.Add(badge);
        }

        header.Add(new VisualElement { style = { flexGrow = 1 } });

        var genBtn = new Button(() => GenerateMissingForScript(script.Name, missing))
        { text = "生成缺失", tooltip = missing.Count > 0 ? $"生成缺失的 {missing.Count} 张模板（{string.Join(", ", missing)}）" : "无缺失模板" };
        genBtn.SetEnabled(missing.Count > 0);
        StyleMiniButton(genBtn, GalleryTheme.BgHover, GalleryTheme.TextPrimary);
        genBtn.style.marginRight = 6;
        header.Add(genBtn);

        var locateBtn = new Button(() =>
        {
            if (File.Exists(script.CsvFullPath))
                EditorUtility.RevealInFinder(script.CsvFullPath);
            else
                EditorUtility.DisplayDialog("提示",
                    $"剧本 CSV 不存在：{script.CsvFullPath}\n请先在剧本管理器点击「转换」。", "确定");
        })
        { text = "定位 CSV" };
        StyleMiniButton(locateBtn, GalleryTheme.BgHover, GalleryTheme.TextPrimary);
        header.Add(locateBtn);

        // ---- 语言 chip 行：状态即操作 ----
        if (_targetLocales.Length > 0)
        {
            var chips = new VisualElement();
            chips.style.flexDirection = FlexDirection.Row;
            chips.style.flexWrap = Wrap.Wrap;
            chips.style.paddingLeft = 18;
            card.Add(chips);

            foreach (var locale in _targetLocales)
                chips.Add(MakeLocaleChip(script, locale));
        }

        AddCardHover(card);
        return card;
    }

    /// <summary>
    /// 单语言 chip：状态即操作——
    /// 绿 = 已有翻译表（点击定位）；橙 = 缺失（点击生成）；黄 = 缺失且该语言未创建 Locale 资产。
    /// </summary>
    private VisualElement MakeLocaleChip(ScriptRow script, string locale)
    {
        script.TablePaths.TryGetValue(locale, out var tablePath);
        bool has = tablePath != null;
        bool noLocaleAsset = !HasLocaleAsset(locale);

        var chip = new Button(() =>
        {
            if (has)
            {
                EditorUtility.RevealInFinder(Path.GetFullPath(tablePath));
                return;
            }

            string generated = GenerateSingle(script.Name, locale);
            if (!string.IsNullOrEmpty(generated))
            {
                VNTranslationAggregator.Aggregate();
                RefreshData();
                RebuildAll();
            }
        })
        { text = has ? locale : (noLocaleAsset ? locale : "+ " + locale) };

        chip.tooltip = has
            ? $"已有翻译表：\n{tablePath}\n\n点击定位文件"
            : noLocaleAsset
                ? $"缺失翻译表：{VNTranslationTablePaths.GetTableAssetPath(VNProjectConfig.Instance, script.Name, locale)}\n\n点击生成模板。\n! 该语言尚未创建 Locale 资产——填好译文后，需到 Project Settings → Localization 创建同名 Locale 才能导入聚合。"
                : $"缺失翻译表：{VNTranslationTablePaths.GetTableAssetPath(VNProjectConfig.Instance, script.Name, locale)}\n\n点击生成模板（新增行自动补入，已有译文永不动）";

        Color bg = has ? GalleryTheme.Hex(GalleryTheme.Success)
            : noLocaleAsset ? GalleryTheme.Hex(GalleryTheme.Warning)
            : new Color(0.90f, 0.55f, 0.25f);
        chip.style.backgroundColor = bg;
        chip.style.color = Color.white;
        chip.style.fontSize = 11;
        chip.style.height = 20;
        chip.style.marginRight = 6;
        chip.style.marginBottom = 4;
        chip.style.paddingLeft = 10;
        chip.style.paddingRight = 10;
        ResourceStyles.SetRadius(chip, 10);
        chip.style.borderTopWidth = 0;
        chip.style.borderBottomWidth = 0;
        chip.style.borderLeftWidth = 0;
        chip.style.borderRightWidth = 0;
        AddButtonFeedback(chip, bg);
        return chip;
    }

    // ==================== 视图：单语言详情 ====================

    private sealed class TableEntryRow
    {
        public string DisplayName;
        public string FullPath;
        public int TotalRows;
        public int TranslatedRows;
        public bool IsUi;
    }

    private VisualElement BuildLanguageView(string locale)
    {
        var config = VNProjectConfig.Instance;
        var container = MakeScrollContent();

        var localeRow = _locales.FirstOrDefault(l =>
            string.Equals(l.Code, locale, StringComparison.OrdinalIgnoreCase));

        // ---- 头部：语言名 + 文件夹操作 ----
        var header = new VisualElement();
        header.style.flexDirection = FlexDirection.Row;
        header.style.alignItems = Align.Center;
        header.style.marginLeft = 4;
        header.style.marginRight = 4;
        header.style.marginBottom = 4;
        container.Add(header);

        var title = new Label($"{locale} — {SettingsPanel.GetLocaleDisplayName(locale)}");
        title.style.fontSize = 16;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.color = GalleryTheme.Hex(GalleryTheme.TextPrimary);
        title.style.marginRight = 12;
        header.Add(title);

        header.Add(new VisualElement { style = { flexGrow = 1 } });

        var openFolderBtn = new Button(() =>
        {
            string folder = VNTranslationTablePaths.GetLocaleFolderFullPath(config, locale);
            Directory.CreateDirectory(folder);
            EditorUtility.RevealInFinder(folder);
        })
        { text = "打开文件夹" };
        StyleMiniButton(openFolderBtn, GalleryTheme.Accent, Color.white);
        header.Add(openFolderBtn);

        var folderLabel = new Label(
            $"{config.GetTranslationFolderPath()}/{VNTranslationTablePaths.SanitizeLocaleFolderName(locale)}/");
        folderLabel.style.fontSize = 11;
        folderLabel.style.color = GalleryTheme.Hex(GalleryTheme.TextMuted);
        folderLabel.style.marginLeft = 4;
        folderLabel.style.marginBottom = 10;
        container.Add(folderLabel);

        // ---- Locale 资产缺失警告 ----
        if (localeRow != null && !localeRow.HasLocaleAsset)
        {
            container.Add(MakeHintBox(
                $"项目尚未创建 Locale「{locale}」资产——填好译文后，需到 Project Settings → Localization 创建同名 Locale 才能导入聚合。",
                GalleryTheme.Hex(GalleryTheme.Warning)));
        }

        // ---- 已有翻译表（该语言文件夹实际文件 + 旧扁平 UI 表） ----
        string folderFull = VNTranslationTablePaths.GetLocaleFolderFullPath(config, locale);
        var entries = new List<TableEntryRow>();

        if (Directory.Exists(folderFull))
        {
            foreach (var f in Directory.GetFiles(folderFull, "*.csv"))
            {
                VNTranslationTablePaths.ReadTableStats(f, out int total, out int translated);
                entries.Add(new TableEntryRow
                {
                    DisplayName = Path.GetFileNameWithoutExtension(f),
                    FullPath = f,
                    TotalRows = total,
                    TranslatedRows = translated,
                    IsUi = string.Equals(Path.GetFileNameWithoutExtension(f),
                        VNTranslationTablePaths.UiTableName, StringComparison.OrdinalIgnoreCase),
                });
            }
        }

        // UI 表可能在旧扁平位置（未迁移）
        string uiPath = VNTranslationTablePaths.FindUiTableAssetPath(config, locale);
        if (uiPath != null && !entries.Any(e => e.IsUi))
        {
            string uiFull = Path.GetFullPath(uiPath);
            VNTranslationTablePaths.ReadTableStats(uiFull, out int total, out int translated);
            entries.Add(new TableEntryRow
            {
                DisplayName = $"{VNTranslationTablePaths.UiTableName}（旧版扁平位置）",
                FullPath = uiFull,
                TotalRows = total,
                TranslatedRows = translated,
                IsUi = true,
            });
        }

        string search = (_search ?? "").Trim();
        if (search.Length > 0)
        {
            entries = entries.Where(e =>
                e.DisplayName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        }

        if (entries.Count > 0)
        {
            var groupTitle = MakeGroupTitle($"翻译表（{entries.Count}）");
            container.Add(groupTitle);

            foreach (var e in entries.OrderBy(x => x.IsUi ? 1 : 0).ThenBy(x => x.DisplayName))
                container.Add(MakeTableEntryCard(e));
        }

        // ---- 缺失模板 ----
        var missingScripts = _scripts
            .Where(s => !s.TablePaths.TryGetValue(locale, out var p) || p == null)
            .Select(s => s.Name)
            .Where(n => search.Length == 0 ||
                        n.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
            .ToList();

        if (missingScripts.Count > 0)
        {
            container.Add(MakeGroupTitle($"缺失模板（{missingScripts.Count}）"));
            foreach (var name in missingScripts)
                container.Add(MakeMissingTemplateRow(name, locale));
        }

        // ---- UI 表缺失提示 ----
        if (uiPath == null)
        {
            var card = GalleryStyles.MakeCard();
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            card.Add(row);

            var label = new Label("UI 界面翻译表未导入（按钮/标题等界面文本）");
            label.style.fontSize = 12;
            label.style.color = GalleryTheme.Hex(GalleryTheme.TextSecondary);
            label.style.flexGrow = 1;
            row.Add(label);

            var btn = new Button(() => SettingsService.OpenProjectSettings("Project/VNovelizer"))
            { text = "前往设置页导入" };
            StyleMiniButton(btn, GalleryTheme.BgHover, GalleryTheme.TextPrimary);
            row.Add(btn);

            AddCardHover(card);
            container.Add(card);
        }

        if (entries.Count == 0 && missingScripts.Count == 0 && uiPath != null)
        {
            container.Add(MakeHintBox("该语言暂无内容。", GalleryTheme.Hex(GalleryTheme.TextMuted)));
        }

        return container;
    }

    private VisualElement MakeGroupTitle(string text)
    {
        var label = new Label(text);
        label.style.fontSize = 12;
        label.style.unityFontStyleAndWeight = FontStyle.Bold;
        label.style.color = GalleryTheme.Hex(GalleryTheme.TextSecondary);
        label.style.marginTop = 10;
        label.style.marginBottom = 6;
        label.style.marginLeft = 4;
        return label;
    }

    private VisualElement MakeTableEntryCard(TableEntryRow entry)
    {
        var card = GalleryStyles.MakeCard();
        card.style.paddingTop = 8;
        card.style.paddingBottom = 8;
        card.style.marginBottom = 8;

        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        card.Add(row);

        var name = new Label(entry.DisplayName + ".csv");
        name.style.fontSize = 12;
        name.style.color = GalleryTheme.Hex(GalleryTheme.TextPrimary);
        name.style.flexGrow = 1;
        row.Add(name);

        // 已译进度
        float ratio = entry.TotalRows > 0 ? (float)entry.TranslatedRows / entry.TotalRows : 0f;
        var progressLabel = new Label(entry.TotalRows > 0
            ? $"已译 {entry.TranslatedRows}/{entry.TotalRows} 条"
            : "空表");
        progressLabel.style.fontSize = 11;
        progressLabel.style.color = entry.TotalRows == 0
            ? GalleryTheme.Hex(GalleryTheme.TextMuted)
            : ratio >= 1f ? GalleryTheme.Hex(GalleryTheme.Success)
            : entry.TranslatedRows == 0 ? GalleryTheme.Hex(GalleryTheme.TextMuted)
            : GalleryTheme.Hex(GalleryTheme.Warning);
        progressLabel.style.marginRight = 10;
        progressLabel.tooltip = "第 3 列（译文列）非空条目数 / 数据行总数";
        row.Add(progressLabel);

        var locateBtn = new Button(() => EditorUtility.RevealInFinder(entry.FullPath)) { text = "定位" };
        StyleMiniButton(locateBtn, GalleryTheme.BgHover, GalleryTheme.TextPrimary);
        locateBtn.style.marginRight = 6;
        row.Add(locateBtn);

        var openBtn = new Button(() => OpenFileWithDefaultApp(entry.FullPath)) { text = "打开" };
        StyleMiniButton(openBtn, GalleryTheme.Accent, Color.white);
        row.Add(openBtn);

        AddCardHover(card);
        return card;
    }

    private VisualElement MakeMissingTemplateRow(string scriptName, string locale)
    {
        var card = GalleryStyles.MakeCard();
        card.style.paddingTop = 6;
        card.style.paddingBottom = 6;
        card.style.marginBottom = 6;

        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        card.Add(row);

        var name = new Label(scriptName);
        name.style.fontSize = 12;
        name.style.color = GalleryTheme.Hex(GalleryTheme.TextSecondary);
        name.style.flexGrow = 1;
        row.Add(name);

        var btn = new Button(() =>
        {
            string generated = GenerateSingle(scriptName, locale);
            if (!string.IsNullOrEmpty(generated))
            {
                VNTranslationAggregator.Aggregate();
                RefreshData();
                RebuildAll();
            }
        })
        { text = "生成模板" };
        StyleMiniButton(btn, new Color(0.90f, 0.55f, 0.25f), Color.white);
        row.Add(btn);

        AddCardHover(card);
        return card;
    }

    // ==================== 视图：旧版扁平表迁移 ====================

    private VisualElement BuildLegacyView()
    {
        var container = MakeScrollContent();

        var intro = GalleryStyles.MakeCard();
        var title = new Label($"检测到 {_legacyFiles.Count} 个旧版扁平翻译表");
        title.style.fontSize = 14;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.color = GalleryTheme.Hex(GalleryTheme.Warning);
        title.style.marginBottom = 8;
        intro.Add(title);

        var desc = new Label(
            "旧版结构把所有翻译表平铺在根目录（如 Chapter1_en.csv）；新版按语言文件夹分类 " +
            "（en/Chapter1.csv），浏览与管理更清晰。\n\n" +
            "· 迁移用 MoveAsset 完成，保留 .meta / GUID，对版本控制友好；\n" +
            "· 目标已存在且译文一致时删除冗余源文件，译文有差异时跳过待人工处理；\n" +
            "· 聚合器同时兼容新旧结构——即使暂不迁移，聚合导入也不受影响。");
        desc.style.fontSize = 12;
        desc.style.color = GalleryTheme.Hex(GalleryTheme.TextSecondary);
        desc.style.whiteSpace = WhiteSpace.Normal;
        intro.Add(desc);

        var migrateBtn = new Button(MigrateNow)
        { text = $"一键迁移到语言文件夹（{_legacyFiles.Count} 个文件）" };
        GalleryStyles.ApplyButton(migrateBtn, GalleryTheme.Accent, true);
        AddButtonFeedback(migrateBtn, GalleryTheme.Hex(GalleryTheme.Accent));
        migrateBtn.style.height = 32;
        migrateBtn.style.marginTop = 10;
        intro.Add(migrateBtn);

        container.Add(intro);

        container.Add(MakeGroupTitle("文件清单"));

        foreach (var f in _legacyFiles)
        {
            var card = GalleryStyles.MakeCard();
            card.style.paddingTop = 6;
            card.style.paddingBottom = 6;
            card.style.marginBottom = 6;

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            card.Add(row);

            var from = new Label(f.FileName);
            from.style.fontSize = 12;
            from.style.color = GalleryTheme.Hex(GalleryTheme.TextPrimary);
            row.Add(from);

            var arrow = new Label("  →  ");
            arrow.style.fontSize = 11;
            arrow.style.color = GalleryTheme.Hex(GalleryTheme.TextMuted);
            row.Add(arrow);

            var to = new Label(f.DestAssetPath);
            to.style.fontSize = 11;
            to.style.color = GalleryTheme.Hex(GalleryTheme.TextSecondary);
            to.style.flexGrow = 1;
            to.style.unityTextAlign = TextAnchor.MiddleLeft;
            row.Add(to);

            var locateBtn = new Button(() => EditorUtility.RevealInFinder(f.SourceFullPath)) { text = "定位" };
            StyleMiniButton(locateBtn, GalleryTheme.BgHover, GalleryTheme.TextPrimary);
            row.Add(locateBtn);

            AddCardHover(card);
            container.Add(card);
        }

        return container;
    }

    private void MigrateNow()
    {
        var config = VNProjectConfig.Instance;
        if (config == null || _legacyFiles.Count == 0)
            return;

        var report = VNTranslationTablePaths.MigrateLegacyTables(config, _legacyFiles);

        // 迁移改变了文件位置：重新聚合 + 全量刷新
        VNTranslationAggregator.Aggregate();
        RefreshData();
        RebuildAll();

        string message = $"迁移完成：移动 {report.Moved} 个" +
                         (report.RemovedDuplicate > 0 ? $"，删除冗余 {report.RemovedDuplicate} 个" : "") +
                         (report.SkippedConflict > 0 ? $"，跳过冲突 {report.SkippedConflict} 个（详见清单）" : "") +
                         (report.Failed > 0 ? $"，失败 {report.Failed} 个" : "") + "\n\n" +
                         string.Join("\n", report.Details);
        EditorUtility.DisplayDialog("迁移旧版翻译表", message, "确定");
    }

    // ==================== 模板生成 ====================

    /// <summary>为单剧本生成单语言模板；剧本 CSV 不存在时提示。返回生成路径（失败 null）。</summary>
    private static string GenerateSingle(string scriptName, string locale)
    {
        var config = VNProjectConfig.Instance;
        if (config == null)
            return null;

        string csvPath = Path.Combine(Path.GetFullPath(config.GetCsvOutputPath()), scriptName + ".csv");
        if (!File.Exists(csvPath))
        {
            EditorUtility.DisplayDialog("提示",
                $"剧本 CSV 不存在：{csvPath}\n请先在剧本管理器点击「转换」。", "确定");
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
    private void GenerateMissingForScript(string scriptName, List<string> missing)
    {
        int count = 0;
        foreach (var locale in missing)
        {
            if (!string.IsNullOrEmpty(GenerateSingle(scriptName, locale)))
                count++;
        }

        if (count > 0)
            VNTranslationAggregator.Aggregate();

        RefreshData();
        RebuildAll();
    }

    /// <summary>为全部剧本补全全部缺失模板（只生成不存在的文件，幂等安全）并聚合。</summary>
    private void GenerateAllMissing()
    {
        var config = VNProjectConfig.Instance;
        if (config == null)
            return;

        if (_targetLocales.Length == 0)
        {
            EditorUtility.DisplayDialog("提示",
                "未配置目标语言。请到 Edit → Project Settings → VNovelizer → 本地化 勾选目标语言。", "确定");
            return;
        }

        string csvFolder = Path.GetFullPath(config.GetCsvOutputPath());
        int count = 0;
        try
        {
            for (int i = 0; i < _scripts.Count; i++)
            {
                var script = _scripts[i];
                if (!File.Exists(script.CsvFullPath))
                    continue;

                string csvText = File.ReadAllText(script.CsvFullPath);
                foreach (var locale in _targetLocales)
                {
                    if (VNTranslationTablePaths.TableExists(config, script.Name, locale))
                        continue; // 只补缺失（新结构或旧扁平任一存在）

                    if (EditorUtility.DisplayCancelableProgressBar(
                            "生成翻译表模板", $"{script.Name} → {locale}",
                            (float)i / Math.Max(1, _scripts.Count)))
                        break;

                    string path = VNTranslationTemplateGenerator.GenerateTemplate(script.Name, locale, csvText);
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
        RefreshData();
        RebuildAll();

        EditorUtility.DisplayDialog("完成", $"已补全 {count} 张缺失模板，并已执行导入。", "确定");
    }

    // ==================== 导入结果 / 状态栏 ====================

    private void AggregateNow()
    {
        VNTranslationAggregator.Aggregate();
        RefreshResultSection();
        RefreshStatusBar();
    }

    private void RefreshResultSection()
    {
        if (_resultSummaryLabel == null)
            return;

        var result = VNTranslationAggregator.LastResult;
        if (result == null)
        {
            _resultSummaryLabel.text = "尚未执行过导入（保存翻译表自动导入）";
            _resultSummaryLabel.style.color = GalleryTheme.Hex(GalleryTheme.TextMuted);
        }
        else if (!result.Success)
        {
            _resultSummaryLabel.text = "最近导入失败";
            _resultSummaryLabel.style.color = GalleryTheme.Hex(GalleryTheme.Danger);
        }
        else
        {
            string counts = string.Join(" · ", result.EntryCounts.Select(kv => $"{kv.Key} {kv.Value} 条"));
            _resultSummaryLabel.text = "成功" + (counts.Length > 0 ? " · " + counts : "") +
                                       (result.Conflicts.Count > 0 ? $" · 冲突 {result.Conflicts.Count}" : "") +
                                       (result.OrphanSkipped.Count > 0 ? $" · 跳过 {result.OrphanSkipped.Count}" : "");
            _resultSummaryLabel.style.color = result.Conflicts.Count > 0
                ? GalleryTheme.Hex(GalleryTheme.Warning)
                : GalleryTheme.Hex(GalleryTheme.Success);
        }

        _resultDetail.Clear();
        if (result == null)
        {
            _resultDetail.Add(MakeResultLine(
                "保存翻译表后自动导入，或点击标题栏「立即导入翻译表」。", GalleryTheme.TextMuted));
            return;
        }

        if (!result.Success)
        {
            _resultDetail.Add(MakeResultLine("导入失败，详情见 Console。", GalleryTheme.Danger));
        }
        else if (result.EntryCounts.Count > 0)
        {
            _resultDetail.Add(MakeResultLine(
                "写入：" + string.Join("，", result.EntryCounts.Select(kv => $"{kv.Key} {kv.Value} 条")),
                GalleryTheme.TextSecondary));
        }

        if (result.Conflicts.Count > 0)
        {
            _resultDetail.Add(MakeResultLine(
                $"key 冲突 {result.Conflicts.Count} 处（后加载的表覆盖先加载的）：", GalleryTheme.Warning));
            foreach (var c in result.Conflicts)
            {
                _resultDetail.Add(MakeResultLine(
                    $"[{c.Locale}] {c.Key}  {Path.GetFileName(c.FileA)} <-> {Path.GetFileName(c.FileB)}",
                    GalleryTheme.TextSecondary));
            }
        }

        if (result.OrphanSkipped.Count > 0)
        {
            _resultDetail.Add(MakeResultLine(
                $"已跳过 {result.OrphanSkipped.Count} 个失效 key（未导入）——" +
                "这些翻译行对应的剧本行已删除/改动，译文仍保留在翻译 CSV 中，请人工迁移到新 key 后删除旧行：",
                GalleryTheme.Warning));
            foreach (var s in result.OrphanSkipped)
                _resultDetail.Add(MakeResultLine("· " + s, GalleryTheme.TextSecondary));
        }

        if (result.Warnings.Count > 0)
        {
            _resultDetail.Add(MakeResultLine($"警告 {result.Warnings.Count} 条：", GalleryTheme.Warning));
            foreach (var w in result.Warnings)
                _resultDetail.Add(MakeResultLine("· " + w, GalleryTheme.TextSecondary));
        }
    }

    private Label MakeResultLine(string text, string colorHex)
    {
        var label = new Label(text);
        label.style.fontSize = 11;
        label.style.color = GalleryTheme.Hex(colorHex);
        label.style.whiteSpace = WhiteSpace.Normal;
        return label;
    }

    private void RefreshStatusBar()
    {
        if (_statusLabel == null)
            return;

        if (!_localizationEnabled)
        {
            _statusLabel.text = "本地化未启用 · Project Settings → VNovelizer";
            return;
        }

        var config = VNProjectConfig.Instance;
        int total = _scripts.Count * _targetLocales.Length;
        int existing = _scripts.Count(s => _targetLocales.Any(t =>
            s.TablePaths.TryGetValue(t, out var p) && p != null));

        string targets = _targetLocales.Length > 0 ? string.Join(", ", _targetLocales) : "（未配置）";
        _statusLabel.text =
            $"{_scripts.Count} 个剧本 · 翻译表 {existing}/{total} · 源语言 {config.SourceLocale} · 目标语言 {targets}" +
            (_legacyFiles.Count > 0 ? $" · 旧版扁平表待迁移 {_legacyFiles.Count} 个" : "");
    }

    // ==================== 通用辅助 ====================

    /// <summary>内容区滚动容器（内边距统一）。</summary>
    private static ScrollView MakeScrollContent()
    {
        var scroll = new ScrollView(ScrollViewMode.Vertical);
        scroll.style.flexGrow = 1;
        scroll.style.flexShrink = 1;
        scroll.style.minHeight = 0;
        scroll.style.paddingTop = 10;
        scroll.style.paddingLeft = 12;
        scroll.style.paddingRight = 12;
        scroll.style.paddingBottom = 12;
        return scroll;
    }

    private static Label MakeBadge(string text, Color bg)
    {
        var badge = new Label(text);
        badge.style.fontSize = 10;
        badge.style.unityFontStyleAndWeight = FontStyle.Bold;
        badge.style.color = Color.white;
        badge.style.backgroundColor = bg;
        badge.style.paddingLeft = 8;
        badge.style.paddingRight = 8;
        badge.style.paddingTop = 2;
        badge.style.paddingBottom = 2;
        badge.style.marginRight = 8;
        ResourceStyles.SetRadius(badge, 8);
        return badge;
    }

    private static VisualElement MakeHintBox(string text, Color accentColor)
    {
        var box = new VisualElement();
        box.style.backgroundColor = new Color(accentColor.r, accentColor.g, accentColor.b, 0.12f);
        box.style.borderTopWidth = 1;
        box.style.borderBottomWidth = 1;
        box.style.borderLeftWidth = 1;
        box.style.borderRightWidth = 1;
        ResourceStyles.SetRadius(box, 6);
        box.style.borderTopColor = new Color(accentColor.r, accentColor.g, accentColor.b, 0.4f);
        box.style.borderBottomColor = new Color(accentColor.r, accentColor.g, accentColor.b, 0.4f);
        box.style.borderLeftColor = new Color(accentColor.r, accentColor.g, accentColor.b, 0.4f);
        box.style.borderRightColor = new Color(accentColor.r, accentColor.g, accentColor.b, 0.4f);
        box.style.paddingTop = 10;
        box.style.paddingBottom = 10;
        box.style.paddingLeft = 12;
        box.style.paddingRight = 12;
        box.style.marginBottom = 10;

        var label = new Label(text);
        label.style.fontSize = 12;
        label.style.color = accentColor;
        label.style.whiteSpace = WhiteSpace.Normal;
        box.Add(label);
        return box;
    }

    /// <summary>小型操作按钮（卡片/行内）：GalleryTheme 十六进制色值重载。</summary>
    private static void StyleMiniButton(Button btn, string bgHex, string fgHex)
        => StyleMiniButton(btn, GalleryTheme.Hex(bgHex), GalleryTheme.Hex(fgHex));

    /// <summary>小型操作按钮（卡片/行内）：背景十六进制 + 前景 Color 重载。</summary>
    private static void StyleMiniButton(Button btn, string bgHex, Color fg)
        => StyleMiniButton(btn, GalleryTheme.Hex(bgHex), fg);

    /// <summary>小型操作按钮（卡片/行内）。</summary>
    private static void StyleMiniButton(Button btn, Color bg, Color fg)
    {
        btn.style.backgroundColor = bg;
        btn.style.color = fg;
        btn.style.fontSize = 11;
        btn.style.height = 22;
        btn.style.paddingLeft = 10;
        btn.style.paddingRight = 10;
        ResourceStyles.SetRadius(btn, 4);
        btn.style.borderTopWidth = 0;
        btn.style.borderBottomWidth = 0;
        btn.style.borderLeftWidth = 0;
        btn.style.borderRightWidth = 0;
        AddButtonFeedback(btn, bg);
    }

    /// <summary>
    /// 按钮三态交互反馈：悬停提亮、按下变暗（悬停中按下保持按下色）。
    /// 状态机闭包随元素销毁，无泄漏；EditorWindow 生命周期内安全。
    /// </summary>
    private static void AddButtonFeedback(Button btn, Color normal)
    {
        Color hover = Color.Lerp(normal, Color.white, 0.15f);
        Color pressed = Color.Lerp(normal, Color.black, 0.30f);

        bool hovering = false, pressing = false;
        void Update()
        {
            btn.style.backgroundColor = pressing ? pressed : (hovering ? hover : normal);
        }

        btn.RegisterCallback<MouseEnterEvent>(_ => { hovering = true; Update(); });
        btn.RegisterCallback<MouseLeaveEvent>(_ => { hovering = false; pressing = false; Update(); });
        btn.RegisterCallback<PointerDownEvent>(_ => { pressing = true; Update(); });
        btn.RegisterCallback<PointerUpEvent>(_ => { pressing = false; Update(); });
    }

    /// <summary>卡片悬停反馈：上边框提亮为强调色（可点击区域的视觉暗示）。</summary>
    private static void AddCardHover(VisualElement card)
    {
        card.RegisterCallback<MouseEnterEvent>(_ =>
            card.style.borderTopColor = GalleryTheme.Hex(GalleryTheme.AccentDim));
        card.RegisterCallback<MouseLeaveEvent>(_ =>
            card.style.borderTopColor = GalleryTheme.Hex(GalleryTheme.Border));
    }

    /// <summary>用系统默认程序打开文件（CSV 关联 Excel）。</summary>
    private static void OpenFileWithDefaultApp(string fullPath)
    {
        try
        {
            ProcessEx.Start(new System.Diagnostics.ProcessStartInfo(fullPath) { UseShellExecute = true });
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[VNLocalizationWindow] 打开文件失败：{fullPath}（{e.Message}），改为定位文件。");
            EditorUtility.RevealInFinder(fullPath);
        }
    }

    // ==================== 翻译表目录修改 ====================

    /// <summary>弹系统文件夹选择框修改翻译表目录（校验 Assets 相对路径）。</summary>
    private void PickTranslationFolder(Label pathLabel)
    {
        var config = VNProjectConfig.Instance;
        if (config == null)
        {
            EditorUtility.DisplayDialog("提示",
                "未找到 VNProjectConfig。请先打开 Edit → Project Settings → VNovelizer 创建配置。", "确定");
            return;
        }

        string picked = EditorUtility.OpenFolderPanel(
            "选择翻译表目录", Path.GetFullPath(config.GetTranslationFolderPath()), "");
        if (string.IsNullOrEmpty(picked))
            return;

        string rel = VNTranslationTablePaths.ToAssetsRelativePath(picked);
        if (rel == null)
        {
            EditorUtility.DisplayDialog("无效路径",
                "翻译表目录必须是工程内 Assets 下的相对路径（如 Assets/Localization/Translations）。", "确定");
            return;
        }

        if (rel == config.GetTranslationFolderPath())
            return;

        config.TranslationFolder = rel;
        SaveConfigAsset(config);

        pathLabel.text = config.GetTranslationFolderPath();
        RefreshData();
        RebuildAll();
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
}
