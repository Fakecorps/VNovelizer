using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VNovelizer.Core;
using VNovelizer.Core.API;
using VNovelizer.Core.Commands;
using VNovelizer.Core.Commands.Chain;
using VNovelizer.Core.Commands.Meta;

namespace VNovelizer.Editor.RowPerformanceEditor
{
    /// <summary>
    /// 右侧属性检查器：由 <c>[VNParam]</c> 元数据**驱动生成**表单，而非为每个命令手写面板。
    ///
    /// <para>
    /// <b>2026-08-28 改造</b>：命令链文本编辑拆到独立的 <see cref="TextChainEditor"/>
    /// 作为中间一整列 —— Inspector 只负责选中节点的参数编辑。
    /// 文本与图的双向联动由 TextChainEditor 直接驱动。
    /// </para>
    ///
    /// <para>两种节点表单形态，由元数据可得性决定：</para>
    /// <list type="bullet">
    /// <item>有元数据 → 结构化表单</item>
    /// <item>无元数据 → 单行原始参数文本框（通用节点态，功能完整）</item>
    /// <item>非命令节点（Fork/Join/终端）→ 结构说明，无可编辑项</item>
    /// </list>
    /// </summary>
    public class InspectorBuilder
    {
        /// <summary>参数被修改（图需重新校验并标脏）</summary>
        public event Action OnValueChanged;

        /// <summary>请求跳转到数据列（📎 徽章点击）</summary>
        public event Action<string> OnRequestJumpToColumn;

        /// <summary>R11：请求给当前选中的 choice 节点新增一个空选项。</summary>
        public event Action OnRequestAddChoiceOption;

        /// <summary>R11：请求删除当前 choice 节点的第 N 个选项（级联删除其命令链）。</summary>
        public event Action<int> OnRequestRemoveChoiceOption;

        /// <summary>R11：选项链只读预览文本提供者（Window 侧注入；portIndex → 链文本）。</summary>
        public Func<int, string> ChoiceChainPreviewProvider;

        private readonly VisualElement _root;
        private VNNodeViewBase _current;

        public InspectorBuilder(VisualElement root)
        {
            _root = root;
            _root.AddToClassList("vn-inspector");
        }

        /// <summary>切换到指定节点（null 显示空状态）。</summary>
        public void Show(VNNodeViewBase node)
        {
            _current = node;
            _root.Clear();
            BuildNodePane();
        }

        /// <summary>刷新当前节点（外部改动后同步显示）。</summary>
        public void Refresh()
        {
            _root.Clear();
            BuildNodePane();
        }

        private void BuildNodePane()
        {
            if (_current == null)
            {
                BuildEmptyState();
                return;
            }

            if (_current is CommandNodeView cmdView)
            {
                BuildCommandInspector(cmdView);
                return;
            }

            if (_current is ChoiceNodeView choiceView)
            {
                BuildChoiceInspector(choiceView);
                return;
            }

            BuildStructuralInspector(_current);
        }

        // ---------------- R11：choice 选项面板 ----------------

        /// <summary>
        /// choice 节点的 Inspector：选项列表（描述可编辑 + 选项链只读预览 + 删除）
        /// + 底部「添加选项」按钮。选项链的编辑完全通过图上连线完成（决策 10）。
        /// </summary>
        private void BuildChoiceInspector(ChoiceNodeView view)
        {
            var data = view.Data;

            BuildHeader("choice", CommandMetaReader.Get("choice"));

            if (!string.IsNullOrEmpty(CommandMetaReader.Get("choice")?.Description))
            {
                var descSection = new VisualElement();
                descSection.AddToClassList("vn-insp-section");
                descSection.Add(new Label(CommandMetaReader.Get("choice").Description));
                _root.Add(descSection);
            }

            var section = new VisualElement();
            section.AddToClassList("vn-insp-section");

            var title = new Label("选项（" + view.OptionCount + "）");
            title.AddToClassList("vn-insp-sectitle");
            section.Add(title);

            var hint = new Label(
                "描述在本处编辑；选项命令链在图上从该选项的出端口连线构成（此处只读预览）。\n" +
                "未连线的选项 = 空链：点击后直接进入下一行。");
            hint.AddToClassList("vn-insp-desc");
            section.Add(hint);

            for (int i = 0; i < view.OptionCount; i++)
                section.Add(BuildChoiceOptionField(view, i));

            _root.Add(section);

            var addBtn = new Button(() => OnRequestAddChoiceOption?.Invoke())
            {
                text = "+ 添加选项"
            };
            addBtn.tooltip = "新增一个空选项（描述待填、命令链待连）。";
            addBtn.AddToClassList("vn-insp-add-choice");
            _root.Add(addBtn);

            BuildBehaviorSection(CommandMetaReader.Get("choice"));
        }

