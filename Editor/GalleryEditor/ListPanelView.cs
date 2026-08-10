using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;

/// <summary>
/// MVP - View：左栏列表 + 搜索框 + 新建按钮 + 右键菜单 + 状态栏。
/// 纯 UI 渲染，所有数据通过 Presenter 流入流出。
/// </summary>
public class ListPanelView : VisualElement
{
    private readonly GalleryEditorPresenter presenter;
    private ListView listView;
    private ToolbarSearchField searchField;
    private Label statusLabel;
    private VisualElement emptyState;

    // 列表项复用结构
    private class CardRefs
    {
        public VisualElement card;
        public Image thumb;
        public Label nameLabel;
        public Label subLabel;
        public Label badge;
    }

    public ListPanelView(GalleryEditorPresenter presenter)
    {
        this.presenter = presenter;
        style.flexDirection = FlexDirection.Column;
        style.backgroundColor = GalleryTheme.Hex(GalleryTheme.BgSecondary);
        style.borderRightWidth = 1;
        style.borderRightColor = GalleryTheme.Hex(GalleryTheme.Border);
        style.minWidth = 280;
        style.width = 340;

        BuildSearchRow();
        BuildList();
        BuildStatusBar();

        // 监听 Presenter
        presenter.OnDataChanged += Refresh;
        presenter.OnModeChanged += _ => Refresh();
        presenter.OnSelectionChanged += _ => Refresh();
    }

    // =========================================================
    //                      构建
    // =========================================================
    private void BuildSearchRow()
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.paddingTop = 8;
        row.style.paddingBottom = 8;
        row.style.paddingLeft = 8;
        row.style.paddingRight = 8;
        row.style.borderBottomWidth = 1;
        row.style.borderBottomColor = GalleryTheme.Hex(GalleryTheme.Border);
        row.style.minWidth = 0;

        searchField = new ToolbarSearchField { style = { flexGrow = 1, flexShrink = 1, minWidth = 60, marginRight = 6 } };
        searchField.RegisterValueChangedCallback(evt =>
        {
            presenter.SearchText = evt.newValue ?? "";
            listView.RefreshItems();
        });
        row.Add(searchField);

        var addBtn = new Button(presenter.CreateNew) { text = "+ 新建" };
        GalleryStyles.ApplyButton(addBtn, GalleryTheme.Accent, true);
        addBtn.style.width = 72;
        addBtn.style.flexShrink = 0;
        row.Add(addBtn);

