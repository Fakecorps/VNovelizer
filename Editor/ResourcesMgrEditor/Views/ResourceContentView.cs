using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 资源管理器右侧内容区视图。
/// 包含面包屑导航（【子文件夹支持】）、文件夹卡片/行、网格视图（卡片）和列表视图（表头 + 行）、空状态。
/// 文件卡片支持拖拽（阈值触发 DragAndDrop），文件夹卡片/面包屑为拖放目标（移动到文件夹）。
/// </summary>
public class ResourceContentView
{
    public VisualElement Root { get; private set; }
    public VisualElement RightPane { get; private set; }
    public VisualElement EmptyState { get; private set; }

    public event Action<ResourceItem, int> OnItemMouseDown;
    public event Action<ResourceItem> OnItemPlay;  // 双击/按钮触发播放
    public event Action<ResourceItem, DropdownMenu> OnItemContextMenu; // 右键菜单（向 evt.menu 追加动作，由 Presenter 决定内容）

    // ===================== 【子文件夹支持】新增事件 =====================
    public event Action<string> OnFolderNavigate;                          // 面包屑导航（"" = 类别根）
    public event Action<ResourceFolderInfo> OnFolderOpen;                  // 双击文件夹进入
    public event Action<ResourceFolderInfo, DropdownMenu> OnFolderContextMenu; // 文件夹右键菜单
    public event Action<DropdownMenu> OnBackgroundContextMenu;             // 空白处右键菜单
    public event Action OnNewFolderClicked;                                // 面包屑右侧「新建文件夹」按钮
    public event Action<List<string>, string> OnItemsMoveToFolder;         // 内部拖拽：移动选中项到文件夹
    public event Action<string[], string> OnExternalDropIntoFolder;        // 外部/Project 资产拖到文件夹卡片

    /// <summary>内部拖拽移动的 DragAndDrop 数据键（选中项 AssetPath 以 '\n' 连接）</summary>
    private const string MoveDragKey = "VnResMoveItems";

    private List<ResourceItem> _items;
    private HashSet<string> _selected;
    private ViewMode _viewMode;
    private float _cardSize;
    private ResType _currentType;
    private bool _searchMode;
    private bool _internalDragging;
    private Vector2 _dragStartPos;

    public ResourceContentView()
    {
        Root = new VisualElement();
        _items = new List<ResourceItem>();
        _selected = new HashSet<string>();
        _viewMode = ViewMode.Grid;
        _cardSize = ResourceStyles.DefaultCardSize;
        _currentType = ResType.Background;
        Build();
    }

    private void Build()
    {
        Root.style.flexDirection = FlexDirection.Column;
        Root.style.backgroundColor = ResourceStyles.Bg;
        Root.style.flexGrow = 1;

        // 【子文件夹支持】面包屑导航条
        Root.Add(BuildBreadcrumb());

        var scroll = new ScrollView(ScrollViewMode.Vertical);
        scroll.style.flexGrow = 1;
        scroll.style.paddingTop = 10;
        scroll.style.paddingLeft = 10;
        scroll.style.paddingRight = 10;
        scroll.style.paddingBottom = 10;

        RightPane = new VisualElement();
        RightPane.style.flexDirection = FlexDirection.Row;
        RightPane.style.flexWrap = Wrap.Wrap;
        RightPane.style.alignItems = Align.FlexStart;
        scroll.Add(RightPane);
        Root.Add(scroll);

        EmptyState = BuildEmptyState();
        Root.Add(EmptyState);

        // 空白处右键菜单（卡片自身的右键事件已 StopPropagation，不会冒泡到这里）
        RightPane.RegisterCallback<ContextualMenuPopulateEvent>(evt =>
        {
            evt.StopPropagation();
            OnBackgroundContextMenu?.Invoke(evt.menu);
        });

        // 拖放支持（空白区域，落位到当前文件夹——由 Presenter 决定导入/分配）
        Root.RegisterCallback<DragEnterEvent>(_ => { });
        Root.RegisterCallback<DragLeaveEvent>(_ => { });
        Root.RegisterCallback<DragUpdatedEvent>(_ =>
        {
            if (DragAndDrop.GetGenericData(MoveDragKey) == null)
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
        });
        Root.RegisterCallback<DragPerformEvent>(_ => DragAndDrop.AcceptDrag());

        // 内部拖拽收尾复位（拖拽结束时 MouseUp 会被拖拽系统吃掉）
        Root.RegisterCallback<DragExitedEvent>(_ => _internalDragging = false);
    }

    // ===================== 面包屑 =====================

    private VisualElement _breadcrumb;

