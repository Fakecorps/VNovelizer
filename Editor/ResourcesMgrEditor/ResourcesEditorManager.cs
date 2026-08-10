using Alchemy.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 资源管理器 - VNovelizer 的资源浏览与编辑窗口。
/// 继承自 AlchemyEditorWindow 以自动获得状态持久化能力。
///
/// 架构：MVP
/// - View 层：ResourceToolbarView / ResourceSidebarView / ResourceContentView / ResourceStatusBarView / AudioPlayerView
/// - Presenter 层：ResourceManagerPresenter
/// - Service 层：ResourceAssetService / ResourceImportService / AudioPreviewService
/// - Model 层：ResourceWindowState / ResourceItem / ResType 等（见 Model/ResourceDataModel.cs）
/// - Helper 层：ResourceStyles / UIElementBuilder（见 Helpers/）
/// </summary>
public class ResourcesEditorManager : AlchemyEditorWindow
{
    private ResourceManagerPresenter _presenter;
    private ResourceWindowState _state;

    [MenuItem("VNovelizer/资源管理器 (Resource Manager)", false, 23)]
    public static void ShowWindow()
    {
        var wnd = GetWindow<ResourcesEditorManager>();
        wnd.titleContent = new GUIContent("资源管理器");
        wnd.minSize = new Vector2(1000, 600);
    }

    protected override string GetWindowDataPath() => "ProjectSettings/ResourceManagerWindow.json";

    protected override void CreateGUI()
    {
        // 1. 加载持久化状态
        LoadWindowData(GetWindowDataPath());
        _state = new ResourceWindowState
        {
            currentType = currentType,
            searchKeyword = searchKeyword,
            viewMode = viewMode,
            sortMode = sortMode,
            cardSize = cardSize,
            showStatusBar = showStatusBar,
            lastSelectedAssetPath = lastSelectedAssetPath
        };

        var root = rootVisualElement;
        root.Clear();
        root.style.backgroundColor = GalleryTheme.Hex(GalleryTheme.BgPrimary);
        root.style.flexDirection = FlexDirection.Column;

        // 2. 创建所有视图
        var toolbar = new ResourceToolbarView();
        var sidebar = new ResourceSidebarView();
        var content = new ResourceContentView();
        var statusBar = new ResourceStatusBarView();
        var audioPlayer = new AudioPlayerView();

        // 2.5 标题栏（统一 Gallery Editor 风格）
        BuildTitleBar(root, toolbar);

        // 3. 组装布局
        var splitView = new TwoPaneSplitView(0, ResourceStyles.SidebarMinWidth, TwoPaneSplitViewOrientation.Horizontal);
        splitView.style.flexGrow = 1;
        root.Add(toolbar.Root);

        var rightContainer = new VisualElement();
        rightContainer.style.flexDirection = FlexDirection.Column;
        rightContainer.style.flexGrow = 1;
        rightContainer.style.backgroundColor = ResourceStyles.Bg;
        rightContainer.Add(content.Root);
        rightContainer.Add(audioPlayer.Root);
        splitView.Add(sidebar.Root);
        splitView.Add(rightContainer);
        root.Add(splitView);

        // 4. 状态栏（根据配置决定是否显示）
        if (_state.showStatusBar) root.Add(statusBar.Root);

        // 5. 创建 Presenter 并初始化
        _presenter = new ResourceManagerPresenter(toolbar, sidebar, content, statusBar, audioPlayer);
        _presenter.OnStatusBarToggled += () =>
        {
            // 重新构建整个窗口以应用状态栏显示变化
            CreateGUI();
        };
        _presenter.Initialize(_state);

        // 6. 拖放支持
        rightContainer.RegisterCallback<DragUpdatedEvent>(evt => DragAndDrop.visualMode = DragAndDropVisualMode.Copy);
        rightContainer.RegisterCallback<DragPerformEvent>(evt =>
        {
            DragAndDrop.AcceptDrag();
            _presenter?.ImportDroppedFiles(DragAndDrop.paths);
        });

        // 7. 键盘快捷键
        rootVisualElement.RegisterCallback<KeyDownEvent>(OnKeyDown);
        rootVisualElement.RegisterCallback<KeyUpEvent>(OnKeyUp);
        rootVisualElement.focusable = true;
        rootVisualElement.Focus();
    }

    private void BuildTitleBar(VisualElement root, ResourceToolbarView toolbar)
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

        var icon = new Label("\u25A0")
        {
            style =
            {
                fontSize = 16,
                color = GalleryTheme.Hex(GalleryTheme.Accent),
                marginRight = 8
            }
        };
        titleBar.Add(icon);

        var title = new Label("资源管理器")
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

        var subtitle = new Label("Resource Manager")
        {
            style =
            {
                fontSize = 11,
                color = GalleryTheme.Hex(GalleryTheme.TextMuted)
            }
        };
        titleBar.Add(subtitle);

        titleBar.Add(new VisualElement { style = { flexGrow = 1 } });

        // 刷新按钮
        var refreshBtn = new Button(() =>
        {
            _presenter?.RefreshContent();
        })
        { text = "\u21BB 刷新" };
        GalleryStyles.ApplyButton(refreshBtn, GalleryTheme.BgCard, false);
        refreshBtn.style.height = 30;
        refreshBtn.style.marginRight = 8;
        titleBar.Add(refreshBtn);
    }

    private void OnKeyDown(KeyDownEvent evt)
    {
        _presenter?.OnKeyDown(evt.keyCode, evt.ctrlKey || evt.commandKey, evt.shiftKey);
    }

    private void OnKeyUp(KeyUpEvent evt) { }

    // ===================== AlchemyEditorWindow 持久化字段 =====================
    // 必须用 [SerializeField] 才能被 SaveWindowData/LoadWindowData 持久化
    // 通过 _presenter.GetState() 与 _state 同步
    [SerializeField] private ResType currentType = ResType.Background;
    [SerializeField] private string searchKeyword = "";
    [SerializeField] private ViewMode viewMode = ViewMode.Grid;
    [SerializeField] private SortMode sortMode = SortMode.NameAsc;
    [SerializeField] private float cardSize = 120f;
    [SerializeField] private bool showStatusBar = true;
    [SerializeField] private string lastSelectedAssetPath = "";

    /// <summary>窗口失焦前保存状态</summary>
    private void OnDisable()
    {
        if (_presenter != null)
        {
            var s = _presenter.GetState();
            currentType = s.currentType;
            searchKeyword = s.searchKeyword;
            viewMode = s.viewMode;
            sortMode = s.sortMode;
            cardSize = s.cardSize;
            showStatusBar = s.showStatusBar;
            lastSelectedAssetPath = s.lastSelectedAssetPath;
        }
        AudioPreviewService.Stop();
        EditorUpdateService.UnregisterCallback();
        try { SaveWindowData(GetWindowDataPath()); } catch { }
    }
}
