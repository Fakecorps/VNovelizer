using UnityEngine.UIElements;
using VNovelizer.Core.Commands;
using VNovelizer.Core.Commands.Chain;
using VNovelizer.Core.Commands.Meta;

namespace VNovelizer.Editor.RowPerformanceEditor
{
    /// <summary>
    /// 命令节点视图。三种形态由**元数据可得性**决定，而非人工配置：
    ///
    /// <list type="table">
    /// <item><term>结构化表单态</term><description>已注册 + 有 <c>[VNCommandMeta]</c> → 参数以 chip 展示，Inspector 给下拉/滑块</description></item>
    /// <item><term>通用节点态</term><description>已注册但无元数据 → 单行原始参数文本，⚙ 角标。**永久兼容层**，功能完整</description></item>
    /// <item><term>未注册警告态</term><description>命令名不在 CommandManager → 通用节点 + ⚠ 角标 + 校验警告</description></item>
    /// </list>
    /// </summary>
    public class CommandNodeView : VNNodeViewBase
    {
        /// <summary>命令的节点化元数据（无元数据时为 null 或 HasMeta=false）</summary>
        public VNCommandInfo Info { get; private set; }

        private VisualElement _paramsContainer;

        public CommandNodeView(ChainGraphNode data, bool isConfirmChain)
            : base(data, isConfirmChain)
        {
            Info = CommandMetaReader.Get(data.CommandName);
            Build();
        }

        protected override void Build()
        {
            SetTitle(Data.CommandName ?? "(未指定)");

            CreateStandardPorts();
            AddAccentBar(ResolveAccentClass());
            AddSemanticBadges();
            BuildParameterArea();

            RefreshExpandedState();
            RefreshPorts();
        }

        /// <summary>分类色带：按元数据分类；无元数据统一用"通用"土色，视觉上即可辨识。</summary>
        private string ResolveAccentClass()
        {
            if (Info == null || !Info.HasMeta) return "vn-accent-generic";

            switch (Info.Category)
            {
                case VNCommandCategory.System:      return "vn-accent-system";
                case VNCommandCategory.Performance: return "vn-accent-performance";
                case VNCommandCategory.Flow:        return "vn-accent-flow";
                case VNCommandCategory.Logic:       return "vn-accent-logic";
                case VNCommandCategory.Audio:       return "vn-accent-audio";
                default:                            return "vn-accent-generic";
            }
        }

        /// <summary>
        /// 语义角标。每个角标都配 tooltip——图标是缩写，用户第一次见必须能查到含义。
        /// </summary>
        private void AddSemanticBadges()
        {
            string name = Data.CommandName ?? "";

            // 未注册（拼写错 / 未实现）
            var manager = CommandManager.GetInstance();
            if (manager.RegisteredCommandCount > 0 && !manager.IsCommandRegistered(name))
            {
                AddBadge("⚠ 未注册", "vn-badge-generic",
                    $"命令 {name} 未在 CommandManager 中注册。可能是拼写错误，或该命令尚未实现——运行时会被忽略。");
                return; // 未注册时其余角标无意义
            }

            // 无元数据 → 通用节点态
            if (Info == null || !Info.HasMeta)
            {
                AddBadge("⚙ 通用", "vn-badge-generic",
                    "该命令尚未标注 [VNCommandMeta] 元数据，参数以原始文本编辑。\n" +
                    "功能完整（可连线、可拖拽、可保存），只是没有结构化表单。");
            }

            // 隐式绑定（引用数据列）
            if (Info != null)
            {
                foreach (var p in Info.Parameters)
                {
                    if (!p.ImplicitBinding) continue;
                    if (!IsArgEmpty()) break; // 有内联值则不显示引用角标

                    AddBadge("📎 " + (p.BoundColumn ?? "数据列"), "vn-badge-ref",
                        $"参数留空 = 引用本行 {p.BoundColumn} 列的值。\n" +
                        (p.InlineForbidden
                            ? "该参数不允许内联，只能改数据列——这保障本地化键不会失效。"
                            : "可在 Inspector 中断开引用改为内联值。"));
                    break;
                }
            }

            // 流程命令（必须链尾）
            if (ChainParser.IsFlowCommand(name))
            {
                AddBadge("链尾", "vn-badge-flow",
                    "流程命令会改变当前行 / 剧本 / 场景。\n" +
                    "必须置于命令链末尾，否则其后的命令会在「行已切换」的上下文中执行。");
            }

            // 阻塞（异步命令，会让所在分支等待）
            if (Info != null && Info.IsAsync)
            {
                AddBadge("⏳", "vn-badge-blocking",
                    "异步命令：所在分支会等待它完成后才继续。\n" +
                    "若不希望阻塞其他演出，把它放进独立的并行分支。");
            }

            // 无 Simulate（读档 / 快进时状态可能不一致）
            if (Info != null && Info.HasMeta && !Info.HasSimulate && !ChainParser.IsFlowCommand(name))
            {
                AddBadge("↩", "vn-badge-noinherit",
                    "该命令未实现 Simulate：读档或快进经过本行时不会重建其效果。\n" +
                    "纯演出命令（震动、等待等）属正常；若它会改变持久状态则需补 Simulate。");
            }
        }