        private VisualElement BuildChoiceOptionField(ChoiceNodeView view, int index)
        {
            var wrapper = new VisualElement();
            wrapper.AddToClassList("vn-insp-choice-option");

            var head = new Label("选项 " + (index + 1));
            head.AddToClassList("vn-insp-sectitle");
            wrapper.Add(head);

            // 描述编辑
            string text = view.Data.ChoiceTexts != null && index < view.Data.ChoiceTexts.Count
                ? view.Data.ChoiceTexts[index] : "";
            var textField = new TextField("描述") { value = text ?? "" };
            textField.AddToClassList("vn-insp-field");
            textField.tooltip = "选项按钮上显示的文字。空描述无法保存。";
            textField.RegisterCallback<FocusOutEvent>(_ =>
            {
                if (view.Data.ChoiceTexts == null) view.Data.ChoiceTexts = new List<string>();
                while (view.Data.ChoiceTexts.Count <= index) view.Data.ChoiceTexts.Add("");
                if (view.Data.ChoiceTexts[index] == (textField.value ?? "")) return;
                view.Data.ChoiceTexts[index] = textField.value ?? "";
                view.RefreshOptionLabel(index);
                view.ChoiceChanged?.Invoke(view);
                OnValueChanged?.Invoke();
            });
            wrapper.Add(textField);

            // 选项链只读预览
            string preview = ChoiceChainPreviewProvider != null
                ? ChoiceChainPreviewProvider(index) : "";
            var chainLabel = new Label(string.IsNullOrEmpty(preview) ? "（空链：点击后直接进入下一行）" : preview);
            chainLabel.AddToClassList("vn-insp-choice-chain");
            chainLabel.tooltip = "该选项点击后执行的命令链。\n通过图上连线编辑——把选项行右侧的出端口连到命令节点即可。";
            wrapper.Add(chainLabel);

            // 删除按钮
            var removeBtn = new Button(() => OnRequestRemoveChoiceOption?.Invoke(index))
            {
                text = "删除此选项"
            };
            removeBtn.AddToClassList("vn-insp-choice-remove");
            removeBtn.tooltip = "删除该选项，并级联删除其端口连出的整条命令链（不可恢复）。";
            wrapper.Add(removeBtn);

            return wrapper;
        }

        // ---------------- 空状态 ----------------

        private void BuildEmptyState()
        {
            var container = new VisualElement();
            container.AddToClassList("vn-empty-state");
            container.Add(new Label(
                "选择一个节点查看与编辑其参数。\n\n" +
                "从左侧命令面板拖拽命令到画布即可添加；\n" +
                "右键画布可插入 FORK / JOIN 并行组。"));
            _root.Add(container);
        }

        // ---------------- 结构节点（Fork/Join/终端） ----------------

        private void BuildStructuralInspector(VNNodeViewBase node)
        {
            var header = new VisualElement();
            header.AddToClassList("vn-insp-header");

            var name = new Label(node.title);
            name.AddToClassList("vn-insp-cmdname");
            header.Add(name);

            var tag = new Label("结构节点");
            tag.AddToClassList("vn-insp-cattag");
            header.Add(tag);

            _root.Add(header);

            var section = new VisualElement();
            section.AddToClassList("vn-insp-section");

            var desc = new Label(node.tooltip);
            desc.AddToClassList("vn-insp-desc");
            section.Add(desc);

            _root.Add(section);
        }

        // ---------------- 命令节点 ----------------

        private void BuildCommandInspector(CommandNodeView view)
        {
            var info = view.Info;
            var data = view.Data;

            BuildHeader(data.CommandName, info);

            if (info == null || !info.HasMeta)
            {
                BuildRawArgsSection(view);
                BuildGenericNodeNote(data.CommandName, info);
            }
            else
            {
                if (!string.IsNullOrEmpty(info.Description))
                {
                    var section = new VisualElement();
                    section.AddToClassList("vn-insp-section");
                    var desc = new Label(info.Description);
                    desc.AddToClassList("vn-insp-desc");
                    section.Add(desc);
                    _root.Add(section);
                }

                if (IsFlagConditionFamily(info))
                    BuildFlagConditionParams(view, info);
                else
                    BuildStructuredParams(view, info);

                BuildBehaviorSection(info);
            }

            BuildPreviewSection(view);
        }

        private void BuildHeader(string commandName, VNCommandInfo info)
        {
            var header = new VisualElement();
            header.AddToClassList("vn-insp-header");

            var name = new Label(commandName ?? "(未指定)");
            name.AddToClassList("vn-insp-cmdname");
            header.Add(name);

            var tag = new Label(CategoryLabel(info));
            tag.AddToClassList("vn-insp-cattag");
            header.Add(tag);

            _root.Add(header);
        }

