using UnityEngine;
using TMPro;

namespace VNovelizer.Core.Commands
{
    /// <summary>
    /// 修改对话文本大小命令
    /// 格式: t_size(fontsize)
    /// 效果不会保存到下一行
    /// </summary>
    public class TSizeCommand : VNCommand
    {
        public override string CommandName { get { return "t_size"; } }

        public override bool Execute(string args)
        {
            if (string.IsNullOrEmpty(args)) return false;

            float fontSize;
            if (!float.TryParse(args.Trim(), out fontSize))
            {
                Debug.LogError($"[TSize] 无法解析参数。请检查字体大小是否为有效数字。");
                return false;
            }

            // 确保字体大小合理（通常 10-100）
            fontSize = Mathf.Clamp(fontSize, 10f, 200f);

            var panel = UIManager.GetInstance().GetPanel<VNGameplayPanel>("VNGameplayPanel");
            if (panel != null)
            {
                panel.SetDialogueTextSize(fontSize);
                Debug.Log($"[TSize] 对话文本大小已设置: {fontSize}");
                return true;
            }
            else
            {
                Debug.LogError("[TSize] 未找到 VNGameplayPanel，请确保该面板已打开。");
                return false;
            }
        }
    }
}

