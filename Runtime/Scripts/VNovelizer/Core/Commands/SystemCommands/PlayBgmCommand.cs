using UnityEngine;
using VNovelizer.Core.API;
using VNovelizer.Core.Commands.Meta;

namespace VNovelizer.Core.Commands.SystemCommands
{
    /// <summary>
    /// 【系统命令】播放背景音乐。
    ///
    /// <para><b>格式</b>：<c>playBGM([name])</c></para>
    ///
    /// <para>
    /// <b>隐式绑定</b>：参数留空 = 引用本行 BGM 列。列为空则本命令**跳过**——
    /// 这保留了框架的"空格 = 继续当前 BGM"继承语义（BGM 是与 Background 并列的
    /// 两个继承列之一）。
    /// </para>
    ///
    /// <para>
    /// <b>取值语义</b>（与引擎 <c>UpdateAudioState</c> 完全一致）：
    /// <c>stop</c> = 停止；<c>pause</c> = 暂停；<c>resume</c> = 恢复；
    /// 普通资源名 = 播放，**同名幂等跳过**（避免行间断续重播）。
    /// </para>
    /// </summary>
    [VNCommandMeta(VNCommandCategory.System,
        "播放 BGM（同名不重播）。空参引用本行 BGM 列；特殊值 stop / pause / resume")]
    public class PlayBgmCommand : VNCommand
    {
        [VNParam(0, "bgm", VNParamType.BgmName,
            Optional = true, ImplicitBinding = true, BoundColumn = "BGM",
            Description = "BGM 资源名；留空则引用本行 BGM 列。特殊值：stop / pause / resume")]
        public override string CommandName => "playbgm";

        /// <summary>取最终 BGM 名；null 表示"无值 → 跳过"（保留继承语义）。</summary>
        private static string ResolveBgm(string args)
        {
            if (!string.IsNullOrWhiteSpace(args)) return args.Trim();

            var ctx = VNAPI.GetCurrentLineContext();
            if (ctx == null)
            {
                Debug.LogWarning("[PlayBGM] 无行上下文且未指定参数，命令跳过");
                return null;
            }

            string bgm = ctx.BGM;
            return string.IsNullOrWhiteSpace(bgm) ? null : bgm.Trim();
        }

        public override bool Execute(string args)
        {
            string bgm = ResolveBgm(args);
            if (bgm == null) return true; // 无值 = 跳过（继承当前 BGM）

            VNManager.GetInstance().SysPlayBGM(bgm);
            return true;
        }

        public override void Simulate(string args)
        {
            string bgm = ResolveBgm(args);
            if (bgm == null) return;

            VNManager.GetInstance().SysSimulateBGM(bgm);
        }
    }
}
