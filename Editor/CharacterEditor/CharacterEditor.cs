using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public class CharacterEditorWindow : EditorWindow
{
    private CharacterEditorPresenter presenter;
    private CharacterListPanelView listPanel;
    private CharacterDetailPanelView detailPanel;
    private BigPreviewOverlay previewOverlay;

    [MenuItem("VNovelizer/角色编辑器 (Character Editor)", false, 21)]
    public static void ShowWindow()
    {
        var wnd = GetWindow<CharacterEditorWindow>();
        wnd.titleContent = new GUIContent("角色编辑器");
        wnd.minSize = new Vector2(960, 640);
    }

    private void OnEnable()
    {
        EnsurePresenter();
        EditorApplication.projectChanged += OnProjectChanged;
    }

    private void OnDisable()
    {
        EditorApplication.projectChanged -= OnProjectChanged;
        DisposeViews();
    }

    public void CreateGUI()
    {
        EnsurePresenter();
        DisposeViews();

        var root = rootVisualElement;
        root.Clear();
        root.style.backgroundColor = GalleryTheme.Hex(GalleryTheme.BgPrimary);
        root.style.flexDirection = FlexDirection.Column;

        // 大图浮层
        previewOverlay = new BigPreviewOverlay();
        root.Add(previewOverlay);

        BuildTitleBar(root);

        var splitView = new TwoPaneSplitView(0, 340, TwoPaneSplitViewOrientation.Horizontal);
        splitView.style.flexGrow = 1;
        splitView.style.minWidth = 0;
        splitView.style.minHeight = 0;

        listPanel = new CharacterListPanelView(presenter);
        detailPanel = new CharacterDetailPanelView(presenter, () => previewOverlay);

        splitView.Add(listPanel);
        splitView.Add(detailPanel);
        root.Add(splitView);

        // ESC 关闭大图浮层
        root.RegisterCallback<KeyDownEvent>(e =>
        {
            if (e.keyCode == KeyCode.Escape && previewOverlay.style.display == DisplayStyle.Flex)
            {
                previewOverlay.Hide();
                e.StopPropagation();
            }
        });

        listPanel.Refresh();
        detailPanel.Rebuild();
    }

    private void BuildTitleBar(VisualElement root)
    {
        var titleBar = new VisualElement();
        titleBar.style.flexDirection = FlexDirection.Row;
        titleBar.style.height = 48;
        titleBar.style.backgroundColor = GalleryTheme.Hex(GalleryTheme.BgSecondary);
        titleBar.style.alignItems = Align.Center;
        titleBar.style.paddingLeft = 16;
        titleBar.style.paddingRight = 12;
        titleBar.style.borderBottomWidth = 1;
        titleBar.style.borderBottomColor = GalleryTheme.Hex(GalleryTheme.Border);
        root.Add(titleBar);

        var icon = new Label("\u25C8")
        {
            style =
            {
                fontSize = 16,
                color = GalleryTheme.Hex(GalleryTheme.Accent),
                marginRight = 8
            }
        };
        titleBar.Add(icon);

        var title = new Label("角色编辑器")
        {
            style =
            {
                fontSize = 16,
                unityFontStyleAndWeight = FontStyle.Bold,
                color = GalleryTheme.Hex(GalleryTheme.TextPrimary),
                marginRight = 24
            }
        };
        titleBar.Add(title);

        var subtitle = new Label("Character Profiles")
        {
            style =
            {
                fontSize = 11,
                color = GalleryTheme.Hex(GalleryTheme.TextMuted)
            }
        };
        titleBar.Add(subtitle);

        titleBar.Add(new VisualElement { style = { flexGrow = 1 } });

        var refreshBtn = new Button(() =>
        {
            presenter.LoadAll(true);
        })
        { text = "\u21BB 刷新" };
        GalleryStyles.ApplyButton(refreshBtn, GalleryTheme.BgCard, false);
        refreshBtn.style.height = 30;
        refreshBtn.style.marginRight = 8;
        titleBar.Add(refreshBtn);

        var createBtn = new Button(presenter.CreateNewCharacter) { text = "+ 新建角色" };
        GalleryStyles.ApplyButton(createBtn, GalleryTheme.Accent, true);
        createBtn.style.height = 30;
        titleBar.Add(createBtn);
    }

    private void EnsurePresenter()
    {
        if (presenter != null) return;
        presenter = new CharacterEditorPresenter();
        presenter.LoadAll(false);
    }

    private void DisposeViews()
    {
        listPanel?.Dispose();
        detailPanel?.Dispose();
        listPanel = null;
        detailPanel = null;
    }

    private void OnProjectChanged()
    {
        if (presenter == null) return;
        presenter.LoadAll(true);
        // LoadAll 内部已通过 OnDataChanged/OnSelectionChanged 触发 View 刷新
    }
}
