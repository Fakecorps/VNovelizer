using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.IO;
using UnityEngine.Events; // 引入以使用 UnityAction

/// <summary>
/// 存档槽位组件 (挂载在 SaveSlot 预制体上)
/// </summary>
public class SaveSlot : MonoBehaviour
{
    // UI组件
    [SerializeField] private Image screenshotImage;
    [SerializeField] private Button deleteButton; // 删除按钮
    [SerializeField] private TextMeshProUGUI slotText;
    [SerializeField] private TextMeshProUGUI dateText;
    [SerializeField] private Button slotButton; // 整个 Slot 的按钮

    // 运行时数据
    private int slotIndex;
    private SaveData saveData;
    private SaveLoadPanel.Mode mode;
    private bool isAutoSaveSlot = false; // 自动存档槽：SlotText 固定 [自动]，不参与普通编号
    private UnityAction<int> onClickCallback;
    private UnityAction<int> onDeleteCallback;

    // 动态加载的截图资源（槽位销毁/刷新时必须手动释放，否则泄漏）
    private Texture2D _loadedTexture;
    private Sprite _loadedSprite;

    // 自我初始化
    private void Awake()
    {
        if (slotButton == null) slotButton = GetComponent<Button>();

        // 自动查找子组件（防止 Inspector 漏拖）
        if (slotText == null) slotText = transform.Find("SlotText")?.GetComponent<TextMeshProUGUI>();
        if (dateText == null) dateText = transform.Find("DateText")?.GetComponent<TextMeshProUGUI>();
        if (screenshotImage == null) screenshotImage = transform.Find("Screenshot")?.GetComponent<Image>();
        if (deleteButton == null) deleteButton = transform.Find("DeleteButton")?.GetComponent<Button>();
    }

    /// <summary>
    /// 初始化存档槽位
    /// </summary>
    public void Init(int index, SaveData data, SaveLoadPanel.Mode mode,
                     UnityAction<int> onClick, UnityAction<int> onDelete, bool isAuto = false)
    {
        this.slotIndex = index;
        this.saveData = data;
        this.mode = mode;
        this.isAutoSaveSlot = isAuto;
        this.onClickCallback = onClick;
        this.onDeleteCallback = onDelete;

        // 绑定点击事件
        if (slotButton != null)
        {
            slotButton.onClick.RemoveAllListeners();
            slotButton.onClick.AddListener(OnSlotClick);
        }

        // 绑定删除事件
        if (deleteButton != null)
        {
            deleteButton.onClick.RemoveAllListeners();
            deleteButton.onClick.AddListener(OnDeleteClick);
        }

        // 更新显示内容
        UpdateDisplay();
    }

    /// <summary>
    /// 更新显示
    /// </summary>
    private void UpdateDisplay()
    {
        if (slotText != null) slotText.text = isAutoSaveSlot ? "[自动]" : $"[{slotIndex + 1}]";

        if (saveData != null)
        {
            // --- 有存档数据 ---
            if (dateText != null) dateText.text = saveData.SaveTime;

            string chapterName = Path.GetFileNameWithoutExtension(saveData.ScriptFileName);

            // 加载截图
            if (screenshotImage != null)
            {
                if (!string.IsNullOrEmpty(saveData.ScreenshotPath) && File.Exists(saveData.ScreenshotPath))
                {
                    StartCoroutine(LoadScreenshot(saveData.ScreenshotPath));
                }
                else
                {
                    SetDefaultScreenshot();
                }
            }

            // 激活交互
            if (slotButton != null) slotButton.interactable = true;

            // 显示删除按钮
            if (deleteButton != null) deleteButton.gameObject.SetActive(true);
        }
        else
        {
            // --- 空槽位 ---
            if (dateText != null) dateText.text = "[Empty]";
            if (screenshotImage != null) SetDefaultScreenshot();

            // 逻辑关键：Save模式可点，Load模式不可点
            if (slotButton != null)
                slotButton.interactable = (mode == SaveLoadPanel.Mode.Save);

            // 隐藏删除按钮
            if (deleteButton != null) deleteButton.gameObject.SetActive(false);
        }
    }

    private void SetDefaultScreenshot()
    {
        if (screenshotImage != null)
        {
            ReleaseLoadedVisual();
            screenshotImage.color = Color.gray;
            screenshotImage.sprite = null;
        }
    }

    /// <summary>
    /// 应用加载完成的截图并登记资源所有权
    /// </summary>
    private void SetLoadedScreenshot(Sprite sprite, Texture2D texture)
    {
        if (screenshotImage == null)
        {
            // Image 丢失时立即释放，避免孤儿纹理
            if (sprite != null) Destroy(sprite);
            if (texture != null) Destroy(texture);
            return;
        }
        ReleaseLoadedVisual();
        _loadedSprite = sprite;
        _loadedTexture = texture;
        screenshotImage.sprite = sprite;
        screenshotImage.color = Color.white;
    }

    /// <summary>
    /// 释放本槽位动态加载的截图资源（槽位每次翻页都会销毁重建，不释放会持续泄漏）
    /// </summary>
    private void ReleaseLoadedVisual()
    {
        if (_loadedSprite != null) { Destroy(_loadedSprite); _loadedSprite = null; }
        if (_loadedTexture != null) { Destroy(_loadedTexture); _loadedTexture = null; }
    }

    private void OnDestroy()
    {
        ReleaseLoadedVisual();
    }

    /// <summary>
    /// 异步加载本地截图
    /// </summary>
    private IEnumerator LoadScreenshot(string path)
    {
        string uri = "file://" + path;

        using (UnityEngine.Networking.UnityWebRequest www = UnityEngine.Networking.UnityWebRequestTexture.GetTexture(uri))
        {
            yield return www.SendWebRequest();

            if (www.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[SaveSlot] 截图加载失败: {www.error}");
                SetDefaultScreenshot();
            }
            else
            {
                if (screenshotImage != null)
                {
                    Texture2D texture = UnityEngine.Networking.DownloadHandlerTexture.GetContent(www);

                    // 兼容旧版全分辨率截图：下采样后再显示，避免整页 12 张全尺寸纹理挤占带宽与显存
                    Texture2D display = SaveManager.CreateThumbnail(texture, SaveManager.ThumbnailMaxSize);
                    if (display != null)
                    {
                        Destroy(texture); // 只保留缩小的显示副本
                        texture = display;
                    }

                    Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
                    SetLoadedScreenshot(sprite, texture);
                }
            }
        }
    }

    private void OnSlotClick()
    {
        onClickCallback?.Invoke(slotIndex);
    }

    private void OnDeleteClick()
    {
        onDeleteCallback?.Invoke(slotIndex);
    }
}