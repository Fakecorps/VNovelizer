using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// MVP - View：角色编辑器左侧列表面板。
/// 包含搜索框、新建按钮、角色卡片网格、右键菜单。
/// </summary>
public class CharacterListPanelView : VisualElement
{
    private readonly CharacterEditorPresenter presenter;
    private ScrollView cardScroll;
    private VisualElement cardContainer;
    private ToolbarSearchField searchField;
    private Label statusLabel;

    public CharacterListPanelView(CharacterEditorPresenter presenter)
    {
        this.presenter = presenter;
        style.flexDirection = FlexDirection.Column;
        style.backgroundColor = GalleryTheme.Hex(GalleryTheme.BgSecondary);
        style.borderRightWidth = 1;
        style.borderRightColor = GalleryTheme.Hex(GalleryTheme.Border);
        style.minWidth = 280;
        style.width = 340;

        BuildSearchRow();
        BuildCardGrid();
        BuildStatusBar();

        presenter.OnDataChanged += Refresh;
        presenter.OnSelectionChanged += OnExternalSelectionChanged;
    }

    public void Dispose()
    {
        presenter.OnDataChanged -= Refresh;
        presenter.OnSelectionChanged -= OnExternalSelectionChanged;
    }

    // =========================================================
    //                      搜索行
    // =========================================================
    private void BuildSearchRow()
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.paddingTop = 8;
        row.style.paddingBottom = 8;
        row.style.paddingLeft = 8;
        row.style.paddingRight = 8;
        row.style.borderBottomWidth = 1;
        row.style.borderBottomColor = GalleryTheme.Hex(GalleryTheme.Border);
        row.style.minWidth = 0;

        searchField = new ToolbarSearchField
        {
            style =
            {
                flexGrow = 1,
                flexShrink = 1,
                minWidth = 60,
                marginRight = 6
            }
        };
        searchField.RegisterValueChangedCallback(evt =>
        {
            presenter.SearchText = evt.newValue ?? "";
            presenter.ApplyFilter();
        });
        row.Add(searchField);

        var createBtn = new Button(presenter.CreateNewCharacter) { text = "+ 新建角色" };
        GalleryStyles.ApplyButton(createBtn, GalleryTheme.Accent, true);
        createBtn.style.width = 88;
        createBtn.style.flexShrink = 0;
        createBtn.style.fontSize = 11;
        row.Add(createBtn);

