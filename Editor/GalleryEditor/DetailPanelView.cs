using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// MVP - View：右栏详情。根据 Presenter.SelectedItem 类型分别渲染 CG / Music / Scene。
/// </summary>
public class DetailPanelView : VisualElement
{
    private readonly GalleryEditorPresenter presenter;
    private readonly BigPreviewOverlay previewOverlay;
    private readonly System.Action onRefreshList;
    private ScrollView scrollRoot;
    private VisualElement rightPane;
    private SpriteGalleryView spriteGallery;

    public DetailPanelView(GalleryEditorPresenter presenter, BigPreviewOverlay overlay, System.Action onRefreshList)
    {
        this.presenter = presenter;
        this.previewOverlay = overlay;
        this.onRefreshList = onRefreshList;
        style.flexGrow = 1;
        style.flexShrink = 1;
        style.minWidth = 0;

        Build();
        presenter.OnSelectionChanged += _ => Rebuild();
        presenter.OnModeChanged += _ => Rebuild();
    }

    private void Build()
    {
        scrollRoot = new ScrollView(ScrollViewMode.Vertical);
        scrollRoot.style.flexGrow = 1;
        scrollRoot.style.flexShrink = 1;
        scrollRoot.style.minWidth = 0;
        Add(scrollRoot);

        rightPane = new VisualElement();
        rightPane.style.paddingTop = 16;
        rightPane.style.paddingLeft = 20;
        rightPane.style.paddingRight = 20;
        rightPane.style.paddingBottom = 16;
        rightPane.style.flexGrow = 1;
        rightPane.style.minWidth = 0;
        scrollRoot.Add(rightPane);
    }

    public void Rebuild()
    {
        rightPane.Clear();
        spriteGallery = null;
        var item = presenter.SelectedItem;

        if (presenter.CurrentMode == GalleryEditorPresenter.Mode.CG && presenter.CgContainer == null)
        {
            rightPane.Add(BuildMissingHint("CGDataContainer", GalleryEditorPresenter.Mode.CG));
            return;
        }
        if (presenter.CurrentMode == GalleryEditorPresenter.Mode.Music && presenter.MusicContainer == null)
        {
            rightPane.Add(BuildMissingHint("MusicDataContainer", GalleryEditorPresenter.Mode.Music));
            return;
        }
        if (presenter.CurrentMode == GalleryEditorPresenter.Mode.Scene && presenter.SceneContainer == null)
        {
            rightPane.Add(BuildMissingHint("SceneDataContainer", GalleryEditorPresenter.Mode.Scene));
            return;
        }

        if (item == null)
        {
            rightPane.Add(BuildEmptyHint());
            return;
        }

        if (item is CGData cg) BuildCG(cg);
        else if (item is VNMusic music) BuildMusic(music);
        else if (item is VNScene scene) BuildScene(scene);
    }

    private VisualElement BuildEmptyHint()
    {
        var box = new VisualElement();
        box.style.flexGrow = 1;
        box.style.alignItems = Align.Center;
        box.style.justifyContent = Justify.Center;
        box.style.paddingTop = 80;
        box.style.color = GalleryTheme.Hex(GalleryTheme.TextMuted);
        box.Add(new Label("请在左侧选择或新建一个项目") { style = { fontSize = 14 } });
        return box;
    }

    private VisualElement BuildMissingHint(string name, GalleryEditorPresenter.Mode mode)
    {
        var box = new VisualElement();
        box.style.alignItems = Align.Center;
        box.style.justifyContent = Justify.Center;
        box.style.flexGrow = 1;

        var icon = new Label("\u26A0") { style = { fontSize = 48, color = GalleryTheme.Hex(GalleryTheme.Warning), marginBottom = 16 } };
        box.Add(icon);

        var label = new Label($"未找到 {name}\n请点击下方按钮创建，或检查 VNProjectConfig 路径配置");
        label.style.color = GalleryTheme.Hex(GalleryTheme.TextSecondary);
        label.style.fontSize = 14;
        label.style.unityTextAlign = TextAnchor.MiddleCenter;
        label.style.whiteSpace = WhiteSpace.Normal;
        label.style.marginBottom = 20;
        box.Add(label);

        var btn = new Button(() => CreateContainer(name, mode)) { text = "立即创建" };
        GalleryStyles.ApplyButton(btn, GalleryTheme.Accent, true);
        btn.style.width = 160;
        box.Add(btn);

        return box;
    }

