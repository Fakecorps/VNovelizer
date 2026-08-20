using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 资源管理器右侧内容区视图。
/// 包含网格视图（卡片）和列表视图（表头 + 行），以及空状态。
/// </summary>
public class ResourceContentView
{
    public VisualElement Root { get; private set; }
    public VisualElement RightPane { get; private set; }
    public VisualElement EmptyState { get; private set; }

    public event Action<ResourceItem, int> OnItemMouseDown;
    public event Action<ResourceItem> OnItemPlay;  // 新增：双击/按钮触发播放
    public event Action<ResourceItem, DropdownMenu> OnItemContextMenu; // 右键菜单（向 evt.menu 追加动作，由 Presenter 决定内容）

    private List<ResourceItem> _items;
    private HashSet<string> _selected;
    private ViewMode _viewMode;
    private float _cardSize;
    private ResType _currentType;

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

        // 拖放支持
        Root.RegisterCallback<DragEnterEvent>(_ => { });
        Root.RegisterCallback<DragLeaveEvent>(_ => { });
        Root.RegisterCallback<DragUpdatedEvent>(evt => DragAndDrop.visualMode = DragAndDropVisualMode.Copy);
        Root.RegisterCallback<DragPerformEvent>(evt => DragAndDrop.AcceptDrag());
    }

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

    /// <summary>渲染资源列表</summary>
    public void RenderItems(List<ResourceItem> items, ResType type, ViewMode viewMode, float cardSize, HashSet<string> selected)
    {
        _items = items ?? new List<ResourceItem>();
        _selected = selected ?? new HashSet<string>();
        _viewMode = viewMode;
        _cardSize = cardSize;
        _currentType = type;
        RightPane.Clear();

        RightPane.style.flexDirection = viewMode == ViewMode.Grid ? FlexDirection.Row : FlexDirection.Column;
        RightPane.style.flexWrap = viewMode == ViewMode.Grid ? Wrap.Wrap : Wrap.NoWrap;

        if (viewMode == ViewMode.Grid)
        {
            foreach (var item in _items)
                CreateGridCard(item);
        }
        else
        {
            CreateListHeader();
            for (int i = 0; i < _items.Count; i++)
                CreateListRow(_items[i], i % 2 == 0);
        }
    }

    // ===================== 网格卡片 =====================
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

        // 文件名（显示逻辑名——剧本作者看到什么，Excel 里就写什么）
        var label = new Label(item.DisplayName);
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
        card.RegisterCallback<MouseDownEvent>(evt => OnItemMouseDown?.Invoke(item, evt.clickCount));
        card.RegisterCallback<ContextualMenuPopulateEvent>(evt =>
        {
            evt.StopPropagation();
            OnItemContextMenu?.Invoke(item, evt.menu);
        });

        RightPane.Add(card);
    }

    // ===================== 列表行 =====================
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

        var nameLabel = new Label(item.DisplayName);
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

        // 路径列
        var pathLabel = new Label(item.AssetPath);
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
        row.RegisterCallback<MouseDownEvent>(evt => OnItemMouseDown?.Invoke(item, evt.clickCount));
        row.RegisterCallback<ContextualMenuPopulateEvent>(evt =>
        {
            evt.StopPropagation();
            OnItemContextMenu?.Invoke(item, evt.menu);
        });

        RightPane.Add(row);
    }

    public const int DeleteAction = -1;
}
