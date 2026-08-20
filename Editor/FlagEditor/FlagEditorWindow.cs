using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Flag 编辑器窗口（VNovelizer → Flag 编辑器）。
/// 管理 FlagRegistry 注册表：按 Group 分组折叠显示、增删改查、合法性校验；
/// Play Mode 下支持查看与临时修改运行时 Flag 值（经 FlagService，立即生效）。
/// UI 风格与资源管理器/画廊编辑器保持一致（ResourceStyles / GalleryTheme）。
/// </summary>
public class FlagEditorWindow : EditorWindow
{
    private FlagRegistry registry;
    private string registryAssetPath;

    private TextField searchField;
    private VisualElement groupContainer;
    private VisualElement detailPanel;
    private Label statusLabel;

    // ---- 运行时调试区（独立底部条，非 Play 隐藏，不参与详情面板重建）----
    private VisualElement runtimeBar;
    private Label runtimeCurrentLabel;
    private TextField runtimeValueField;
    private Label runtimeScopeLabel;
    private double lastRuntimeRefresh = -1;

    private string searchFilter = "";
    private FlagRegistry.FlagDefinition selected;
    private bool lastPlaying;

    // 分组折叠状态：group -> true 表示折叠（未记录 = 展开）
    private readonly Dictionary<string, bool> collapsedGroups = new Dictionary<string, bool>();
    // 校验结果：name -> 错误描述
    private readonly Dictionary<string, string> validationErrors = new Dictionary<string, string>();

    private const string UngroupedLabel = "（未分组）";

    [MenuItem("VNovelizer/Flag 编辑器 (Flag Editor)", false, 16)]
    public static void ShowWindow()
    {
        var wnd = GetWindow<FlagEditorWindow>();
        wnd.titleContent = new GUIContent("Flag 编辑器");
        wnd.minSize = new Vector2(860, 520);
    }

    public void CreateGUI()
    {
        LoadRegistry();
        BuildUI();
        RefreshAll();
    }

    private void Update()
    {
        // Play 状态翻转：切换调试区显示/隐藏并同步当前值（替代 playModeStateChanged 订阅，
        // EditorWindow.Update 在编辑器与播放模式均每帧调用，无版本差异）
        if (Application.isPlaying != lastPlaying)
        {
            lastPlaying = Application.isPlaying;
            RefreshRuntimeSection();
        }

        // Play Mode 下节流刷新运行时值显示（0.3s，只更新文本，绝不覆盖用户正在输入的 value）
        if (Application.isPlaying && selected != null && EditorApplication.timeSinceStartup - lastRuntimeRefresh > 0.3)
        {
            lastRuntimeRefresh = EditorApplication.timeSinceStartup;
            UpdateRuntimeDisplay();
        }
    }

    // ==================== 资产定位 / 创建 ====================

    private void LoadRegistry()
    {
        registry = null;
        registryAssetPath = null;

        var guids = AssetDatabase.FindAssets("t:FlagRegistry");
        if (guids.Length > 0)
        {
            registryAssetPath = AssetDatabase.GUIDToAssetPath(guids[0]);
            registry = AssetDatabase.LoadAssetAtPath<FlagRegistry>(registryAssetPath);
        }
    }

    private void CreateRegistryAsset()
    {
        // 默认落点（工作区优先，旧版目录存在时沿用）
        string defaultDir = VNProjectPaths.ResourceKeyToFolder(VNResourceKeys.KeyToCategory(FlagService.DefaultRegistryPath));
        VNProjectPaths.EnsureFolder(defaultDir);

        // 保存位置由用户自选（SaveFilePanelInProject 限定项目内）：
        // 运行时按固定资源键索引（注册写地址），与文件保存位置/文件名无关
        string path = EditorUtility.SaveFilePanelInProject(
            "创建 Flag 注册表", "VNFlagRegistry", "asset",
            "选择 Flag 注册表的保存位置（保存在项目内任意位置均可，运行时索引不受影响）。",
            defaultDir);
        if (string.IsNullOrEmpty(path)) return; // 用户取消

        if (AssetDatabase.LoadAssetAtPath<Object>(path) != null)
        {
            EditorUtility.DisplayDialog("已存在", $"目标位置已有同名资产：\n{path}", "确定");
            return;
        }

        var asset = CreateInstance<FlagRegistry>();
        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssets();

        // 注册进 Addressables（资源键 = 运行时 FlagService 查询键；未初始化的项目自动跳过）
        VNAddressablesRegistrar.RegisterAssetAtPath(path, FlagService.DefaultRegistryPath);

        registry = asset;
        registryAssetPath = path;
        Debug.Log($"[FlagEditor] 已创建 Flag 注册表: {path}");
    }

