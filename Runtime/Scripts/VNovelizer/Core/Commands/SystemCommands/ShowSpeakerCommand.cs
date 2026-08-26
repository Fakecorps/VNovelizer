using UnityEngine;
using VNovelizer.Core.API;
using VNovelizer.Core.Commands.Meta;

namespace VNovelizer.Core.Commands.SystemCommands
{
    /// <summary>
    /// 【系统命令】刷新说话人姓名框与头像。
    ///
    /// <para><b>格式</b>：<c>showSpeaker()</c></para>
    ///
    /// <para>
    /// <b>说话人不可内联</b>：与 <c>showDialogue</c> 同理，说话人永远引用本行 Speaker 列，
    /// 使本地化键 <c>speaker.{lineID}</c> 结构性不可能失效。
    /// </para>
    ///
    /// <para>
    /// <b>与 showDialogue 的关系</b>：引擎隐式路径中说话人与正文同属 <c>UpdateDialogue</c>
    /// 一步完成，因此默认模板把两者并列于同一并行组即等价。单独使用本命令的场景是
    /// 定制行中只想刷新说话人/头像而不重播正文（如一行内多次换发言人的旁白切换）。
    /// </para>
    /// </summary>
    [VNCommandMeta(VNCommandCategory.System,
        "刷新说话人姓名框与头像（引用本行 Speaker / HeadProfile 列，不可内联）")]
    public class ShowSpeakerCommand : VNCommand
    {
        [VNParam(0, "speaker", VNParamType.String,
            Optional = true, ImplicitBinding = true, BoundColumn = "Speaker", InlineForbidden = true,
            Description = "说话人：永远引用本行 Speaker 列，不可内联")]
        public override string CommandName => "showspeaker";

        public override bool Execute(string args)
        {
            if (!string.IsNullOrWhiteSpace(args))
            {
                Debug.LogWarning(
                    "[ShowSpeaker] 本命令不接受内联说话人（保障 speaker.{lineID} 本地化键不失效），" +
                    $"已忽略参数 '{args.Trim()}'。请改本行 Speaker 列。");
            }

            var ctx = VNAPI.GetCurrentLineContext();
            if (ctx == null)
            {
                Debug.LogWarning("[ShowSpeaker] 无行上下文，命令跳过");
                return true;
            }

            // null 表示"不覆盖"，由 VNManager 走本行数据 + 本地化解析
            VNManager.GetInstance().SysShowSpeaker(null, null);
            return true;
        }

        /// <summary>快进预演：说话人是纯呈现，无状态副作用。</summary>
        public override void Simulate(string args)
        {
        }
    }
}