        private static string CategoryLabel(VNCommandInfo info)
        {
            if (info == null || !info.HasMeta) return "通用节点";

            switch (info.Category)
            {
                case VNCommandCategory.System:      return "系统命令";
                case VNCommandCategory.Performance: return "演出";
                case VNCommandCategory.Flow:        return "流程";
                case VNCommandCategory.Logic:       return "逻辑";
                case VNCommandCategory.Audio:       return "音频";
                default:                            return "其他";
            }
        }

        // ---------------- 结构化参数表单 ----------------

        private void BuildStructuredParams(CommandNodeView view, VNCommandInfo info)
        {
            var section = new VisualElement();
            section.AddToClassList("vn-insp-section");

            var title = new Label("参数");
            title.AddToClassList("vn-insp-sectitle");
            section.Add(title);

            var values = SplitArgs(view.Data.Args, info.ArgSeparator);

            for (int i = 0; i < info.Parameters.Count; i++)
            {
                var param = info.Parameters[i];
                string value = i < values.Count ? values[i] : "";

                // 隐式绑定且当前为空 → 显示"引用数据列"而非空输入框
                if (param.ImplicitBinding && string.IsNullOrWhiteSpace(value))
                {
                    section.Add(BuildBoundField(view, info, param, i));
                    continue;
                }

                section.Add(BuildValueField(view, info, param, i, value));
            }

            if (info.VariadicArgs)
            {
                var note = new Label("该命令支持可变数量参数，超出声明的部分请用下方原始参数编辑。");
                note.AddToClassList("vn-insp-desc");
                section.Add(note);
                section.Add(BuildRawArgsField(view));
            }

            _root.Add(section);
        }

        // ---------------- 条件命令族三字段表单（jumpif / loadscriptif 等） ----------------

        /// <summary>
        /// 条件命令族（jumpif / jumpifnot / loadscriptif / loadscriptifnot）：
        /// 元数据声明 flag + condition + 后续参数，但序列化仍为旧二段格式
        /// （段 0 = 合并后的条件表达式，如 `intflag1>1`）。
        /// 本表单把段 0 拆为三个控件编辑：flag 下拉 + 操作符下拉 + 值控件，
        /// 写回时合并回段 0，运行时与存量剧本 100% 兼容。
        /// </summary>
        private static bool IsFlagConditionFamily(VNCommandInfo info)
        {
            return info != null && info.Parameters.Count >= 2
                && info.Parameters[0].Type == VNParamType.FlagName
                && info.Parameters[1].Type == VNParamType.FlagCondition;
        }

        /// <summary>元数据参数位置 → 序列化段下标（条件族 flag+condition 共享段 0）</summary>
        private static int PhysicalIndex(VNCommandInfo info, int metaIndex)
        {
            return IsFlagConditionFamily(info) && metaIndex >= 2 ? metaIndex - 1 : metaIndex;
        }