    // ==================== UI 构建 ====================

    private void BuildUI()
    {
        var root = rootVisualElement;
        root.Clear();
        root.style.backgroundColor = ResourceStyles.Bg;

        // ---- 顶部工具栏 ----
        var toolbar = new VisualElement();
        toolbar.style.flexDirection = FlexDirection.Row;
        toolbar.style.backgroundColor = ResourceStyles.Toolbar;
        toolbar.style.paddingTop = 5;
        toolbar.style.paddingBottom = 5;
        toolbar.style.paddingLeft = 6;
        toolbar.style.paddingRight = 6;
        toolbar.style.borderBottomWidth = 1;
        toolbar.style.borderBottomColor = ResourceStyles.CardBorder;
        root.Add(toolbar);

        if (registry == null)
        {
            var hint = new Label("未找到 Flag 注册表。剧本中的 jumpif/loadscriptif 等条件命令仍可运行（兼容模式），但无法享受类型校验、作用域管理与默认值复位。")
            {
                style = { color = ResourceStyles.TextSecondary, whiteSpace = WhiteSpace.Normal, marginRight = 12, alignSelf = Align.Center, flexGrow = 1 }
            };
            toolbar.Add(hint);

            var createBtn = new Button(CreateAndReload) { text = "创建注册表" };
            ResourceStyles.StylePrimary(createBtn, ResourceStyles.Accent);
            toolbar.Add(createBtn);
            return;
        }

        var addBtn = new Button(AddFlag) { text = "＋ 新建" };
        ResourceStyles.StylePrimary(addBtn, ResourceStyles.Accent);
        toolbar.Add(addBtn);

        var deleteBtn = new Button(DeleteSelected) { text = "删除" };
        ResourceStyles.StyleNormal(deleteBtn);
        deleteBtn.style.color = ResourceStyles.DangerNormal;
        deleteBtn.style.marginLeft = 6;
        toolbar.Add(deleteBtn);

        // 展开 / 折叠全部分组
        var expandBtn = new Button(() => { collapsedGroups.Clear(); RebuildGroupList(); }) { text = "全部展开" };
        ResourceStyles.StyleNormal(expandBtn);
        expandBtn.style.marginLeft = 6;
        toolbar.Add(expandBtn);

        var collapseBtn = new Button(CollapseAllGroups) { text = "全部折叠" };
        ResourceStyles.StyleNormal(collapseBtn);
        collapseBtn.style.marginLeft = 6;
        toolbar.Add(collapseBtn);

        searchField = new TextField("搜索") { value = searchFilter };
        searchField.style.width = 200;
        searchField.style.marginLeft = 12;
        searchField.style.color = ResourceStyles.TextPrimary;
        searchField.RegisterValueChangedCallback(evt =>
        {
            searchFilter = evt.newValue;
            RebuildGroupList();
        });
        toolbar.Add(searchField);

        toolbar.Add(ResourceStyles.MakeSpacer());

        var pathLabel = new Label(registryAssetPath) { style = { color = ResourceStyles.TextSecondary, alignSelf = Align.Center, fontSize = 10 } };
        toolbar.Add(pathLabel);

        // ---- 主区域：左分组列表 + 右详情 ----
        var split = new TwoPaneSplitView(0, 340, TwoPaneSplitViewOrientation.Horizontal);
        split.style.flexGrow = 1;
        root.Add(split);

        // 左：按 Group 折叠的分组列表
        var leftPane = new VisualElement();
        leftPane.style.backgroundColor = ResourceStyles.Sidebar;
        split.Add(leftPane);

        var leftScroll = new ScrollView(ScrollViewMode.Vertical);
        leftScroll.style.flexGrow = 1;
        leftScroll.style.paddingTop = 4;
        leftScroll.style.paddingBottom = 4;
        leftScroll.style.paddingLeft = 4;
        leftScroll.style.paddingRight = 4;
        leftPane.Add(leftScroll);
        groupContainer = leftScroll;

        // 右：详情面板（ScrollView 承载，内容超长可滚动，不再被窗口裁切）
        detailPanel = new ScrollView(ScrollViewMode.Vertical);
        detailPanel.style.paddingTop = 10;
        detailPanel.style.paddingBottom = 10;
        detailPanel.style.paddingLeft = 16;
        detailPanel.style.paddingRight = 16;
        detailPanel.style.flexGrow = 1;
        split.Add(detailPanel);

        // ---- 运行时调试条（状态栏上方，仅 Play Mode 且选中 Flag 时显示）----
        // 布局策略：纯 flex 流，按钮放在输入框右侧自然排列，避免 absolute 与 marginRight 双重防御带来的对齐不确定性。
        // 容器高度由内容撑开，不裁切；所有控件明示最小/最大宽度，窄窗口下按比例收缩但不消失。
        runtimeBar = new VisualElement();
        runtimeBar.style.display = DisplayStyle.None;
        runtimeBar.style.backgroundColor = ResourceStyles.Toolbar;
        runtimeBar.style.borderTopWidth = 1;
        runtimeBar.style.borderTopColor = ResourceStyles.CardBorder;
        runtimeBar.style.paddingTop = 6;
        runtimeBar.style.paddingBottom = 6;
        runtimeBar.style.paddingLeft = 10;
        runtimeBar.style.paddingRight = 10;
        runtimeBar.style.flexShrink = 0;
        root.Add(runtimeBar);

        // 第一行：状态点 + 当前值（最大宽度 220）+ 输入框（吃剩余）+ 应用按钮（固定 64）
        var debugRow = new VisualElement();
        debugRow.style.flexDirection = FlexDirection.Row;
        debugRow.style.alignItems = Align.Center;
        debugRow.style.flexShrink = 0;
        runtimeBar.Add(debugRow);

        var dot = new Label("●");
        dot.style.color = ResourceStyles.AccentSuccess;
        dot.style.fontSize = 11;
        dot.style.marginRight = 6;
        dot.style.flexShrink = 0;
        debugRow.Add(dot);

        // 当前值标签：定宽 + ellipsis 截断；不再参与挤压按钮（按钮在 flex 流末位）
        runtimeCurrentLabel = new Label("当前值: -");
        runtimeCurrentLabel.style.color = ResourceStyles.TextPrimary;
        runtimeCurrentLabel.style.fontSize = 12;
        runtimeCurrentLabel.style.width = 220;
        runtimeCurrentLabel.style.maxWidth = 220;
        runtimeCurrentLabel.style.minWidth = 60;
        runtimeCurrentLabel.style.flexShrink = 1;
        runtimeCurrentLabel.style.overflow = Overflow.Hidden;
        runtimeCurrentLabel.style.textOverflow = TextOverflow.Ellipsis;
        runtimeCurrentLabel.style.marginRight = 8;
        debugRow.Add(runtimeCurrentLabel);

        // 输入框：吃剩余宽度，限定最小可识别宽度（避免被压缩成 0 不可读）
        runtimeValueField = new TextField();
        runtimeValueField.style.flexGrow = 1;
        runtimeValueField.style.flexShrink = 1;
        runtimeValueField.style.minWidth = 80;
        runtimeValueField.style.color = ResourceStyles.TextPrimary;
        runtimeValueField.style.marginRight = 8;
        runtimeValueField.RegisterValueChangedCallback(_ => MarkDebugDirty());
        debugRow.Add(runtimeValueField);

        // 应用按钮：在 flex 流末尾，固定宽度不会被任何兄弟节点挤压
        var applyBtn = new Button(ApplyRuntimeValue) { text = "应用" };
        ResourceStyles.StylePrimary(applyBtn, ResourceStyles.Accent);
        applyBtn.style.flexShrink = 0;
        applyBtn.style.width = 64;
        applyBtn.style.minWidth = 64;
        debugRow.Add(applyBtn);

        // 第二行：写入位置说明（占据全部宽度，whiteSpace.Normal 自动换行，不再溢出）
        runtimeScopeLabel = new Label("");
        runtimeScopeLabel.style.color = ResourceStyles.TextSecondary;
        runtimeScopeLabel.style.fontSize = 10;
        runtimeScopeLabel.style.marginTop = 3;
        runtimeScopeLabel.style.whiteSpace = WhiteSpace.Normal;
        runtimeScopeLabel.style.flexShrink = 0;
        runtimeBar.Add(runtimeScopeLabel);

        // ---- 底部状态栏 ----
        var statusBar = new VisualElement();
        statusBar.style.height = ResourceStyles.StatusBarHeight;
        statusBar.style.backgroundColor = ResourceStyles.StatusBar;
        statusBar.style.borderTopWidth = 1;
        statusBar.style.borderTopColor = ResourceStyles.CardBorder;
        statusBar.style.flexDirection = FlexDirection.Row;
        statusBar.style.alignItems = Align.Center;
        statusBar.style.paddingLeft = 8;
        root.Add(statusBar);

        statusLabel = new Label("");
        statusLabel.style.color = ResourceStyles.TextSecondary;
        statusLabel.style.fontSize = 10;
        statusBar.Add(statusLabel);
    }

