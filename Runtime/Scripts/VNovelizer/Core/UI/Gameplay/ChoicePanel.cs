using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ChoicePanel : BasePanel
{
    private Transform container;
    private GameObject choiceItemPrefab;
    private List<GameObject> activeItems = new List<GameObject>();

    protected override void Awake()
    {
        base.Awake();
        container = transform.Find("ChoiceContainer");
        // 记得把路径配置到 VNProjectConfig 或者硬编码
        choiceItemPrefab = ResourcesManager.GetInstance().Load<GameObject>(VNProjectConfig.Instance.UI_ChoicePath + "/ChoiceItem");
    }

    /// <summary>
    /// 显示选项
    /// </summary>
    /// <param name="choices">选项数据列表 (Text, CommandString)</param>
    public void ShowChoices(List<ChoiceData> choices)
    {
        // 清理旧按钮
        foreach (var item in activeItems) Destroy(item);
        activeItems.Clear();

        // 生成新按钮
        foreach (var data in choices)
        {
            GameObject btnObj = Instantiate(choiceItemPrefab, container);
            activeItems.Add(btnObj);

            // 设置文字
            TMP_Text textComp = btnObj.GetComponentInChildren<TMP_Text>();
            if (textComp != null) textComp.text = data.Text;

            // 绑定事件
            Button btn = btnObj.GetComponent<Button>();
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() => OnChoiceClicked(data.Command));
        }

        ShowMe();
    }

    private void OnChoiceClicked(string command)
    {
        // 关闭面板
        UIManager.GetInstance().HidePanel("ChoicePanel");

        GameStateManager.GetInstance().SetState(GameState.Gameplay);

        // 注意：这里需要调用 VNManager 或 CommandManager 来执行
        if (!string.IsNullOrEmpty(command))
        {

            VNManager.GetInstance().ExecuteChoiceCommand(command);
        }
        else
        {
            // 如果选项没配命令（比如只是“继续”），那就直接下一行
            VNManager.GetInstance().NextLine();
        }
    }

    // 在 ChoicePanel.cs 中添加/修改

    public void AddChoice(string text, string command)
    {
        // 确保 Container 存在
        if (container == null) container = transform.Find("ChoiceContainer");

        GameObject btnObj = Instantiate(choiceItemPrefab, container);
        activeItems.Add(btnObj);

        TMP_Text textComp = btnObj.GetComponentInChildren<TMP_Text>();
        Debug.Log(textComp.text);
        if (textComp != null) textComp.text = text;

        Button btn = btnObj.GetComponent<Button>();
        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(() => OnChoiceClicked(command));

        ShowMe();
    }
}

// 简单的数据结构
public class ChoiceData
{
    public string Text;
    public string Command;
}