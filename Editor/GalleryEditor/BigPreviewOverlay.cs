using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 大图预览浮层：覆盖整窗口的半透明遮罩，点击任意处关闭。
/// Bug 修复：背景层需要 pickingMode=Ignore 让 click 透传给遮罩，
/// 图片与关闭提示同样接受 pickingMode 让它们不阻塞 click。
/// </summary>
public class BigPreviewOverlay : VisualElement
{
    private readonly Image bigImage;
    private readonly VisualElement backdrop;

    public BigPreviewOverlay()
    {
        style.position = Position.Absolute;
        style.left = 0;
        style.top = 0;
        style.right = 0;
        style.bottom = 0;
        style.alignItems = Align.Center;
        style.justifyContent = Justify.Center;
        style.display = DisplayStyle.None;
        pickingMode = PickingMode.Ignore;

        // 背景遮罩（接收点击关闭）
        backdrop = new VisualElement();
        backdrop.style.position = Position.Absolute;
        backdrop.style.left = 0;
        backdrop.style.top = 0;
        backdrop.style.right = 0;
        backdrop.style.bottom = 0;
        backdrop.style.backgroundColor = new Color(0, 0, 0, 0.9f);
        backdrop.pickingMode = PickingMode.Position;
        backdrop.RegisterCallback<ClickEvent>(OnBackdropClick);
        Add(backdrop);

        // 图片（不接收点击，让 click 透传给 backdrop）
        bigImage = new Image();
        bigImage.name = "bigImage";
        bigImage.scaleMode = ScaleMode.ScaleToFit;
        bigImage.style.width = new Length(90, LengthUnit.Percent);
        bigImage.style.height = new Length(90, LengthUnit.Percent);
        bigImage.style.maxWidth = 1200;
        bigImage.style.maxHeight = 800;
        bigImage.pickingMode = PickingMode.Ignore;
        Add(bigImage);

        // 关闭提示
        var hint = new Label("点击任意位置关闭 · ESC 关闭");
        hint.style.position = Position.Absolute;
        hint.style.bottom = 24;
        hint.style.left = 0;
        hint.style.right = 0;
        hint.style.unityTextAlign = TextAnchor.MiddleCenter;
        hint.style.color = GalleryTheme.Hex(GalleryTheme.TextSecondary);
        hint.style.fontSize = 12;
        hint.pickingMode = PickingMode.Ignore;
        Add(hint);
    }

    public void Show(Sprite sprite)
    {
        if (sprite == null) return;
        bigImage.image = null;
        bigImage.sprite = sprite;
        style.display = DisplayStyle.Flex;
        BringToFront();
    }

    public void Show(Texture image)
    {
        if (image == null) return;
        bigImage.sprite = null;
        bigImage.image = image;
        style.display = DisplayStyle.Flex;
        BringToFront();
    }

    public void Hide()
    {
        style.display = DisplayStyle.None;
        bigImage.sprite = null;
        bigImage.image = null;
    }

    public void Toggle(Sprite sprite)
    {
        if (sprite == null)
        {
            Hide();
            return;
        }

        if (style.display == DisplayStyle.Flex && bigImage.sprite == sprite) Hide();
        else Show(sprite);
    }

    public void Toggle(Texture image)
    {
        if (image == null)
        {
            Hide();
            return;
        }

        if (style.display == DisplayStyle.Flex && bigImage.image == image) Hide();
        else Show(image);
    }

    private void OnBackdropClick(ClickEvent evt) => Hide();
}