    private VisualElement BuildBreadcrumb()
    {
        _breadcrumb = new VisualElement();
        _breadcrumb.name = "breadcrumb";
        _breadcrumb.style.flexDirection = FlexDirection.Row;
        _breadcrumb.style.alignItems = Align.Center;
        _breadcrumb.style.height = 30;
        _breadcrumb.style.paddingLeft = 10;
        _breadcrumb.style.paddingRight = 10;
        _breadcrumb.style.backgroundColor = GalleryTheme.Hex(GalleryTheme.BgSecondary);
        _breadcrumb.style.borderBottomWidth = 1;
        _breadcrumb.style.borderBottomColor = GalleryTheme.Hex(GalleryTheme.Border);
        return _breadcrumb;
    }

    /// <summary>更新面包屑（visible = false 时隐藏，如 Video 类别 / 回收站模式）。每次全量重建。</summary>
    public void SetBreadcrumb(ResType type, string currentFolder, bool visible)
    {
        _breadcrumb.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        if (!visible) return;
        _breadcrumb.Clear();

        void AddSegment(string label, string navigateTo, bool isCurrent, bool isRoot)
        {
            var btn = new Button(() => OnFolderNavigate?.Invoke(navigateTo)) { text = label };
            btn.style.backgroundColor = Color.clear;
            btn.style.borderTopWidth = 0; btn.style.borderBottomWidth = 0;
            btn.style.borderLeftWidth = 0; btn.style.borderRightWidth = 0;
            btn.style.fontSize = 12;
            btn.style.height = 20;
            btn.style.paddingLeft = 4;
            btn.style.paddingRight = 4;
            btn.style.color = isCurrent
                ? ResourceStyles.TextPrimary
                : (isRoot ? ResourceStyles.Accent : ResourceStyles.TextSecondary);
            if (isCurrent) btn.style.unityFontStyleAndWeight = FontStyle.Bold;
            btn.style.unityTextAlign = TextAnchor.MiddleLeft;
            _breadcrumb.Add(btn);
        }

        string folder = (currentFolder ?? "").Trim().Trim('/');
        AddSegment(UIElementBuilder.GetTypeDisplayName(type) + (folder.Length == 0 ? "" : " /"), "", folder.Length == 0, true);

        if (folder.Length > 0)
        {
            string[] segments = folder.Split('/');
            string acc = "";
            for (int i = 0; i < segments.Length; i++)
            {
                acc = acc.Length == 0 ? segments[i] : acc + "/" + segments[i];
                bool isLast = i == segments.Length - 1;
                AddSegment(isLast ? segments[i] : segments[i] + " /", acc, isLast, false);
            }
        }

        var spacer = new VisualElement { style = { flexGrow = 1 } };
        _breadcrumb.Add(spacer);

        var newFolderBtn = UIElementBuilder.MakeIconButton(
            UIElementBuilder.GetIcon("Folder Icon", "Folder", "d_Folder"), "新建文件夹");
        GalleryStyles.ApplyButton(newFolderBtn, GalleryTheme.BgCard, false);
        newFolderBtn.style.fontSize = 11;
        newFolderBtn.style.height = 22;
        newFolderBtn.tooltip = "在当前文件夹下新建子文件夹";
        newFolderBtn.clicked += () => OnNewFolderClicked?.Invoke();
        _breadcrumb.Add(newFolderBtn);
    }

    // ===================== 空状态 =====================

    private VisualElement BuildEmptyState()
    {
        var container = new VisualElement();
        container.style.flexGrow = 1;
        container.style.alignItems = Align.Center;
        container.style.justifyContent = Justify.Center;
        container.style.display = DisplayStyle.None;
        container.style.paddingTop = 40;

        var icon = new Image();
        icon.image = UIElementBuilder.GetIcon("Folder Icon", "Folder", "FolderEmpty Icon", "d_Folder");
        icon.style.width = 80;
        icon.style.height = 80;
        icon.style.opacity = 0.4f;
        icon.style.marginBottom = 16;
        icon.name = "icon";
        container.Add(icon);

        var title = new Label("暂无资源");
        title.style.fontSize = 18;
        title.style.color = ResourceStyles.TextPrimary;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.marginBottom = 8;
        title.name = "title";
        container.Add(title);

        var desc = new Label("");
        desc.style.color = ResourceStyles.TextSecondary;
        desc.style.fontSize = 12;
        desc.style.whiteSpace = WhiteSpace.Normal;
        desc.style.unityTextAlign = TextAnchor.MiddleCenter;
        desc.style.maxWidth = 400;
        desc.name = "desc";
        container.Add(desc);

        return container;
    }