        private void BuildFlagConditionParams(CommandNodeView view, VNCommandInfo info)
        {
            var section = new VisualElement();
            section.AddToClassList("vn-insp-section");

            var title = new Label("参数");
            title.AddToClassList("vn-insp-sectitle");
            section.Add(title);

            var values = SplitArgs(view.Data.Args, info.ArgSeparator);
            string rawCond = values.Count > 0 ? values[0].Trim() : "";

            // ---- 从旧二段格式的段 0 拆出 flag / 操作符 / 值 ----
            string flag = "";
            string opKey = "";
            string condValue = "";
            bool keepQuotes = false;
            string parseError = null;

            if (!string.IsNullOrEmpty(rawCond))
            {
                ConditionParser.Condition cond;
                string error;
                if (ConditionParser.TryParse(rawCond, out cond, out error))
                {
                    flag = cond.Name ?? "";
                    keepQuotes = cond.ValueIsQuoted;
                    opKey = cond.Negated ? "!" : (cond.Op ?? "");
                    condValue = cond.Value ?? "";
                }
                else
                {
                    // 兜底：无法拆分的表达式整段作为 flag 自由文本展示
                    flag = rawCond;
                    parseError = error;
                }
            }

            FlagType flagType = FlagType.Bool;
            bool flagRegistered = ParamCandidateProvider.TryGetFlagType(flag, out flagType);

            // ---- flag 下拉 ----
            var flagParam = info.Parameters[0];
            var flagChoices = ParamCandidateProvider.GetCandidates(flagParam, null);
            if (flagChoices == null) flagChoices = new List<string>();

            const string kNoFlag = "（未选择标志）";
            if (string.IsNullOrEmpty(flag))
            {
                if (!flagChoices.Contains(kNoFlag)) flagChoices.Insert(0, kNoFlag);
            }
            else if (!flagChoices.Contains(flag))
            {
                flagChoices.Add(flag); // 未注册 flag 也保留当前值可显示
            }

            if (flagChoices.Count > 0)
            {
                string flagDisplay = string.IsNullOrEmpty(flag) ? kNoFlag : flag;
                var flagPopup = new PopupField<string>(flagParam.Name, flagChoices,
                    Mathf.Max(0, flagChoices.IndexOf(flagDisplay)));
                flagPopup.AddToClassList("vn-insp-field");
                flagPopup.tooltip = BuildParamTooltip(flagParam);
                flagPopup.RegisterValueChangedCallback(evt =>
                {
                    string newFlag = evt.newValue == kNoFlag ? "" : (evt.newValue ?? "");
                    string newOp = opKey;
                    FlagType t;
                    if (!string.IsNullOrEmpty(newFlag) &&
                        ParamCandidateProvider.TryGetFlagType(newFlag, out t) &&
                        !IsOpAllowed(newOp, t))
                        newOp = ""; // 新 flag 类型不支持当前操作符 → 回退直判
                    MergeFlagCondition(view, info, newFlag, newOp, condValue, keepQuotes);
                    Refresh(); // flag 类型变了 → 重建操作符候选与值控件
                });
                section.Add(flagPopup);
            }

            // ---- condition（操作符下拉 + 值控件）----
            var condParam = info.Parameters[1];

            var condRow = new VisualElement();
            condRow.AddToClassList("vn-insp-cond-row");

            var opDisplayChoices = BuildOpDisplayChoices(
                flagRegistered ? flagType : (FlagType?)null, opKey);
            string opDisplay = OpDisplay(opKey);
            if (!opDisplayChoices.Contains(opDisplay)) opDisplayChoices.Add(opDisplay);
            var opPopup = new PopupField<string>(condParam.Name, opDisplayChoices,
                Mathf.Max(0, opDisplayChoices.IndexOf(opDisplay)));
            opPopup.AddToClassList("vn-insp-field");
            opPopup.tooltip = BuildParamTooltip(condParam);
            opPopup.SetEnabled(!string.IsNullOrEmpty(flag)); // flag 未选 → 操作符不可用
            opPopup.RegisterValueChangedCallback(evt =>
            {
                MergeFlagCondition(view, info, flag, OpKeyFromDisplay(evt.newValue),
                    condValue, keepQuotes);
                Refresh(); // 操作符变了 → 值控件显示/隐藏、候选变化
            });
            condRow.Add(opPopup);

            if (IsComparisonOp(opKey))
            {
                if (flagRegistered && flagType == FlagType.Bool)
                {
                    // bool flag：值为 true/false 下拉（序列化为 flag == true / != false）
                    var valueChoices = new List<string> { "true", "false" };
                    var valuePopup = new PopupField<string>("值", valueChoices,
                        Mathf.Max(0, valueChoices.IndexOf(condValue.ToLowerInvariant())));
                    valuePopup.AddToClassList("vn-insp-field");
                    valuePopup.tooltip = "布尔比较值";
                    valuePopup.RegisterValueChangedCallback(evt =>
                    {
                        MergeFlagCondition(view, info, flag, opKey, evt.newValue, keepQuotes);
                    });
                    condRow.Add(valuePopup);
                }
                else
                {
                    // 数值 / 字符串 / 未注册：自由文本（字符串类型序列化时自动加引号）
                    var valueField = new TextField("值") { value = condValue };
                    valueField.AddToClassList("vn-insp-field");
                    valueField.tooltip = flagRegistered && flagType == FlagType.String
                        ? "字符串值（写入时自动加双引号，值内含逗号也可正确解析）"
                        : "比较值（数值或字符串）";
                    valueField.RegisterCallback<FocusOutEvent>(_ =>
                    {
                        if (valueField.value == condValue) return;
                        MergeFlagCondition(view, info, flag, opKey, valueField.value, keepQuotes);
                    });
                    condRow.Add(valueField);
                }
            }
            else
            {
                var note = new Label(opKey == "!"
                    ? "取反直判：flag 为 false 时条件成立。"
                    : "直判：flag 为 true 时条件成立。");
                note.AddToClassList("vn-insp-desc");
                condRow.Add(note);
            }

            section.Add(condRow);

            if (!string.IsNullOrEmpty(parseError))
            {
                var warn = new Label("条件表达式无法拆分（" + parseError + "），已按原文保留在 flag 字段。");
                warn.AddToClassList("vn-insp-note");
                warn.AddToClassList("vn-insp-note--warn");
                section.Add(warn);
            }

            // ---- 后续参数（targetID / script / startId）：物理段 = 元数据位置 - 1 ----
            for (int i = 2; i < info.Parameters.Count; i++)
            {
                var param = info.Parameters[i];
                int physical = PhysicalIndex(info, i);
                string value = physical < values.Count ? values[physical] : "";

                if (param.ImplicitBinding && string.IsNullOrWhiteSpace(value))
                {
                    section.Add(BuildBoundField(view, info, param, physical));
                    continue;
                }

                section.Add(BuildValueField(view, info, param, physical, value));
            }

            _root.Add(section);
        }

