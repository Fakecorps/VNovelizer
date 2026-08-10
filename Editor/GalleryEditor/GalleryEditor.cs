using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 画廊编辑器主窗口（MVP - View 入口）。
/// 只负责：装配 Presenter + 左栏 ListPanelView + 右栏 DetailPanelView + 大图浮层。
/// 所有业务逻辑在 GalleryEditorPresenter 中。
/// </summary>
public class GalleryEditor : EditorWindow
{
    private GalleryEditorPresenter presenter;
    private BigPreviewOverlay previewOverlay;
    private ListPanelView listPanel;
    private DetailPanelView detailPanel;

    [MenuItem("VNovelizer/画廊编辑器 (Gallery Editor)", false, 26)]
    public static void ShowWindow()
    {
        var wnd = GetWindow<GalleryEditor>();
        wnd.titleContent = new GUIContent("画廊编辑器");
        wnd.minSize = new Vector2(960, 640);
    }

    private void OnEnable()
    {
        presenter = new GalleryEditorPresenter();
        presenter.LoadAll();
        EditorApplication.projectChanged += OnProjectChanged;
    }

    private void OnDisable()
    {
        EditorApplication.projectChanged -= OnProjectChanged;
    }

    private void OnProjectChanged()
    {
        if (presenter != null)
        {
            presenter.ClearPreviewCache();
            presenter.LoadAll();
            presenter.SwitchMode(presenter.CurrentMode);
        }
    }

    public void CreateGUI()
    {
        var root = rootVisualElement;
        root.style.backgroundColor = GalleryTheme.Hex(GalleryTheme.BgPrimary);

        // 大图浮层（覆盖整窗口，最后添加以保证在最上）
        previewOverlay = new BigPreviewOverlay();
        root.Add(previewOverlay);

        // 装配视图
        listPanel = new ListPanelView(presenter);
        detailPanel = new DetailPanelView(presenter, previewOverlay, RefreshSelectedListItem);

        // OnDataChanged：数据细节变化，只刷左栏单行（避免详情页重建导致 ListView 回收）
        presenter.OnDataChanged += RefreshSelectedListItem;
        // OnModeChanged：模式变化，重建左右栏
        presenter.OnModeChanged += _ =>
        {
            listPanel.Refresh();
            detailPanel.Rebuild();
        };
        // OnSelectionChanged：选中变化，重建详情
        presenter.OnSelectionChanged += _ => detailPanel.Rebuild();
        presenter.OnToast += ShowToast;

        // ESC 关闭大图浮层
        root.RegisterCallback<KeyDownEvent>(e =>
        {
            if (e.keyCode == KeyCode.Escape && previewOverlay.style.display == DisplayStyle.Flex)
            {
                previewOverlay.Hide();
                e.StopPropagation();
            }
        });

        BuildLayout(root);
        listPanel.Refresh();
        detailPanel.Rebuild();
    }

    private void BuildLayout(VisualElement root)
    {
        // ===== 标题栏 =====
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

        var icon = new Label("\u25B6") { style = { fontSize = 16, color = GalleryTheme.Hex(GalleryTheme.Accent), marginRight = 8 } };
        titleBar.Add(icon);

        var title = new Label("画廊编辑器") { style = { fontSize = 16, unityFontStyleAndWeight = FontStyle.Bold, color = GalleryTheme.Hex(GalleryTheme.TextPrimary), marginRight = 24 } };
        titleBar.Add(title);

        // Tab 切换
        var tabGroup = new VisualElement();
        tabGroup.style.flexDirection = FlexDirection.Row;
        tabGroup.style.alignItems = Align.FlexEnd;
        titleBar.Add(tabGroup);
        AddTab(tabGroup, "CG 管理", GalleryEditorPresenter.Mode.CG);
        AddTab(tabGroup, "音乐管理", GalleryEditorPresenter.Mode.Music);
        AddTab(tabGroup, "场景管理", GalleryEditorPresenter.Mode.Scene);
        UpdateTabSelection();

        var spacer = new VisualElement { style = { flexGrow = 1 } };
        titleBar.Add(spacer);

        var refreshBtn = new Button(() =>
        {
            presenter.ClearPreviewCache();
            presenter.LoadAll();
            presenter.SwitchMode(presenter.CurrentMode);
        })
        { text = "\u21BB 刷新" };
        GalleryStyles.ApplyButton(refreshBtn, GalleryTheme.BgCard, false);
        refreshBtn.style.height = 30;
        refreshBtn.style.marginRight = 8;
        titleBar.Add(refreshBtn);

        // ===== 主分栏 =====
        var splitView = new TwoPaneSplitView(0, 360, TwoPaneSplitViewOrientation.Horizontal);
        splitView.style.flexGrow = 1;
        splitView.style.minHeight = 0;
        splitView.style.minWidth = 0;
        root.Add(splitView);

        splitView.Add(listPanel);
        splitView.Add(detailPanel);
    }

    private readonly System.Collections.Generic.Dictionary<GalleryEditorPresenter.Mode, Button> tabButtons
        = new System.Collections.Generic.Dictionary<GalleryEditorPresenter.Mode, Button>();

    private void AddTab(VisualElement parent, string text, GalleryEditorPresenter.Mode mode)
    {
        var btn = new Button(() =>
        {
            presenter.SwitchMode(mode);
            UpdateTabSelection();
        })
        { text = text };
        btn.style.height = 36;
        btn.style.paddingLeft = 16;
        btn.style.paddingRight = 16;
        btn.style.marginRight = 4;
        btn.style.borderTopLeftRadius = 6;
        btn.style.borderTopRightRadius = 6;
        btn.style.borderBottomLeftRadius = 0;
        btn.style.borderBottomRightRadius = 0;
        btn.style.borderTopWidth = 0;
        btn.style.borderLeftWidth = 0;
        btn.style.borderRightWidth = 0;
        btn.style.borderBottomWidth = 2;
        btn.style.fontSize = 13;
        btn.style.unityFontStyleAndWeight = FontStyle.Normal;
        parent.Add(btn);
        tabButtons[mode] = btn;
    }

    private void UpdateTabSelection()
    {
        foreach (var kv in tabButtons)
        {
            bool active = kv.Key == presenter.CurrentMode;
            kv.Value.style.backgroundColor = GalleryTheme.Hex(active ? GalleryTheme.Accent : GalleryTheme.BgCard);
            kv.Value.style.color = active ? Color.white : GalleryTheme.Hex(GalleryTheme.TextSecondary);
            kv.Value.style.borderBottomColor = active ? Color.white : GalleryTheme.Hex(GalleryTheme.Border);
            kv.Value.style.unityFontStyleAndWeight = active ? FontStyle.Bold : FontStyle.Normal;
        }
    }

    private void ShowToast(string msg)
    {
        EditorApplication.Beep();
        // 简化：仅调用 Console 通知
        Debug.Log("[GalleryEditor] " + msg);
    }

    private void RefreshSelectedListItem()
    {
        if (listPanel == null || presenter == null) return;

        int idx = presenter.GetLastSelection(presenter.CurrentMode);
        if (idx >= 0) listPanel.RefreshItem(idx);
        else listPanel.Refresh();
    }
}