using UnityEngine;
using VNovelizer.Core.API;
using VNovelizer.Core.Compat;
using VNovelizer.Core.Commands.Meta;

namespace VNovelizer.Core.Commands
{
    /// <summary>
    /// 显示提示信息
    /// 格式：showprompt(文字, [可选]停留时间, [InEase], [OutEase])
    /// 示例：showprompt(好感度+1)
    /// R13：InEase=浮入曲线（缺省沿用现状：透明度 OutQuad + 位移 OutBack）、
    ///      OutEase=浮出曲线（缺省沿用现状 InQuad）。
    /// </summary>
    [VNCommandMeta(VNCommandCategory.Performance,
        "屏幕提示文字（如「好感度+1」，浮动显示后自动消失）")]
    public class ShowPromptCommand : VNCommand
    {
        [VNParam(0, "text", VNParamType.String,
            Description = "提示文字内容")]
        [VNParam(1, "duration", VNParamType.Float, Min = 0.5f, Max = 10f, Default = "2",
            Optional = true, Description = "停留秒数（默认 2）")]
        [VNParam(2, "inEase", VNParamType.Enum, Options = EaseArgParser.InEaseOptions,
            Optional = true, Description = "浮入曲线（可选；缺省=OutQuad 淡入 + OutBack 回弹位移，只填一个=进出同曲线）")]
        [VNParam(3, "outEase", VNParamType.Enum, Options = EaseArgParser.OutEaseOptions,
            Optional = true, Description = "浮出曲线（可选；缺省 InQuad）")]
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

            // R13：解析 [InEase, OutEase]（只填一个 = 进出同曲线；缺省由面板沿用现状曲线）
            var easeArgs = EaseArgParser.ParseAt(parts, 2);

            // 调用 API
            VNAPI.ShowPrompt(text, duration, easeArgs.In, easeArgs.Out);

            return true;
        }
        
    }
}