    private void CreateContainer(string name, GalleryEditorPresenter.Mode mode)
    {
        string folder = "Assets/Resources/VNovelizerRes/GalleryContent/" + mode.ToString();
        if (VNProjectConfig.Instance != null)
        {
            if (mode == GalleryEditorPresenter.Mode.CG && !string.IsNullOrEmpty(VNProjectConfig.Instance.CG_DataPath))
                folder = "Assets/Resources/" + VNProjectConfig.Instance.CG_DataPath;
            else if (mode == GalleryEditorPresenter.Mode.Music && !string.IsNullOrEmpty(VNProjectConfig.Instance.Music_DataPath))
                folder = "Assets/Resources/" + VNProjectConfig.Instance.Music_DataPath;
            else if (mode == GalleryEditorPresenter.Mode.Scene && !string.IsNullOrEmpty(VNProjectConfig.Instance.Scene_DataPath))
                folder = "Assets/Resources/" + VNProjectConfig.Instance.Scene_DataPath;
        }

        if (!System.IO.Directory.Exists(folder)) System.IO.Directory.CreateDirectory(folder);

        ScriptableObject so;
        if (name == "CGDataContainer") so = ScriptableObject.CreateInstance<CGDataContainer>();
        else if (name == "MusicDataContainer") so = ScriptableObject.CreateInstance<MusicDataContainer>();
        else so = ScriptableObject.CreateInstance<SceneDataContainer>();

        AssetDatabase.CreateAsset(so, $"{folder}/{name}.asset");
        AssetDatabase.SaveAssets();

        presenter.LoadAll();
        presenter.SwitchMode(presenter.CurrentMode);
    }

    // =========================================================
    //                      通用：Header（ID + 删除）
    // =========================================================
    private void DrawHeader(string label, string value, System.Action<string> onRename, bool duplicate)
    {
        var box = new VisualElement();
        box.style.flexDirection = FlexDirection.Row;
        box.style.alignItems = Align.Center;
        box.style.marginBottom = 16;
        box.style.paddingBottom = 16;
        box.style.borderBottomWidth = 1;
        box.style.borderBottomColor = GalleryTheme.Hex(GalleryTheme.Border);
        box.style.minWidth = 0;

        var nameField = new TextField(label) { value = value, style = { flexGrow = 1, flexShrink = 1, minWidth = 0, marginRight = 8 } };
        GalleryStyles.ApplyField(nameField);
        if (duplicate)
        {
            nameField.style.borderBottomColor = GalleryTheme.Hex(GalleryTheme.Warning);
            nameField.style.borderBottomWidth = 2;
            nameField.tooltip = "警告：名称与其它项目重复";
        }
        nameField.RegisterValueChangedCallback(evt => onRename(evt.newValue));
        box.Add(nameField);

        bool pending = presenter.IsCurrentPendingDelete();
        var delBtn = new Button(() => presenter.DeleteSelected())
        {
            text = pending ? "确认删除?" : "删除"
        };
        GalleryStyles.ApplyButton(delBtn, pending ? GalleryTheme.Danger : GalleryTheme.BgCard, pending);
        delBtn.style.width = pending ? 110 : 72;
        delBtn.style.flexShrink = 0;
        box.Add(delBtn);

        rightPane.Add(box);
    }

    // =========================================================
    //                      CG 详情
    // =========================================================
    private void BuildCG(CGData cg)
    {
        DrawHeader("CG ID", cg.cgName, presenter.RenameSelected, presenter.IsNameDuplicate(cg.cgName, cg));

        var stateCard = GalleryStyles.MakeCard();
        var toggle = new Toggle("已解锁（调试）") { value = cg.isUnlocked };
        GalleryStyles.ApplyToggle(toggle);
        toggle.RegisterValueChangedCallback(evt => presenter.SetBool("Toggle Unlock", evt.newValue));
        stateCard.Add(toggle);
        rightPane.Add(stateCard);

        DrawSpriteField("未解锁占位图", cg.lockedSprite, sprite => presenter.SetSprite("Locked Sprite", sprite));

        // 差分图区
        spriteGallery = new SpriteGalleryView(cg, presenter, previewOverlay, onRefreshList);
        rightPane.Add(spriteGallery);
    }

    // =========================================================
    //                      Music 详情
    // =========================================================
    private void BuildMusic(VNMusic music)
    {
        DrawHeader("音乐名称", music.name, presenter.RenameSelected, presenter.IsNameDuplicate(music.name, music));

        var stateCard = GalleryStyles.MakeCard();
        var toggle = new Toggle("已解锁（调试）") { value = music.isUnlocked };
        GalleryStyles.ApplyToggle(toggle);
        toggle.RegisterValueChangedCallback(evt => presenter.SetBool("Toggle Unlock", evt.newValue));
        stateCard.Add(toggle);
        rightPane.Add(stateCard);

        DrawSpriteField("封面图（Cover）", music.picture, sprite => presenter.SetSprite("Cover", sprite));

        var clipCard = GalleryStyles.MakeCard();
        var clipField = new ObjectField("音频文件（Clip）") { objectType = typeof(AudioClip), value = music.music };
        clipField.style.flexShrink = 1;
        clipField.style.minWidth = 0;
        GalleryStyles.ApplyField(clipField);
        clipField.RegisterValueChangedCallback(evt => presenter.SetClip(evt.newValue as AudioClip));
        clipCard.Add(clipField);
        rightPane.Add(clipCard);
    }

