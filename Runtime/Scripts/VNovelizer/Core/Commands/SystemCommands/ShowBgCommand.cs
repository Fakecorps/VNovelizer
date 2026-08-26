using UnityEngine;
using VNovelizer.Core.API;
using VNovelizer.Core.Commands.Meta;

namespace VNovelizer.Core.Commands.SystemCommands
{
    /// <summary>
    /// 【系统命令】显示/切换背景（瞬时，无过渡效果——需过渡请用 <c>bgfade</c>）。
    ///
    /// <para><b>格式</b>：<c>showbg([name])</c></para>
    ///
    /// <para>
    /// <b>隐式绑定</b>：参数留空 = 引用本行 Background 列（**继承已应用**——
    /// 空单元格时读到的是上一有效背景，与引擎"空格=继承"语义一致）。
    /// 传入参数则为内联值，脱离数据列。
    /// </para>
    ///
    /// <para>
    /// <b>取值语义</b>（与引擎 <c>UpdateVisualState</c> 完全一致）：
    /// 普通资源名 = 切换；<c>black</c> = 纯黑；<c>hide</c> = 隐藏背景层。
    /// </para>
    /// </summary>
    [VNCommandMeta(VNCommandCategory.System,
        "显示/切换背景（瞬时）。空参引用本行 Background 列")]
    public class ShowBgCommand : VNCommand
    {
        [VNParam(0, "background", VNParamType.BackgroundName,
            Optional = true, ImplicitBinding = true, BoundColumn = "Background",
            Description = "背景资源名；留空则引用本行 Background 列。特殊值：black / hide")]
        public override string CommandName => "showbg";

        /// <summary>
        /// 取最终背景名：显式参数优先，否则走隐式绑定读本行 Background 列。
        /// 返回 null 表示"无可用值 → 本命令跳过"（保留数据列为空时的继承语义）。
        /// </summary>
        private static string ResolveBackground(string args)
        {
            if (!string.IsNullOrWhiteSpace(args)) return args.Trim();

            var ctx = VNAPI.GetCurrentLineContext();
            if (ctx == null)
            {
                Debug.LogWarning("[ShowBg] 无行上下文且未指定参数，命令跳过");
                return null;
            }

            string bg = ctx.Background;
            return string.IsNullOrWhiteSpace(bg) ? null : bg.Trim();
        }

        public override bool Execute(string args)
        {
            string bg = ResolveBackground(args);
            if (bg == null) return true; // 无值 = 跳过（不算失败）

            VNManager.GetInstance().SysShowBackground(bg);
            return true;
        }

        public override void Simulate(string args)
        {
            string bg = ResolveBackground(args);
            if (bg == null) return;

            VNManager.GetInstance().SysSimulateBackground(bg);
        }
    }
}
