using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 音乐槽位（音乐列表中的按钮）
/// </summary>
public class MusicSlot : MonoBehaviour
{
    private Button button;
    private TextMeshProUGUI nameText;
    
    public VNMusic musicData;
    private System.Action<VNMusic> onClickCallback;
    
    public void Init(VNMusic music, bool isUnlocked, System.Action<VNMusic> onClickCallback)
    {
        this.musicData = music;
        this.onClickCallback = onClickCallback;
        
        // 获取组件
        button = GetComponent<Button>();
        if (button == null)
        {
            Debug.LogError("[MusicSlot] 未找到Button组件！");
        }
        
        // 查找名称文本（通常在Button的子对象中）
        nameText = GetComponentInChildren<TextMeshProUGUI>();
        if (nameText == null)
        {
            Debug.LogWarning("[MusicSlot] 未找到TextMeshProUGUI组件，无法显示音乐名称");
        }
        
        // 设置音乐名称
        if (nameText != null && music != null)
        {
            nameText.text = music.name;
        }
        
        // 设置按钮状态和事件
        if (button != null)
        {
            button.interactable = isUnlocked; // 未解锁时禁用按钮
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(OnClick);
        }
    }
    
    /// <summary>
    /// 解锁此音乐槽位
    /// </summary>
    public void Unlock()
    {
        if (button != null)
        {
            button.interactable = true;
        }
    }
    
    /// <summary>
    /// 点击事件
    /// </summary>
    private void OnClick()
    {
        if (onClickCallback != null && musicData != null)
        {
            onClickCallback(musicData);
        }
    }
}