        private bool IsArgEmpty() => string.IsNullOrWhiteSpace(Data.Args);

        // ---------------- 参数区 ----------------

        private void BuildParameterArea()
        {
            _paramsContainer = new VisualElement();
            mainContainer.Add(_paramsContainer);
            RefreshParameters();
        }

        /// <summary>重建参数显示（Inspector 改参数后调用）。</summary>
        public void RefreshParameters()
        {
            _paramsContainer.Clear();

            bool structured = Info != null && Info.HasMeta && Info.Parameters.Count > 0;

            if (structured)
            {
                BuildStructuredChips();
            }
            else if (!IsArgEmpty())
            {
                // 通用节点态：原始参数单行显示
                _paramsContainer.AddToClassList("vn-node-rawargs");
                _paramsContainer.Add(new Label(Data.Args));
            }
            else if (Info != null && Info.HasMeta)
            {
                // 有元数据但无参数的命令（如 showSpeaker()）——不显示参数区，保持紧凑
            }
        }

        private void BuildStructuredChips()
        {
            _paramsContainer.AddToClassList("vn-node-params");

            var values = SplitArgs();
            bool anyShown = false;

            for (int i = 0; i < Info.Parameters.Count; i++)
            {
                var p = Info.Parameters[i];
                string value = i < values.Count ? values[i].Trim() : "";

                // 空值且支持隐式绑定 → 已由 📎 角标表达，不重复占位
                if (string.IsNullOrEmpty(value) && p.ImplicitBinding) continue;

                // 空值且可选 → 省略（显示默认值会误导用户以为已显式设置）
                if (string.IsNullOrEmpty(value) && p.Optional) continue;

                var chip = new VisualElement();
                chip.AddToClassList("vn-param-chip");
                chip.tooltip = BuildParamTooltip(p, value);

                var key = new Label(p.Name + ":");
                key.AddToClassList("vn-param-key");
                chip.Add(key);

                if (string.IsNullOrEmpty(value))
                {
                    var empty = new Label("(空)");
                    empty.AddToClassList("vn-param-empty");
                    chip.Add(empty);
                }
                else
                {
                    var val = new Label(value);
                    val.AddToClassList("vn-param-value");
                    chip.Add(val);
                }

                _paramsContainer.Add(chip);
                anyShown = true;
            }

            // 溢出参数（可变长命令 / 参数数超过声明）
            if (values.Count > Info.Parameters.Count)
            {
                int extra = values.Count - Info.Parameters.Count;
                var chip = new VisualElement();
                chip.AddToClassList("vn-param-chip");
                chip.tooltip = "超出元数据声明的额外参数（可变长命令属正常）";
                var label = new Label("+" + extra);
                label.AddToClassList("vn-param-value");
                chip.Add(label);
                _paramsContainer.Add(chip);
                anyShown = true;
            }

            if (!anyShown) _paramsContainer.RemoveFromClassList("vn-node-params");
        }

        private string BuildParamTooltip(VNParamInfo p, string value)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(p.Name).Append("  (").Append(p.Type).Append(')');
            if (!string.IsNullOrEmpty(p.Description)) sb.Append('\n').Append(p.Description);
            if (p.HasRange) sb.Append("\n范围：").Append(p.Min).Append(" ~ ").Append(p.Max);
            if (p.Options != null && p.Options.Length > 0)
                sb.Append("\n可选：").Append(string.Join(" | ", p.Options));
            if (!string.IsNullOrEmpty(p.Default)) sb.Append("\n默认：").Append(p.Default);
            if (p.Optional) sb.Append("\n（可省略）");
            return sb.ToString();
        }

        /// <summary>
        /// 按元数据声明的分隔符拆分参数（顶层拆分，不切引号内与括号内的内容）。
        /// </summary>
        private System.Collections.Generic.List<string> SplitArgs()
        {
            char sep = Info != null ? Info.ArgSeparator : ',';
            return ConditionParser.SplitTopLevel(Data.Args ?? "", sep);
        }
    }
}