    private void CreateAndReload()
    {
        CreateRegistryAsset();
        BuildUI();
        RefreshAll();
    }

    private void CollapseAllGroups()
    {
        foreach (var g in GetFiltered().Select(d => d.Group ?? "").Distinct())
        {
            collapsedGroups[g] = true;
        }
        RebuildGroupList();
    }

    // ==================== 分组列表 ====================

    private List<FlagRegistry.FlagDefinition> GetFiltered()
    {
        if (registry == null || registry.Definitions == null) return new List<FlagRegistry.FlagDefinition>();
        IEnumerable<FlagRegistry.FlagDefinition> defs = registry.Definitions;
        if (!string.IsNullOrEmpty(searchFilter))
        {
            string f = searchFilter.ToLower();
            defs = defs.Where(d => (d.Name != null && d.Name.ToLower().Contains(f)) || (d.Group != null && d.Group.ToLower().Contains(f)));
        }
        return defs.OrderBy(d => d.Name).ToList();
    }

    /// <summary>
    /// 重建左侧分组折叠列表（Flag 数量级小，全量重建无压力）
    /// </summary>
    private void RebuildGroupList()
    {
        if (groupContainer == null) return;
        groupContainer.Clear();
        if (registry == null) return;

        var filtered = GetFiltered();
        bool searching = !string.IsNullOrEmpty(searchFilter);

        // 按组聚合：组名升序，未分组（空串）自然排在最前
        var groups = filtered
            .GroupBy(d => d.Group ?? "")
            .OrderBy(g => g.Key, System.StringComparer.Ordinal);

        foreach (var g in groups)
        {
            var defs = g.ToList();
            bool collapsed = !searching && collapsedGroups.TryGetValue(g.Key, out bool c) && c;

            groupContainer.Add(MakeGroupHeader(g.Key, defs, collapsed));
            if (collapsed) continue;

            foreach (var def in defs)
            {
                groupContainer.Add(MakeFlagRow(def));
            }
        }

        if (filtered.Count == 0)
        {
            groupContainer.Add(new Label(searching ? "无匹配项" : "暂无 Flag，点击「＋ 新建」创建")
            {
                style = { color = ResourceStyles.TextSecondary, marginTop = 12, marginLeft = 8 }
            });
        }
    }

