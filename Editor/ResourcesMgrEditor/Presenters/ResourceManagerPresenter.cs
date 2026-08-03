using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 资源管理器 Presenter - 协调 Model、View、Service。
/// 负责：
/// - 处理用户交互（按钮、菜单、键盘）
/// - 调用 Service 加载/排序/导入资源
/// - 更新 View 渲染
/// - 维护选中状态、播放状态等运行时数据
/// </summary>
public class ResourceManagerPresenter
{
    private readonly ResourceToolbarView _toolbar;
    private readonly ResourceSidebarView _sidebar;
    private readonly ResourceContentView _content;
    private readonly ResourceStatusBarView _statusBar;
    private readonly AudioPlayerView _audioPlayer;

    private ResourceWindowState _state;
    private List<ResourceItem> _items;
    private HashSet<string> _selected;
    private string _lastClickedPath;

    public ResourceManagerPresenter(
        ResourceToolbarView toolbar,
        ResourceSidebarView sidebar,
        ResourceContentView content,
        ResourceStatusBarView statusBar,
        AudioPlayerView audioPlayer)
    {
        _toolbar = toolbar;
        _sidebar = sidebar;
        _content = content;
        _statusBar = statusBar;
        _audioPlayer = audioPlayer;

        _items = new List<ResourceItem>();
        _selected = new HashSet<string>();
        _state = new ResourceWindowState();

        // 绑定 UI 事件
        _toolbar.OnImportFile += HandleImportFile;
        _toolbar.OnImportFolder += HandleImportFolder;
        _toolbar.OnSearchChanged += HandleSearchChanged;
        _toolbar.OnViewGrid += () => SetViewMode(ViewMode.Grid);
        _toolbar.OnViewList += () => SetViewMode(ViewMode.List);
        _toolbar.OnSortClicked += ShowSortMenu;
        _toolbar.OnSettingsClicked += ShowSettingsMenu;
        _toolbar.OnRefreshClicked += () => RefreshContent();

        _sidebar.OnCategorySelected += SetCategory;

        _content.OnItemMouseDown += HandleItemMouseDown;
        _content.OnItemPlay += HandleItemPlay;
    }

    // ===================== 初始化 =====================
    public void Initialize(ResourceWindowState state)
    {
        _state = state;
        _toolbar.SetSearchValue(_state.searchKeyword);
        _toolbar.SetViewModeButtons(_state.viewMode);

        if (!string.IsNullOrEmpty(_state.lastSelectedAssetPath))
            _selected.Add(_state.lastSelectedAssetPath);

        // 恢复分类选择
        _sidebar.SetSelectedCategory(_state.currentType);
        _sidebar.UpdateCounts(CountAllCategories());

        RefreshContent();
    }

    // ===================== 状态导出（用于持久化） =====================
    public ResourceWindowState GetState()
    {
        return _state;
    }

    // ===================== 分类切换 =====================
    public void SetCategory(ResType type)
    {
        if (_state.currentType == type) return;
        _state.currentType = type;
        ClearSelection();
        // 切换分类时自动隐藏音频播放器
        _audioPlayer.Hide();
        RefreshContent();
    }

    // ===================== 视图模式 =====================
    public void SetViewMode(ViewMode mode)
    {
        if (_state.viewMode == mode) return;
        _state.viewMode = mode;
        _toolbar.SetViewModeButtons(mode);
        RefreshContent();
    }

    // ===================== 排序 =====================
    public void SetSortMode(SortMode mode)
    {
        if (_state.sortMode == mode) return;
        _state.sortMode = mode;
        RefreshContent();
    }

    // ===================== 搜索 =====================
    private void HandleSearchChanged(string value)
    {
        _state.searchKeyword = value;
        RefreshContent();
    }

