using UnityEditor;
using VNovelizer.Core.Commands;
using VNovelizer.Core.Commands.Chain;

namespace VNovelizer.Editor.RowPerformanceEditor
{
    /// <summary>行的三层形态。</summary>
    public enum RowForm
    {
        /// <summary>Command 列为空——纯数据列驱动，零成本</summary>
        Normal,

        /// <summary>Command 列仅含普通命令——默认演出 + 命令追加（与旧版语义完全一致）</summary>
        Enhanced,

        /// <summary>Command 列含系统命令——模板已实体化，引擎跳过隐式演出</summary>
        Custom,
    }

    /// <summary>
    /// 三层行形态的判定与「按需提升」流程。
    ///
    /// <para>
    /// <b>提升是单向且需确认的语义变更</b>：一旦把模板实体化写入 Command 列，
    /// 该行的演出就从"引擎兜底"变成"作者完全掌控"——数据列改动仍会通过隐式绑定
    /// 生效，但演出时序、命令增删都归图管。这不能静默发生，否则用户会困惑
    /// "我只是拖了下节点，为什么 Excel 的 Command 列突然多出一大串"。
    /// </para>
    /// </summary>
    public static class RowPromotion
    {
        /// <summary>判定行形态。</summary>
        public static RowForm DetermineForm(string commandColumn)
        {
            if (string.IsNullOrWhiteSpace(commandColumn)) return RowForm.Normal;

            return CommandManager.ContainsSystemCommand(commandColumn)
                ? RowForm.Custom
                : RowForm.Enhanced;
        }

        /// <summary>形态的中文显示名。</summary>
        public static string FormLabel(RowForm form)
        {
            switch (form)
            {
                case RowForm.Normal:   return "普通行";
                case RowForm.Enhanced: return "增强行";
                case RowForm.Custom:   return "定制行";
                default:               return "未知";
            }
        }

        /// <summary>形态徽章的样式类。</summary>
        public static string FormStyleClass(RowForm form)
        {
            switch (form)
            {
                case RowForm.Normal:   return "vn-rowform-normal";
                case RowForm.Enhanced: return "vn-rowform-enhanced";
                case RowForm.Custom:   return "vn-rowform-custom";
                default:               return "vn-rowform-normal";
            }
        }

        /// <summary>形态说明（工具栏徽章的 tooltip）。</summary>
        public static string FormTooltip(RowForm form)
        {
            switch (form)
            {
                case RowForm.Normal:
                    return "普通行：Command 列为空，完全由数据列驱动。\n" +
                           "画布上的「默认演出」胶囊是引擎行为的可视化，未占用 Command 列。";
                case RowForm.Enhanced:
                    return "增强行：Command 列含普通命令，在默认演出之后追加执行。\n" +
                           "与旧版剧本语义完全一致——这是绝大多数已有剧本的形态。";
                case RowForm.Custom:
                    return "定制行：Command 列含系统命令，模板已实体化。\n" +
                           "引擎不再播放隐式演出，本行的一切由命令链决定。";
                default:
                    return "";
            }
        }

        /// <summary>
        /// 弹出提升确认对话框。
        /// </summary>
        /// <param name="currentForm">当前形态</param>
        /// <returns>用户是否确认提升</returns>
        public static bool ConfirmPromotion(RowForm currentForm)
        {
            string extra = currentForm == RowForm.Enhanced
                ? "\n\n你已有的命令会保留，并接在系统命令之后（与现在的执行顺序一致）。"
                : "";

            return EditorUtility.DisplayDialog(
                "提升为定制行？",
                "你正在修改默认演出。继续将把完整命令链写入本行的 Command 列，" +
                "该行升级为「定制行」：\n\n" +
                "· 演出时序完全由你掌控\n" +
                "· 数据列仍通过隐式绑定生效（改台词照旧改 Excel）\n" +
                "· 引擎不再为本行播放默认演出" + extra + "\n\n" +
                "提升后可「重置回模板」，但会丢弃定制内容。",
                "提升为定制行", "取消");
        }

        /// <summary>
        /// 生成提升后的命令链文本：模板 + 原有用户命令。
        /// 直接调用 <see cref="DefaultPerformanceTemplate.BuildText"/>——
        /// 模板结构只有那一处定义，避免此处再抄一份造成漂移。
        /// </summary>
        public static string BuildPromotedText(string existingUserChain)
        {
            return DefaultPerformanceTemplate.BuildText(existingUserChain);
        }

        /// <summary>
        /// 弹出「重置回模板」确认框。
        /// </summary>
        public static bool ConfirmReset()
        {
            return EditorUtility.DisplayDialog(
                "重置回默认模板？",
                "本行将丢弃全部定制编排，恢复为由数据列驱动的默认演出。\n\n" +
                "系统命令会从 Command 列移除；你添加的普通命令将被保留" +
                "（该行退回「增强行」）。\n\n" +
                "此操作可用 Ctrl+Z 撤销。",
                "重置", "取消");
        }

        /// <summary>
        /// 从命令链中剔除系统命令，保留用户命令——「重置回模板」的实现。
        /// </summary>
        /// <returns>剔除后的命令链文本（可能为空串 = 退回普通行）</returns>
        public static string StripSystemCommands(string commandChain)
        {
            if (string.IsNullOrWhiteSpace(commandChain)) return "";

            var parsed = ChainParser.Parse(commandChain);
            if (parsed.Root == null) return "";

            var stripped = StripNode(parsed.Root);
            return stripped == null ? "" : ChainSerializer.Serialize(stripped);
        }

        /// <summary>
        /// 递归剔除系统命令节点；容器节点若因此变空则一并移除（返回 null）。
        /// </summary>
        private static ChainNode StripNode(ChainNode node)
        {
            if (node is CommandNode cmd)
            {
                return CommandManager.IsSystemCommand(cmd.Name) ? null : cmd;
            }

            if (node is SeqNode seq)
            {
                var kept = new SeqNode { Position = seq.Position };
                foreach (var child in seq.Children)
                {
                    var s = StripNode(child);
                    if (s != null) kept.Children.Add(s);
                }
                if (kept.Children.Count == 0) return null;
                return kept.Children.Count == 1 ? kept.Children[0] : kept;
            }

            if (node is ParNode par)
            {
                var kept = new ParNode { Position = par.Position };
                foreach (var child in par.Children)
                {
                    var s = StripNode(child);
                    if (s != null) kept.Children.Add(s);
                }
                if (kept.Children.Count == 0) return null;
                return kept.Children.Count == 1 ? kept.Children[0] : kept;
            }

            return node;
        }
    }
}
