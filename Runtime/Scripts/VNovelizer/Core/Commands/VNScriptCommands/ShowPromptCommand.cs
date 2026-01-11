using UnityEngine;
using VNovelizer.Core.API;

namespace VNovelizer.Core.Commands
{
    /// <summary>
    /// 显示提示信息
    /// 格式：showprompt(文字, [可选]停留时间)
    /// 示例：showprompt(好感度+1)
    /// </summary>
    public class ShowPromptCommand : VNCommand
    {
        public override string CommandName { get { return "showprompt"; } }

        public override bool Execute(string args)
        {
            // 解析参数
            string[] parts = args.Split(',');
            string text = parts[0].Trim();
            float duration = 2.0f;
            if (parts.Length > 1)
            {
                if (!float.TryParse(parts[1].Trim(), out duration))
                {
                    Debug.LogWarning($"[ShowPrompt] 时间参数无效: {parts[1]}，使用默认值。");
                    duration = 2.0f;
                }
            }

            // 调用 API
            VNAPI.ShowPrompt(text, duration);

            return true;
        }
        
    }
}