    // ===================== 刷新 =====================
    public void RefreshContent()
    {
        string path = ResourceAssetService.GetPathFromConfig(_state.currentType);

        if (string.IsNullOrEmpty(path))
        {
            _content.ShowEmpty("未配置路径", "请检查 VNProjectConfig 中的资源路径设置。");
            UpdateStatusBar();
            return;
        }

        if (!Directory.Exists(path))
        {
            _content.ShowEmpty("目标文件夹不存在", $"路径：{path}\n\n点击下方按钮创建该文件夹。");
            _content.AddEmptyButton("创建文件夹", () => CreateFolder(path));
            UpdateStatusBar();
            return;
        }

        _items = ResourceAssetService.LoadAssets(_state.currentType, _state.searchKeyword);
        _items = ResourceAssetService.Sort(_items, _state.sortMode);

        if (_items.Count == 0)
        {
            _content.ShowEmpty("暂无资源",
                $"点击「导入文件」或拖入文件到此处。\n\n支持的格式：{UIElementBuilder.GetExtensionDescription(_state.currentType)}");
            UpdateStatusBar();
            return;
        }

        _content.HideEmpty();
        _content.RenderItems(_items, _state.currentType, _state.viewMode, _state.cardSize, _selected);
        _sidebar.UpdateCounts(CountAllCategories());
        UpdateStatusBar();
    }

    private Dictionary<ResType, int> CountAllCategories()
    {
        var dict = new Dictionary<ResType, int>();
        foreach (ResType t in System.Enum.GetValues(typeof(ResType)))
        {
            dict[t] = ResourceAssetService.CountAssets(t);
        }
        return dict;
    }

    // ===================== 选中操作 =====================
    private void HandleItemMouseDown(ResourceItem item, int clickCount)
    {
        if (clickCount == ResourceContentView.DeleteAction)
        {
            DeleteItems(new List<ResourceItem> { item });
            return;
        }

        bool isCtrl = UnityEngine.Event.current?.control == true || UnityEngine.Event.current?.command == true;
        bool isShift = UnityEngine.Event.current?.shift == true;

        if (isShift && !string.IsNullOrEmpty(_lastClickedPath))
        {
            _selected.Clear();
            bool inRange = false;
            foreach (var i in _items)
            {
                if (i.AssetPath == _lastClickedPath || i.AssetPath == item.AssetPath) inRange = !inRange;
                if (inRange) _selected.Add(i.AssetPath);
            }
        }
        else if (isCtrl)
        {
            if (_selected.Contains(item.AssetPath)) _selected.Remove(item.AssetPath);
            else _selected.Add(item.AssetPath);
            _lastClickedPath = item.AssetPath;
        }
        else
        {
            _selected.Clear();
            _selected.Add(item.AssetPath);
            _lastClickedPath = item.AssetPath;
        }

        _state.lastSelectedAssetPath = item.AssetPath;

        if (item.Asset != null)
        {
            Selection.activeObject = item.Asset;
            EditorGUIUtility.PingObject(item.Asset);
        }

        if (clickCount == 2)
        {
            if (item.Asset != null) AssetDatabase.OpenAsset(item.Asset);
            else if (File.Exists(item.FullPath)) EditorUtility.RevealInFinder(item.FullPath);
        }

        UpdateStatusBar();
        _content.RenderItems(_items, _state.currentType, _state.viewMode, _state.cardSize, _selected);
    }

    private void HandleItemPlay(ResourceItem item)
    {
        if (item.Asset is AudioClip clip)
        {
            _audioPlayer.LoadAndPlay(clip, item.Name);
            UpdateStatusBar();
        }
    }

    public void ClearSelection()
    {
        if (_selected.Count == 0) return;
        _selected.Clear();
        _content.RenderItems(_items, _state.currentType, _state.viewMode, _state.cardSize, _selected);
    }

    public void SelectAll()
    {
        _selected.Clear();
        foreach (var i in _items) _selected.Add(i.AssetPath);
        _content.RenderItems(_items, _state.currentType, _state.viewMode, _state.cardSize, _selected);
    }