    /// <summary>显示空状态</summary>
    public void ShowEmpty(string title, string desc)
    {
        EmptyState.style.display = DisplayStyle.Flex;
        EmptyState.Q<Label>("title").text = title;
        EmptyState.Q<Label>("desc").text = desc;
        for (int i = EmptyState.childCount - 1; i >= 0; i--)
        {
            var c = EmptyState[i];
            if (c.name == "dynBtn") EmptyState.RemoveAt(i);
        }
        RightPane.style.display = DisplayStyle.None;
    }

    /// <summary>添加空状态下的动态按钮</summary>
    public void AddEmptyButton(string text, Action onClick)
    {
        var btn = new Button(onClick) { text = text };
        btn.name = "dynBtn";
        ResourceStyles.StylePrimary(btn, ResourceStyles.AccentSuccess);
        btn.style.marginTop = 12;
        EmptyState.Add(btn);
    }

    /// <summary>隐藏空状态并显示内容</summary>
    public void HideEmpty()
    {
        EmptyState.style.display = DisplayStyle.None;
        RightPane.style.display = DisplayStyle.Flex;
        RightPane.style.flexDirection = _viewMode == ViewMode.Grid ? FlexDirection.Row : FlexDirection.Column;
        RightPane.style.flexWrap = _viewMode == ViewMode.Grid ? Wrap.Wrap : Wrap.NoWrap;
    }

    // ===================== 渲染 =====================

    /// <summary>
    /// 渲染资源列表。【子文件夹支持】folders = 当前层子文件夹（搜索模式下为 null/空，平铺全量结果）。
    /// </summary>
    public void RenderItems(List<ResourceItem> items, List<ResourceFolderInfo> folders, ResType type,
        ViewMode viewMode, float cardSize, HashSet<string> selected, bool searchMode)
    {
        _items = items ?? new List<ResourceItem>();
        _selected = selected ?? new HashSet<string>();
        _viewMode = viewMode;
        _cardSize = cardSize;
        _currentType = type;
        _searchMode = searchMode;
        RightPane.Clear();

        RightPane.style.flexDirection = viewMode == ViewMode.Grid ? FlexDirection.Row : FlexDirection.Column;
        RightPane.style.flexWrap = viewMode == ViewMode.Grid ? Wrap.Wrap : Wrap.NoWrap;

        var folderList = (folders ?? new List<ResourceFolderInfo>());

        if (viewMode == ViewMode.Grid)
        {
            foreach (var folder in folderList)
                CreateFolderCard(folder);
            foreach (var item in _items)
                CreateGridCard(item);
        }
        else
        {
            CreateListHeader();
            foreach (var folder in folderList)
                CreateFolderListRow(folder);
            for (int i = 0; i < _items.Count; i++)
                CreateListRow(_items[i], i % 2 == 0);
        }
    }

    /// <summary>条目显示名：文件夹视图下只显示最后一段（所在文件夹已由面包屑表达），搜索模式显示完整逻辑名</summary>
    private string GetDisplayLabel(ResourceItem item)
    {
        string name = item.DisplayName;
        if (_searchMode || !name.Contains("/")) return name;
        return name.Substring(name.LastIndexOf('/') + 1);
    }

    // ===================== 文件夹卡片（网格） =====================

