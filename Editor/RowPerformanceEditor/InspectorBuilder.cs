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
    /// <summary>Inspector 页签：节点参数视图 / 命令链文本视图。</summary>
    public enum InspectorTab
    {
        Node,
        Text,
    }

    /// <summary>
    /// 右侧属性检查器：由 <c>[VNParam]</c> 元数据**驱动生成**表单，而非为每个命令手写面板。
    ///
    /// <para>
    /// 两个页签（2026-08-27 用户需求 5）：
    /// </para>
    /// <list type="bullet">
    /// <item><b>节点</b>：按参数类型生成下拉 / 滑块 / 输入框，含隐式绑定切换</item>
    /// <item><b>文本</b>：命令链原始文本 ↔ 图双向联动编辑，选中节点的命令片段实时高亮</item>
    /// </list>
    ///
    /// <para>三种节点表单形态，由元数据可得性决定：</para>
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

        /// <summary>命令链文本被编辑（isConfirm, 新文本）——外部负责解析重建图</summary>
        public event Action<bool, string> OnChainTextChanged;

        private readonly VisualElement _root;
        private VNNodeViewBase _current;

        private InspectorTab _activeTab = InspectorTab.Node;
        private string _entryText = "";
        private string _confirmText = "";

        public InspectorBuilder(VisualElement root)
        {
            _root = root;
            _root.AddToClassList("vn-inspector");
        }

        /// <summary>外部（Window）在图 Rebuild 后同步链文本。</summary>
        public void SetChainTexts(string entry, string confirm)
        {
            _entryText = entry ?? "";
            _confirmText = confirm ?? "";
            if (_activeTab == InspectorTab.Text) RebuildForCurrentTab();
        }

        /// <summary>切换到指定节点（null 显示空状态）。</summary>
        public void Show(VNNodeViewBase node)
        {
            _current = node;
            RebuildForCurrentTab();
        }

        /// <summary>刷新当前节点（外部改动后同步显示）。</summary>
        public void Refresh() => RebuildForCurrentTab();

        // ---------------- 页签骨架 ----------------

        private void RebuildForCurrentTab()
        {
            _root.Clear();
            BuildTabBar();

            if (_activeTab == InspectorTab.Node) BuildNodePane();
            else BuildTextPane();
        }

        private void BuildTabBar()
        {
            var bar = new VisualElement();
            bar.AddToClassList("vn-insp-tabs");

            var nodeTab = new Button(() => { _activeTab = InspectorTab.Node; RebuildForCurrentTab(); })
                { text = "节点" };
            nodeTab.AddToClassList("vn-insp-tab");
            if (_activeTab == InspectorTab.Node) nodeTab.AddToClassList("vn-insp-tab--active");
            bar.Add(nodeTab);

            var textTab = new Button(() => { _activeTab = InspectorTab.Text; RebuildForCurrentTab(); })
                { text = "文本" };
            textTab.AddToClassList("vn-insp-tab");
            if (_activeTab == InspectorTab.Text) textTab.AddToClassList("vn-insp-tab--active");
            bar.Add(textTab);

            _root.Add(bar);
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

            BuildStructuralInspector(_current);
        }

        // ---------------- 文本页签 ----------------

        /// <summary>
        /// 命令链文本编辑器（双向联动）：
        /// 上方只读高亮预览（选中节点的命令片段蓝色加粗），
        /// 下方可编辑多行文本（失焦提交 → OnChainTextChanged → 图重建）。
        /// </summary>
        private void BuildTextPane()
        {
            BuildChainTextSection("进入段", isConfirm: false, _entryText);
            BuildChainTextSection("出口段 @Confirm", isConfirm: true, _confirmText);

            var help = new Label(
                "编辑失焦后自动解析并重建图。文本与节点是同一真值的两个视图。\n" +
                "语法：cmd(args) 串行 -> ；并行 & ；分组 [] ；出口段用 @Confirm: 分隔。");
            help.AddToClassList("vn-insp-desc");
            var section = new VisualElement();
            section.AddToClassList("vn-insp-section");
            section.Add(help);
            _root.Add(section);
        }

        private void BuildChainTextSection(string title, bool isConfirm, string text)
        {
            var section = new VisualElement();
            section.AddToClassList("vn-insp-section");

            var t = new Label(title);
            t.AddToClassList("vn-insp-sectitle");
            section.Add(t);

            // 高亮预览（选中节点的命令片段着色）
            var preview = new Label(BuildHighlightedText(isConfirm, text));
            preview.AddToClassList("vn-insp-preview");
            preview.enableRichText = true;
            section.Add(preview);

            // 可编辑文本
            var field = new TextField { value = text, multiline = true };
            field.AddToClassList("vn-insp-chainfield");
            field.tooltip = "失焦后提交：解析 → 重建图 → 校验";
            field.RegisterCallback<FocusOutEvent>(_ =>
            {
                string newValue = field.value?.Trim() ?? "";
                string old = (isConfirm ? _confirmText : _entryText)?.Trim() ?? "";
                if (newValue == old) return;
                OnChainTextChanged?.Invoke(isConfirm, newValue);
            });
            section.Add(field);

            _root.Add(section);
        }

        /// <summary>
        /// 转义为 rich-text 安全文本，并把选中命令节点的命令片段高亮（蓝色加粗）。
        /// 命令链文本含 &、->、[] 等符号，& 与 &lt; 必须转义。
        /// </summary>
        private string BuildHighlightedText(bool isConfirm, string chainText)
        {
            if (string.IsNullOrEmpty(chainText)) return "<i>（空）</i>";

            string escaped = EscapeRichText(chainText);

            var cmdView = _current as CommandNodeView;
            if (cmdView == null || cmdView.IsConfirmChain != isConfirm ||
                string.IsNullOrEmpty(cmdView.Data?.CommandName))
                return escaped;

            string signature = EscapeRichText(
                (cmdView.Data.CommandName ?? "") + "(" + (cmdView.Data.Args ?? "") + ")");
            string bareName = EscapeRichText(cmdView.Data.CommandName + "(");

            // 尽量匹配完整签名；找不到再退化为命令名前缀
            if (escaped.Contains(signature))
                escaped = escaped.Replace(signature,
                    "<color=#6FB8E8><b>" + signature + "</b></color>");
            else if (escaped.Contains(bareName))
                escaped = escaped.Replace(bareName,
                    "<color=#6FB8E8><b>" + bareName + "</b></color>");

            return escaped;
        }

        private static string EscapeRichText(string s)
        {
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
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

            return ParamCandidateProvider.GetCandidates(param, null);
        }

        private static string FindPrecedingValue(VNCommandInfo info, List<string> values,
            VNParamType type)
        {
            for (int i = 0; i < info.Parameters.Count; i++)
                if (info.Parameters[i].Type == type)
                    return i < values.Count ? values[i].Trim() : "";
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