    public void DeleteSelected()
    {
        var toDelete = _items.FindAll(i => _selected.Contains(i.AssetPath));
        if (toDelete.Count == 0) return;
        DeleteItems(toDelete);
    }

    private void DeleteItems(List<ResourceItem> items)
    {
        if (items.Count == 0) return;
        string list = string.Join("\n  ", items.ConvertAll(i => i.AssetPath));
        if (EditorUtility.DisplayDialog("删除确认",
            items.Count == 1
                ? $"确定要删除以下文件吗？\n\n{list}\n\n此操作无法撤销！"
                : $"确定要删除以下 {items.Count} 个文件吗？\n\n  {list}\n\n此操作无法撤销！",
            items.Count == 1 ? "删除" : "全部删除", "取消"))
        {
            foreach (var item in items)
            {
                AssetDatabase.DeleteAsset(item.AssetPath);
                _selected.Remove(item.AssetPath);
            }
            AssetDatabase.Refresh();
            RefreshContent();
        }
    }

    // ===================== 导入 =====================
    private void HandleImportFile()
    {
        string targetAssetPath = ResourceAssetService.GetPathFromConfig(_state.currentType);
        if (string.IsNullOrEmpty(targetAssetPath))
        {
            EditorUtility.DisplayDialog("错误", "未配置资源路径，请检查 VNProjectConfig。", "确定");
            return;
        }
        if (!Directory.Exists(targetAssetPath))
        {
            bool create = EditorUtility.DisplayDialog("文件夹不存在",
                $"目标文件夹不存在：\n{targetAssetPath}\n\n是否创建？", "创建", "取消");
            if (create) CreateFolder(targetAssetPath);
            else return;
        }
        string extFilter = string.Join(",", UIElementBuilder.GetExtensionsList(_state.currentType));
        string srcPath = EditorUtility.OpenFilePanel($"导入 {UIElementBuilder.GetTypeDisplayName(_state.currentType)}", "", extFilter);
        if (string.IsNullOrEmpty(srcPath)) return;

        var result = ResourceImportService.ImportSingleFile(_state.currentType, srcPath);
        if (result.Success)
        {
            AssetDatabase.Refresh();
            RefreshContent();
        }
    }

    private void HandleImportFolder()
    {
        string targetAssetPath = ResourceAssetService.GetPathFromConfig(_state.currentType);
        if (string.IsNullOrEmpty(targetAssetPath))
        {
            EditorUtility.DisplayDialog("错误", "未配置资源路径，请检查 VNProjectConfig。", "确定");
            return;
        }
        if (!Directory.Exists(targetAssetPath))
        {
            bool create = EditorUtility.DisplayDialog("文件夹不存在",
                $"目标文件夹不存在：\n{targetAssetPath}\n\n是否创建？", "创建", "取消");
            if (create) CreateFolder(targetAssetPath);
            else return;
        }
        string srcDir = EditorUtility.OpenFolderPanel("选择要导入的文件夹", "", "");
        if (string.IsNullOrEmpty(srcDir)) return;

        int count = ResourceImportService.ImportDirectoryRecursive(_state.currentType, srcDir, targetAssetPath);
        if (count > 0)
        {
            AssetDatabase.Refresh();
            RefreshContent();
            Debug.Log($"[资源管理器] 已导入 {count} 个文件");
        }
        else
        {
            EditorUtility.DisplayDialog("提示", "所选文件夹中没有匹配的文件。", "确定");
        }
    }

    public void ImportDroppedFiles(string[] paths)
    {
        if (paths == null || paths.Length == 0) return;
        int imported = 0;
        string target = ResourceAssetService.GetPathFromConfig(_state.currentType);
        foreach (var src in paths)
        {
            if (Directory.Exists(src))
                imported += ResourceImportService.ImportDirectoryRecursive(_state.currentType, src, target);
            else if (File.Exists(src))
            {
                var r = ResourceImportService.ImportSingleFile(_state.currentType, src);
                if (r.Success) imported++;
            }
        }
        if (imported > 0)
        {
            AssetDatabase.Refresh();
            RefreshContent();
            Debug.Log($"[资源管理器] 已导入 {imported} 个文件");
        }
    }

