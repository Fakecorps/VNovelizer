using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 资源管理器左侧侧边栏视图。
/// 显示 5 个分类（背景/视频/BGM/SFX/Voice）及其资源计数。
/// 选中项高亮 + 悬停效果。
/// </summary>
public class ResourceSidebarView
{
    public VisualElement Root { get; private set; }
    public ListView CategoryList { get; private set; }

    public event Action<ResType> OnCategorySelected;

    /// <summary>【子文件夹支持】回收站入口（null = 当前选中的仍是资源类别）</summary>
    public event Action OnTrashSelected;

    private List<ResTypeItem> _items;

    public ResourceSidebarView()
    {
        Root = new VisualElement();
        _items = new List<ResTypeItem>
        {
            new() { Type = ResType.Background, DisplayName = "背景 (Backgrounds)", Index = 0 },
            new() { Type = ResType.Video,      DisplayName = "视频 (Videos)",       Index = 1 },
            new() { Type = ResType.BGM,        DisplayName = "背景音乐 (BGM)",      Index = 2 },
            new() { Type = ResType.SFX,        DisplayName = "音效 (SFX)",          Index = 3 },
            new() { Type = ResType.Voice,      DisplayName = "语音 (Voice)",        Index = 4 },
        };
        Build();
    }

    private void Build()
    {
        Root.style.backgroundColor = GalleryTheme.Hex(GalleryTheme.BgSecondary);
        Root.style.paddingTop = 10;
        Root.style.borderRightWidth = 1;
        Root.style.borderRightColor = GalleryTheme.Hex(GalleryTheme.Border);

        // 标题
        var title = new Label("资源分类");
        title.style.fontSize = 13;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.color = GalleryTheme.Hex(GalleryTheme.TextPrimary);
        title.style.marginLeft = 12;
        title.style.marginBottom = 8;
        Root.Add(title);

        // 分类列表
        CategoryList = new ListView();
        CategoryList.itemsSource = _items;
        CategoryList.fixedItemHeight = 28;
        CategoryList.selectionType = SelectionType.Single;
        CategoryList.makeItem = MakeCategoryItem;
        CategoryList.bindItem = BindCategoryItem;
        CategoryList.style.flexGrow = 1;
        CategoryList.style.borderTopWidth = 0; CategoryList.style.borderBottomWidth = 0;
        CategoryList.style.borderLeftWidth = 0; CategoryList.style.borderRightWidth = 0;
        CategoryList.style.backgroundColor = Color.clear;
        CategoryList.selectedIndicesChanged += OnSelectionChanged;
        Root.Add(CategoryList);

        // 【子文件夹支持】回收站入口（底部）
        _trashButton = new Button(() => OnTrashSelected?.Invoke());
        _trashButton.style.flexDirection = FlexDirection.Row;
        _trashButton.style.alignItems = Align.Center;
        _trashButton.style.height = 30;
        _trashButton.style.marginLeft = 8;
        _trashButton.style.marginRight = 8;
        _trashButton.style.marginBottom = 8;
        _trashButton.style.borderTopWidth = 1;
        _trashButton.style.borderTopColor = GalleryTheme.Hex(GalleryTheme.Border);
        _trashButton.style.borderBottomWidth = 0;
        _trashButton.style.borderLeftWidth = 0;
        _trashButton.style.borderRightWidth = 0;
        ResourceStyles.SetRadius(_trashButton, 0);
        _trashButton.style.backgroundColor = Color.clear;

        var trashIcon = new Image();
        var trashTex = UIElementBuilder.GetIcon("TreeEditor.Trash", "d_TreeEditor.Trash", "Trash", "d_Trash") as Texture;
        if (trashTex != null) trashIcon.image = trashTex;
        trashIcon.style.width = 14;
        trashIcon.style.height = 14;
        trashIcon.style.marginRight = 6;
        _trashButton.Add(trashIcon);

        var trashLabel = new Label("回收站");
        trashLabel.style.fontSize = 12;
        trashLabel.style.color = GalleryTheme.Hex(GalleryTheme.TextPrimary);
        trashLabel.style.flexGrow = 1;
        trashLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
        _trashButton.Add(trashLabel);

        _trashCountLabel = new Label("0");
        _trashCountLabel.style.fontSize = 11;
        _trashCountLabel.style.paddingLeft = 6;
        _trashCountLabel.style.paddingRight = 6;
        _trashCountLabel.style.paddingTop = 1;
        _trashCountLabel.style.paddingBottom = 1;
        _trashCountLabel.style.color = Color.white;
        _trashCountLabel.style.backgroundColor = ResourceStyles.Accent;
        ResourceStyles.SetRadius(_trashCountLabel, 8);
        _trashCountLabel.style.display = DisplayStyle.None; // 0 时隐藏
        _trashButton.Add(_trashCountLabel);

        Root.Add(_trashButton);

        // 提示
        var tip = new Label("提示\n\u2022 双击卡片打开资源\n\u2022 Delete 键删除选中\n\u2022 Ctrl/Shift 多选\n\u2022 拖入文件直接导入\n\u2022 拖动文件到文件夹卡片移动");
        tip.style.color = GalleryTheme.Hex(GalleryTheme.TextMuted);
        tip.style.fontSize = 10;
        tip.style.whiteSpace = WhiteSpace.Normal;
        tip.style.marginLeft = 12;
        tip.style.marginTop = 10;
        tip.style.marginRight = 12;
        tip.style.paddingTop = 8;
        tip.style.paddingBottom = 8;
        tip.style.paddingLeft = 8;
        tip.style.paddingRight = 8;
        tip.style.backgroundColor = GalleryTheme.Hex(GalleryTheme.BgPrimary);
        ResourceStyles.SetRadius(tip, ResourceStyles.ButtonRadius);
        Root.Add(tip);
    }

