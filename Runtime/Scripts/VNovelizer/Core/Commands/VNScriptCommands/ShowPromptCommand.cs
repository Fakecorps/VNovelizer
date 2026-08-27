using UnityEngine;
using VNovelizer.Core.API;
using VNovelizer.Core.Commands.Meta;

namespace VNovelizer.Core.Commands
{
    /// <summary>
    /// 显示提示信息
    /// 格式：showprompt(文字, [可选]停留时间)
    /// 示例：showprompt(好感度+1)
    /// </summary>
    [VNCommandMeta(VNCommandCategory.Performance,
        "屏幕提示文字（如「好感度+1」，浮动显示后自动消失）")]
    public class ShowPromptCommand : VNCommand
    {
        [VNParam(0, "text", VNParamType.String,
            Description = "提示文字内容")]
        [VNParam(1, "duration", VNParamType.Float, Min = 0.5f, Max = 10f, Default = "2",
            Optional = true, Description = "停留秒数（默认 2）")]
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