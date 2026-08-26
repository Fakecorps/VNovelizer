using UnityEngine;
using VNovelizer.Core.API;
using VNovelizer.Core.Commands.Meta;

namespace VNovelizer.Core.Commands.SystemCommands
{
    /// <summary>
    /// 【系统命令】播放本行语音。
    ///
    /// <para><b>格式</b>：<c>playVoice([name])</c></para>
    ///
    /// <para>
    /// <b>隐式绑定</b>：参数留空 = 引用本行 Voice 列的**解析后**取值。
    /// "解析后"很关键——框架支持"Voice 列留空时按行 ID 自动生成路径"
    /// （如 ID <c>1003</c> → <c>1003.mp3</c>），该规则已在
    /// <c>VNManager.ResolveLine</c> 中应用，因此本命令读到的是最终可播路径。
    /// </para>
    ///
    /// <para>
    /// <b>语音开关</b>：Voice 列填 <c>false</c> 会关闭后续语音（框架既有语义），
    /// 此时解析后取值为空串 → 本命令跳过。
    /// </para>
    ///
    /// <para>
    /// <b>预演不播音频</b>：<c>Simulate</c> 为空实现。且预演路径的行上下文中
    /// Voice 恒为空串（快进不需要语音路径），双重保证读档时不会突然播放语音。
    /// </para>
    /// </summary>
    [VNCommandMeta(VNCommandCategory.System,
        "播放语音。空参引用本行 Voice 列的解析后路径（含按 ID 自动生成）")]
    public class PlayVoiceCommand : VNCommand
    {
        [VNParam(0, "voice", VNParamType.VoiceName,
            Optional = true, ImplicitBinding = true, BoundColumn = "Voice",
            Description = "语音资源路径；留空则引用本行 Voice 列（解析后，含自动生成路径）")]
        public override string CommandName => "playvoice";

        public override bool Execute(string args)
        {
            string voice = args;

            if (string.IsNullOrWhiteSpace(voice))
            {
                var ctx = VNAPI.GetCurrentLineContext();
                if (ctx == null)
                {
                    Debug.LogWarning("[PlayVoice] 无行上下文且未指定参数，命令跳过");
                    return true;
                }
                voice = ctx.Voice;
            }

            if (string.IsNullOrWhiteSpace(voice)) return true; // 无语音 / 语音已关闭 = 跳过

            VNManager.GetInstance().SysPlayVoice(voice.Trim());
            return true;
        }

        /// <summary>快进预演：不播放音频。</summary>
        public override void Simulate(string args)
        {
        }
    }
}
