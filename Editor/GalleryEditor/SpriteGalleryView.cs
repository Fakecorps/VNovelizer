using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;

/// <summary>
/// CG 差分图区：虚拟化 ListView + 拖放添加 + 拖拽重排 + 点击大图。
/// 修复要点：
/// 1) makeItem 创建固定结构，bindItem 仅更新数据（修复虚拟化复用性能）
/// 2) 修改 sprite 后回调里通知外层刷新左栏缩略图
/// 3) 大图预览通过回调传入的 overlay 显示
/// </summary>
public class SpriteGalleryView : VisualElement
{
    private readonly CGData cg;
    private readonly GalleryEditorPresenter presenter;
    private readonly BigPreviewOverlay previewOverlay;
    private readonly System.Action onRefreshRequested;
    private ListView spriteList;
    private Label titleLabel;

    public SpriteGalleryView(CGData cg, GalleryEditorPresenter presenter, BigPreviewOverlay overlay, System.Action onRefreshRequested)
    {
        this.cg = cg;
        this.presenter = presenter;
        this.previewOverlay = overlay;
        this.onRefreshRequested = onRefreshRequested;
        style.marginTop = 20;
        style.minWidth = 0;
        style.flexShrink = 1;

        BuildHeader();
        BuildDropZone();
    }

    private class RowRefs
    {
        public Image preview;
        public ObjectField field;
        public Button delBtn;
        public int index = -1;
    }

    private void BuildHeader()
    {
        var header = new VisualElement();
        header.style.flexDirection = FlexDirection.Row;
        header.style.alignItems = Align.Center;
        header.style.marginBottom = 8;
        header.style.paddingBottom = 6;
        header.style.borderBottomWidth = 1;
        header.style.borderBottomColor = GalleryTheme.Hex(GalleryTheme.Border);
        header.style.minWidth = 0;

        titleLabel = new Label($"差分图片（{cg.sprites.Count}）");
        titleLabel.style.fontSize = 14;
        titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        titleLabel.style.color = GalleryTheme.Hex(GalleryTheme.TextPrimary);
        titleLabel.style.flexGrow = 1;
        titleLabel.style.flexShrink = 1;
        titleLabel.style.overflow = Overflow.Hidden;
        titleLabel.style.textOverflow = TextOverflow.Ellipsis;
        titleLabel.style.whiteSpace = WhiteSpace.NoWrap;
        header.Add(titleLabel);

        var addBtn = new Button(() => presenter.AddSpriteAt(cg, cg.sprites.Count))
        { text = "+ 添加差分" };
        GalleryStyles.ApplyButton(addBtn, GalleryTheme.Accent, true);
        addBtn.style.flexShrink = 0;
        header.Add(addBtn);

        Add(header);
    }

    private void BuildDropZone()
    {
        var dropZone = new VisualElement();
        dropZone.style.flexGrow = 1;
        dropZone.style.flexShrink = 1;
        dropZone.style.minWidth = 0;
        dropZone.style.minHeight = 120;
        dropZone.style.borderTopWidth = 1;
        dropZone.style.borderBottomWidth = 1;
        dropZone.style.borderLeftWidth = 1;
        dropZone.style.borderRightWidth = 1;
        dropZone.style.borderTopColor = GalleryTheme.Hex(GalleryTheme.Border);
        dropZone.style.borderBottomColor = GalleryTheme.Hex(GalleryTheme.Border);
        dropZone.style.borderLeftColor = GalleryTheme.Hex(GalleryTheme.Border);
        dropZone.style.borderRightColor = GalleryTheme.Hex(GalleryTheme.Border);
        dropZone.style.borderTopLeftRadius = 6;
        dropZone.style.borderTopRightRadius = 6;
        dropZone.style.borderBottomLeftRadius = 6;
        dropZone.style.borderBottomRightRadius = 6;
        dropZone.style.marginBottom = 8;

        spriteList = new ListView();
        spriteList.style.flexGrow = 1;
        spriteList.style.flexShrink = 1;
        spriteList.style.minWidth = 0;
        spriteList.style.backgroundColor = GalleryTheme.Transparent_Color;
        spriteList.style.borderBottomWidth = 0;
        spriteList.fixedItemHeight = 72;
        spriteList.itemsSource = cg.sprites;
        spriteList.reorderable = true;
        spriteList.itemIndexChanged += (from, to) =>
        {
            if (from == to) return;
            presenter.PersistSpriteOrder(cg);
            UpdateTitle();
            onRefreshRequested?.Invoke();
            spriteList.RefreshItems();
        };

        // makeItem：固定结构 + 一次性回调，回调通过 currentTarget 找控件，再读 userData 取 index
        spriteList.makeItem = () =>
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.paddingLeft = 8;
            row.style.paddingRight = 8;
            row.style.paddingTop = 4;
            row.style.paddingBottom = 4;
            row.style.borderBottomWidth = 1;
            row.style.borderBottomColor = GalleryTheme.Hex(GalleryTheme.Border);
            row.style.minWidth = 0;
            row.style.overflow = Overflow.Hidden;

            var refs = new RowRefs();
            row.userData = refs;

            var preview = new Image();
            preview.name = "preview";
            preview.style.width = 56;
            preview.style.height = 56;
            preview.style.minWidth = 56;
            preview.style.minHeight = 56;
            preview.style.flexShrink = 0;
            preview.style.marginRight = 10;
            preview.scaleMode = ScaleMode.ScaleToFit;
            preview.style.backgroundColor = GalleryTheme.Hex(GalleryTheme.BgPrimary);
            preview.style.borderTopLeftRadius = 4;
            preview.style.borderTopRightRadius = 4;
            preview.style.borderBottomLeftRadius = 4;
            preview.style.borderBottomRightRadius = 4;
            preview.tooltip = "点击查看大图";
            preview.userData = refs;
            preview.RegisterCallback<ClickEvent>(OnPreviewClick);
            refs.preview = preview;
            row.Add(preview);

            var field = new ObjectField { objectType = typeof(Sprite) };
            field.name = "spriteField";
            field.style.flexGrow = 1;
            field.style.flexShrink = 1;
            field.style.minWidth = 0;
            GalleryStyles.ApplyField(field);
            field.userData = refs;
            field.RegisterValueChangedCallback(OnFieldChanged);
            refs.field = field;
            row.Add(field);

            var delBtn = new Button { text = "X" };
            delBtn.name = "delBtn";
            delBtn.style.flexShrink = 0;
            GalleryStyles.ApplyButton(delBtn, GalleryTheme.Danger, true);
            delBtn.style.width = 32;
            delBtn.userData = refs;
            delBtn.clicked += () => OnDeleteClick(refs);
            refs.delBtn = delBtn;
            row.Add(delBtn);

            return row;
        };

