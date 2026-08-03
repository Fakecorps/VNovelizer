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
        Root.style.backgroundColor = ResourceStyles.Sidebar;
        Root.style.paddingTop = 10;
        Root.style.borderRightWidth = 1;
        Root.style.borderRightColor = ResourceStyles.CardBorder;

        // 标题
        var title = new Label("资源分类");
        title.style.fontSize = 13;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.color = ResourceStyles.TextPrimary;
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

        // 提示
        var tip = new Label("提示\n• 双击卡片打开资源\n• Delete 键删除选中\n• Ctrl/Shift 多选\n• 拖入文件直接导入");
        tip.style.color = ResourceStyles.TextSecondary;
        tip.style.fontSize = 10;
        tip.style.whiteSpace = WhiteSpace.Normal;
        tip.style.marginLeft = 12;
        tip.style.marginTop = 10;
        tip.style.marginRight = 12;
        tip.style.paddingTop = 8;
        tip.style.paddingBottom = 8;
        tip.style.paddingLeft = 8;
        tip.style.paddingRight = 8;
        tip.style.backgroundColor = new Color(0.10f, 0.10f, 0.10f);
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
        name.style.color = ResourceStyles.TextPrimary;
        name.style.fontSize = 12;
        item.Add(name);

        var count = new Label { name = "count" };
        count.style.color = ResourceStyles.TextSecondary;
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
