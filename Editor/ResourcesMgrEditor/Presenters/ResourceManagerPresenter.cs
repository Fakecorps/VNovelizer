using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 资源管理器 Presenter - 协调 Model、View、Service。
/// 负责：
/// - 处理用户交互（按钮、菜单、键盘、拖拽）
/// - 调用 Service 加载/排序/导入资源
/// - 【子文件夹支持】文件夹导航 / 新建 / 重命名 / 删除 / 移动 / 导入落位
/// - 【子文件夹支持】回收站（软删除 / 恢复 / 清空）
/// - 【子文件夹支持】剧本引用扫描警告 + 缺失引用检查
/// - 更新 View 渲染，维护选中状态、播放状态等运行时数据
/// </summary>
public class ResourceManagerPresenter
{
    private readonly ResourceToolbarView _toolbar;
    private readonly ResourceSidebarView _sidebar;
    private readonly ResourceContentView _content;
    private readonly ResourceStatusBarView _statusBar;
    private readonly AudioPlayerView _audioPlayer;
    private readonly ResourceTrashPanelView _trashPanel;

    private ResourceWindowState _state;
    private List<ResourceItem> _items;          // 当前视图（文件夹过滤后）的条目
    private List<ResourceFolderInfo> _currentFolders; // 当前视图的子文件夹卡片（重渲染保持）
    private HashSet<string> _selected;
    private string _lastClickedPath;
    private bool _trashMode;
    private const string FolderSelectionPrefix = "#folder:";
    private string _pressedSelectionKey;

    public bool IsTrashMode => _trashMode;
    public string CurrentFolder => NormalizeFolder(_state.currentFolder);
    public ResType CurrentType => _state.currentType;

    /// <summary>Label 派生规则升级后的存量自愈：每编辑器会话只跑一次</summary>
    private static bool _labelMigrationDone;

    public ResourceManagerPresenter(
        ResourceToolbarView toolbar,
        ResourceSidebarView sidebar,
        ResourceContentView content,
        ResourceStatusBarView statusBar,
        AudioPlayerView audioPlayer,
        ResourceTrashPanelView trashPanel)
    {
        _toolbar = toolbar;
        _sidebar = sidebar;
        _content = content;
        _statusBar = statusBar;
        _audioPlayer = audioPlayer;
        _trashPanel = trashPanel;

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

        _sidebar.OnCategorySelected += SetCategory;
        _sidebar.OnTrashSelected += EnterTrashMode;

        _content.OnItemMouseDown += HandleItemMouseDown;
        _content.OnItemPointerDown += HandleItemPointerDown;
        _content.OnItemPointerUp += HandleItemPointerUp;
        _content.OnFolderPointerDown += HandleFolderPointerDown;
        _content.OnFolderPointerUp += HandleFolderPointerUp;
        _content.OnItemPlay += HandleItemPlay;
        _content.OnItemContextMenu += HandleItemContextMenu;
        _content.OnFolderNavigate += HandleFolderNavigate;
        _content.OnFolderOpen += HandleFolderOpen;
        _content.OnFolderContextMenu += HandleFolderContextMenu;
        _content.OnBackgroundContextMenu += HandleBackgroundContextMenu;
        _content.OnNewFolderClicked += HandleNewFolder;
        _content.OnItemsMoveToFolder += HandleItemsMoveToFolder;
        _content.OnExternalDropIntoFolder += HandleExternalDropIntoFolder;

        _trashPanel.OnRestore += HandleTrashRestore;
        _trashPanel.OnDeletePermanent += HandleTrashDeletePermanent;
        _trashPanel.OnClearAll += HandleTrashClearAll;
        _trashPanel.OnClose += ExitTrashMode;
        _trashPanel.OnRepairIndex += HandleTrashRepairIndex;
    }

    // ===================== 初始化 =====================
    public void Initialize(ResourceWindowState state)
    {
        _state = state;
        _state.currentFolder = NormalizeFolder(_state.currentFolder);
        _toolbar.SetSearchValue(_state.searchKeyword);
        _toolbar.SetViewModeButtons(_state.viewMode);

        if (!string.IsNullOrEmpty(_state.lastSelectedAssetPath))
            _selected.Add(_state.lastSelectedAssetPath);

        // Label 派生规则升级后的存量自愈（旧派生子标签 …_Chapter01 会查不到——只重打 Label 不动地址）
        if (!_labelMigrationDone)
        {
            _labelMigrationDone = true;
            VNAddressablesRegistrar.RelabelAllCategories();
        }

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
        // 从回收站回到资源浏览（即使类别未变也要刷新）
        if (_trashMode)
        {
            _trashMode = false;
            _sidebar.SetTrashActive(false);
        }

        if (_state.currentType == type)
        {
            RefreshContent();
            return;
        }
        _state.currentType = type;
        _state.currentFolder = ""; // 切换分类回到类别根
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
        UpdateTrashBadge();

        // 回收站模式：内容区隐藏，显示回收站面板
        if (_trashMode)
        {
            _content.Root.style.display = DisplayStyle.None;
            _trashPanel.SetVisible(true);
            try { _trashPanel.Refresh(ResourceTrashService.ListEntries()); }
            catch (System.Exception e) { _trashPanel.ShowError(e.Message); }
            UpdateStatusBar();
            return;
        }
        _trashPanel.SetVisible(false);
        _content.Root.style.display = DisplayStyle.Flex;

        // Addressables 托管模式：数据源是组内条目（物理位置无关），无文件夹存在性要求
        bool managed = VNAddressablesRegistrar.IsManagedMode && _state.currentType != ResType.Video;
        string path = ResourceAssetService.GetPathFromConfig(_state.currentType);

        if (!managed)
        {
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
        }

        // 【子文件夹支持】全量加载（搜索过滤本地做——文件夹派生需要全量逻辑名）
        var allItems = ResourceAssetService.LoadAssets(_state.currentType, null);
        bool foldersEnabled = _state.currentType != ResType.Video;
        string keyword = (_state.searchKeyword ?? "").Trim();
        bool searchMode = foldersEnabled && keyword.Length > 0;
        string folder = foldersEnabled ? NormalizeFolder(_state.currentFolder) : "";
        while (folder.Length > 0 && !FolderExists(folder, path, allItems))
        {
            int slash = folder.LastIndexOf('/');
            folder = slash > 0 ? folder.Substring(0, slash) : "";
        }
        _state.currentFolder = folder;

        List<ResourceItem> shown;
        List<ResourceFolderInfo> folders = null;

        if (!foldersEnabled)
        {
            // Video：无文件夹概念，平铺 + 关键字过滤
            shown = FilterByKeyword(allItems, keyword);
            shown = ResourceAssetService.Sort(shown, _state.sortMode);
        }
        else if (searchMode)
        {
            // 搜索模式：跨文件夹全类别范围平铺，显示完整逻辑名（照抄进 Excel 即可）
            shown = FilterByKeyword(allItems, keyword);
            shown = ResourceAssetService.Sort(shown, _state.sortMode);
        }
        else
        {
            // 文件夹视图：当前层文件 + 子文件夹卡片
            folders = ResourceAssetService.ListSubfolders(_state.currentType, folder);
            string prefix = folder.Length == 0 ? "" : folder + "/";
            shown = allItems.FindAll(i =>
            {
                string ln = i.LogicalName ?? i.Name;
                if (string.IsNullOrEmpty(ln)) return false;
                if (folder.Length == 0) return !ln.Contains("/");
                if (!ln.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase)) return false;
                return ln.Substring(prefix.Length).IndexOf('/') < 0;
            });
            shown = ResourceAssetService.Sort(shown, _state.sortMode);
        }
        _currentFolders = folders;
        _items = shown; // 点击/选择触发的重渲染（HandleItemMouseDown 等）依赖 _items，缺了它一点击内容就消失

