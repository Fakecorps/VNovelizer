using UnityEngine;
using UnityEngine.UI;

public class SceneSlot : MonoBehaviour
{
    private Image image;
    private Button button;

    public VNScene sceneData;
    public bool isUnlocked;
    private System.Action<VNScene> onClickCallback;

    public void Init(VNScene sceneData, bool isUnlocked, System.Action<VNScene> onClickCallback)
    {
        this.sceneData = sceneData;
        this.isUnlocked = isUnlocked;
        this.onClickCallback = onClickCallback;

        // 初始化控件
        button = GetComponent<Button>();
        if (button == null)
        {
            Debug.LogError("[SceneSlot] 未找到Button组件！");
        }

        // 获取Image
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
            Debug.LogError("[SceneSlot] 未找到Image组件！");
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
        }
        else
        {
            Debug.LogError("[SceneSlot] Button组件为null，无法绑定点击事件！");
        }
    }

    /// <summary>
    /// 更新图片显示
    /// </summary>
    private void UpdateImage()
    {
        if (image == null)
        {
            Debug.LogWarning("[SceneSlot] Image组件为null，无法更新图片");
            return;
        }

        if (isUnlocked && sceneData != null && sceneData.UnLockedSprite != null)
        {
            // 已解锁：显示解锁后的缩略图
            image.sprite = sceneData.UnLockedSprite;
            image.color = Color.white;
        }
        else
        {
            // 未解锁：显示锁定占位图
            if (sceneData != null && sceneData.LockedSprite != null)
            {
                image.sprite = sceneData.LockedSprite;
            }
            else
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
    /// 解锁此场景槽位
    /// </summary>
    public void Unlock()
    {
        isUnlocked = true;
        UpdateImage();
    }

    /// <summary>
    /// 点击事件
    /// </summary>
    private void OnClick()
    {
        if (!isUnlocked)
        {
            Debug.Log($"[SceneSlot] 场景 {sceneData?.VNscriptID} 未解锁，无法回放");
            return;
        }

        if (onClickCallback != null && sceneData != null)
        {
            Debug.Log($"[SceneSlot] 调用回调函数开始场景回放: {sceneData.VNscriptID}");
            onClickCallback(sceneData);
        }
        else
        {
            Debug.LogWarning($"[SceneSlot] onClickCallback或sceneData为null - callback: {onClickCallback != null}, sceneData: {sceneData != null}");
        }
    }
}