    private VisualElement MakeGroupHeader(string group, List<FlagRegistry.FlagDefinition> defs, bool collapsed)
    {
        var header = new VisualElement();
        header.style.flexDirection = FlexDirection.Row;
        header.style.alignItems = Align.Center;
        header.style.height = 28;
        header.style.paddingLeft = 6;
        header.style.paddingRight = 8;
        header.style.marginBottom = 2;
        header.style.backgroundColor = ResourceStyles.Card;
        ResourceStyles.SetRadius(header, ResourceStyles.ButtonRadius);

        var arrow = new Label(collapsed ? "▶" : "▼");
        arrow.style.fontSize = 9;
        arrow.style.color = ResourceStyles.TextSecondary;
        arrow.style.width = 14;
        header.Add(arrow);

        string groupName = string.IsNullOrEmpty(group) ? UngroupedLabel : group;
        var title = new Label(groupName);
        title.style.color = string.IsNullOrEmpty(group) ? ResourceStyles.TextSecondary : ResourceStyles.TextPrimary;
        title.style.fontSize = 12;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        header.Add(title);

        var count = new Label(defs.Count.ToString());
        count.style.color = ResourceStyles.TextSecondary;
        count.style.fontSize = 10;
        count.style.marginLeft = 6;
        header.Add(count);

        // 组内存在校验错误时在组头提示
        if (defs.Any(d => validationErrors.ContainsKey(d.Name ?? "")))
        {
            var warn = new Label("⚠");
            warn.style.color = ResourceStyles.DangerNormal;
            warn.style.fontSize = 12;
            warn.style.marginLeft = 4;
            warn.tooltip = "组内存在校验错误（展开查看）";
            header.Add(warn);
        }

        header.Add(ResourceStyles.MakeSpacer());

        header.RegisterCallback<ClickEvent>(_ =>
        {
            collapsedGroups[group] = !collapsed;
            RebuildGroupList();
        });
        header.RegisterCallback<MouseEnterEvent>(_ => header.style.backgroundColor = ResourceStyles.CardHover);
        header.RegisterCallback<MouseLeaveEvent>(_ => header.style.backgroundColor = ResourceStyles.Card);

        return header;
    }

