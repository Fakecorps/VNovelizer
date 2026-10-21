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
            _trashPanel.Refresh(ResourceTrashService.ListEntries());
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
        string folder = NormalizeFolder(_state.currentFolder);

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

        _content.SetBreadcrumb(_state.currentType, folder, foldersEnabled);
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
        _sidebar.UpdateTrashCount(ResourceTrashService.Count);
    }

    // ===================== 文件夹导航 / 操作 =====================

    private void HandleFolderNavigate(string target)
    {
        _state.currentFolder = NormalizeFolder(target);
        ClearSelection();
        RefreshContent();
    }

    private void HandleFolderOpen(ResourceFolderInfo folder)
    {
        _state.currentFolder = NormalizeFolder(folder.Path);
        ClearSelection();
        RefreshContent();
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
        var refs = ScriptReferenceScanner.FindReferencesUnderFolder(_state.currentType, folder.Path);
        if (!ConfirmFolderReferenceWarning(refs, "重命名")) return;

        VNInputDialogue.Show("重命名文件夹",
            $"重命名「{folder.Path}」。\n其中资源的引用名（Excel 中填写的名字）会同步更新前缀。",
            folder.Name, "重命名", newName =>
        {
            string oldPath = NormalizeFolder(folder.Path);
            string error = ResourceFolderService.RenameFolder(_state.currentType, oldPath, newName);
            if (error != null)
            {
                EditorUtility.DisplayDialog("重命名失败", error, "确定");
                return;
            }
            Debug.Log($"[资源管理器] 文件夹已重命名: {oldPath} → {newName}");
            // 当前浏览位置同步更新
            if (_state.currentFolder == oldPath)
                _state.currentFolder = newName;
            else if (_state.currentFolder.StartsWith(oldPath + "/"))
                _state.currentFolder = newName + _state.currentFolder.Substring(oldPath.Length);
            RefreshContent();
        });
    }

    /// <summary>文件夹右键：删除（引用警告 + 全部内容进回收站）</summary>
    private void HandleDeleteFolder(ResourceFolderInfo folder)
    {
        var refs = ScriptReferenceScanner.FindReferencesUnderFolder(_state.currentType, folder.Path);
        if (!ConfirmFolderReferenceWarning(refs, "删除")) return;

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

    private void MoveItems(List<ResourceItem> items, string targetFolder)
    {
        var errors = new List<string>();
        int ok = 0;
        foreach (var item in items)
        {
            string error = ResourceFolderService.MoveItemToFolder(_state.currentType, item, targetFolder);
            if (error == null)
            {
                ok++;
                _selected.Remove(item.AssetPath);
            }
            else
            {
                errors.Add($"{item.DisplayName}: {error}");
            }
        }
        if (ok > 0)
        {
            Debug.Log($"[资源管理器] 已移动 {ok} 项到「{(targetFolder.Length == 0 ? "类别根" : targetFolder)}」");
            RefreshContent();
        }
        if (errors.Count > 0)
            EditorUtility.DisplayDialog("部分移动失败", string.Join("\n", errors), "确定");
    }

    /// <summary>外部文件 / Project 资产拖到文件夹卡片（导入/分配落位到该文件夹）</summary>
    private void HandleExternalDropIntoFolder(string[] paths, string targetFolder)
    {
        HandleDroppedPaths(paths, targetFolder);
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
        _content.RenderItems(_items, _currentFolders, _state.currentType, _state.viewMode, _state.cardSize, _selected, IsSearchMode());
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
        if (_selected.Count == 0) return;
        _selected.Clear();
        _content.RenderItems(_items, _currentFolders, _state.currentType, _state.viewMode, _state.cardSize, _selected, IsSearchMode());
    }

    public void SelectAll()
    {
        _selected.Clear();
        foreach (var i in _items) _selected.Add(i.AssetPath);
        _content.RenderItems(_items, _currentFolders, _state.currentType, _state.viewMode, _state.cardSize, _selected, IsSearchMode());
    }

    private bool IsSearchMode()
    {
        return _state.currentType != ResType.Video && !string.IsNullOrEmpty((_state.searchKeyword ?? "").Trim());
    }

    public void DeleteSelected()
    {
        var toDelete = _items.FindAll(i => _selected.Contains(i.AssetPath));
        if (toDelete.Count == 0) return;
        DeleteItems(toDelete);
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
                foreach (var item in items)
                    VNAddressablesRegistrar.UnassignAsset(item.AssetPath);
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
        var refs = new List<ScriptReferenceScanner.ScriptResourceReference>();
        foreach (var item in items)
        {
            string logicalName = item.LogicalName ?? item.Name;
            if (!string.IsNullOrEmpty(logicalName))
                refs.AddRange(ScriptReferenceScanner.FindReferences(_state.currentType, logicalName));
        }
        return ConfirmReferenceWarning(refs, actionLabel);
    }

    /// <summary>文件夹删除/重命名前的引用警告（前缀匹配）。返回 false = 用户取消。</summary>
    private bool ConfirmFolderReferenceWarning(List<ScriptReferenceScanner.ScriptResourceReference> refs, string actionLabel)
    {
        return ConfirmReferenceWarning(refs, actionLabel);
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
        var missing = ScriptReferenceScanner.FindMissing();
        if (missing.Count == 0)
        {
            EditorUtility.DisplayDialog("检查完成", "所有剧本引用的资源均存在，没有发现缺失引用。", "确定");
            return;
        }
        string full = string.Join("\n", GetPreviewLines(missing, int.MaxValue));
        Debug.Log($"[资源管理器] 缺失引用 {missing.Count} 处：\n{full}");
        ResourceRefReportWindow.Show("缺失引用", "以下引用的资源不存在（资源被删除/改名后 Excel 未同步，或尚未导入）", missing);
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
            foreach (var f in ResourceAssetService.ListSubfolders(_state.currentType, ""))
                menu.AppendAction($"移动到/{f.Name}", _ => MoveItems(targets, f.Path));
            menu.AppendAction("移动到/新建文件夹…", _ => CreateFolderAndMove(targets));
        }
        menu.AppendSeparator();
        menu.AppendAction("删除文件（移入回收站）", _ => DeleteItemsManagedBypass(new List<ResourceItem> { item }));
    }

    /// <summary>重命名逻辑名（改 Addressables 地址尾段，支持 Chapter01/Voice01 子文件夹路径）</summary>
    private void RenameItem(ResourceItem item)
    {
        VNInputDialogue.Show("重命名逻辑名",
            $"剧本/Excel 中索引该资源所用的名字（原文件名不受影响，可用 / 表示子文件夹）。\n当前：{item.DisplayName}",
            item.DisplayName, "重命名", newName =>
        {
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
            string dir = Path.GetDirectoryName(item.AssetPath)?.Replace('\\', '/');
            string dst = dir + "/" + newName + Path.GetExtension(item.AssetPath);
            string error = AssetDatabase.RenameAsset(item.AssetPath, newName);
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
            string error = ResourceFolderService.CreateSubfolder(_state.currentType, parent, name);
            if (error != null)
            {
                EditorUtility.DisplayDialog("新建失败", error, "确定");
                return;
            }
            string target = parent.Length == 0 ? name : parent + "/" + name;
            MoveItems(items, target);
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

        var result = ResourceImportService.ImportSingleFile(_state.currentType, srcPath, CurrentImportSubfolder());
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
        int count = ResourceImportService.ImportDirectoryRecursive(_state.currentType, srcDir, destDir);
        if (count > 0)
        {
            AssetDatabase.Refresh();
            // 导入落位后注册进 Addressables（未初始化的项目自动跳过）
            VNAddressablesRegistrar.SyncWorkspace();
            RefreshContent();
            Debug.Log($"[资源管理器] 已导入 {count} 个文件" +
                      (sub.Length > 0 ? $" 到「{sub}」" : ""));
        }
        else
        {
            EditorUtility.DisplayDialog("提示", "所选文件夹中没有匹配的文件。", "确定");
        }
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
        string categoryRoot = ResourceAssetService.GetPathFromConfig(_state.currentType);
        bool physicallyInCategory = !string.IsNullOrEmpty(categoryRoot)
            && assetPath.StartsWith(categoryRoot + "/", System.StringComparison.OrdinalIgnoreCase);

        if (physicallyInCategory && folder.Length > 0)
        {
            var item = new ResourceItem
            {
                AssetPath = assetPath,
                Name = Path.GetFileNameWithoutExtension(assetPath),
                LogicalName = Path.GetFileNameWithoutExtension(assetPath),
            };
            string error = ResourceFolderService.MoveItemToFolder(_state.currentType, item, folder);
            if (error != null)
                EditorUtility.DisplayDialog("移动失败", $"{assetPath}\n{error}", "确定");
            RefreshContent();
            return;
        }

        string defaultLogical = folder.Length == 0
            ? null
            : folder + "/" + Path.GetFileNameWithoutExtension(assetPath);
        string error2 = VNAddressablesRegistrar.AssignToCategory(_state.currentType, assetPath, defaultLogical);
        if (error2 != null)
            EditorUtility.DisplayDialog("分配失败", $"{assetPath}\n{error2}", "确定");
        else
            Debug.Log($"[资源管理器] 已分配「{Path.GetFileName(assetPath)}」到" +
                      (folder.Length == 0 ? "类别根" : $"「{folder}」") + "（文件保留原位）");
        RefreshContent();
    }

    /// <summary>外部文件/文件夹导入（托管模式导入后自动注册）</summary>
    private void ImportSingleExternalFile(string src, string folder)
    {
        string target = ResourceAssetService.GetPathFromConfig(_state.currentType);
        if (string.IsNullOrEmpty(target)) return;
        int imported = 0;
        if (Directory.Exists(src))
        {
            string destDir = folder.Length == 0
                ? target
                : target + "/" + folder.Replace('/', Path.DirectorySeparatorChar);
            imported += ResourceImportService.ImportDirectoryRecursive(_state.currentType, src, destDir);
        }
        else if (File.Exists(src))
        {
            var r = ResourceImportService.ImportSingleFile(_state.currentType, src, folder);
            if (r.Success) imported++;
        }
        if (imported > 0)
        {
            AssetDatabase.Refresh();
            VNAddressablesRegistrar.SyncWorkspace();
            RefreshContent();
            Debug.Log($"[资源管理器] 已导入 {imported} 个文件" +
                      (folder.Length > 0 ? $" 到「{folder}」" : ""));
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

    // ===================== 回收站 =====================

    private void EnterTrashMode()
    {
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
        int count = ResourceTrashService.Count;
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
