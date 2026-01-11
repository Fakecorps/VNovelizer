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
    private UnityAction<int> onClickCallback;
    private UnityAction<int> onDeleteCallback;

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
                     UnityAction<int> onClick, UnityAction<int> onDelete)
    {
        this.slotIndex = index;
        this.saveData = data;
        this.mode = mode;
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
        if (slotText != null) slotText.text = $"[{slotIndex + 1}]";

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
            screenshotImage.color = Color.gray;
            screenshotImage.sprite = null;
        }
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
                    Texture2D texture = ((UnityEngine.Networking.DownloadHandlerTexture)www.downloadHandler).texture;
                    Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
                    screenshotImage.sprite = sprite;
                    screenshotImage.color = Color.white;
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