    private void CreateFolder(string path)
    {
        if (ResourceImportService.CreateFolderIfMissing(path))
        {
            RefreshContent();
            Debug.Log($"[资源管理器] 创建文件夹: {path}");
        }
    }

    // ===================== 菜单 =====================
    private void ShowSortMenu()
    {
        var menu = new GenericMenu();
        menu.AddItem(new GUIContent("名称 ↑"), _state.sortMode == SortMode.NameAsc, () => SetSortMode(SortMode.NameAsc));
        menu.AddItem(new GUIContent("名称 ↓"), _state.sortMode == SortMode.NameDesc, () => SetSortMode(SortMode.NameDesc));
        menu.AddSeparator("");
        menu.AddItem(new GUIContent("修改时间 ↑ (最早)"), _state.sortMode == SortMode.DateOldest, () => SetSortMode(SortMode.DateOldest));
        menu.AddItem(new GUIContent("修改时间 ↓ (最新)"), _state.sortMode == SortMode.DateNewest, () => SetSortMode(SortMode.DateNewest));
        menu.ShowAsContext();
    }

    private void ShowSettingsMenu()
    {
        var menu = new GenericMenu();
        menu.AddItem(new GUIContent("显示状态栏"), _state.showStatusBar, () =>
        {
            _state.showStatusBar = !_state.showStatusBar;
            OnStatusBarToggled?.Invoke();
        });
        menu.AddSeparator("");
        menu.AddItem(new GUIContent("卡片大小/小 (100)"), Mathf.Approximately(_state.cardSize, ResourceStyles.MinCardSize),
            () => { _state.cardSize = ResourceStyles.MinCardSize; RefreshContent(); });
        menu.AddItem(new GUIContent("卡片大小/中 (120)"), Mathf.Approximately(_state.cardSize, ResourceStyles.DefaultCardSize),
            () => { _state.cardSize = ResourceStyles.DefaultCardSize; RefreshContent(); });
        menu.AddItem(new GUIContent("卡片大小/大 (160)"), Mathf.Approximately(_state.cardSize, ResourceStyles.MaxCardSize),
            () => { _state.cardSize = ResourceStyles.MaxCardSize; RefreshContent(); });
        menu.AddSeparator("");
        menu.AddItem(new GUIContent("在文件管理器中显示当前路径"), false, () =>
        {
            string p = ResourceAssetService.GetPathFromConfig(_state.currentType);
            if (Directory.Exists(p)) EditorUtility.RevealInFinder(p);
            else EditorUtility.DisplayDialog("提示", $"文件夹不存在：\n{p}", "确定");
        });
        menu.AddItem(new GUIContent("复制当前路径到剪贴板"), false, () =>
        {
            EditorGUIUtility.systemCopyBuffer = ResourceAssetService.GetPathFromConfig(_state.currentType);
        });
        menu.ShowAsContext();
    }

    /// <summary>状态栏显示切换（外部订阅以重新构建 UI）</summary>
    public event System.Action OnStatusBarToggled;

    // ===================== 状态栏 =====================
    private void UpdateStatusBar()
    {
        int total = ResourceAssetService.CountAssets(_state.currentType);
        _statusBar.Update(_selected.Count, total, _state.currentType, _state.viewMode, _state.sortMode,
            ResourceAssetService.GetPathFromConfig(_state.currentType));
    }

    // ===================== 键盘事件 =====================
    public void OnKeyDown(KeyCode key, bool ctrl, bool shift)
    {
        if (key == KeyCode.A && ctrl)
            SelectAll();
        else if (key == KeyCode.Delete && _selected.Count > 0)
            DeleteSelected();
        else if (key == KeyCode.Escape)
            ClearSelection();
    }
}