    private void CreateFolderCard(ResourceFolderInfo folder)
    {
        var card = new VisualElement();
        card.style.width = _cardSize;
        card.style.height = _cardSize + 30;
        card.style.marginRight = 10;
        card.style.marginBottom = 10;
        card.style.backgroundColor = ResourceStyles.Card;
        ResourceStyles.SetRadius(card, ResourceStyles.CardRadius);
        ResourceStyles.SetBorder(card, ResourceStyles.CardBorder, 1.5f);
        card.style.overflow = Overflow.Hidden;
        card.name = "folderCard";

        // 文件夹图标
        var icon = new Image();
        var folderTex = UIElementBuilder.GetIcon("Folder Icon", "Folder", "d_Folder") as Texture;
        if (folderTex != null) icon.image = folderTex;
        icon.style.width = _cardSize - 40;
        icon.style.height = _cardSize - 60;
        icon.style.alignSelf = Align.Center;
        icon.style.marginTop = 10;
        icon.scaleMode = ScaleMode.ScaleToFit;
        card.Add(icon);

        // 名称
        var label = new Label(folder.Name);
        label.style.overflow = Overflow.Hidden;
        label.style.textOverflow = TextOverflow.Ellipsis;
        label.style.whiteSpace = WhiteSpace.NoWrap;
        label.style.width = _cardSize - 10;
        label.style.alignSelf = Align.Center;
        label.style.unityTextAlign = TextAnchor.MiddleCenter;
        label.style.fontSize = 11;
        label.style.color = ResourceStyles.TextPrimary;
        label.style.marginTop = 4;
        card.Add(label);

        // 数量徽标
        var count = new Label(folder.ItemCount + (folder.SubfolderCount > 0 ? $" 项 + {folder.SubfolderCount} 夹" : " 项"));
        count.style.alignSelf = Align.Center;
        count.style.fontSize = 10;
        count.style.color = ResourceStyles.TextSecondary;
        card.Add(count);

        // 交互
        card.RegisterCallback<MouseEnterEvent>(_ =>
        {
            card.style.backgroundColor = ResourceStyles.CardHover;
            ResourceStyles.SetBorder(card, ResourceStyles.Accent, 1.5f);
        });
        card.RegisterCallback<MouseLeaveEvent>(_ =>
        {
            card.style.backgroundColor = ResourceStyles.Card;
            ResourceStyles.SetBorder(card, ResourceStyles.CardBorder, 1.5f);
        });
        card.RegisterCallback<MouseDownEvent>(evt =>
        {
            if (evt.clickCount == 2) OnFolderOpen?.Invoke(folder);
        });
        card.RegisterCallback<ContextualMenuPopulateEvent>(evt =>
        {
            evt.StopPropagation();
            OnFolderContextMenu?.Invoke(folder, evt.menu);
        });
        RegisterDropTarget(card, folder.Path);

        RightPane.Add(card);
    }

    // ===================== 文件夹行（列表） =====================

    private void CreateFolderListRow(ResourceFolderInfo folder)
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.height = 34;
        row.style.backgroundColor = GalleryTheme.Hex(GalleryTheme.BgCard);
        row.style.paddingLeft = 12;
        row.style.paddingRight = 12;
        row.style.width = Length.Percent(100);
        row.style.flexShrink = 0;
        row.style.borderLeftWidth = 3;
        row.style.borderLeftColor = ResourceStyles.TransparentBlack;

        var nameCell = new VisualElement();
        nameCell.style.width = Length.Percent(ResourceStyles.ColPct_Name);
        nameCell.style.flexDirection = FlexDirection.Row;
        nameCell.style.alignItems = Align.Center;
        nameCell.style.flexShrink = 0;

        var icon = new Image();
        var folderTex = UIElementBuilder.GetIcon("Folder Icon", "Folder", "d_Folder") as Texture;
        if (folderTex != null) icon.image = folderTex;
        icon.style.width = 22;
        icon.style.height = 22;
        icon.style.marginRight = 8;
        icon.style.flexShrink = 0;
        icon.scaleMode = ScaleMode.ScaleToFit;
        nameCell.Add(icon);

        var nameLabel = new Label(folder.Name);
        nameLabel.style.flexGrow = 1;
        nameLabel.style.color = ResourceStyles.TextPrimary;
        nameLabel.style.overflow = Overflow.Hidden;
        nameLabel.style.textOverflow = TextOverflow.Ellipsis;
        nameCell.Add(nameLabel);
        row.Add(nameCell);

        var typeBadge = new Label("文件夹");
        typeBadge.style.width = Length.Percent(ResourceStyles.ColPct_Type);
        typeBadge.style.flexShrink = 0;
        typeBadge.style.color = ResourceStyles.TextSecondary;
        typeBadge.style.fontSize = 10;
        typeBadge.style.paddingLeft = 6;
        typeBadge.style.paddingRight = 6;
        typeBadge.style.paddingTop = 2;
        typeBadge.style.paddingBottom = 2;
        typeBadge.style.backgroundColor = new Color(0, 0, 0, 0.25f);
        ResourceStyles.SetRadius(typeBadge, 3);
        row.Add(typeBadge);

        var countLabel = new Label($"{folder.ItemCount} 个文件" + (folder.SubfolderCount > 0 ? $" / {folder.SubfolderCount} 个子文件夹" : ""));
        countLabel.style.width = Length.Percent(ResourceStyles.ColPct_Path);
        countLabel.style.flexShrink = 0;
        countLabel.style.color = ResourceStyles.TextSecondary;
        countLabel.style.fontSize = 10;
        countLabel.style.overflow = Overflow.Hidden;
        countLabel.style.textOverflow = TextOverflow.Ellipsis;
        row.Add(countLabel);

