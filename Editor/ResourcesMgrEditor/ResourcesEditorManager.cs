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
    // 【Fix-55】当前音频播放器视图：CreateGUI 重建前调用 Dispose 退订静态事件
    private AudioPlayerView _audioPlayerView;

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
        if (_presenter != null) _state = _presenter.GetState();
        else
        {
            try { LoadWindowData(GetWindowDataPath()); }
            catch (System.Exception e) { Debug.LogWarning($"[资源管理器] 窗口状态读取失败：{e.Message}"); }
            _state = new ResourceWindowState
            {
                currentType = currentType,
                searchKeyword = searchKeyword ?? "",
                viewMode = viewMode,
                sortMode = sortMode,
                cardSize = Mathf.Clamp(cardSize, ResourceStyles.MinCardSize, ResourceStyles.MaxCardSize),
                showStatusBar = showStatusBar,
                lastSelectedAssetPath = lastSelectedAssetPath,
                currentFolder = currentFolder
            };
        }

        var root = rootVisualElement;
        root.Clear();
        root.style.backgroundColor = GalleryTheme.Hex(GalleryTheme.BgPrimary);
        root.style.flexDirection = FlexDirection.Column;

        // 2. 创建所有视图
        // 【Fix-55】重建前释放旧 AudioPlayerView 的静态事件订阅（状态栏切换会整体重建 UI）
        _audioPlayerView?.Dispose();

        var toolbar = new ResourceToolbarView();
        var sidebar = new ResourceSidebarView();
        var content = new ResourceContentView();
        var statusBar = new ResourceStatusBarView();
        var audioPlayer = new AudioPlayerView();
        var trashPanel = new ResourceTrashPanelView(); // 【子文件夹支持】回收站面板
        _audioPlayerView = audioPlayer;

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
        rightContainer.Add(trashPanel.Root);
        rightContainer.Add(audioPlayer.Root);
        splitView.Add(sidebar.Root);
        splitView.Add(rightContainer);
        root.Add(splitView);

        // 4. 状态栏（根据配置决定是否显示）
        root.Add(statusBar.Root);
        statusBar.Root.style.display = _state.showStatusBar ? DisplayStyle.Flex : DisplayStyle.None;

        // 5. 创建 Presenter 并初始化
        _presenter = new ResourceManagerPresenter(toolbar, sidebar, content, statusBar, audioPlayer, trashPanel);
        _presenter.OnStatusBarToggled += () =>
        {
            statusBar.Root.style.display = _presenter.GetState().showStatusBar ? DisplayStyle.Flex : DisplayStyle.None;
            PersistState();
        };
        _presenter.Initialize(_state);

        // 6. 拖放支持（双模式：Project 窗口资产 → 拖放分配；OS 外部文件 → 导入复制；
        //    落位到当前浏览的文件夹）
        rightContainer.RegisterCallback<DragUpdatedEvent>(evt =>
        {
            if (_presenter == null || _presenter.IsTrashMode) DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;
            else if (DragAndDrop.GetGenericData("VnResMoveItems") is System.Collections.Generic.List<string>)
                DragAndDrop.visualMode = DragAndDropVisualMode.Move;
            else
            {
                bool fromProject = DragAndDrop.paths != null && DragAndDrop.paths.Length > 0 &&
                    (DragAndDrop.paths[0].StartsWith("Assets/", System.StringComparison.OrdinalIgnoreCase) ||
                     DragAndDrop.paths[0].StartsWith("Packages/", System.StringComparison.OrdinalIgnoreCase));
                string category = ResourceAssetService.GetPathFromConfig(_presenter.CurrentType);
                bool move = fromProject && _presenter.CurrentType != ResType.Video && !string.IsNullOrEmpty(category) &&
                    DragAndDrop.paths[0].StartsWith(category + "/", System.StringComparison.OrdinalIgnoreCase);
                DragAndDrop.visualMode = move ? DragAndDropVisualMode.Move :
                    fromProject && _presenter.CurrentType != ResType.Video ? DragAndDropVisualMode.Link : DragAndDropVisualMode.Copy;
            }
            evt.StopPropagation();
        });
        rightContainer.RegisterCallback<DragPerformEvent>(evt =>
        {
            if (_presenter == null || _presenter.IsTrashMode) { evt.StopPropagation(); return; }
            DragAndDrop.AcceptDrag();
            if (DragAndDrop.GetGenericData("VnResMoveItems") is System.Collections.Generic.List<string> items)
                _presenter.HandleInternalDrop(items);
            else _presenter.HandleDroppedPaths(DragAndDrop.paths);
            evt.StopPropagation();
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

        var icon = new Image { image = UIElementBuilder.GetIcon("Folder Icon", "d_Folder Icon"), scaleMode = ScaleMode.ScaleToFit };
        icon.style.width = 18;
        icon.style.height = 18;
        icon.style.flexShrink = 0;
        icon.style.marginRight = 8;
        icon.pickingMode = PickingMode.Ignore;
        if (icon.image != null) titleBar.Add(icon);

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
        var refreshBtn = UIElementBuilder.MakeIconButton(UIElementBuilder.GetIcon("Refresh", "d_Refresh"),
            "刷新", () => _presenter?.RefreshContent());
        GalleryStyles.ApplyButton(refreshBtn, GalleryTheme.BgCard, false);
        refreshBtn.style.height = 30;
        refreshBtn.style.marginRight = 8;
        titleBar.Add(refreshBtn);
    }

    private void OnKeyDown(KeyDownEvent evt)
    {
        var focused = rootVisualElement.panel?.focusController?.focusedElement as VisualElement;
        for (var element = focused; element != null; element = element.parent)
            if (element is TextField || element.ClassListContains("unity-base-text-field")) return;
        if (_presenter != null && _presenter.OnKeyDown(evt.keyCode, evt.ctrlKey || evt.commandKey, evt.shiftKey))
            evt.StopPropagation();
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
    [SerializeField] private string currentFolder = "";

    private void OnLostFocus() => PersistState();

    private void OnDisable()
    {
        PersistState();
        rootVisualElement.UnregisterCallback<KeyDownEvent>(OnKeyDown);
        rootVisualElement.UnregisterCallback<KeyUpEvent>(OnKeyUp);
        _audioPlayerView?.Dispose();
        _audioPlayerView = null;
        _presenter = null;
        AudioPreviewService.Stop();
        EditorUpdateService.UnregisterCallback();
    }

    private void PersistState()
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
            currentFolder = s.currentFolder;
        }
        try { SaveWindowData(GetWindowDataPath()); }
        catch (System.Exception e) { Debug.LogWarning($"[资源管理器] 窗口状态保存失败：{e.Message}"); }
    }
}
