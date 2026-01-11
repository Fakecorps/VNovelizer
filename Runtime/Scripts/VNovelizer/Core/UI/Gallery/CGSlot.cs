using UnityEngine;
using UnityEngine.UI;

public class CGSlot : MonoBehaviour
{
    private Image image;
    private Button button;

    public CGData cgData;
    public bool isUnlocked;
    private System.Action<CGData> onClickCallback;

    // 未解锁时的占位图（可以通过Resources加载，或设置为public字段在Inspector中设置）
    private Sprite lockedSprite;

    public void Init(CGData cgData, bool isUnlocked, System.Action<CGData> onClickCallback)
    {
        this.cgData = cgData;
        this.isUnlocked = isUnlocked;
        this.onClickCallback = onClickCallback;

        // 初始化控件
        button = GetComponent<Button>();
        if (button == null)
        {
            Debug.LogError("[CGSlot] 未找到Button组件！");
        }

        // Image是Button的子对象
        Transform imageTransform = transform.Find("Image");
        if (imageTransform != null)
        {
            image = imageTransform.GetComponent<Image>();
        }

        // 如果找不到Image，尝试从子对象中查找
        if (image == null)
        {
            image = GetComponentInChildren<Image>();
        }

        if (image == null)
        {
            Debug.LogError("[CGSlot] 未找到Image组件！");
        }
        else
        {
            // 确保Image不会阻挡Button的点击
            image.raycastTarget = false;
        }

        // 设置图片
        UpdateImage();

        // 设置按钮状态
        if (button != null)
        {
            button.interactable = true; // 允许点击，在OnClick中判断是否解锁
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(OnClick);

            // 确保Button自带的Image组件不会阻挡点击
            Image buttonImage = button.GetComponent<Image>();
            if (buttonImage != null)
            {
                buttonImage.raycastTarget = true; // Button的Image需要接收射线才能点击
            }

            Debug.Log($"[CGSlot] 按钮初始化完成 - interactable: {button.interactable}, onClick listeners: {button.onClick.GetPersistentEventCount()}");
        }
        else
        {
            Debug.LogError("[CGSlot] Button组件为null，无法绑定点击事件！");
        }
    }

    /// <summary>
    /// 更新图片显示
    /// </summary>
    private void UpdateImage()
    {
        if (image == null)
        {
            Debug.LogWarning("[CGSlot] Image组件为null，无法更新图片");
            return;
        }

        //Debug.Log($"{cgData.cgName},{cgData.isUnlocked},{cgData.sprites.Count},{ cgData == null}");
        if (isUnlocked && cgData != null && cgData.sprites != null && cgData.sprites.Count > 0)
        {
            // 已解锁：显示第一张CG图片
            image.sprite = cgData.sprites[0];
            image.color = Color.white;

            if (image.sprite == null)
            {
                Debug.LogWarning($"[CGSlot] CG {cgData.cgName} 的第一张图片为null");
            }
        }
        else
        {

            lockedSprite = cgData.lockedSprite;

            if (lockedSprite == null)
            {
                if (cgData != null && cgData.lockedSprite != null)
                {
                    image.sprite = cgData.lockedSprite;
                }
            }

            image.sprite = lockedSprite;
            image.color = Color.white;

            // 如果没有图片，使用灰色占位
            if (image.sprite == null)
            {
                image.sprite = null;
                image.color = new Color(0.3f, 0.3f, 0.3f, 1f);
            }
        }

        // 确保Image启用
        if (image != null)
        {
            image.enabled = true;
        }
    }

    /// <summary>
    /// 解锁此CG槽位
    /// </summary>
    public void Unlock()
    {
        isUnlocked = true;

        // 更新图片
        UpdateImage();

        // 按钮已经可以点击（在Init中已设置），这里不需要额外操作
    }

    /// <summary>
    /// 点击事件
    /// </summary>
    private void OnClick()
    {

        if (!isUnlocked)
        {
            Debug.Log($"[CGSlot] CG {cgData?.cgName} 未解锁，无法查看");
            return;
        }

        if (onClickCallback != null && cgData != null)
        {
            Debug.Log($"[CGSlot] 调用回调函数打开CG: {cgData.cgName}");
            onClickCallback(cgData);
        }
        else
        {
            Debug.LogWarning($"[CGSlot] onClickCallback或cgData为null - callback: {onClickCallback != null}, cgData: {cgData != null}");
        }
    }
}