        row.RegisterCallback<MouseEnterEvent>(_ => row.style.backgroundColor = ResourceStyles.CardHover);
        row.RegisterCallback<MouseLeaveEvent>(_ => row.style.backgroundColor = GalleryTheme.Hex(GalleryTheme.BgCard));
        row.RegisterCallback<MouseDownEvent>(evt =>
        {
            if (evt.clickCount == 2) OnFolderOpen?.Invoke(folder);
        });
        row.RegisterCallback<ContextualMenuPopulateEvent>(evt =>
        {
            evt.StopPropagation();
            OnFolderContextMenu?.Invoke(folder, evt.menu);
        });
        RegisterDropTarget(row, folder.Path);

        RightPane.Add(row);
    }

    // ===================== 拖放目标（文件夹卡片/行） =====================

    /// <summary>把元素注册为"移动到文件夹"拖放目标：内部拖拽 = 移动；外部文件/Project 资产 = 导入/分配到该文件夹</summary>
    private void RegisterDropTarget(VisualElement element, string folderPath)
    {
        Color originalBg = Color.clear;
        element.RegisterCallback<DragEnterEvent>(_ =>
        {
            originalBg = element.resolvedStyle.backgroundColor;
            element.style.backgroundColor = ResourceStyles.CardSelected;
        });
        element.RegisterCallback<DragLeaveEvent>(_ =>
        {
            element.style.backgroundColor = originalBg;
        });
        element.RegisterCallback<DragUpdatedEvent>(_ =>
        {
            if (DragAndDrop.GetGenericData(MoveDragKey) != null)
                DragAndDrop.visualMode = DragAndDropVisualMode.Move;
            else
            {
                bool fromProject = DragAndDrop.paths != null && DragAndDrop.paths.Length > 0
                    && DragAndDrop.paths[0].StartsWith("Assets/", StringComparison.OrdinalIgnoreCase);
                DragAndDrop.visualMode = fromProject ? DragAndDropVisualMode.Link : DragAndDropVisualMode.Copy;
            }
        });
        element.RegisterCallback<DragPerformEvent>(evt =>
        {
            evt.StopPropagation();
            DragAndDrop.AcceptDrag();
            element.style.backgroundColor = originalBg;

            string moveData = DragAndDrop.GetGenericData(MoveDragKey) as string;
            if (!string.IsNullOrEmpty(moveData))
            {
                var paths = new List<string>(moveData.Split('\n'));
                paths.RemoveAll(string.IsNullOrEmpty);
                if (paths.Count > 0) OnItemsMoveToFolder?.Invoke(paths, folderPath);
                return;
            }
            if (DragAndDrop.paths != null && DragAndDrop.paths.Length > 0)
                OnExternalDropIntoFolder?.Invoke(DragAndDrop.paths, folderPath);
        });
    }

    // ===================== 网格卡片（文件） =====================

    private void CreateGridCard(ResourceItem item)
    {
        bool isSelected = _selected.Contains(item.AssetPath);

        var card = new VisualElement();
        card.style.width = _cardSize;
        card.style.height = _cardSize + 30;
        card.style.marginRight = 10;
        card.style.marginBottom = 10;
        card.style.backgroundColor = isSelected ? ResourceStyles.CardSelected : ResourceStyles.Card;
        ResourceStyles.SetRadius(card, ResourceStyles.CardRadius);
        ResourceStyles.SetBorder(card, isSelected ? ResourceStyles.Accent : ResourceStyles.CardBorder, 1.5f);
        card.style.overflow = Overflow.Hidden;

        // 预览
        Texture preview = item.Asset != null ? AssetPreview.GetAssetPreview(item.Asset) : null;
        if (preview == null && item.Asset != null) preview = AssetPreview.GetMiniThumbnail(item.Asset);

        if (preview != null)
        {
            var icon = new Image { image = preview };
            icon.style.width = _cardSize - 10;
            icon.style.height = _cardSize - 10;
            icon.style.marginTop = 5;
            icon.style.alignSelf = Align.Center;
            icon.scaleMode = ScaleMode.ScaleToFit;
            card.Add(icon);
        }
        else
        {
            var placeholder = new Image();
            var tex = UIElementBuilder.GetTypeIconTexture(_currentType);
            if (tex != null) placeholder.image = tex;
            placeholder.style.width = _cardSize - 30;
            placeholder.style.height = _cardSize - 30;
            placeholder.style.alignSelf = Align.Center;
            placeholder.style.marginTop = 5;
            placeholder.style.opacity = 0.4f;
            card.Add(placeholder);
        }

        // 文件名（显示逻辑名——剧本作者看到什么，Excel 里就写什么；
        // 文件夹视图下只显示尾段，完整路径由面包屑 + 文件夹层级表达）
        var label = new Label(GetDisplayLabel(item));
        label.style.overflow = Overflow.Hidden;
        label.style.textOverflow = TextOverflow.Ellipsis;
        label.style.whiteSpace = WhiteSpace.NoWrap;
        label.style.width = _cardSize - 10;
        label.style.alignSelf = Align.Center;
        label.style.unityTextAlign = TextAnchor.MiddleCenter;
        label.style.fontSize = 11;
        label.style.color = ResourceStyles.TextPrimary;
        label.style.marginTop = 4;
        card.Add(label);

        // 音频卡片额外显示播放按钮
        bool isAudio = _currentType == ResType.BGM || _currentType == ResType.SFX || _currentType == ResType.Voice;
        if (isAudio && item.Asset is AudioClip)
        {
            var playBtn = new Button(() => OnItemPlay?.Invoke(item)) { text = "" };
            playBtn.style.position = Position.Absolute;
            playBtn.style.left = 6;
            playBtn.style.top = 6;
            playBtn.style.width = 26;
            playBtn.style.height = 22;
            playBtn.style.backgroundColor = new Color(0, 0, 0, 0.7f);
            var playIcon = new Image();
            playIcon.image = UIElementBuilder.GetIcon("PlayButton", "Play", "d_PlayButton");
            playIcon.style.width = 12;
            playIcon.style.height = 12;
            playBtn.Add(playIcon);
            playBtn.tooltip = "试听";
            playBtn.style.opacity = 0;
            playBtn.name = "playBtn";
            card.Add(playBtn);
        }

        // 删除按钮（悬停时显示）
        var delBtn = new Button(() => OnItemMouseDown?.Invoke(item, DeleteAction)) { text = "X" };
        delBtn.style.position = Position.Absolute;
        delBtn.style.top = 6;
        delBtn.style.right = 6;
        delBtn.style.width = 22;
        delBtn.style.height = 20;
        delBtn.style.fontSize = 11;
        delBtn.style.backgroundColor = ResourceStyles.DangerNormal;
        delBtn.style.color = Color.white;
        ResourceStyles.SetRadius(delBtn, 3);
        ResourceStyles.SetBorder(delBtn, new Color(0, 0, 0, 0.3f), 1);
        delBtn.tooltip = "删除";
        delBtn.name = "delBtn";
        delBtn.style.opacity = 0;
        ResourceStyles.AddHover(delBtn, ResourceStyles.DangerNormal, ResourceStyles.DangerHover);
        card.Add(delBtn);

        // 交互 - 悬停显示删除按钮和播放按钮
        card.RegisterCallback<MouseEnterEvent>(_ =>
        {
            if (!_selected.Contains(item.AssetPath))
                card.style.backgroundColor = ResourceStyles.CardHover;
            var del = card.Q<Button>("delBtn");
            if (del != null) del.style.opacity = 1;
            var play = card.Q<Button>("playBtn");
            if (play != null) play.style.opacity = 1;
        });
        card.RegisterCallback<MouseLeaveEvent>(_ =>
        {
            card.style.backgroundColor = _selected.Contains(item.AssetPath) ? ResourceStyles.CardSelected : ResourceStyles.Card;
            var del = card.Q<Button>("delBtn");
            if (del != null) del.style.opacity = 0;
            var play = card.Q<Button>("playBtn");
            if (play != null) play.style.opacity = 0;
        });
        card.RegisterCallback<MouseDownEvent>(evt =>
        {
            OnItemMouseDown?.Invoke(item, evt.clickCount);
            // 记录拖拽起点（左键按下且未松开时移动超过阈值 → 启动内部拖拽）
            _dragStartPos = evt.mousePosition;
        });
        // 【子文件夹支持】拖拽移动：按住选中项拖动 8px 后启动 DragAndDrop（携带全部选中项）
        card.RegisterCallback<MouseMoveEvent>(evt =>
        {
            if (evt.pressedButtons != 1 || _internalDragging) return;
            if ((evt.mousePosition - _dragStartPos).sqrMagnitude < 64) return;

            var moving = _items.FindAll(i => _selected.Contains(i.AssetPath));
            if (moving.Count == 0) moving = new List<ResourceItem> { item };
            DragAndDrop.PrepareStartDrag();
            DragAndDrop.SetGenericData(MoveDragKey, string.Join("\n", moving.ConvertAll(i => i.AssetPath)));
            DragAndDrop.StartDrag(moving.Count > 1 ? $"移动 {moving.Count} 项到文件夹" : "移动到文件夹");
            _internalDragging = true;
        });
        card.RegisterCallback<MouseUpEvent>(_ => _internalDragging = false);
        card.RegisterCallback<ContextualMenuPopulateEvent>(evt =>
        {
            evt.StopPropagation();
            OnItemContextMenu?.Invoke(item, evt.menu);
        });

        RightPane.Add(card);
    }

    // ===================== 列表行（文件） =====================

    private void CreateListHeader()
    {
        var header = new VisualElement();
        header.style.flexDirection = FlexDirection.Row;
        header.style.alignItems = Align.Center;
        header.style.height = 28;
        header.style.backgroundColor = GalleryTheme.Hex(GalleryTheme.BgCard);
        header.style.borderBottomWidth = 1;
        header.style.borderBottomColor = GalleryTheme.Hex(GalleryTheme.Border);
        header.style.paddingLeft = 12;
        header.style.paddingRight = 12;
        header.style.width = Length.Percent(100);

        void AddCol(string text, float pct, TextAnchor anchor = TextAnchor.MiddleLeft)
        {
            var l = new Label(text);
            l.style.width = Length.Percent(pct);
            l.style.flexShrink = 0;
            l.style.unityTextAlign = anchor;
            l.style.color = ResourceStyles.TextSecondary;
            l.style.fontSize = 11;
            l.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.Add(l);
        }
        AddCol("名称", ResourceStyles.ColPct_Name);
        AddCol("类型", ResourceStyles.ColPct_Type);
        AddCol("路径", ResourceStyles.ColPct_Path);
        AddCol("大小", ResourceStyles.ColPct_Size, TextAnchor.MiddleRight);
        AddCol("操作", ResourceStyles.ColPct_Op, TextAnchor.MiddleCenter);

        RightPane.Add(header);
    }

    private void CreateListRow(ResourceItem item, bool isEven)
    {
        bool isSelected = _selected.Contains(item.AssetPath);
        Color baseColor = isEven ? GalleryTheme.Hex(GalleryTheme.BgPrimary) : GalleryTheme.Hex(GalleryTheme.BgCard);

        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.height = 34;
        row.style.backgroundColor = isSelected ? ResourceStyles.CardSelected : baseColor;
        row.style.paddingLeft = 12;
        row.style.paddingRight = 12;
        row.style.width = Length.Percent(100);
        row.style.flexShrink = 0;
        row.style.borderLeftWidth = 3;
        row.style.borderLeftColor = isSelected ? ResourceStyles.Accent : ResourceStyles.TransparentBlack;

        // 名称列
        var nameCell = new VisualElement();
        nameCell.style.width = Length.Percent(ResourceStyles.ColPct_Name);
        nameCell.style.flexDirection = FlexDirection.Row;
        nameCell.style.alignItems = Align.Center;
        nameCell.style.flexShrink = 0;

        var icon = new Image { image = AssetPreview.GetMiniThumbnail(item.Asset) };
        icon.style.width = 22;
        icon.style.height = 22;
        icon.style.marginRight = 8;
        icon.style.flexShrink = 0;
        icon.scaleMode = ScaleMode.ScaleToFit;
        nameCell.Add(icon);

        var nameLabel = new Label(GetDisplayLabel(item));
        nameLabel.style.flexGrow = 1;
        nameLabel.style.color = isSelected ? Color.white : ResourceStyles.TextPrimary;
        nameLabel.style.overflow = Overflow.Hidden;
        nameLabel.style.textOverflow = TextOverflow.Ellipsis;
        nameLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
        nameCell.Add(nameLabel);
        row.Add(nameCell);

        // 类型列
        var typeBadge = new Label(UIElementBuilder.GetTypeDisplayName(_currentType));
        typeBadge.style.width = Length.Percent(ResourceStyles.ColPct_Type);
        typeBadge.style.flexShrink = 0;
        typeBadge.style.color = ResourceStyles.TextSecondary;
        typeBadge.style.fontSize = 10;
        typeBadge.style.paddingLeft = 6;
        typeBadge.style.paddingRight = 6;
        typeBadge.style.paddingTop = 2;
        typeBadge.style.paddingBottom = 2;
        typeBadge.style.backgroundColor = new Color(0, 0, 0, 0.25f);
        ResourceStyles.SetRadius(typeBadge, 3);
        typeBadge.style.unityTextAlign = TextAnchor.MiddleLeft;
        row.Add(typeBadge);

        // 路径列（文件夹视图下显示完整逻辑名，方便照抄进 Excel）
        var pathLabel = new Label(item.DisplayName);
        pathLabel.style.width = Length.Percent(ResourceStyles.ColPct_Path);
        pathLabel.style.flexShrink = 0;
        pathLabel.style.color = ResourceStyles.TextSecondary;
        pathLabel.style.fontSize = 10;
        pathLabel.style.overflow = Overflow.Hidden;
        pathLabel.style.textOverflow = TextOverflow.Ellipsis;
        pathLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
        row.Add(pathLabel);

        // 大小列
        var sizeLabel = new Label(item.FormattedSize);
        sizeLabel.style.width = Length.Percent(ResourceStyles.ColPct_Size);
        sizeLabel.style.flexShrink = 0;
        sizeLabel.style.color = ResourceStyles.TextSecondary;
        sizeLabel.style.fontSize = 11;
        sizeLabel.style.unityTextAlign = TextAnchor.MiddleRight;
        row.Add(sizeLabel);

        // 操作列
        var actions = new VisualElement();
        actions.style.width = Length.Percent(ResourceStyles.ColPct_Op);
        actions.style.flexShrink = 0;
        actions.style.flexDirection = FlexDirection.Row;
        actions.style.justifyContent = Justify.Center;
        actions.style.alignItems = Align.Center;

        bool isAudio = _currentType == ResType.BGM || _currentType == ResType.SFX || _currentType == ResType.Voice;
        if (isAudio && item.Asset is AudioClip)
        {
            var playBtn = new Button(() => OnItemPlay?.Invoke(item)) { text = "" };
            playBtn.style.width = 24;
            playBtn.style.height = 20;
            playBtn.style.backgroundColor = ResourceStyles.Accent;
            playBtn.style.marginRight = 4;
            var playIcon = new Image();
            playIcon.image = UIElementBuilder.GetIcon("PlayButton", "Play");
            playIcon.style.width = 12;
            playIcon.style.height = 12;
            playBtn.Add(playIcon);
            playBtn.tooltip = "试听";
            ResourceStyles.AddHover(playBtn, ResourceStyles.Accent, new Color(0.30f, 0.65f, 0.95f));
            actions.Add(playBtn);
        }

        var delBtn = new Button(() => OnItemMouseDown?.Invoke(item, DeleteAction)) { text = "X" };
        delBtn.style.width = 24;
        delBtn.style.height = 20;
        delBtn.style.backgroundColor = ResourceStyles.DangerNormal;
        delBtn.style.color = Color.white;
        ResourceStyles.SetRadius(delBtn, 3);
        ResourceStyles.SetBorder(delBtn, new Color(0, 0, 0, 0.2f), 1);
        delBtn.tooltip = "删除";
        ResourceStyles.AddHover(delBtn, ResourceStyles.DangerNormal, ResourceStyles.DangerHover);
        actions.Add(delBtn);
        row.Add(actions);

        // 行交互
        var baseC = baseColor;
        row.RegisterCallback<MouseEnterEvent>(_ =>
        {
            if (!_selected.Contains(item.AssetPath))
                row.style.backgroundColor = ResourceStyles.CardHover;
        });
        row.RegisterCallback<MouseLeaveEvent>(_ =>
        {
            row.style.backgroundColor = _selected.Contains(item.AssetPath) ? ResourceStyles.CardSelected : baseC;
        });
        row.RegisterCallback<MouseDownEvent>(evt =>
        {
            OnItemMouseDown?.Invoke(item, evt.clickCount);
            _dragStartPos = evt.mousePosition;
        });
        row.RegisterCallback<MouseMoveEvent>(evt =>
        {
            if (evt.pressedButtons != 1 || _internalDragging) return;
            if ((evt.mousePosition - _dragStartPos).sqrMagnitude < 64) return;

            var moving = _items.FindAll(i => _selected.Contains(i.AssetPath));
            if (moving.Count == 0) moving = new List<ResourceItem> { item };
            DragAndDrop.PrepareStartDrag();
            DragAndDrop.SetGenericData(MoveDragKey, string.Join("\n", moving.ConvertAll(i => i.AssetPath)));
            DragAndDrop.StartDrag(moving.Count > 1 ? $"移动 {moving.Count} 项到文件夹" : "移动到文件夹");
            _internalDragging = true;
        });
        row.RegisterCallback<MouseUpEvent>(_ => _internalDragging = false);
        row.RegisterCallback<ContextualMenuPopulateEvent>(evt =>
        {
            evt.StopPropagation();
            OnItemContextMenu?.Invoke(item, evt.menu);
        });

        RightPane.Add(row);
    }

    public const int DeleteAction = -1;
}
