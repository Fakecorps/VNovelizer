using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Events;

public class ConfirmPanel : BasePanel
{
    [SerializeField]private TextMeshProUGUI messageText;
    [SerializeField]private Button yesBtn;
    [SerializeField]private Button noBtn;

    private UnityAction onConfirmCallback;
    private UnityAction onCancelCallback;
    private UnityAction onOkCallback;
    protected override void Awake()
    {
        base.Awake();
        messageText = GetControl<TextMeshProUGUI>("Message");
        yesBtn = GetControl<Button>("Yes");
        noBtn = GetControl<Button>("No");

        yesBtn.onClick.AddListener(OnYesClick);
        noBtn.onClick.AddListener(OnNoClick);
    }

    /// <summary>
    /// 显示弹窗
    /// </summary>
    /// <param name="title">标题</param>
    /// <param name="message">内容</param>
    /// <param name="onConfirm">点击确定的回调</param>
    /// <param name="onCancel">点击取消的回调(可选)</param>
    public void Show(string title, string message, UnityAction onConfirm, UnityAction onCancel = null)
    {

        messageText.text = message;
        onConfirmCallback = onConfirm;
        onCancelCallback = onCancel;

        ShowMe();
    }

    private void OnYesClick()
    {
        onConfirmCallback?.Invoke();
        ClosePanel();
    }

    private void OnNoClick()
    {
        onCancelCallback?.Invoke();
        ClosePanel();
    }

    private void ClosePanel()
    {
        UIManager.GetInstance().HidePanel("ConfirmPanel");
    }
}