        /// <summary>
        /// 合并 flag + 操作符 + 值为条件表达式并写回段 0：
        /// 直判 → `flag`；取反 → `!flag`；比较 → `flag op value`（String 类型值自动加引号）。
        /// </summary>
        private void MergeFlagCondition(CommandNodeView view, VNCommandInfo info,
            string flag, string opKey, string value, bool keepQuotes)
        {
            string expr;
            if (string.IsNullOrEmpty(flag))
            {
                expr = ""; // 未选择标志 → 清空条件段
            }
            else if (string.IsNullOrEmpty(opKey))
            {
                expr = flag;
            }
            else if (opKey == "!")
            {
                expr = "!" + flag;
            }
            else
            {
                bool quote = keepQuotes; // 未注册 flag 保持拆出时的引号状态
                FlagType t;
                if (ParamCandidateProvider.TryGetFlagType(flag, out t) && t == FlagType.String)
                    quote = true;

                string v = value ?? "";
                expr = flag + " " + opKey + (quote ? " \"" + v + "\"" : " " + v);
            }

            SetParamValue(view, info, 0, expr);
        }

        private static bool IsComparisonOp(string opKey)
        {
            return opKey == ">" || opKey == "<" || opKey == ">=" || opKey == "<="
                || opKey == "==" || opKey == "!=";
        }

        /// <summary>操作符对 flag 类型是否合法（Bool 仅 ==/!=；String 仅 ==/!=）</summary>
        private static bool IsOpAllowed(string opKey, FlagType type)
        {
            if (string.IsNullOrEmpty(opKey) || opKey == "!") return true; // 直判 / 取反
            if (type == FlagType.Bool || type == FlagType.String)
                return opKey == "==" || opKey == "!=";
            return true; // Int / Float 支持全部比较符
        }

        private static List<string> BuildOpDisplayChoices(FlagType? type, string currentOp)
        {
            var keys = new List<string>();
            if (type == null)
                keys.AddRange(new[] { "", "!", ">", "<", ">=", "<=", "==", "!=" });
            else if (type == FlagType.Bool)
                keys.AddRange(new[] { "", "!", "==", "!=" });
            else if (type == FlagType.String)
                keys.AddRange(new[] { "==", "!=" });
            else
                keys.AddRange(new[] { ">", "<", ">=", "<=", "==", "!=" });

            var choices = new List<string>();
            foreach (string k in keys) choices.Add(OpDisplay(k));
            if (!string.IsNullOrEmpty(currentOp) && !keys.Contains(currentOp))
                choices.Add(OpDisplay(currentOp)); // 旧数据操作符不在合法列表 → 保留显示
            return choices;
        }

        private static string OpDisplay(string opKey)
        {
            if (string.IsNullOrEmpty(opKey)) return "直判 (flag == true)";
            if (opKey == "!") return "! (flag == false)";
            return opKey;
        }

        private static string OpKeyFromDisplay(string display)
        {
            if (string.IsNullOrEmpty(display)) return "";
            if (display.StartsWith("直判", StringComparison.Ordinal)) return "";
            if (display.StartsWith("!", StringComparison.Ordinal)) return "!";
            return display;
        }

        /// <summary>隐式绑定态：显示"📎 引用 XX 列"+ 断开按钮。</summary>
        private VisualElement BuildBoundField(CommandNodeView view, VNCommandInfo info,
            VNParamInfo param, int index)
        {
            var wrapper = new VisualElement();

            var label = new Label(param.Name);
            label.AddToClassList("vn-insp-sectitle");
            wrapper.Add(label);

            var row = new VisualElement();
            row.AddToClassList("vn-insp-bound");

            var bound = new Label(">> 引用 " + (param.BoundColumn ?? "数据列") + " 列");
            bound.AddToClassList("vn-insp-bound-label");
            bound.tooltip = "点击跳转到表格对应单元格";
            bound.RegisterCallback<MouseDownEvent>(evt =>
            {
                OnRequestJumpToColumn?.Invoke(param.BoundColumn);
                evt.StopPropagation();
            });
            row.Add(bound);

            if (!param.InlineForbidden)
            {
                var breakBtn = new Button(() => TryBreakBinding(view, info, param, index))
                {
                    text = "断开引用"
                };
                breakBtn.tooltip = "改为在本节点内联写死一个值，不再引用数据列。";
                row.Add(breakBtn);
            }

            wrapper.Add(row);

            if (param.InlineForbidden)
            {
                var note = new Label(
                    $"该参数不允许内联，只能修改 {param.BoundColumn} 列。\n" +
                    "这保障本地化键不会失效——内容归数据列，编排归命令链。");
                note.AddToClassList("vn-insp-note");
                note.AddToClassList("vn-insp-note--info");
                wrapper.Add(note);
            }

            return wrapper;
        }