    // =========================================================
    //                      Scene 详情
    // =========================================================
    private void BuildScene(VNScene scene)
    {
        DrawHeader("场景 ID", scene.VNscriptID, presenter.RenameSelected, presenter.IsNameDuplicate(scene.VNscriptID, scene));

        var stateCard = GalleryStyles.MakeCard();
        var toggle = new Toggle("已解锁（调试）") { value = scene.isUnLocked };
        GalleryStyles.ApplyToggle(toggle);
        toggle.RegisterValueChangedCallback(evt => presenter.SetBool("Toggle Unlock", evt.newValue));
        stateCard.Add(toggle);
        rightPane.Add(stateCard);

        DrawSpriteField("未解锁图（Locked）", scene.LockedSprite, sprite => presenter.SetSprite("Locked Sprite", sprite));
        DrawSpriteField("缩略图（Unlocked）", scene.UnLockedSprite, sprite => presenter.SetSprite("Thumbnail", sprite));

        var scriptCard = GalleryStyles.MakeCard();
        var scripts = GetAvailableScripts();
        if (!string.IsNullOrEmpty(scene.ScriptName) && !scripts.Contains(scene.ScriptName))
            scripts.Add(scene.ScriptName);

        var selectedIndex = Mathf.Max(0, scripts.IndexOf(scene.ScriptName));
        var scriptPopup = new PopupField<string>("剧本文件名", scripts.Count > 0 ? scripts : new List<string> { "" }, selectedIndex);
        scriptPopup.style.flexShrink = 1;
        scriptPopup.style.minWidth = 0;
        GalleryStyles.ApplyField(scriptPopup);
        scriptPopup.RegisterValueChangedCallback(evt => presenter.SetSceneField("ScriptName", evt.newValue));
        scriptCard.Add(scriptPopup);
        rightPane.Add(scriptCard);

        var lineCard = GalleryStyles.MakeCard();
        var startField = new TextField("起始行 ID") { value = scene.StartLineID };
        var endField = new TextField("结束行 ID") { value = scene.EndLineID };
        startField.style.flexShrink = 1; startField.style.minWidth = 0;
        endField.style.flexShrink = 1; endField.style.minWidth = 0;
        GalleryStyles.ApplyField(startField);
        GalleryStyles.ApplyField(endField);
        startField.RegisterValueChangedCallback(evt => presenter.SetSceneField("StartLineID", evt.newValue));
        endField.RegisterValueChangedCallback(evt => presenter.SetSceneField("EndLineID", evt.newValue));
        lineCard.Add(startField);
        lineCard.Add(endField);
        rightPane.Add(lineCard);
    }

    private void DrawSpriteField(string label, Sprite value, System.Action<Sprite> onChange)
    {
        var card = GalleryStyles.MakeCard();
        card.style.flexDirection = FlexDirection.Row;
        card.style.alignItems = Align.Center;

        var preview = new Image();
        preview.style.width = 64;
        preview.style.height = 64;
        preview.style.minWidth = 64;
        preview.style.minHeight = 64;
        preview.style.flexShrink = 0;
        preview.style.marginRight = 12;
        preview.scaleMode = ScaleMode.ScaleToFit;
        preview.style.backgroundColor = GalleryTheme.Hex(GalleryTheme.BgPrimary);
        preview.style.borderTopLeftRadius = 6;
        preview.style.borderTopRightRadius = 6;
        preview.style.borderBottomLeftRadius = 6;
        preview.style.borderBottomRightRadius = 6;
        if (value != null) preview.image = presenter.GetCachedPreview(value);
        preview.userData = value;
        preview.tooltip = value != null ? value.name : "空";
        preview.RegisterCallback<ClickEvent>(_ =>
        {
            var sprite = preview.userData as Sprite;
            if (sprite != null) previewOverlay.Show(sprite);
        });
        card.Add(preview);

        var field = new ObjectField(label) { objectType = typeof(Sprite), value = value, style = { flexGrow = 1, flexShrink = 1, minWidth = 0 } };
        GalleryStyles.ApplyField(field);
        field.RegisterValueChangedCallback(evt =>
        {
            var sprite = evt.newValue as Sprite;
            onChange(sprite);
            preview.image = sprite != null ? presenter.GetCachedPreview(sprite) : null;
            preview.userData = sprite;
            preview.tooltip = sprite != null ? sprite.name : "空";
            onRefreshList?.Invoke();
        });
        card.Add(field);

        rightPane.Add(card);
    }

    private List<string> GetAvailableScripts()
    {
        var result = new List<string> { "" };
        try
        {
            string resPath = VNProjectConfig.Instance != null ? VNProjectConfig.Instance.VNScriptResPath : "VNovelizerRes/VNScripts";
            string folder = "Assets/Resources/" + resPath;
            if (System.IO.Directory.Exists(folder))
            {
                foreach (var f in System.IO.Directory.GetFiles(folder, "*.csv", System.IO.SearchOption.TopDirectoryOnly))
                    result.Add(System.IO.Path.GetFileNameWithoutExtension(f));
            }
        }
        catch { }
        return result;
    }
}