        var visibleKeys = new HashSet<string>(VisibleSelectionKeys());
        _selected.IntersectWith(visibleKeys);
        if (!visibleKeys.Contains(_lastClickedPath ?? "")) _lastClickedPath = null;
        if (folders != null && _state.sortMode == SortMode.NameDesc) folders.Reverse();
        _content.SetBreadcrumb(_state.currentType, folder, foldersEnabled, searchMode);
        _sidebar.UpdateCounts(CountAllCategories());

        if (shown.Count == 0 && (folders == null || folders.Count == 0))
        {
            string desc = searchMode
                ? "没有匹配的资源。"
                : managed
                    ? "把 Project 窗口中的资产拖到这里即可分配（资产可放在项目内任何位置）。\n" +
                      "也可以直接拖入外部文件（将导入到工作区后自动注册）。\n\n" +
                      $"支持的格式：{UIElementBuilder.GetExtensionDescription(_state.currentType)}"
                    : $"点击「导入文件」或拖入文件到此处。\n\n支持的格式：{UIElementBuilder.GetExtensionDescription(_state.currentType)}";
            _content.ShowEmpty(folder.Length == 0 ? "暂无资源" : "文件夹为空", desc);
            UpdateStatusBar();
            return;
        }

        _content.HideEmpty();
        _content.RenderItems(shown, folders, _state.currentType, _state.viewMode, _state.cardSize, _selected, searchMode);
        UpdateStatusBar();
    }

    private static List<ResourceItem> FilterByKeyword(List<ResourceItem> items, string keyword)
    {
        if (string.IsNullOrEmpty(keyword)) return items;
        string kw = keyword.ToLower();
        return items.FindAll(i => (i.LogicalName ?? i.Name).ToLower().Contains(kw));
    }

    private static string NormalizeFolder(string folder)
    {
        return (folder ?? "").Trim().Trim('/').Replace('\\', '/').Trim('/');
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

    private void UpdateTrashBadge()
    {
        try { _sidebar.UpdateTrashCount(ResourceTrashService.Count); }
        catch { _sidebar.UpdateTrashCount(-1); }
    }

    // ===================== 文件夹导航 / 操作 =====================

    private void HandleFolderNavigate(string target)
    {
        _state.currentFolder = NormalizeFolder(target);
        _state.searchKeyword = "";
        _toolbar.SetSearchValue("");
        ClearSelection();
        RefreshContent();
    }

    private void HandleFolderOpen(ResourceFolderInfo folder)
    {
        HandleFolderNavigate(folder.Path);
    }

    private static bool FolderExists(string folder, string root, List<ResourceItem> items)
    {
        if (!string.IsNullOrEmpty(root) && Directory.Exists(root + "/" + folder)) return true;
        return items.Exists(i => (i.LogicalName ?? i.Name ?? "").StartsWith(folder + "/", System.StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>文件夹右键菜单：打开 / 重命名 / 删除</summary>
    private void HandleFolderContextMenu(ResourceFolderInfo folder, DropdownMenu menu)
    {
        menu.AppendAction("打开", _ => HandleFolderOpen(folder));
        menu.AppendSeparator();
        menu.AppendAction("重命名文件夹…", _ => HandleRenameFolder(folder));
        menu.AppendAction("删除文件夹（含内容，进回收站）…", _ => HandleDeleteFolder(folder));
    }

    /// <summary>面包屑「新建文件夹」/ 空白处右键「新建文件夹」</summary>
    private void HandleNewFolder()
    {
        if (_state.currentType == ResType.Video)
        {
            EditorUtility.DisplayDialog("不支持", "视频类别（StreamingAssets）不支持子文件夹。", "确定");
            return;
        }
        string parent = NormalizeFolder(_state.currentFolder);
        string hint = parent.Length == 0 ? "" : parent + "/";
        VNInputDialogue.Show("新建文件夹",
            $"在「{(parent.Length == 0 ? UIElementBuilder.GetTypeDisplayName(_state.currentType) : parent)}」下新建子文件夹。\n" +
            $"Excel 中以 \"{hint}文件夹名/资源名\" 的形式引用其中的资源（如 {hint}Chapter01/Voice01）。",
            "", "创建", name =>
        {
            string error = ResourceFolderService.CreateSubfolder(_state.currentType, parent, name);
            if (error != null)
            {
                EditorUtility.DisplayDialog("新建失败", error, "确定");
                return;
            }
            RefreshContent();
        });
    }

    /// <summary>文件夹右键：重命名（引用警告 + 物理目录改名 + 外部分配条目批量改地址）</summary>
    private void HandleRenameFolder(ResourceFolderInfo folder)
    {
        VNInputDialogue.Show("重命名文件夹",
            $"重命名「{folder.Path}」。\n资源索引前缀会改变；Excel/CSV 中的旧引用需要手动更新。",
            folder.Name, "重命名", newName =>
        {
            string oldPath = NormalizeFolder(folder.Path);
            newName = (newName ?? "").Trim();
            string invalid = VNAddressablesRegistrar.ValidateLogicalName(newName);
            if (invalid != null || newName.Contains("/")) { EditorUtility.DisplayDialog("名称无效", invalid ?? "一次只能修改一层文件夹名称", "确定"); return; }
            if (newName == folder.Name) return;
            if (!ConfirmFolderReferenceWarning(oldPath, "重命名")) return;
            string error = ResourceFolderService.RenameFolder(_state.currentType, oldPath, newName);
            if (error != null)
            {
                EditorUtility.DisplayDialog("重命名失败", error, "确定");
                return;
            }
            Debug.Log($"[资源管理器] 文件夹已重命名: {oldPath} → {newName}");
            int slash = oldPath.LastIndexOf('/');
            string newPath = slash >= 0 ? oldPath.Substring(0, slash + 1) + newName : newName;
            if (_state.currentFolder == oldPath)
                _state.currentFolder = newPath;
            else if (_state.currentFolder.StartsWith(oldPath + "/", System.StringComparison.OrdinalIgnoreCase))
                _state.currentFolder = newPath + _state.currentFolder.Substring(oldPath.Length);
            ClearSelection();
            RefreshContent();
        });
    }

    /// <summary>文件夹右键：删除（引用警告 + 全部内容进回收站）</summary>
    private void HandleDeleteFolder(ResourceFolderInfo folder)
    {
        if (_selected.Contains(FolderSelectionPrefix + folder.Path) && _selected.Count > 1) { DeleteSelected(); return; }
        if (!ConfirmFolderReferenceWarning(folder.Path, "删除")) return;

        if (!EditorUtility.DisplayDialog("删除文件夹",
                $"将把「{folder.Path}」及其全部内容移入回收站。\n（含 {folder.ItemCount} 个文件" +
                (folder.SubfolderCount > 0 ? $" + {folder.SubfolderCount} 个子文件夹" : "") +
                "；外部分配条目仅移除分配，物理文件保留）\n\n可随时在回收站中恢复。",
                "删除", "取消"))
            return;

        string error = ResourceFolderService.DeleteFolder(_state.currentType, folder.Path, out int physical, out int unassigned);
        if (error != null)
        {
            EditorUtility.DisplayDialog("删除失败", error, "确定");
            return;
        }
        Debug.Log($"[资源管理器] 文件夹已移入回收站: {folder.Path}（{physical} 个文件" +
                  (unassigned > 0 ? $" + {unassigned} 个外部分配" : "") + "）");

        // 当前浏览位置在被删文件夹内 → 退回上级
        string deleted = NormalizeFolder(folder.Path);
        if (_state.currentFolder == deleted || _state.currentFolder.StartsWith(deleted + "/"))
        {
            int slash = deleted.LastIndexOf('/');
            _state.currentFolder = slash > 0 ? deleted.Substring(0, slash) : "";
        }
        ClearSelection();
        RefreshContent();
    }

    /// <summary>拖拽移动：文件卡片拖到文件夹卡片 / 上下文菜单「移动到」</summary>
    private void HandleItemsMoveToFolder(List<string> assetPaths, string targetFolder)
    {
        var items = _items.FindAll(i => assetPaths.Contains(i.AssetPath));
        if (items.Count == 0) return;
        MoveItems(items, NormalizeFolder(targetFolder));
    }

    private void MoveItems(List<ResourceItem> items, string targetFolder, bool referencesConfirmed = false)
    {
        if (_trashMode || _state.currentType == ResType.Video) return;
        targetFolder = NormalizeFolder(targetFolder);
        items = items.FindAll(i => ParentLogicalFolder(i) != targetFolder);
        if (items.Count == 0) return;
        string error = ResourceFolderService.ValidateMoveItems(items, _state.currentType, targetFolder);
        if (error != null) { EditorUtility.DisplayDialog("不能移动", error, "确定"); return; }
        if (!referencesConfirmed && !ConfirmItemReferenceWarning(items, "移动")) return;
        error = ResourceFolderService.MoveItemsToFolder(items, _state.currentType, targetFolder);
        if (error != null) EditorUtility.DisplayDialog("移动失败", error, "确定");
        else
        {
            ClearSelection();
            Debug.Log($"[资源管理器] 已移动 {items.Count} 项到「{(targetFolder.Length == 0 ? "类别根" : targetFolder)}」");
        }
        RefreshContent();
    }

    private static string ParentLogicalFolder(ResourceItem item)
    {
        string name = (item.LogicalName ?? item.Name ?? "").Replace('\\', '/');
        int slash = name.LastIndexOf('/');
        return slash >= 0 ? name.Substring(0, slash) : "";
    }

    /// <summary>外部文件 / Project 资产拖到文件夹卡片（导入/分配落位到该文件夹）</summary>
    private void HandleExternalDropIntoFolder(string[] paths, string targetFolder)
    {
        HandleDroppedPaths(paths, targetFolder);
    }

    // ===================== 选中操作 =====================
    private void HandleItemMouseDown(ResourceItem item, int clickCount)
    {
        if (_trashMode) return;
        if (clickCount == ResourceContentView.DeleteAction)
        {
            if (_selected.Contains(item.AssetPath)) DeleteSelected();
            else DeleteItems(new List<ResourceItem> { item });
            return;
        }
        HandleItemPointerDown(item, clickCount, 0, false, false);
    }

    private void HandleItemPointerDown(ResourceItem item, int clickCount, int button, bool ctrl, bool shift)
    {
        if (_trashMode || (button != 0 && button != 1)) return;
        SelectKey(item.AssetPath, button, ctrl, shift);
        _state.lastSelectedAssetPath = item.AssetPath;
        if (button == 0 && item.Asset != null)
        {
            Selection.activeObject = item.Asset;
            EditorGUIUtility.PingObject(item.Asset);
        }
        if (button == 0 && clickCount == 2)
        {
            if (item.Asset != null) AssetDatabase.OpenAsset(item.Asset);
            else if (File.Exists(item.FullPath)) EditorUtility.RevealInFinder(item.FullPath);
        }
    }

    private void HandleItemPointerUp(ResourceItem item, int button, bool ctrl, bool shift)
    {
        if (_trashMode || button != 0 || ctrl || shift || _pressedSelectionKey != item.AssetPath) return;
        if (_selected.Count > 1)
        {
            _selected.Clear();
            _selected.Add(item.AssetPath);
            SelectionChanged();
        }
        _pressedSelectionKey = null;
    }

    private void HandleFolderPointerDown(ResourceFolderInfo folder, int clickCount, int button, bool ctrl, bool shift)
    {
        if (_trashMode || (button != 0 && button != 1)) return;
        SelectKey(FolderSelectionPrefix + folder.Path, button, ctrl, shift);
    }

    private void HandleFolderPointerUp(ResourceFolderInfo folder, int button, bool ctrl, bool shift)
    {
        string key = FolderSelectionPrefix + folder.Path;
        if (_trashMode || button != 0 || ctrl || shift || _pressedSelectionKey != key) return;
        _selected.Clear();
        _selected.Add(key);
        _pressedSelectionKey = null;
        SelectionChanged();
    }

    private void SelectKey(string key, int button, bool ctrl, bool shift)
    {
        List<string> order = VisibleSelectionKeys();
        int target = order.IndexOf(key);
        int anchor = order.IndexOf(_lastClickedPath);
        if (target < 0) return;
        if (button == 1)
        {
            if (!_selected.Contains(key)) { _selected.Clear(); _selected.Add(key); }
        }
        else if (shift && anchor >= 0)
        {
            if (!ctrl) _selected.Clear();
            for (int i = System.Math.Min(anchor, target); i <= System.Math.Max(anchor, target); i++)
                _selected.Add(order[i]);
        }
        else if (ctrl)
        {
            if (!_selected.Add(key)) _selected.Remove(key);
            _lastClickedPath = key;
        }
        else
        {
            if (!_selected.Contains(key)) { _selected.Clear(); _selected.Add(key); }
            _lastClickedPath = key;
            _pressedSelectionKey = key;
        }
        SelectionChanged();
    }

    private List<string> VisibleSelectionKeys()
    {
        var keys = new List<string>();
        if (_currentFolders != null)
            foreach (var folder in _currentFolders) keys.Add(FolderSelectionPrefix + folder.Path);
        foreach (var item in _items) keys.Add(item.AssetPath);
        return keys;
    }

    private void SelectionChanged()
    {
        _content.UpdateSelection(_selected);
        UpdateStatusBar();
    }

    private void HandleItemPlay(ResourceItem item)
    {
        if (item.Asset is AudioClip clip)
        {
            _audioPlayer.LoadAndPlay(clip, item.DisplayName);
            UpdateStatusBar();
        }
    }

    public void ClearSelection()
    {
        _selected.Clear();
        _lastClickedPath = null;
        _pressedSelectionKey = null;
        _state.lastSelectedAssetPath = "";
        SelectionChanged();
    }

    public void SelectAll()
    {
        if (_trashMode) return;
        _selected.Clear();
        foreach (string key in VisibleSelectionKeys()) _selected.Add(key);
        SelectionChanged();
    }

    private bool IsSearchMode()
    {
        return _state.currentType != ResType.Video && !string.IsNullOrEmpty((_state.searchKeyword ?? "").Trim());
    }

    public void DeleteSelected()
    {
        if (_trashMode) return;
        var folders = (_currentFolders ?? new List<ResourceFolderInfo>()).FindAll(f => _selected.Contains(FolderSelectionPrefix + f.Path));
        var files = _items.FindAll(i => _selected.Contains(i.AssetPath));
        if (folders.Count == 0) { if (files.Count > 0) DeleteItems(files); return; }
        DeleteMixedSelection(files, folders);
    }

    private void DeleteMixedSelection(List<ResourceItem> files, List<ResourceFolderInfo> folders)
    {
        string root = ResourceAssetService.GetPathFromConfig(_state.currentType);
        string category = VNAddressablesRegistrar.GetCategoryKey(_state.currentType);
        var paths = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        var unassign = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        var directories = new List<string>();
        var affected = new List<ResourceItem>(files);
        var all = ResourceAssetService.LoadAssets(_state.currentType, null);
        try
        {
            foreach (var folder in folders)
            {
                string directory = root + "/" + folder.Path;
                if (Directory.Exists(directory))
                {
                    directories.Add(directory);
                    foreach (var file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
                        if (!file.EndsWith(".meta", System.StringComparison.OrdinalIgnoreCase)) paths.Add(file.Replace('\\', '/'));
                }
                foreach (var item in all)
                {
                    if (!(item.LogicalName ?? item.Name).StartsWith(folder.Path + "/", System.StringComparison.OrdinalIgnoreCase)) continue;
                    if (!affected.Exists(i => i.AssetPath == item.AssetPath)) affected.Add(item);
                    if (!item.AssetPath.StartsWith(directory + "/", System.StringComparison.OrdinalIgnoreCase)) unassign.Add(item.AssetPath);
                }
            }
            foreach (var item in files)
            {
                bool external = !item.AssetPath.StartsWith(root + "/", System.StringComparison.OrdinalIgnoreCase);
                if (item.AssetPath.StartsWith("Packages/", System.StringComparison.OrdinalIgnoreCase) || external) unassign.Add(item.AssetPath);
                else paths.Add(item.AssetPath);
            }
            paths.ExceptWith(unassign);
            if (!ConfirmItemReferenceWarning(affected, "删除")) return;
            if (!EditorUtility.DisplayDialog("删除选中内容", $"将 {folders.Count} 个文件夹及 {files.Count} 个文件移入回收站。\n父文件夹内容只处理一次，外部分配资产仅移除分配。\n\n此操作不会自动修改 Excel/CSV。", "移入回收站", "取消")) return;
            string error = ResourceTrashService.TrashPaths(new List<string>(paths), "选中的文件与文件夹", _state.currentType,
                category, new List<string>(unassign), directories);
            if (error != null) EditorUtility.DisplayDialog("删除失败", error, "确定");
            ClearSelection();
            RefreshContent();
        }
        catch (System.Exception e) { EditorUtility.DisplayDialog("删除失败", e.Message, "确定"); }
    }

    // ===================== 删除（引用警告 + 回收站） =====================

    private void DeleteItems(List<ResourceItem> items)
    {
        if (items.Count == 0) return;

        // 引用警告：删除前扫描剧本引用（Voice/BGM/背景列 + playvoice 等命令参数）
        if (!ConfirmItemReferenceWarning(items, "删除")) return;

        // Addressables 托管模式：区分"移除分配"与"删除文件"
        bool managed = VNAddressablesRegistrar.IsManagedMode && _state.currentType != ResType.Video;
        if (managed)
        {
            string list = string.Join("\n  ", items.ConvertAll(i => i.DisplayName));
            int option = EditorUtility.DisplayDialogComplex("移除资源",
                items.Count == 1
                    ? $"要如何移除「{items[0].DisplayName}」？"
                    : $"要如何移除这 {items.Count} 个资源？\n\n  {list}",
                "移除分配（保留文件）", "取消", "删除文件（移入回收站）");

            if (option == 1) return;

            if (option == 0)
            {
                string error = ResourceTrashService.TrashPaths(new List<string>(),
                    items.Count == 1 ? items[0].DisplayName : $"{items.Count} 个资源的分配",
                    _state.currentType, VNAddressablesRegistrar.GetCategoryKey(_state.currentType),
                    items.ConvertAll(i => i.AssetPath));
                if (error != null) EditorUtility.DisplayDialog("移除分配失败", error, "确定");
                ClearSelection();
                RefreshContent();
                return;
            }
            // option == 2：删除文件 → 直接进回收站（上方对话框已确认意图）
        }
        else
        {
            string fileList = string.Join("\n  ", items.ConvertAll(i => i.DisplayName));
            if (!EditorUtility.DisplayDialog("删除确认",
                items.Count == 1
                    ? $"确定要删除「{items[0].DisplayName}」吗？\n\n文件将移入回收站，可随时恢复。"
                    : $"确定要删除这 {items.Count} 个资源吗？\n\n  {fileList}\n\n文件将移入回收站，可随时恢复。",
                items.Count == 1 ? "删除" : "全部删除", "取消"))
                return;
        }

        TrashItems(items);
    }

    /// <summary>右键菜单"删除文件"专用：跳过"移除分配"分支，直接确认 + 进回收站</summary>
    private void DeleteItemsManagedBypass(List<ResourceItem> items)
    {
        if (items.Count == 0) return;
        if (!ConfirmItemReferenceWarning(items, "删除")) return;

        string fileList = string.Join("\n  ", items.ConvertAll(i => i.DisplayName));
        if (EditorUtility.DisplayDialog("删除确认",
            items.Count == 1
                ? $"确定要删除「{items[0].DisplayName}」吗？\n\n文件将移入回收站，可随时恢复。"
                : $"确定要删除这 {items.Count} 个资源吗？\n\n  {fileList}\n\n文件将移入回收站，可随时恢复。",
            items.Count == 1 ? "删除" : "全部删除", "取消"))
        {
            TrashItems(items);
        }
    }

    /// <summary>把条目移入回收站（文件 + .meta，GUID 保留，可恢复）</summary>
    private void TrashItems(List<ResourceItem> items)
    {
        string title = items.Count == 1 ? items[0].DisplayName : $"{items.Count} 个资源";
        string error = ResourceTrashService.TrashPaths(
            items.ConvertAll(i => i.AssetPath), title,
            _state.currentType, VNAddressablesRegistrar.GetCategoryKey(_state.currentType));
        if (error != null)
        {
            EditorUtility.DisplayDialog("删除失败", error, "确定");
            return;
        }
        foreach (var item in items) _selected.Remove(item.AssetPath);
        RefreshContent();
    }

    // ===================== 引用警告（删除/重命名防呆） =====================

    /// <summary>条目删除/重命名前的引用警告。返回 false = 用户取消。</summary>
    private bool ConfirmItemReferenceWarning(List<ResourceItem> items, string actionLabel)
    {
        var scan = ScriptReferenceScanner.ScanAll();
        if (!ConfirmScanComplete(scan)) return false;
        var refs = new List<ScriptReferenceScanner.ScriptResourceReference>();
        var seen = new HashSet<ScriptReferenceScanner.ScriptResourceReference>();
        foreach (var item in items)
        {
            string logicalName = item.LogicalName ?? item.Name;
            if (string.IsNullOrEmpty(logicalName)) continue;
            foreach (var reference in ScriptReferenceScanner.FindReferences(scan, _state.currentType, logicalName))
                if (seen.Add(reference)) refs.Add(reference);
        }
        return ConfirmReferenceWarning(refs, actionLabel);
    }

    /// <summary>文件夹删除/重命名前的引用警告（前缀匹配）。返回 false = 用户取消。</summary>
    private bool ConfirmFolderReferenceWarning(string folderPath, string actionLabel)
    {
        var scan = ScriptReferenceScanner.ScanAll();
        return ConfirmScanComplete(scan) && ConfirmReferenceWarning(
            ScriptReferenceScanner.FindReferencesUnderFolder(scan, _state.currentType, folderPath), actionLabel);
    }

    private static bool ConfirmScanComplete(ResourceReferenceScanResult scan)
    {
        if (scan.IsComplete) return true;
        ResourceRefReportWindow.Show("引用核验未完成", "部分剧本或资源无法核验；本次操作已取消，不会修改文件或索引。", scan);
        EditorUtility.DisplayDialog("无法完成引用核验", string.Join("\n", scan.Errors) + "\n\n请处理以上问题后重试。", "确定");
        return false;
    }

    private bool ConfirmReferenceWarning(List<ScriptReferenceScanner.ScriptResourceReference> refs, string actionLabel)
    {
        if (refs == null || refs.Count == 0) return true;

        string preview = string.Join("\n", GetPreviewLines(refs, 8));
        string more = refs.Count > 8 ? $"\n…" : "";
        string full = string.Join("\n", GetPreviewLines(refs, int.MaxValue));
        Debug.Log($"[资源管理器] {actionLabel}将影响 {refs.Count} 处剧本引用：\n{full}");

        // ok(0)=继续, cancel(1)=取消, alt(2)=查看详情
        int option = EditorUtility.DisplayDialogComplex("剧本引用警告",
            $"{actionLabel}后以下 {refs.Count} 处剧本引用将失效（Excel 中的名字需同步修改）：\n\n{preview}{more}\n\n完整清单已输出到控制台。",
            $"仍然{actionLabel}", "取消", "查看详情");
        if (option == 2)
        {
            ResourceRefReportWindow.Show("受影响的剧本引用", $"{actionLabel}后将失效的引用",
                refs);
            return false;
        }
        return option == 0;
    }

    private static IEnumerable<string> GetPreviewLines(List<ScriptReferenceScanner.ScriptResourceReference> refs, int max)
    {
        int count = 0;
        foreach (var r in refs)
        {
            if (count++ >= max) yield break;
            yield return $"《{r.ScriptName}》 行 {r.LineId} · {r.Source}: {r.Name}";
        }
    }

    /// <summary>「检查缺失引用」：报告引用了不存在资源的所有行</summary>
    private void CheckMissingReferences()
    {
        var scan = ScriptReferenceScanner.ScanAll();
        var missing = ScriptReferenceScanner.FindMissing(scan);
        var potential = ScriptReferenceScanner.FindMissing(scan, true).FindAll(r => r.Potential);
        if (missing.Count == 0 && scan.IsComplete)
            EditorUtility.DisplayDialog("检查完成", $"未发现明确引用的缺失资源。\n另有 {potential.Count} 处可选自动配音依赖，未配音不算错误。", "确定");
        var report = new List<ScriptReferenceScanner.ScriptResourceReference>(missing);
        report.AddRange(potential);
        if (report.Count > 0 || !scan.IsComplete)
            ResourceRefReportWindow.Show("资源引用核验", "明确缺失与潜在自动配音分别展示；扫描失败不能视为全部通过。", report, scan);
    }

    // ===================== 条目操作（逻辑名） =====================

    /// <summary>右键菜单：重命名/移除分配/移动到/删除（向 evt.menu 追加动作，由 Presenter 决定内容）</summary>
    private void HandleItemContextMenu(ResourceItem item, DropdownMenu menu)
    {
        bool managed = VNAddressablesRegistrar.IsManagedMode && _state.currentType != ResType.Video;

        if (managed)
        {
            menu.AppendAction("重命名（逻辑名，Excel 索引名）", _ => RenameItem(item));
            menu.AppendAction("移除分配（保留文件）", _ => DeleteItems(new List<ResourceItem> { item }));
        }
        else
        {
            menu.AppendAction("重命名（文件）…", _ => RenameFile(item));
        }

        // 移动到文件夹（子菜单：类别根 + 全部子文件夹 + 新建文件夹）
        if (_state.currentType != ResType.Video)
        {
            var targets = new List<ResourceItem> { item };
            if (_selected.Contains(item.AssetPath) && _selected.Count > 1)
                targets = _items.FindAll(i => _selected.Contains(i.AssetPath));

            menu.AppendAction("移动到/（类别根）", _ => MoveItems(targets, ""));
            var pending = new Queue<string>();
            var visited = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase) { "" };
            pending.Enqueue("");
            while (pending.Count > 0)
            {
                foreach (var folder in ResourceAssetService.ListSubfolders(_state.currentType, pending.Dequeue()))
                {
                    if (!visited.Add(folder.Path)) continue;
                    string target = folder.Path;
                    menu.AppendAction($"移动到/{target}/移入此文件夹", _ => MoveItems(targets, target));
                    pending.Enqueue(target);
                }
            }
            menu.AppendAction("移动到/新建文件夹…", _ => CreateFolderAndMove(targets));
        }
        menu.AppendSeparator();
        menu.AppendAction("删除文件（移入回收站）", _ =>
        {
            if (_selected.Contains(item.AssetPath) && _selected.Count > 1)
            {
                if ((_currentFolders ?? new List<ResourceFolderInfo>()).Exists(f => _selected.Contains(FolderSelectionPrefix + f.Path))) DeleteSelected();
                else DeleteItemsManagedBypass(_items.FindAll(i => _selected.Contains(i.AssetPath)));
            }
            else DeleteItemsManagedBypass(new List<ResourceItem> { item });
        });
    }

    /// <summary>重命名逻辑名（改 Addressables 地址尾段，支持 Chapter01/Voice01 子文件夹路径）</summary>
    private void RenameItem(ResourceItem item)
    {
        VNInputDialogue.Show("重命名逻辑名",
            $"剧本/Excel 中索引该资源所用的名字（原文件名不受影响，可用 / 表示子文件夹）。\n当前：{item.DisplayName}",
            item.DisplayName, "重命名", newName =>
        {
            newName = (newName ?? "").Replace('\\', '/').Trim().Trim('/');
            if (newName == item.DisplayName) return;
            string invalid = VNAddressablesRegistrar.ValidateLogicalName(newName);
            if (invalid != null) { EditorUtility.DisplayDialog("名称无效", invalid, "确定"); return; }
            string key = VNAddressablesRegistrar.GetCategoryKey(_state.currentType) + "/" + newName;
            string conflict = VNAddressablesRegistrar.ValidateAddressChanges(new Dictionary<string, string> { { item.AssetPath, key } });
            if (conflict != null) { EditorUtility.DisplayDialog("名称冲突", conflict, "确定"); return; }
            if (!ConfirmItemReferenceWarning(new List<ResourceItem> { item }, "重命名")) return;
            string error = VNAddressablesRegistrar.RenameAssignment(item.AssetPath, newName);
            if (error != null)
            {
                EditorUtility.DisplayDialog("重命名失败", error, "确定");
                return;
            }
            Debug.Log($"[资源管理器] 逻辑名已重命名: {item.DisplayName} → {newName}（文件未改动）");
            RefreshContent();
        });
    }

    /// <summary>文件夹模式下重命名物理文件</summary>
    private void RenameFile(ResourceItem item)
    {
        VNInputDialogue.Show("重命名文件",
            $"重命名文件（物理文件与引用名同步更新）。\n当前：{item.DisplayName}",
            item.Name, "重命名", newName =>
        {
            newName = (newName ?? "").Trim();
            string invalid = VNAddressablesRegistrar.ValidateLogicalName(newName);
            if (invalid != null || newName.Contains("/")) { EditorUtility.DisplayDialog("名称无效", invalid ?? "文件名不能包含路径分隔符", "确定"); return; }
            if (newName == item.Name) return;
            string dir = Path.GetDirectoryName(item.AssetPath)?.Replace('\\', '/');
            string dst = dir + "/" + newName + Path.GetExtension(item.AssetPath);
            if (File.Exists(dst)) { EditorUtility.DisplayDialog("名称冲突", "目标文件已存在", "确定"); return; }
            if (!ConfirmItemReferenceWarning(new List<ResourceItem> { item }, "重命名")) return;
            string error;
            if (_state.currentType == ResType.Video)
            {
                try
                {
                    File.Move(item.AssetPath, dst);
                    try { if (File.Exists(item.AssetPath + ".meta")) File.Move(item.AssetPath + ".meta", dst + ".meta"); }
                    catch { File.Move(dst, item.AssetPath); throw; }
                    AssetDatabase.Refresh();
                    error = null;
                }
                catch (System.Exception e) { error = e.Message; }
            }
            else error = AssetDatabase.RenameAsset(item.AssetPath, newName);
            if (!string.IsNullOrEmpty(error))
            {
                EditorUtility.DisplayDialog("重命名失败", error, "确定");
                return;
            }
            Debug.Log($"[资源管理器] 文件已重命名 → {dst}");
            RefreshContent();
        });
    }

    /// <summary>「移动到/新建文件夹…」：先建文件夹再移动</summary>
    private void CreateFolderAndMove(List<ResourceItem> items)
    {
        string parent = NormalizeFolder(_state.currentFolder);
        VNInputDialogue.Show("新建文件夹并移入",
            $"新建子文件夹并把 {items.Count} 个资源移入其中。",
            "", "创建并移入", name =>
        {
            name = (name ?? "").Trim();
            string invalid = VNAddressablesRegistrar.ValidateLogicalName(name);
            if (invalid != null || name.Contains("/")) { EditorUtility.DisplayDialog("名称无效", invalid ?? "请填写单层文件夹名", "确定"); return; }
            string target = parent.Length == 0 ? name : parent + "/" + name;
            string error = ResourceFolderService.ValidateMoveItems(items, _state.currentType, target);
            if (error != null) { EditorUtility.DisplayDialog("不能移动", error, "确定"); return; }
            if (!ConfirmItemReferenceWarning(items, "移动")) return;
            error = ResourceFolderService.CreateSubfolder(_state.currentType, parent, name);
            if (error != null) { EditorUtility.DisplayDialog("新建失败", error, "确定"); return; }
            MoveItems(items, target, true);
        });
    }

    // ===================== 空白处右键 =====================

    private void HandleBackgroundContextMenu(DropdownMenu menu)
    {
        if (_state.currentType != ResType.Video)
        {
            // Video（StreamingAssets 独立链路）不支持子文件夹——不显示该项；
            // 面包屑的「新建文件夹」按钮仍可见，点击会被 HandleNewFolder 弹窗拦截
            menu.AppendAction("新建文件夹…", _ => HandleNewFolder());
        }
        menu.AppendSeparator();
        menu.AppendAction("导入文件到当前文件夹…", _ => HandleImportFile());
        menu.AppendAction("导入文件夹到当前文件夹…", _ => HandleImportFolder());
        menu.AppendSeparator();
        menu.AppendAction("检查缺失引用…", _ => CheckMissingReferences());
    }

    // ===================== 导入（落位当前文件夹） =====================

    private string CurrentImportSubfolder()
    {
        return _state.currentType == ResType.Video ? "" : NormalizeFolder(_state.currentFolder);
    }

    private void HandleImportFile()
    {
        if (_trashMode) ExitTrashMode();
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

        var result = ResourceImportService.ImportSingleFile(_state.currentType, srcPath, CurrentImportSubfolder(), ConfirmImportOverwrite);
        if (result.Success)
        {
            AssetDatabase.Refresh();
            // 导入落位后注册进 Addressables（未初始化的项目自动跳过）
            VNAddressablesRegistrar.SyncWorkspace();
            RefreshContent();
        }
        else if (!result.WasSkipped && !string.IsNullOrEmpty(result.Error))
        {
            EditorUtility.DisplayDialog("导入失败", result.Error, "确定");
        }
    }

    private void HandleImportFolder()
    {
        if (_trashMode) ExitTrashMode();
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

        // 【子文件夹支持】落位到当前文件夹：所选文件夹的子结构整体挂到当前文件夹之下
        string sub = CurrentImportSubfolder();
        string destDir = string.IsNullOrEmpty(sub)
            ? targetAssetPath
            : targetAssetPath + "/" + sub.Replace('/', Path.DirectorySeparatorChar);
        var result = ResourceImportService.ImportDirectory(_state.currentType, srcDir, destDir);
        FinishImport(result.Imported);
        EditorUtility.DisplayDialog("目录导入结果", result.Message +
            (_state.currentType == ResType.Video ? "\n\n视频采用平铺导入，不创建子目录。" : "\n\n保留所选目录内部结构，落位到当前文件夹。"), "确定");
    }

    /// <summary>
    /// 拖放统一入口（双模式分流，targetFolder = 落位目标文件夹，null = 当前浏览文件夹）：
    /// - Project 窗口资产（路径在 Assets/ 下）→ 物理在类别文件夹内则移动，否则拖放分配
    ///   （注册进 Addressables，赋 folder/文件名 逻辑名——文件留在原位）；
    /// - OS 外部文件/文件夹 → 导入复制到目标文件夹（托管模式下导入到工作区并自动注册）。
    /// </summary>
    public void HandleDroppedPaths(string[] paths, string targetFolder = null)
    {
        if (paths == null || paths.Length == 0) return;
        if (_trashMode) ExitTrashMode();

        string folder = _state.currentType == ResType.Video ? "" : NormalizeFolder(targetFolder ?? _state.currentFolder);

        foreach (var p in paths)
        {
            bool isProjectAsset = p != null
                && (p.StartsWith("Assets/", System.StringComparison.OrdinalIgnoreCase)
                 || p.StartsWith("Packages/", System.StringComparison.OrdinalIgnoreCase));
            if (isProjectAsset) HandleDroppedProjectAsset(p, folder);
            else ImportSingleExternalFile(p, folder);
        }
    }

    /// <summary>Project 资产拖放：物理在类别文件夹内 → 移动到目标文件夹；否则分配逻辑名（文件不动）</summary>
    private void HandleDroppedProjectAsset(string assetPath, string folder)
    {
        if (_state.currentType == ResType.Video)
        {
            ImportSingleExternalFile(assetPath, "");
            return;
        }
        if (AssetDatabase.IsValidFolder(assetPath))
        {
            ImportSingleExternalFile(assetPath, folder);
            return;
        }
        string categoryRoot = ResourceAssetService.GetPathFromConfig(_state.currentType);
        bool physicallyInCategory = !string.IsNullOrEmpty(categoryRoot) &&
            assetPath.StartsWith(categoryRoot + "/", System.StringComparison.OrdinalIgnoreCase);
        var item = ResourceAssetService.LoadAssets(_state.currentType, null).Find(i => i.AssetPath == assetPath);
        if (string.IsNullOrEmpty(item.AssetPath))
            item = new ResourceItem { AssetPath = assetPath, Name = Path.GetFileNameWithoutExtension(assetPath),
                LogicalName = ResourceAssetService.GetRelativeLogicalName(categoryRoot, assetPath) };
        if (physicallyInCategory)
        {
            MoveItems(new List<ResourceItem> { item }, folder);
            return;
        }
        string tail = item.LogicalName ?? item.Name;
        int slash = tail.LastIndexOf('/');
        if (slash >= 0) tail = tail.Substring(slash + 1);
        string logical = folder.Length == 0 ? tail : folder + "/" + tail;
        var oldReference = FindAssignedIdentity(assetPath);
        string newKey = VNAddressablesRegistrar.GetCategoryKey(_state.currentType) + "/" + logical;
        if (oldReference != null && oldReference.address == newKey) return;
        string error = VNAddressablesRegistrar.ValidateAddressChanges(new Dictionary<string, string> { { assetPath, newKey } });
        if (error != null) { EditorUtility.DisplayDialog("分配冲突", error, "确定"); return; }
        if (oldReference != null && !ConfirmIdentityReferenceWarning(oldReference, "重新分配")) return;
        error = VNAddressablesRegistrar.AssignToCategory(_state.currentType, assetPath, logical);
        if (error != null) EditorUtility.DisplayDialog("分配失败", $"{assetPath}\n{error}", "确定");
        RefreshContent();
    }

    private UnityEditor.AddressableAssets.Settings.AddressableAssetEntry FindAssignedIdentity(string assetPath)
    {
        foreach (ResType type in System.Enum.GetValues(typeof(ResType)))
        {
            if (type == ResType.Video) continue;
            foreach (var entry in VNAddressablesRegistrar.GetCategoryEntries(VNAddressablesRegistrar.GetCategoryKey(type)))
                if (entry.AssetPath == assetPath) return entry;
        }
        return null;
    }

    private bool ConfirmIdentityReferenceWarning(UnityEditor.AddressableAssets.Settings.AddressableAssetEntry entry, string action)
    {
        var scan = ScriptReferenceScanner.ScanAll();
        if (!ConfirmScanComplete(scan)) return false;
        foreach (ResType type in System.Enum.GetValues(typeof(ResType)))
        {
            if (type == ResType.Video) continue;
            string category = VNAddressablesRegistrar.GetCategoryKey(type);
            if (entry.address.StartsWith(category + "/", System.StringComparison.OrdinalIgnoreCase))
                return ConfirmReferenceWarning(ScriptReferenceScanner.FindReferences(scan, type, entry.address.Substring(category.Length + 1)), action);
        }
        return true;
    }

    /// <summary>外部文件/文件夹导入（托管模式导入后自动注册）</summary>
    private void ImportSingleExternalFile(string src, string folder)
    {
        string target = ResourceAssetService.GetPathFromConfig(_state.currentType);
        if (string.IsNullOrEmpty(target)) { EditorUtility.DisplayDialog("导入失败", "未配置目标路径", "确定"); return; }
        if (Directory.Exists(src))
        {
            string destDir = folder.Length == 0 ? target : target + "/" + folder;
            var result = ResourceImportService.ImportDirectory(_state.currentType, src, destDir);
            FinishImport(result.Imported);
            EditorUtility.DisplayDialog("目录导入结果", result.Message, "确定");
        }
        else
        {
            var result = ResourceImportService.ImportSingleFile(_state.currentType, src, folder, ConfirmImportOverwrite);
            FinishImport(result.Success ? 1 : 0);
            if (!result.Success && !string.IsNullOrEmpty(result.Error))
                EditorUtility.DisplayDialog(result.WasSkipped ? "导入已跳过" : "导入失败", result.Error, "确定");
        }
    }

    private bool ConfirmImportOverwrite(string assetPath)
    {
        var item = ResourceAssetService.LoadAssets(_state.currentType, null).Find(i => i.AssetPath == assetPath);
        if (!string.IsNullOrEmpty(item.AssetPath) && !ConfirmItemReferenceWarning(new List<ResourceItem> { item }, "覆盖")) return false;
        return EditorUtility.DisplayDialog("覆盖已有文件", $"将替换 {assetPath} 的文件内容，保留现有 GUID。\n请确认已有备份。", "覆盖", "跳过");
    }

    private void FinishImport(int imported)
    {
        if (imported <= 0) return;
        AssetDatabase.Refresh();
        if (_state.currentType != ResType.Video) VNAddressablesRegistrar.SyncWorkspace();
        RefreshContent();
        Debug.Log($"[资源管理器] 已导入 {imported} 个文件");
    }

    private void CreateFolder(string path)
    {
        if (ResourceImportService.CreateFolderIfMissing(path))
        {
            RefreshContent();
            Debug.Log($"[资源管理器] 创建文件夹: {path}");
        }
    }

    // ===================== 回收站 =====================

    private void EnterTrashMode()
    {
        ClearSelection();
        _items.Clear();
        _currentFolders = null;
        _trashMode = true;
        _audioPlayer.Hide();
        _sidebar.SetTrashActive(true);
        RefreshContent();
    }

    private void ExitTrashMode()
    {
        _trashMode = false;
        _sidebar.SetTrashActive(false);
        _sidebar.SetSelectedCategory(_state.currentType);
        RefreshContent();
    }

    private void HandleTrashRestore(string id)
    {
        string error = ResourceTrashService.Restore(id);
        if (error != null)
        {
            EditorUtility.DisplayDialog("恢复失败", error, "确定");
            return;
        }
        Debug.Log("[资源管理器] 已从回收站恢复（原地址重新注册）");
        RefreshContent();
    }

    private void HandleTrashDeletePermanent(string id)
    {
        if (!EditorUtility.DisplayDialog("彻底删除", "将从回收站永久删除该条目（不可恢复）。", "彻底删除", "取消"))
            return;
        string error = ResourceTrashService.DeletePermanent(id);
        if (error != null)
        {
            EditorUtility.DisplayDialog("删除失败", error, "确定");
            return;
        }
        RefreshContent();
    }

    private void HandleTrashClearAll()
    {
        int count;
        try { count = ResourceTrashService.Count; }
        catch (System.Exception e) { _trashPanel.ShowError(e.Message); return; }
        if (count == 0) return;
        if (!EditorUtility.DisplayDialog("清空回收站",
                $"将永久删除全部 {count} 个条目（不可恢复）。", "清空", "取消"))
            return;
        string error = ResourceTrashService.ClearAll();
        if (error != null)
        {
            EditorUtility.DisplayDialog("清空失败", error, "确定");
            return;
        }
        RefreshContent();
    }

    private void HandleTrashRepairIndex()
    {
        if (!EditorUtility.DisplayDialog("修复回收站索引", "将从保留的备份及条目元数据恢复索引。\n不会删除任何孤立备份，也不会移动真实资源。", "修复索引", "取消")) return;
        string error = ResourceTrashService.RepairIndex();
        if (error != null) EditorUtility.DisplayDialog("修复失败", error, "确定");
        RefreshContent();
    }

    private string CurrentPhysicalPath()
    {
        string root = ResourceAssetService.GetPathFromConfig(_state.currentType);
        return string.IsNullOrEmpty(root) ? "" : CurrentImportSubfolder().Length == 0 ? root : root + "/" + CurrentImportSubfolder();
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
        menu.AddItem(new GUIContent("检查缺失引用…"), false, () => CheckMissingReferences());
        menu.AddSeparator("");
        menu.AddItem(new GUIContent("在文件管理器中显示当前路径"), false, () =>
        {
            string p = CurrentPhysicalPath();
            if (Directory.Exists(p)) EditorUtility.RevealInFinder(p);
            else EditorUtility.DisplayDialog("提示", $"文件夹不存在：\n{p}", "确定");
        });
        menu.AddItem(new GUIContent("复制当前路径到剪贴板"), false, () =>
        {
            EditorGUIUtility.systemCopyBuffer = CurrentPhysicalPath();
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
            _trashMode ? "回收站（普通资源快捷键已隔离）" : CurrentPhysicalPath());
    }

    // ===================== 键盘事件 =====================
    public bool OnKeyDown(KeyCode key, bool ctrl, bool shift)
    {
        if (_trashMode)
        {
            if (key == KeyCode.Escape) { ExitTrashMode(); return true; }
            return false;
        }
        if (key == KeyCode.A && ctrl) { SelectAll(); return true; }
        if (key == KeyCode.Delete && _selected.Count > 0) { DeleteSelected(); return true; }
        if (key == KeyCode.Escape) { ClearSelection(); return true; }
        return false;
    }

    public void HandleInternalDrop(List<string> assetPaths)
    {
        if (!_trashMode) HandleItemsMoveToFolder(assetPaths, CurrentImportSubfolder());
    }
}