        Add(row);
    }

    private void BuildList()
    {
        var listContainer = new VisualElement();
        listContainer.style.flexGrow = 1;
        listContainer.style.flexShrink = 1;
        listContainer.style.minWidth = 0;
        listContainer.style.position = Position.Relative;

        listView = new ListView();
        listView.selectionType = SelectionType.Single;
        listView.style.flexGrow = 1;
        listView.style.flexShrink = 1;
        listView.style.minWidth = 0;
        listView.style.backgroundColor = GalleryTheme.Transparent_Color;
        listView.style.borderBottomWidth = 0;
        listView.fixedItemHeight = 60;
        listView.reorderable = true;
        listView.makeItem = MakeCard;
        listView.bindItem = BindCard;
        listView.unbindItem = UnbindCard;
        listView.selectionChanged += OnSelection;
        listView.itemIndexChanged += OnItemReorder;
        listContainer.Add(listView);

        emptyState = MakeEmptyCard();
        emptyState.style.position = Position.Absolute;
        emptyState.style.left = 0;
        emptyState.style.right = 0;
        emptyState.style.top = 0;
        emptyState.style.bottom = 0;
        emptyState.style.display = DisplayStyle.None;
        emptyState.pickingMode = PickingMode.Ignore;
        listContainer.Add(emptyState);

        Add(listContainer);
    }

    private void BuildStatusBar()
    {
        statusLabel = new Label("");
        statusLabel.style.paddingTop = 6;
        statusLabel.style.paddingBottom = 6;
        statusLabel.style.paddingLeft = 12;
        statusLabel.style.paddingRight = 12;
        statusLabel.style.color = GalleryTheme.Hex(GalleryTheme.TextMuted);
        statusLabel.style.fontSize = 11;
        statusLabel.style.borderTopWidth = 1;
        statusLabel.style.borderTopColor = GalleryTheme.Hex(GalleryTheme.Border);
        statusLabel.style.backgroundColor = GalleryTheme.Hex(GalleryTheme.BgPrimary);
        Add(statusLabel);
    }

    // =========================================================
    //                      列表项
    // =========================================================
    private VisualElement MakeCard()
    {
        var card = new VisualElement();
        card.style.flexDirection = FlexDirection.Row;
        card.style.alignItems = Align.Center;
        card.style.height = 56;
        card.style.paddingLeft = 10;
        card.style.paddingRight = 10;
        card.style.marginTop = 2;
        card.style.marginBottom = 2;
        card.style.borderBottomWidth = 1;
        card.style.borderBottomColor = GalleryTheme.Hex(GalleryTheme.Border);
        card.style.borderLeftWidth = 3;
        card.style.borderLeftColor = GalleryTheme.Transparent_Color;
        card.style.backgroundColor = GalleryTheme.Transparent_Color;
        card.style.overflow = Overflow.Hidden;

        var thumb = new Image();
        thumb.name = "thumb";
        thumb.style.width = 44;
        thumb.style.height = 44;
        thumb.style.minWidth = 44;
        thumb.style.minHeight = 44;
        thumb.style.flexShrink = 0;
        thumb.style.marginRight = 10;
        thumb.scaleMode = ScaleMode.ScaleToFit;
        thumb.style.backgroundColor = GalleryTheme.Hex(GalleryTheme.BgPrimary);
        thumb.style.borderTopLeftRadius = 4;
        thumb.style.borderTopRightRadius = 4;
        thumb.style.borderBottomLeftRadius = 4;
        thumb.style.borderBottomRightRadius = 4;
        card.Add(thumb);

        var textCol = new VisualElement { style = { flexGrow = 1, flexShrink = 1, minWidth = 0, overflow = Overflow.Hidden } };

        var nameLabel = new Label { name = "nameLabel" };
        nameLabel.style.fontSize = 13;
        nameLabel.style.color = GalleryTheme.Hex(GalleryTheme.TextPrimary);
        nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        nameLabel.style.overflow = Overflow.Hidden;
        nameLabel.style.textOverflow = TextOverflow.Ellipsis;
        nameLabel.style.whiteSpace = WhiteSpace.NoWrap;
        textCol.Add(nameLabel);

        var subLabel = new Label { name = "subLabel" };
        subLabel.style.fontSize = 10;
        subLabel.style.color = GalleryTheme.Hex(GalleryTheme.TextMuted);
        subLabel.style.overflow = Overflow.Hidden;
        subLabel.style.textOverflow = TextOverflow.Ellipsis;
        subLabel.style.whiteSpace = WhiteSpace.NoWrap;
        textCol.Add(subLabel);

        card.Add(textCol);

        var badge = new Label { name = "badge" };
        badge.style.fontSize = 10;
        badge.style.paddingLeft = 6;
        badge.style.paddingRight = 6;
        badge.style.paddingTop = 2;
        badge.style.paddingBottom = 2;
        badge.style.borderTopLeftRadius = 8;
        badge.style.borderTopRightRadius = 8;
        badge.style.borderBottomLeftRadius = 8;
        badge.style.borderBottomRightRadius = 8;
        badge.style.marginLeft = 6;
        badge.style.flexShrink = 0;
        card.Add(badge);

        // hover
        card.RegisterCallback<MouseEnterEvent>(_ =>
        {
            if (!card.ClassListContains("selected"))
                card.style.backgroundColor = GalleryTheme.Hex(GalleryTheme.BgHover);
        });
        card.RegisterCallback<MouseLeaveEvent>(_ =>
        {
            if (!card.ClassListContains("selected"))
                card.style.backgroundColor = GalleryTheme.Transparent_Color;
        });

        // 右键菜单
        card.AddManipulator(new ContextualMenuManipulator(evt => BuildItemContextMenu(evt)));

        // userData: CardRefs 便于 Bind / Unbind
        card.userData = new CardRefs { card = card, thumb = thumb, nameLabel = nameLabel, subLabel = subLabel, badge = badge };
        return card;
    }

    private void BindCard(VisualElement element, int index)
    {
        var source = listView.itemsSource;
        if (source == null || index < 0 || index >= source.Count) return;
        var item = source[index];
        var refs = element.userData as CardRefs;
        if (refs == null) return;

        var sprite = presenter.GetThumbSprite(item);
        refs.thumb.image = sprite != null ? presenter.GetCachedPreview(sprite) : null;

        string name = presenter.GetName(item);
        refs.nameLabel.text = name;
        refs.subLabel.text = presenter.GetSubText(item);

        bool unlocked = presenter.IsUnlocked(item);
        refs.badge.style.display = unlocked ? DisplayStyle.Flex : DisplayStyle.None;
        refs.badge.text = "解锁";
        refs.badge.style.color = GalleryTheme.Hex(GalleryTheme.Success);
        refs.badge.style.backgroundColor = new Color(0.18f, 0.36f, 0.32f, 1f);

        // 选中态
        bool selected = listView.selectedIndex == index;
        refs.card.EnableInClassList("selected", selected);
        refs.card.style.borderLeftColor = selected
            ? GalleryTheme.Hex(GalleryTheme.Accent)
            : GalleryTheme.Transparent_Color;

        // 搜索过滤
        bool match = string.IsNullOrEmpty(presenter.SearchText)
            || name.IndexOf(presenter.SearchText, System.StringComparison.OrdinalIgnoreCase) >= 0;
        element.style.display = match ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private void UnbindCard(VisualElement element, int index)
    {
        var refs = element.userData as CardRefs;
        if (refs == null) return;
        refs.thumb.image = null;
    }

    private VisualElement MakeEmptyCard()
    {
        var box = new VisualElement();
        box.style.flexGrow = 1;
        box.style.flexDirection = FlexDirection.Column;
        box.style.alignItems = Align.Center;
        box.style.justifyContent = Justify.Center;
        box.style.paddingTop = 32;
        box.style.paddingBottom = 32;

        var icon = new Label("\u2205") { style = { fontSize = 28, color = GalleryTheme.Hex(GalleryTheme.TextMuted), marginBottom = 8 } };
        box.Add(icon);

        var tip = new Label("空列表") { style = { color = GalleryTheme.Hex(GalleryTheme.TextMuted), fontSize = 12 } };
        box.Add(tip);

        return box;
    }

    private void OnSelection(IEnumerable<object> selection)
    {
        foreach (var item in selection)
        {
            presenter.SetSelected(item, listView.selectedIndex);
            break;
        }
    }

    private void OnItemReorder(int from, int to)
    {
        if (from == to) return;
        presenter.PersistCurrentOrder();
    }

    // =========================================================
    //                      右键菜单
    // =========================================================
    private void BuildItemContextMenu(ContextualMenuPopulateEvent evt)
    {
        int index = listView.selectedIndex;
        var source = listView.itemsSource;
        if (index < 0 || index >= source.Count) return;
        var item = source[index];

        evt.menu.AppendAction("重命名", _ => { listView.Focus(); presenter.SetSelected(item, index); });
        evt.menu.AppendAction("复制", _ => { presenter.SetSelected(item, index); presenter.CopySelected(); });
        evt.menu.AppendAction("粘贴", _ => presenter.Paste(),
            presenter.ClipboardItem != null && presenter.ClipboardMode == presenter.CurrentMode
                ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
        evt.menu.AppendSeparator();
        evt.menu.AppendAction("上移", _ => presenter.Move(index, -1),
            index > 0 ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
        evt.menu.AppendAction("下移", _ => presenter.Move(index, 1),
            index < source.Count - 1 ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
        evt.menu.AppendSeparator();
        evt.menu.AppendAction("删除...", _ => presenter.DeleteWithDialog(item));
    }

    // =========================================================
    //                      公开 API
    // =========================================================
    public void Refresh()
    {
        listView.itemsSource = presenter.GetSourceList(presenter.CurrentMode);
        listView.Rebuild();

        // 恢复或建立默认选中
        var source = listView.itemsSource;
        int idx = presenter.GetLastSelection(presenter.CurrentMode);
        if (idx < 0 || idx >= source.Count)
            idx = source.Count > 0 ? 0 : -1;

        if (idx >= 0)
        {
            if (listView.selectedIndex != idx)
                listView.SetSelection(idx);
            else if (!ReferenceEquals(presenter.SelectedItem, source[idx]))
                presenter.SetSelected(source[idx], idx);
        }
        else if (listView.selectedIndex >= 0)
        {
            listView.ClearSelection();
        }

        UpdateStatus(source.Count);
        if (emptyState != null)
            emptyState.style.display = source.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
    }

    public int SelectedIndex => listView != null ? listView.selectedIndex : -1;

    public void RefreshItem(int index)
    {
        if (listView != null && index >= 0 && index < (listView.itemsSource?.Count ?? 0))
            listView.RefreshItem(index);
    }

    private void UpdateStatus(int count)
    {
        string modeName = presenter.CurrentMode == GalleryEditorPresenter.Mode.CG ? "CG"
            : presenter.CurrentMode == GalleryEditorPresenter.Mode.Music ? "音乐" : "场景";
        statusLabel.text = $"  {modeName}  ·  共 {count} 项";
    }
}