        /// <summary>
        /// 断开隐式绑定 → 内联值。**必须二次确认**（决策 s3）：
        /// 这一步会让该节点脱离数据列，作者改 Excel 后图不再跟随，
        /// 且若涉及本地化字段还会导致译文失效。是不可静默发生的语义变更。
        /// </summary>
        private void TryBreakBinding(CommandNodeView view, VNCommandInfo info,
            VNParamInfo param, int index)
        {
            bool ok = EditorUtility.DisplayDialog(
                "断开数据列引用？",
                $"参数 {param.Name} 当前引用本行 {param.BoundColumn} 列。\n\n" +
                "断开后将在本节点内联写死一个值：\n" +
                $"· 修改 {param.BoundColumn} 列不再影响本行演出\n" +
                "· 该值不参与本地化，多语言项目需注意\n\n" +
                "确定断开吗？",
                "断开并内联", "取消");

            if (!ok) return;

            // 用数据列当前值作为内联初值——避免用户面对空框不知填什么
            string seed = "";
            var ctx = VNAPI.GetCurrentLineContext();
            if (ctx != null && !string.IsNullOrEmpty(param.BoundColumn))
                seed = ctx.GetColumn(param.BoundColumn) ?? "";

            SetParamValue(view, info, index, string.IsNullOrEmpty(seed) ? "?" : seed);
            Refresh();
        }