        Add(row);
    }

    // =========================================================
    //                      卡片网格
    // =========================================================
    private void BuildCardGrid()
    {
        cardScroll = new ScrollView(ScrollViewMode.Vertical);
        cardScroll.style.flexGrow = 1;
        cardScroll.style.flexShrink = 1;
        cardScroll.style.minWidth = 0;
        cardScroll.style.minHeight = 0;

        cardContainer = new VisualElement();
        cardContainer.style.flexDirection = FlexDirection.Row;
        cardContainer.style.flexWrap = Wrap.Wrap;
        cardContainer.style.alignContent = Align.FlexStart;
        cardContainer.style.paddingTop = 4;
        cardContainer.style.paddingLeft = 4;
        cardContainer.style.paddingRight = 4;

        cardScroll.Add(cardContainer);
        Add(cardScroll);
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
    //                      刷新
    // =========================================================
    public void Refresh()
    {
        cardContainer.Clear();

        foreach (var profile in presenter.FilteredProfiles)
        {
            cardContainer.Add(MakeCard(profile));
        }

        if (presenter.FilteredProfiles.Count == 0)
        {
            var hint = new Label(presenter.AllProfiles.Count == 0 ? "暂无角色，点击右上角 + 新建" : "没有匹配的角色")
            {
                style =
                {
                    color = GalleryTheme.Hex(GalleryTheme.TextMuted),
                    fontSize = 12,
                    paddingTop = 10,
                    paddingLeft = 6
                }
            };
            cardContainer.Add(hint);
        }

        UpdateStatus();
    }

    private void UpdateStatus()
    {
        statusLabel.text = $"  共 {presenter.FilteredProfiles.Count} 个角色  ·  总计 {presenter.AllProfiles.Count} 个";
    }

    private void OnExternalSelectionChanged(CharacterProfile profile)
    {
        // 更新所有卡片的选中高亮
        foreach (var child in cardContainer.Children())
        {
            if (child.userData is CharacterProfile cardProfile)
            {
                bool selected = cardProfile == profile;
                child.style.backgroundColor = selected
                    ? GalleryTheme.Hex(GalleryTheme.AccentDim)
                    : GalleryTheme.Hex(GalleryTheme.BgCard);
                SetCardBorder(child, selected ? GalleryTheme.Hex(GalleryTheme.Accent) : GalleryTheme.Transparent_Color);
            }
        }
    }

    // =========================================================
    //                      卡片
    // =========================================================
    private VisualElement MakeCard(CharacterProfile profile)
    {
        bool isSelected = presenter.SelectedProfile == profile;

        var card = new VisualElement();
        card.style.width = 92;
        card.style.marginTop = 4;
        card.style.marginBottom = 4;
        card.style.marginLeft = 4;
        card.style.marginRight = 4;
        card.style.paddingTop = 4;
        card.style.paddingBottom = 4;
        card.style.backgroundColor = isSelected
            ? GalleryTheme.Hex(GalleryTheme.AccentDim)
            : GalleryTheme.Hex(GalleryTheme.BgCard);
        card.style.borderTopLeftRadius = 6;
        card.style.borderTopRightRadius = 6;
        card.style.borderBottomLeftRadius = 6;
        card.style.borderBottomRightRadius = 6;
        card.userData = profile;
        SetCardBorder(card, isSelected ? GalleryTheme.Hex(GalleryTheme.Accent) : GalleryTheme.Transparent_Color);

        // 封面图
        var cover = profile.ElementSprites != null
            ? profile.ElementSprites.FirstOrDefault(e => e != null && e.Sprite != null)?.Sprite
            : null;

        var imgWrap = new VisualElement();
        imgWrap.style.width = 84;
        imgWrap.style.height = 84;
        imgWrap.style.alignSelf = Align.Center;
        imgWrap.style.backgroundColor = GalleryTheme.Hex(GalleryTheme.BgPrimary);
        imgWrap.style.borderTopLeftRadius = 4;
        imgWrap.style.borderTopRightRadius = 4;
        imgWrap.style.borderBottomLeftRadius = 4;
        imgWrap.style.borderBottomRightRadius = 4;

        if (cover != null)
        {
            var img = new Image { scaleMode = ScaleMode.ScaleToFit, sprite = cover };
            img.style.position = Position.Absolute;
            img.style.left = 0;
            img.style.right = 0;
            img.style.top = 0;
            img.style.bottom = 0;
            imgWrap.Add(img);
        }
        else
        {
            var ph = new Label("无立绘")
            {
                style =
                {
                    position = Position.Absolute,
                    left = 0, right = 0, top = 0, bottom = 0,
                    unityTextAlign = TextAnchor.MiddleCenter,
                    color = GalleryTheme.Hex(GalleryTheme.TextMuted),
                    fontSize = 10
                }
            };
            imgWrap.Add(ph);
        }

        var nameLabel = new Label(profile.CharacterID)
        {
            style =
            {
                unityTextAlign = TextAnchor.MiddleCenter,
                fontSize = 11,
                marginTop = 3,
                whiteSpace = WhiteSpace.Normal,
                color = GalleryTheme.Hex(GalleryTheme.TextPrimary)
            }
        };

        card.Add(imgWrap);
        card.Add(nameLabel);

        card.RegisterCallback<ClickEvent>(_ => presenter.SelectProfile(profile));

        // Hover
        card.RegisterCallback<MouseEnterEvent>(_ =>
        {
            if (presenter.SelectedProfile != profile)
                card.style.backgroundColor = GalleryTheme.Hex(GalleryTheme.BgHover);
        });
        card.RegisterCallback<MouseLeaveEvent>(_ =>
        {
            if (presenter.SelectedProfile != profile)
                card.style.backgroundColor = GalleryTheme.Hex(GalleryTheme.BgCard);
        });

        // 右键菜单
        card.AddManipulator(new ContextualMenuManipulator(evt =>
        {
            evt.menu.AppendAction("编辑", _ => presenter.SelectProfile(profile));
            evt.menu.AppendAction("在 Project 中显示", _ => EditorGUIUtility.PingObject(profile));
            evt.menu.AppendSeparator();
            evt.menu.AppendAction("复制角色", _ => presenter.DuplicateCharacter(profile));
            evt.menu.AppendAction("删除角色", _ => presenter.DeleteCharacter(profile));
        }));

        return card;
    }

    private void SetCardBorder(VisualElement card, Color color)
    {
        card.style.borderTopWidth = 1;
        card.style.borderTopColor = color;
        card.style.borderBottomWidth = 1;
        card.style.borderBottomColor = color;
        card.style.borderLeftWidth = 1;
        card.style.borderLeftColor = color;
        card.style.borderRightWidth = 1;
        card.style.borderRightColor = color;
    }
}
