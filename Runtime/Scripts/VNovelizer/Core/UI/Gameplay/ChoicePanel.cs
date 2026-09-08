using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using VNovelizer.Core.Commands;
using VNovelizer.Core.Commands.Chain;

public class ChoicePanel : BasePanel
{
    private Transform container;
    private GameObject choiceItemPrefab;
    private List<GameObject> activeItems = new List<GameObject>();

    protected override void Awake()
    {
        base.Awake();
        container = transform.Find("ChoiceContainer");
        // 加载选项预制体（模板覆写优先，fallback 经资源服务链；键即默认地址）
        choiceItemPrefab = VNUIPrefabs.Load(VNUIPrefabKeys.ChoiceItem, VNUIPrefabKeys.ChoiceItem);
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
            btn.onClick.AddListener(() => OnChoiceClicked(data.Command, data.Chain));
        }

        ShowMe();
    }

    private void OnChoiceClicked(string command, ChainNode chain)
    {
        // 关闭面板
        UIManager.GetInstance().HidePanel("ChoicePanel");

        GameStateManager.GetInstance().SetState(GameState.Gameplay);

        // [Confirm 出口] 选项即本行出口：choice 行声明的 @Confirm: 段不再执行
        VNManager.GetInstance().ConsumeConfirmExit();

        // 自动存档：在执行选项跳转命令前触发（快照停留在 choice 行，读档后重新弹出选项）
        VNManager.GetInstance().TriggerAutoSaveOnChoice();

        // R11：块语法选项携带 AST 子树 → 经 ChainExecutor 独立执行（支持完整链语法）。
        // 段信息随选项传递：出口段 choice 的选项链按出口段语义推进与埋点。
        if (chain != null)
        {
            VNManager.GetInstance().ExecuteChoiceChain(chain,
                VNovelizer.Core.Commands.ChoiceCommand.LastPushedIsConfirm);
        }
        else if (!string.IsNullOrEmpty(command))
        {
            // 旧语法路径（完全兼容）
            VNManager.GetInstance().ExecuteChoiceCommand(command);
        }
        else
        {
            // 如果选项没配命令（比如只是“继续”），那就直接下一行
            VNManager.GetInstance().NextLine();
        }
    }

    /// <summary>
    /// 追加一个选项按钮。
    /// </summary>
    /// <param name="text">按钮文字（本地化已解析）</param>
    /// <param name="command">旧语法命令文本（可为 null）</param>
    /// <param name="chain">R11 块语法选项链 AST（可为 null = 空链，点击后直接下一行）</param>
    public void AddChoice(string text, string command, ChainNode chain = null)
    {
        // 确保 Container 存在
        if (container == null) container = transform.Find("ChoiceContainer");

        GameObject btnObj = Instantiate(choiceItemPrefab, container);
        activeItems.Add(btnObj);

        TMP_Text textComp = btnObj.GetComponentInChildren<TMP_Text>();
        if (textComp != null) textComp.text = text;

        Button btn = btnObj.GetComponent<Button>();
        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(() => OnChoiceClicked(command, chain));

        ShowMe();
    }
}

// 简单的数据结构
public class ChoiceData
{
    public string Text;
    public string Command;

    /// <summary>R11：块语法选项链 AST（旧语法为 null）</summary>
    public ChainNode Chain;
}