using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace VNovelizer.Core.Commands
{
    public class ChoiceCommand : VNCommand
    {
        public override string CommandName { get { return "choice"; } }

        public override bool Execute(string args)
        {
            // 1. 切换到 Choice 状态，阻止游戏点击下一句
            GameStateManager.GetInstance().SetState(GameState.Choice);

            // 2. 解析参数 (使用新的 | 分隔符)
            var result = ParseArgs(args);
            string text = result.Item1;
            string cmd = result.Item2;

            // 调试：看看解析对不对
            Debug.Log($"[ChoiceCommand] 解析选项 -> Text: {text}, Cmd: {cmd}");

            // 3. 获取或打开面板
            var panel = UIManager.GetInstance().GetPanel<ChoicePanel>("ChoicePanel");

            if (panel == null || !panel.gameObject.activeSelf)
            {
                // 如果面板没开，先打开
                // 确保 VNProjectConfig 里配了 UI_ChoicePath
                string path = VNProjectConfig.Instance.UI_ChoicePath;
                if (string.IsNullOrEmpty(path)) path = "VNPrefabs/UI/Choice"; // 保底路径

                UIManager.GetInstance().ShowPanel<ChoicePanel>("ChoicePanel", path, E_UI_Layer.Top, (p) =>
                {
                    p.AddChoice(text, cmd);
                });
            }
            else
            {
                // 如果已经开了，直接加按钮
                panel.AddChoice(text, cmd);
            }

            return true;
        }

        private (string, string) ParseArgs(string args)
        {
            if (string.IsNullOrEmpty(args)) return ("", "");

            // 找到第一个竖线的位置
            int pipeIndex = args.IndexOf('|');

            string text = "";
            string cmd = "";

            if (pipeIndex == -1)
            {
                // 没有竖线，说明整个 args 都是文字，没有命令
                text = args.Trim();
            }
            else
            {
                // 有竖线，分割成两部分
                text = args.Substring(0, pipeIndex).Trim();
                cmd = args.Substring(pipeIndex + 1).Trim();
            }

            return (text, cmd);
        }
    }
}