    private VisualElement MakeFlagRow(FlagRegistry.FlagDefinition def)
    {
        bool isSelected = def == selected;

        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.height = 30;
        row.style.paddingLeft = 22; // 相对组头缩进
        row.style.paddingRight = 8;
        row.style.marginBottom = 1;
        row.style.backgroundColor = isSelected ? ResourceStyles.CardSelected : Color.clear;
        ResourceStyles.SetRadius(row, ResourceStyles.ButtonRadius);
        row.tooltip = BuildRowTooltip(def);

        var nameLabel = new Label(def.Name);
        nameLabel.style.color = ResourceStyles.TextPrimary;
        nameLabel.style.fontSize = 12;
        nameLabel.style.flexGrow = 1;
        row.Add(nameLabel);

        var typeBadge = new Label(def.Type.ToString());
        typeBadge.style.color = GetTypeColor(def.Type);
        typeBadge.style.fontSize = 10;
        typeBadge.style.unityTextAlign = TextAnchor.MiddleRight;
        typeBadge.style.width = 48;
        row.Add(typeBadge);

        var errorBadge = new Label("⚠");
        errorBadge.style.color = ResourceStyles.DangerNormal;
        errorBadge.style.fontSize = 12;
        errorBadge.style.width = 16;
        if (validationErrors.TryGetValue(def.Name ?? "", out string err))
        {
            errorBadge.tooltip = err;
        }
        else
        {
            errorBadge.style.display = DisplayStyle.None;
        }
        row.Add(errorBadge);

        // 单击选中 / 双击重命名
        row.RegisterCallback<ClickEvent>(evt =>
        {
            if (evt.clickCount == 2)
            {
                StartInlineRename(row, def);
            }
            else if (selected != def)
            {
                selected = def;
                RefreshAll();
            }
        });

        // 悬停高亮
        row.RegisterCallback<MouseEnterEvent>(_ =>
        {
            if (selected != def) row.style.backgroundColor = ResourceStyles.CardHover;
        });
        row.RegisterCallback<MouseLeaveEvent>(_ =>
        {
            row.style.backgroundColor = (selected == def) ? ResourceStyles.CardSelected : Color.clear;
        });

        // 右键菜单
        row.AddManipulator(new ContextualMenuManipulator(evt =>
        {
            evt.menu.AppendAction("重命名", _ => StartInlineRename(row, def));
            evt.menu.AppendAction("复制", _ => DuplicateFlag(def));
            evt.menu.AppendSeparator();
            evt.menu.AppendAction("删除", _ =>
            {
                selected = def;
                DeleteSelected();
            });
        }));

        return row;
    }