        /// <summary>按参数类型生成合适的编辑控件。</summary>
        private VisualElement BuildValueField(CommandNodeView view, VNCommandInfo info,
            VNParamInfo param, int index, string value)
        {
            var candidates = ResolveCandidates(view, info, param, index);

            VisualElement field;

            if (candidates != null && candidates.Count > 0)
            {
                int selected = Mathf.Max(0, candidates.IndexOf(value.Trim()));
                var popup = new PopupField<string>(param.Name, candidates, selected);
                popup.RegisterValueChangedCallback(evt =>
                {
                    SetParamValue(view, info, index, evt.newValue);
                    // 重建表单：联动参数（角色→分组→表情、剧本→起始行）候选随之更新
                    Refresh();
                });
                field = popup;
            }
            else if (param.Type == VNParamType.Float && param.HasRange)
            {
                float parsed = float.TryParse(value, out float f) ? f : DefaultFloat(param);
                var slider = new Slider(param.Name, param.Min, param.Max) { value = parsed };
                slider.showInputField = true;
                slider.RegisterValueChangedCallback(evt =>
                {
                    SetParamValue(view, info, index,
                        evt.newValue.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                });
                field = slider;
            }
            else if (param.Type == VNParamType.Int && param.HasRange)
            {
                int parsed = int.TryParse(value, out int n) ? n : (int)DefaultFloat(param);
                var slider = new SliderInt(param.Name, (int)param.Min, (int)param.Max) { value = parsed };
                slider.showInputField = true;
                slider.RegisterValueChangedCallback(evt =>
                {
                    SetParamValue(view, info, index, evt.newValue.ToString());
                });
                field = slider;
            }
            else
            {
                var text = new TextField(param.Name) { value = value };
                text.RegisterCallback<FocusOutEvent>(_ =>
                {
                    SetParamValue(view, info, index, text.value);
                });
                field = text;
            }

            field.AddToClassList("vn-insp-field");
            field.tooltip = BuildParamTooltip(param);

            // 支持隐式绑定但当前是内联值 → 提供"恢复引用"入口
            if (param.ImplicitBinding)
            {
                var row = new VisualElement();
                row.Add(field);

                var restore = new Button(() =>
                {
                    SetParamValue(view, info, index, "");
                    Refresh();
                }) { text = "恢复引用 " + param.BoundColumn + " 列" };
                restore.tooltip = "清空本参数，重新引用数据列的值。";
                row.Add(restore);

                return row;
            }

            return field;
        }

        private List<string> ResolveCandidates(CommandNodeView view, VNCommandInfo info,
            VNParamInfo param, int index)
        {
            // 参数联动：分组 / 表情依赖前面已选的角色
            if (param.Type == VNParamType.CharacterGroup || param.Type == VNParamType.Emotion)
            {
                var values = SplitArgs(view.Data.Args, info.ArgSeparator);
                string charId = FindPrecedingValue(info, values, VNParamType.CharacterId);

                if (param.Type == VNParamType.CharacterGroup)
                    return ParamCandidateProvider.GetCharacterGroups(charId);

                string group = FindPrecedingValue(info, values, VNParamType.CharacterGroup);
                return ParamCandidateProvider.GetEmotions(charId, group);
            }

            // 跨剧本行 ID：依赖前置 ScriptName 参数的值（loadscript 族的 startId）
            if (param.Type == VNParamType.ScriptLineId)
            {
                var values = SplitArgs(view.Data.Args, info.ArgSeparator);
                string script = FindPrecedingValue(info, values, VNParamType.ScriptName);
                return ParamCandidateProvider.GetScriptLineIds(script);
            }

            return ParamCandidateProvider.GetCandidates(param, null);
        }

        private static string FindPrecedingValue(VNCommandInfo info, List<string> values,
            VNParamType type)
        {
            for (int i = 0; i < info.Parameters.Count; i++)
                if (info.Parameters[i].Type == type)
                {
                    int physical = PhysicalIndex(info, i);
                    return physical < values.Count ? values[physical].Trim() : "";
                }
            return "";
        }

        private static float DefaultFloat(VNParamInfo param)
        {
            if (!string.IsNullOrEmpty(param.Default) &&
                float.TryParse(param.Default, out float d)) return d;
            return param.HasRange ? param.Min : 0f;
        }

        // ---------------- 原始参数（通用节点态） ----------------

        /// <summary>
        /// 无元数据命令的参数编辑（2026-08-27 用户需求 6b）：
        /// 按逗号拆分为独立 TextField（Param1/2/…N），替代单行原始文本框。
        /// 位置参数无语义名——Param N 与节点上的 "P N:" 行一一对应。
        /// </summary>
        private void BuildRawArgsSection(CommandNodeView view)
        {
            var section = new VisualElement();
            section.AddToClassList("vn-insp-section");

            var title = new Label("参数（按位置）");
            title.AddToClassList("vn-insp-sectitle");
            section.Add(title);

            var values = SplitArgs(view.Data.Args, ',');

            if (values.Count == 0)
            {
                var empty = new Label("（无参数）");
                empty.AddToClassList("vn-insp-desc");
                section.Add(empty);
                _root.Add(section);
                return;
            }

            for (int i = 0; i < values.Count; i++)
            {
                int index = i;
                var field = new TextField("Param" + (i + 1)) { value = values[i].Trim() };
                field.AddToClassList("vn-insp-field");
                field.tooltip = $"第 {i + 1} 个位置参数（逗号分隔的第 {i + 1} 项）";
                field.RegisterCallback<FocusOutEvent>(_ =>
                {
                    SetIndexedArg(view, index, field.value);
                });
                section.Add(field);
            }

            _root.Add(section);
        }

        /// <summary>
        /// 写入无元数据命令的第 index 个位置参数并重拼 args 串。
        /// 尾部空参数裁剪与 SetParamValue 一致——保持 showbg() 而非 showbg(,,)。
        /// </summary>
        private void SetIndexedArg(CommandNodeView view, int index, string value)
        {
            var values = SplitArgs(view.Data.Args, ',');

            while (values.Count <= index) values.Add("");
            values[index] = value ?? "";

            while (values.Count > 0 && string.IsNullOrWhiteSpace(values[values.Count - 1]))
                values.RemoveAt(values.Count - 1);

            var sb = new StringBuilder();
            for (int i = 0; i < values.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(values[i].Trim());
            }

            string newArgs = sb.ToString();
            if (view.Data.Args == newArgs) return;

            view.Data.Args = newArgs;
            view.RefreshParameters();
            OnValueChanged?.Invoke();
        }

        private VisualElement BuildRawArgsField(CommandNodeView view)
        {
            var field = new TextField("args") { value = view.Data.Args ?? "", multiline = true };
            field.AddToClassList("vn-insp-field");
            field.tooltip = "括号内的完整参数串，按命令自身的格式书写。";

            field.RegisterCallback<FocusOutEvent>(_ =>
            {
                if (view.Data.Args == field.value) return;
                view.Data.Args = field.value;
                view.RefreshParameters();
                OnValueChanged?.Invoke();
            });

            return field;
        }

        private void BuildGenericNodeNote(string commandName, VNCommandInfo info)
        {
            var section = new VisualElement();
            section.AddToClassList("vn-insp-section");

            var manager = CommandManager.GetInstance();
            bool registered = manager.RegisteredCommandCount == 0 ||
                              manager.IsCommandRegistered(commandName);

            var note = new Label();
            note.AddToClassList("vn-insp-note");

            if (!registered)
            {
                note.AddToClassList("vn-insp-note--warn");
                note.text =
                    $"命令 {commandName} 未注册。可能是拼写错误，或该命令尚未实现——" +
                    "运行时会被忽略并输出警告。";
            }
            else
            {
                note.AddToClassList("vn-insp-note--info");
                note.text =
                    "该命令尚未标注 [VNCommandMeta] / [VNParam] 元数据，因此以原始文本编辑参数。\n\n" +
                    "功能不受影响：可连线、可拖拽、可正常保存运行。\n" +
                    "若为自己的命令，加上特性即可获得结构化表单与参数校验。";
            }

            section.Add(note);
            _root.Add(section);
        }

        // ---------------- 行为特征 ----------------

        private void BuildBehaviorSection(VNCommandInfo info)
        {
            var facts = new List<string>();

            if (info.IsAsync)
                facts.Add("[~] 异步：所在分支会等待它完成。不想阻塞其他演出就放进独立并行分支。");
            if (info.IsFlowCommand)
                facts.Add("链尾：会改变当前行 / 剧本 / 场景，必须是命令链的最后一个命令。");
            if (!info.HasSimulate && !info.IsFlowCommand)
                facts.Add("[no-sim] 无预演：读档 / 快进经过本行时不重建其效果（纯演出命令属正常）。");
            if (info.HasInterrupt)
                facts.Add("可中断：玩家点击跳过时会快进到最终状态。");
            if (info.Planned)
                facts.Add("[计划中] 该命令标记为「计划中」，行为可能尚不完整。");

            if (facts.Count == 0) return;

            var section = new VisualElement();
            section.AddToClassList("vn-insp-section");

            var title = new Label("行为特征");
            title.AddToClassList("vn-insp-sectitle");
            section.Add(title);

            foreach (string fact in facts)
            {
                var label = new Label("· " + fact);
                label.AddToClassList("vn-insp-desc");
                section.Add(label);
            }

            _root.Add(section);
        }

        // ---------------- 序列化预览 ----------------

        private void BuildPreviewSection(CommandNodeView view)
        {
            var section = new VisualElement();
            section.AddToClassList("vn-insp-section");

            var title = new Label("本节点序列化结果");
            title.AddToClassList("vn-insp-sectitle");
            section.Add(title);

            var preview = new Label(
                (view.Data.CommandName ?? "") + "(" + (view.Data.Args ?? "") + ")");
            preview.AddToClassList("vn-insp-preview");
            section.Add(preview);

            _root.Add(section);
        }

        // ---------------- 参数读写 ----------------

        private static List<string> SplitArgs(string args, char separator)
        {
            return ConditionParser.SplitTopLevel(args ?? "", separator);
        }

        /// <summary>
        /// 写入第 <paramref name="index"/> 个参数并重拼参数串。
        ///
        /// 尾部空参数会被裁掉——保持 <c>showbg()</c> 而非 <c>showbg(,,)</c>，
        /// 这既是可读性要求，也是隐式绑定语义的前提（"参数留空 = 引用数据列"）。
        /// </summary>
        private void SetParamValue(CommandNodeView view, VNCommandInfo info,
            int index, string value)
        {
            char sep = info != null ? info.ArgSeparator : ',';
            var values = SplitArgs(view.Data.Args, sep);

            while (values.Count <= index) values.Add("");
            values[index] = value ?? "";

            while (values.Count > 0 && string.IsNullOrWhiteSpace(values[values.Count - 1]))
                values.RemoveAt(values.Count - 1);

            var sb = new StringBuilder();
            for (int i = 0; i < values.Count; i++)
            {
                if (i > 0) sb.Append(sep);
                sb.Append(values[i].Trim());
            }

            string newArgs = sb.ToString();
            if (view.Data.Args == newArgs) return;

            view.Data.Args = newArgs;
            view.RefreshParameters();
            OnValueChanged?.Invoke();
        }

        private static string BuildParamTooltip(VNParamInfo p)
        {
            var sb = new StringBuilder();
            sb.Append(p.Name).Append("  (").Append(p.Type).Append(')');
            if (!string.IsNullOrEmpty(p.Description)) sb.Append('\n').Append(p.Description);
            if (p.HasRange) sb.Append("\n范围：").Append(p.Min).Append(" ~ ").Append(p.Max);
            if (!string.IsNullOrEmpty(p.Default)) sb.Append("\n默认：").Append(p.Default);
            if (p.Optional) sb.Append("\n（可省略）");
            return sb.ToString();
        }
    }
}