        spriteList.bindItem = BindRow;
        spriteList.unbindItem = UnbindRow;

        dropZone.Add(spriteList);

        // 拖放：从 Project 窗口拖入 Sprite
        dropZone.RegisterCallback<DragUpdatedEvent>(e =>
        {
            bool hasSprite = false;
            foreach (var o in DragAndDrop.objectReferences)
                if (o is Sprite) { hasSprite = true; break; }
            if (hasSprite) DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
        });
        dropZone.RegisterCallback<DragPerformEvent>(e =>
        {
            var sprites = new System.Collections.Generic.List<Sprite>();
            foreach (var o in DragAndDrop.objectReferences)
                if (o is Sprite s) sprites.Add(s);
            if (sprites.Count > 0)
            {
                presenter.AddSpritesFromDrag(cg, sprites.ToArray());
                UpdateTitle();
            }
        });

        Add(dropZone);
    }

    private void BindRow(VisualElement element, int index)
    {
        var refs = element.userData as RowRefs;
        if (refs == null) return;
        refs.index = index;

        var sprite = (index >= 0 && index < cg.sprites.Count) ? cg.sprites[index] : null;
        refs.preview.image = sprite != null ? presenter.GetCachedPreview(sprite) : null;
        refs.preview.userData = sprite;
        refs.preview.tooltip = sprite != null ? sprite.name : "空";
        refs.field.SetValueWithoutNotify(sprite);
    }

    private void UnbindRow(VisualElement element, int index)
    {
        var refs = element.userData as RowRefs;
        if (refs == null) return;

        refs.index = -1;
        refs.preview.image = null;
        refs.preview.userData = null;
        refs.preview.tooltip = "空";
        refs.field.SetValueWithoutNotify(null);
    }

    private void OnPreviewClick(ClickEvent evt)
    {
        var preview = evt.currentTarget as Image;
        if (preview == null) return;

        var sprite = preview.userData as Sprite;
        if (sprite != null)
            previewOverlay.Show(sprite);
    }

    private void OnFieldChanged(ChangeEvent<Object> evt)
    {
        var field = evt.currentTarget as ObjectField;
        var refs = field != null ? field.userData as RowRefs : null;
        if (refs == null) return;
        if (refs.index < 0 || refs.index >= cg.sprites.Count) return;
        var sprite = evt.newValue as Sprite;
        presenter.SetSpriteAt(cg, refs.index, sprite);

        // 立即刷新当前行的预览图（修复 Bug：原版不刷新预览）
        refs.preview.image = sprite != null ? presenter.GetCachedPreview(sprite) : null;
        refs.preview.userData = sprite;
        refs.preview.tooltip = sprite != null ? sprite.name : "空";

        // 通知外层刷新左栏 CG 主缩略图（sprites[0] 变化时）
        onRefreshRequested?.Invoke();
    }

    private void OnDeleteClick(RowRefs refs)
    {
        if (refs == null) return;
        if (refs.index < 0 || refs.index >= cg.sprites.Count) return;
        int capturedIndex = refs.index;
        presenter.RemoveSpriteAt(cg, capturedIndex);
        UpdateTitle();
        // 删完后该 row 会被 ListView 回收给另一行，BindRow 会刷新 refs.index
    }

    public void UpdateTitle()
    {
        if (titleLabel != null) titleLabel.text = $"差分图片（{cg.sprites.Count}）";
    }

    public ListView InnerListView => spriteList;
}