    private string BuildRowTooltip(FlagRegistry.FlagDefinition def)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(def.Type).Append(" · ").Append(def.Scope);
        if (!string.IsNullOrEmpty(def.DefaultValue)) sb.Append(" · 默认: ").Append(def.DefaultValue);
        if (!string.IsNullOrEmpty(def.Comment)) sb.Append("\n").Append(def.Comment);
        sb.Append("\n双击重命名 · 右键更多操作");
        return sb.ToString();
    }

    private static Color GetTypeColor(FlagType type)
    {
        switch (type)
        {
            case FlagType.Bool: return ResourceStyles.Accent;
            case FlagType.Int: return ResourceStyles.AccentSuccess;
            case FlagType.Float: return GalleryTheme.Hex(GalleryTheme.Warning);
            default: return ResourceStyles.TextSecondary;
        }
    }

    // ==================== 内联重命名 ====================

    private void StartInlineRename(VisualElement row, FlagRegistry.FlagDefinition def)
    {
        row.Clear();
        var field = new TextField { value = def.Name };
        field.style.flexGrow = 1;
        field.style.color = ResourceStyles.TextPrimary;
        row.Add(field);
        field.Focus();
        field.SelectAll();

        bool committed = false;
        System.Action commit = () =>
        {
            if (committed) return;
            committed = true;
            string newName = field.value.Trim();
            if (!string.IsNullOrEmpty(newName) && newName != def.Name)
            {
                Undo.RecordObject(registry, "Rename Flag");
                def.Name = newName;
                SaveRegistry();
            }
            RefreshAll();
        };

        field.RegisterCallback<FocusOutEvent>(_ => commit());
        field.RegisterCallback<KeyDownEvent>(evt =>
        {
            if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
            {
                commit();
                evt.StopPropagation();
            }
            else if (evt.keyCode == KeyCode.Escape)
            {
                committed = true;
                RefreshAll();
                evt.StopPropagation();
            }
        });
    }

    // ==================== 详情面板 ====================

    private void RefreshDetail()
    {
        if (detailPanel == null) return;
        detailPanel.Clear();

        if (selected == null)
        {
            detailPanel.Add(new Label("在左侧选择或新建一个 Flag") { style = { color = ResourceStyles.TextSecondary } });
            return;
        }

        // 标题 + 分组面包屑
        var title = new Label(selected.Name);
        title.style.fontSize = 16;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.color = ResourceStyles.TextPrimary;
        detailPanel.Add(title);

        var breadcrumb = new Label(string.IsNullOrEmpty(selected.Group) ? UngroupedLabel : selected.Group);
        breadcrumb.style.color = ResourceStyles.TextSecondary;
        breadcrumb.style.fontSize = 10;
        breadcrumb.style.marginBottom = 10;
        detailPanel.Add(breadcrumb);

        // 字段编辑（经 Undo 记录）
        detailPanel.Add(MakeTextField("名称 Name", selected.Name, v =>
        {
            Undo.RecordObject(registry, "Edit Flag Name");
            selected.Name = v;
            SaveAndRefreshList();
        }));

        detailPanel.Add(MakeEnumField<FlagType>("类型 Type", selected.Type, v =>
        {
            Undo.RecordObject(registry, "Edit Flag Type");
            selected.Type = (FlagType)v;
            SaveAndRefreshList();
        }));

        detailPanel.Add(MakeEnumField<FlagScope>("作用域 Scope", selected.Scope, v =>
        {
            Undo.RecordObject(registry, "Edit Flag Scope");
            selected.Scope = (FlagScope)v;
            SaveAndRefreshList();
        }));

        var scopeHint = new Label(selected.Scope == FlagScope.Global
            ? "Global：跨存档持久（global_data.json），读档不回退。适合好感度、累计解锁等。"
            : "Save：随存档快照保存，读档回退，新游戏复位为默认值。适合章节进度、分支标记等。")
        {
            style = { color = ResourceStyles.TextSecondary, fontSize = 10, whiteSpace = WhiteSpace.Normal, marginBottom = 8 }
        };
        detailPanel.Add(scopeHint);

        detailPanel.Add(MakeTextField("默认值 Default", selected.DefaultValue, v =>
        {
            Undo.RecordObject(registry, "Edit Flag Default");
            selected.DefaultValue = v;
            SaveRegistry();
        }));

        detailPanel.Add(MakeTextField("分组 Group", selected.Group, v =>
        {
            Undo.RecordObject(registry, "Edit Flag Group");
            selected.Group = v;
            SaveAndRefreshList();
        }));

        detailPanel.Add(MakeTextField("备注 Comment", selected.Comment, v =>
        {
            Undo.RecordObject(registry, "Edit Flag Comment");
            selected.Comment = v;
            SaveRegistry();
        }, multiline: true));

        // 运行时调试区在独立底部条（不随详情面板重建），此处仅同步显示状态
        RefreshRuntimeSection();
    }

    // ==================== 运行时调试（独立底部条） ====================

    /// <summary>输入框内容被用户手动修改过时，刷新循环不再回写覆盖</summary>
    private bool debugDirty;

    private void MarkDebugDirty()
    {
        debugDirty = true;
    }

    /// <summary>
    /// 切换调试条显隐 + 选中项切换/进入 Play 时同步当前值到输入框。
    /// 输入框只在选中/Play 切换时同步，刷新期间绝不覆盖（防止用户手打内容被冲掉）。
    /// </summary>
    private void RefreshRuntimeSection()
    {
        if (runtimeBar == null) return;

        bool show = Application.isPlaying && selected != null;
        runtimeBar.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        if (!show) return;

        runtimeScopeLabel.text = $"写入位置: {(selected.Scope == FlagScope.Global ? "Global（global_data.json，立即落盘）" : "Save（内存快照，随下次存档序列化）")}";

        // 选中/Play 切换时：同步一次当前值到输入框（此后用户可自由编辑）
        debugDirty = false;
        string current = GetRuntimeValueText(selected);
        runtimeValueField.SetValueWithoutNotify(current);
        runtimeCurrentLabel.text = "当前值: " + current;
    }

    /// <summary>刷新循环：只更新"当前值"展示文本；输入框未被手动修改时才跟随同步</summary>
    private void UpdateRuntimeDisplay()
    {
        if (runtimeBar == null || selected == null || !Application.isPlaying) return;

        string current = GetRuntimeValueText(selected);
        runtimeCurrentLabel.text = "当前值: " + current;

        if (!debugDirty)
        {
            runtimeValueField.SetValueWithoutNotify(current);
        }
    }

    private string GetRuntimeValueText(FlagRegistry.FlagDefinition def)
    {
        if (!Application.isPlaying) return "-";
        var svc = FlagService.GetInstance();
        switch (def.Type)
        {
            case FlagType.Bool: return svc.GetBool(def.Name).ToString();
            case FlagType.Int: return svc.GetInt(def.Name).ToString();
            case FlagType.Float: return svc.GetFloat(def.Name).ToString("0.###");
            default: return svc.GetString(def.Name);
        }
    }

    private void ApplyRuntimeValue()
    {
        if (selected == null || runtimeValueField == null || !Application.isPlaying) return;
        string raw = runtimeValueField.value.Trim();

        var svc = FlagService.GetInstance();
        switch (selected.Type)
        {
            case FlagType.Bool:
                bool b;
                if (bool.TryParse(raw, out b)) svc.SetBool(selected.Name, b);
                else EditorUtility.DisplayDialog("格式错误", $"无法将 \"{raw}\" 解析为 Bool（true/false）", "确定");
                break;
            case FlagType.Int:
                int i;
                if (int.TryParse(raw, out i)) svc.SetInt(selected.Name, i);
                else EditorUtility.DisplayDialog("格式错误", $"无法将 \"{raw}\" 解析为 Int", "确定");
                break;
            case FlagType.Float:
                float f;
                if (float.TryParse(raw, out f)) svc.SetFloat(selected.Name, f);
                else EditorUtility.DisplayDialog("格式错误", $"无法将 \"{raw}\" 解析为 Float", "确定");
                break;
            default:
                svc.SetString(selected.Name, raw);
                break;
        }
        // 应用成功后按新值重读显示；输入框保留用户输入（不再回写，避免打断连续调试）
        debugDirty = false;
        UpdateRuntimeDisplay();
    }

    // ==================== 操作 ====================

    private void AddFlag()
    {
        if (registry == null) return;
        Undo.RecordObject(registry, "Add Flag");

        // 避免重名：NewFlag / NewFlag1 / NewFlag2 ...
        string baseName = "NewFlag";
        string name = baseName;
        int suffix = 1;
        while (registry.Definitions.Any(d => d.Name == name))
        {
            name = baseName + suffix;
            suffix++;
        }

        // 新建继承当前选中项的分组，便于连续创建同组 Flag
        var def = new FlagRegistry.FlagDefinition
        {
            Name = name,
            Group = selected != null ? selected.Group : ""
        };
        registry.Definitions.Add(def);
        selected = def;

        SaveRegistry();
        RefreshAll();
    }

    private void DuplicateFlag(FlagRegistry.FlagDefinition def)
    {
        if (registry == null || def == null) return;
        Undo.RecordObject(registry, "Duplicate Flag");

        string baseName = def.Name + "_Copy";
        string name = baseName;
        int suffix = 1;
        while (registry.Definitions.Any(d => d.Name == name))
        {
            name = baseName + suffix;
            suffix++;
        }

        var copy = new FlagRegistry.FlagDefinition
        {
            Name = name,
            Type = def.Type,
            Scope = def.Scope,
            DefaultValue = def.DefaultValue,
            Group = def.Group,
            Comment = def.Comment
        };
        registry.Definitions.Add(copy);
        selected = copy;

        SaveRegistry();
        RefreshAll();
    }

    private void DeleteSelected()
    {
        if (registry == null || selected == null) return;
        Undo.RecordObject(registry, "Delete Flag");
        registry.Definitions.Remove(selected);
        selected = null;
        SaveRegistry();
        RefreshAll();
    }

    private void SaveRegistry()
    {
        if (registry == null) return;
        EditorUtility.SetDirty(registry);
        AssetDatabase.SaveAssets();
    }

    private void SaveAndRefreshList()
    {
        SaveRegistry();
        Validate();
        RebuildGroupList();
        if (detailPanel != null && selected != null)
        {
            var title = detailPanel.ElementAt(0) as Label;
            if (title != null) title.text = selected.Name;
            var breadcrumb = detailPanel.ElementAt(1) as Label;
            if (breadcrumb != null) breadcrumb.text = string.IsNullOrEmpty(selected.Group) ? UngroupedLabel : selected.Group;
        }
    }

    // ==================== 校验 / 刷新 ====================

    private void Validate()
    {
        validationErrors.Clear();
        if (registry == null || registry.Definitions == null) return;

        var seen = new HashSet<string>();
        foreach (var def in registry.Definitions)
        {
            if (def == null) continue;

            string nameErr;
            if (!FlagRegistry.IsValidName(def.Name, out nameErr))
            {
                validationErrors[def.Name ?? ""] = nameErr;
                continue;
            }
            if (!seen.Add(def.Name))
            {
                validationErrors[def.Name] = "名称重复";
            }
        }
    }

    private void RefreshAll()
    {
        Validate();
        RebuildGroupList();
        RefreshDetail();

        if (statusLabel == null) return;
        if (registry == null)
        {
            statusLabel.text = "未加载注册表（兼容模式）";
            statusLabel.style.color = ResourceStyles.TextSecondary;
        }
        else
        {
            int total = registry.Definitions.Count;
            int global = registry.Definitions.Count(d => d.Scope == FlagScope.Global);
            int groupCount = registry.Definitions.Select(d => d.Group ?? "").Distinct().Count();
            string baseText = $"共 {total} 个 Flag · {groupCount} 个分组 · Global {global} / Save {total - global}";
            if (validationErrors.Count > 0)
            {
                statusLabel.text = baseText + $" · 校验错误 {validationErrors.Count} 项（⚠ 悬停查看）";
                statusLabel.style.color = ResourceStyles.DangerNormal;
            }
            else
            {
                statusLabel.text = baseText + " · 校验通过";
                statusLabel.style.color = ResourceStyles.AccentSuccess;
            }
        }
    }

    // ==================== 字段构建工具 ====================

    private VisualElement MakeTextField(string label, string value, System.Action<string> onChanged, bool multiline = false)
    {
        var field = new TextField(label) { value = value ?? "", multiline = multiline };
        field.style.color = ResourceStyles.TextPrimary;
        field.style.marginBottom = 6;
        if (multiline) field.style.height = 48;
        field.RegisterValueChangedCallback(evt => onChanged(evt.newValue));
        return field;
    }

    private VisualElement MakeEnumField<T>(string label, T value, System.Action<System.Enum> onChanged) where T : struct, System.Enum
    {
        var field = new EnumField(label, value);
        field.style.color = ResourceStyles.TextPrimary;
        field.style.marginBottom = 6;
        field.RegisterValueChangedCallback(evt => onChanged(evt.newValue));
        return field;
    }
}
