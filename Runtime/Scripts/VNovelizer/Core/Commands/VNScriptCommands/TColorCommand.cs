using UnityEngine;
using TMPro;

namespace VNovelizer.Core.Commands
{
    /// <summary>
    /// 修改对话文本颜色命令
    /// 格式: t_color(R,G,B)
    /// 效果不会保存到下一行
    /// </summary>
    public class TColorCommand : VNCommand
    {
        public override string CommandName { get { return "t_color"; } }

        public override bool Execute(string args)
        {
            if (string.IsNullOrEmpty(args)) return false;

            string[] parts = args.Split(',');
            if (parts.Length < 3)
            {
                Debug.LogError($"[TColor] 参数不足，需要3个参数：R, G, B");
                return false;
            }

            float r, g, b;
            if (!float.TryParse(parts[0].Trim(), out r) ||
                !float.TryParse(parts[1].Trim(), out g) ||
                !float.TryParse(parts[2].Trim(), out b))
            {
                Debug.LogError($"[TColor] 无法解析参数。请检查 R, G, B 是否为有效数字。");
                return false;
            }

            // 确保颜色值在 0-255 范围内，然后转换为 0-1
            r = Mathf.Clamp(r, 0f, 255f) / 255f;
            g = Mathf.Clamp(g, 0f, 255f) / 255f;
            b = Mathf.Clamp(b, 0f, 255f) / 255f;

            var panel = UIManager.GetInstance().GetPanel<VNGameplayPanel>("VNGameplayPanel");
            if (panel != null)
            {
                panel.SetDialogueTextColor(new Color(r, g, b, 1f));
                Debug.Log($"[TColor] 对话文本颜色已设置: R={r * 255}, G={g * 255}, B={b * 255}");
                return true;
            }
            else
            {
                Debug.LogError("[TColor] 未找到 VNGameplayPanel，请确保该面板已打开。");
                return false;
            }
        }
    }
}