    private VisualElement MakeCategoryItem()
    {
        var item = new VisualElement();
        item.style.flexDirection = FlexDirection.Row;
        item.style.alignItems = Align.Center;
        item.style.paddingLeft = 12;
        item.style.paddingRight = 8;
        item.style.height = 28;

        var icon = new Image { name = "icon" };
        icon.style.width = 16;
        icon.style.height = 16;
        icon.style.marginRight = 6;
        item.Add(icon);

        var name = new Label { name = "name" };
        name.style.flexGrow = 1;
        name.style.color = GalleryTheme.Hex(GalleryTheme.TextPrimary);
        name.style.fontSize = 12;
        item.Add(name);

        var count = new Label { name = "count" };
        count.style.color = GalleryTheme.Hex(GalleryTheme.TextMuted);
        count.style.fontSize = 11;
        count.style.paddingLeft = 4;
        count.style.paddingRight = 4;
        count.style.backgroundColor = new Color(0, 0, 0, 0.25f);
        ResourceStyles.SetRadius(count, 8);
        item.Add(count);

        return item;
    }

    private void BindCategoryItem(VisualElement element, int index)
    {
        var item = _items[index];
        element.Q<Image>("icon").image = UIElementBuilder.GetTypeIconTexture(item.Type);
        element.Q<Label>("name").text = item.DisplayName;
        element.Q<Label>("count").text = item.Count.ToString();
        // 背景色由单独的高亮机制处理
    }

    private void OnSelectionChanged(IEnumerable<int> selected)
    {
        foreach (var idx in selected)
        {
            if (idx < 0 || idx >= _items.Count) continue;
            OnCategorySelected?.Invoke(_items[idx].Type);
            break;
        }
    }

    // ===================== 回收站入口状态 =====================

    private Button _trashButton;
    private Label _trashCountLabel;

    /// <summary>更新回收站徽标数量</summary>
    public void UpdateTrashCount(int count)
    {
        if (_trashCountLabel == null) return;
        _trashCountLabel.text = count.ToString();
        _trashCountLabel.style.display = count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
    }

    /// <summary>回收站激活态视觉（true = 激活高亮，同时取消类别列表选中）</summary>
    public void SetTrashActive(bool active)
    {
        if (_trashButton == null) return;
        _trashButton.style.backgroundColor = active
            ? GalleryTheme.Hex(GalleryTheme.Accent)
            : Color.clear;
        if (active && CategoryList.selectedIndex >= 0)
            CategoryList.SetSelection(-1);
    }

    public void UpdateCounts(Dictionary<ResType, int> counts)
    {
        foreach (var item in _items)
        {
            item.Count = counts.TryGetValue(item.Type, out var c) ? c : 0;
        }
        CategoryList.RefreshItems();
    }

    public void SetSelectedCategory(ResType type)
    {
        int idx = _items.FindIndex(i => i.Type == type);
        if (idx >= 0) CategoryList.SetSelection(idx);
    }
}
