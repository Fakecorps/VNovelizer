using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 新建角色类型选择对话框（模态浮层）：第一张固定「普通立绘角色」，
/// 其后每张对应一个已注册的角色类型扩展（如 Live2D）。
/// 选定后调用 Presenter.CreateNewCharacter(extension)（null = 普通角色）。
/// 供角色编辑器标题栏与左侧列表的「+ 新建角色」按钮共用。
/// </summary>
public static class CharacterCreateTypeDialog
{
    public static void Show(CharacterEditorPresenter presenter, VisualElement mountRoot)
    {
        if (presenter == null || mountRoot == null) return;

        var overlay = new VisualElement();
        overlay.style.position = Position.Absolute;
        overlay.style.left = 0;
        overlay.style.right = 0;
        overlay.style.top = 0;
        overlay.style.bottom = 0;
        overlay.style.backgroundColor = new Color(0, 0, 0, 0.6f);
        overlay.style.justifyContent = Justify.Center;
        overlay.style.alignItems = Align.Center;
        overlay.name = "createTypeOverlay";

        var box = GalleryStyles.MakeCard();
        box.style.width = 560;
        box.style.maxHeight = 520;
        box.style.backgroundColor = GalleryTheme.Hex(GalleryTheme.BgSecondary);

        box.Add(new Label("选择角色类型")
        {
            style =
            {
                fontSize = 15,
                unityFontStyleAndWeight = FontStyle.Bold,
                color = GalleryTheme.Hex(GalleryTheme.TextPrimary),
                marginBottom = 8
            }
        });

        var list = new ScrollView(ScrollViewMode.Vertical);
        list.style.flexGrow = 1;
        list.style.minHeight = 60;
        box.Add(list);

        // 普通角色卡片（固定第一张）
        list.Add(BuildTypeCard("普通立绘角色",
            "静态立绘角色：分组管理立绘与头像 Sprite，剧本按 角色ID_表情 / 角色ID#分组#表情 引用。",
            null, presenter, overlay));

        // 已注册扩展（Live2D 等）
        foreach (var extension in CharacterTypeExtensionRegistry.All)
        {
            list.Add(BuildTypeCard(extension.TypeName, extension.TypeDescription, extension, presenter, overlay));
        }

        var btnRow = new VisualElement();
        btnRow.style.flexDirection = FlexDirection.Row;
        btnRow.style.justifyContent = Justify.FlexEnd;
        btnRow.style.marginTop = 10;
        var cancelBtn = new Button(() => overlay.RemoveFromHierarchy()) { text = "取消" };
        GalleryStyles.ApplyButton(cancelBtn, GalleryTheme.BgCard, false);
        cancelBtn.style.width = 80;
        btnRow.Add(cancelBtn);
        box.Add(btnRow);

        overlay.Add(box);
        overlay.RegisterCallback<ClickEvent>(evt =>
        {
            if (evt.target == overlay) overlay.RemoveFromHierarchy();
        });

        mountRoot.Add(overlay);
        overlay.BringToFront();
    }

    private static VisualElement BuildTypeCard(string typeName, string description,
        ICharacterTypeExtension extension, CharacterEditorPresenter presenter, VisualElement overlay)
    {
        var card = new VisualElement();
        card.style.flexDirection = FlexDirection.Row;
        card.style.alignItems = Align.Center;
        card.style.marginBottom = 8;
        card.style.paddingTop = 10;
        card.style.paddingBottom = 10;
        card.style.paddingLeft = 12;
        card.style.paddingRight = 12;
        card.style.backgroundColor = GalleryTheme.Hex(GalleryTheme.BgCard);
        card.style.borderTopLeftRadius = 6;
        card.style.borderTopRightRadius = 6;
        card.style.borderBottomLeftRadius = 6;
        card.style.borderBottomRightRadius = 6;

        var textBox = new VisualElement();
        textBox.style.flexGrow = 1;
        textBox.style.flexShrink = 1;
        textBox.style.minWidth = 0;
        textBox.Add(new Label(typeName)
        {
            style =
            {
                fontSize = 13,
                unityFontStyleAndWeight = FontStyle.Bold,
                color = GalleryTheme.Hex(GalleryTheme.TextPrimary)
            }
        });
        textBox.Add(new Label(description)
        {
            style =
            {
                fontSize = 11,
                color = GalleryTheme.Hex(GalleryTheme.TextSecondary),
                whiteSpace = WhiteSpace.Normal,
                marginTop = 2
            }
        });

        var chooseBtn = new Button(() =>
        {
            overlay.RemoveFromHierarchy();
            presenter.CreateNewCharacter(extension);
        }) { text = "创建" };
        GalleryStyles.ApplyButton(chooseBtn, GalleryTheme.Accent, true);
        chooseBtn.style.width = 72;
        chooseBtn.style.flexShrink = 0;
        chooseBtn.style.marginLeft = 10;

        card.Add(textBox);
        card.Add(chooseBtn);

        card.RegisterCallback<MouseEnterEvent>(_ =>
            card.style.backgroundColor = GalleryTheme.Hex(GalleryTheme.BgHover));
        card.RegisterCallback<MouseLeaveEvent>(_ =>
            card.style.backgroundColor = GalleryTheme.Hex(GalleryTheme.BgCard));

        return card;
    }
}
