using System.Collections.Generic;
using UnityEditor.Experimental.GraphView;
using UnityEngine.UIElements;
using VNovelizer.Core.Commands.Chain;

namespace VNovelizer.Editor.RowPerformanceEditor
{
    /// <summary>
    /// R11：choice 节点视图。
    ///
    /// <para>
    /// 结构：左侧单入端口（主链）；节点体内垂直列出全部选项——
    /// 每行「序号 + 描述 + 右侧出端口」，出端口连出该选项的命令链；
    /// 未连线的端口 = 空链（点击后直接进入下一行）。
    /// 标题栏右侧是主延续端口（连 End 终端或另一个 choice 节点实现合并展示）；
    /// 右下角 + 按钮即时新增选项（描述为空、端口未连），Inspector 亦可增删。
    /// </para>
    /// </summary>
    public class ChoiceNodeView : VNNodeViewBase
    {
        /// <summary>选项被 + 号新增时触发（Window 侧同步 Inspector 并聚焦新描述输入框）。</summary>
        public event System.Action<ChoiceNodeView> OptionAdded;

        /// <summary>
        /// 选项描述被修改时触发（Window 侧标脏 + 回填文本）。
        /// 声明为字段（非 event）——InspectorBuilder 编辑描述后需从外部 Invoke。
        /// </summary>
        public System.Action<ChoiceNodeView> ChoiceChanged;

        private VisualElement _optionsContainer;
        private readonly List<Port> _optionPorts = new List<Port>();
        private readonly List<Label> _optionLabels = new List<Label>();

        /// <summary>主链延续端口（PortIndex = <see cref="ChoicePort.Main"/>）。</summary>
        public Port MainOutputPort { get; private set; }

        /// <summary>当前选项数量（与 <see cref="ChainGraphNode.ChoiceTexts"/> 同步）。</summary>
        public int OptionCount => Data.ChoiceTexts != null ? Data.ChoiceTexts.Count : 0;

        public ChoiceNodeView(ChainGraphNode data, bool isConfirmChain)
            : base(data, isConfirmChain)
        {
            AddToClassList("vn-choice");
            if (isConfirmChain) AddToClassList("vn-choice--confirm");
            Build();
        }

        protected override void Build()
        {
            SetTitle("choice");
            tooltip = "选项分支：执行到本节点时弹出选项面板。\n" +
                      "每个选项行右侧的出端口连出该选项点击后执行的命令链；\n" +
                      "未连线的选项 = 空链（点击后直接进入下一行）。\n" +
                      "右下角 + 按钮新增选项，Inspector 可增删选项与编辑描述。";

            // UE 蓝图式：Flow 分类整条着色（与 CommandNodeView 的分类色一致）
            AddToClassList("vn-node--cat-flow");

            AddBadge("链尾", "vn-badge-flow",
                "choice 是流程命令：弹出选项后由玩家选择推进。\n" +
                "主链上应位于末尾（其后只可接 End 终端或另一个 choice 节点合并展示）。");

            // 输入端口（标准左侧单入）
            InputPort = CreatePort(Direction.Input, Port.Capacity.Single);

            // 主延续端口：标题栏右侧独立定位
            MainOutputPort = InstantiateChoicePort(Direction.Output, Port.Capacity.Single,
                "vn-port-choice-main");
            MainOutputPort.tooltip = "主链延续：通常连接 End 终端（执行完选项链后等待确认）；\n" +
                                     "连到另一个 choice 节点可实现「单行多选项合并展示」。";
            var mainPortSlot = new VisualElement();
            mainPortSlot.AddToClassList("vn-choice-mainport-slot");
            mainPortSlot.Add(MainOutputPort);
            titleContainer.Add(mainPortSlot);

            _optionsContainer = new VisualElement();
            _optionsContainer.AddToClassList("vn-choice-options");
            mainContainer.Add(_optionsContainer);

            RebuildOptions();
            BuildAddButton();

            RefreshExpandedState();
            RefreshPorts();
        }

        // ---------------- 端口 ----------------

        /// <summary>
        /// 创建 choice 专用端口（不放入 input/outputContainer，而是跟随选项行/标题栏定位）。
        /// 样式与基类 CreatePort 对齐（正圆连接器，USS 控制）。
        /// </summary>
        private Port InstantiateChoicePort(Direction direction, Port.Capacity capacity, string styleClass)
        {
            var port = InstantiatePort(Orientation.Horizontal, direction, capacity, typeof(bool));
            port.portName = "";
            if (!string.IsNullOrEmpty(styleClass)) port.AddToClassList(styleClass);
            if (IsConfirmChain) port.AddToClassList("vn-port-confirm");
            port.style.width = 14;
            port.style.height = 14;
            return port;
        }

        /// <summary>按端口反查 PortIndex（连线同步用）。主延续 = -1，选项 = 0..N-1，未知 = 0。</summary>
        public int GetPortIndex(Port port)
        {
            if (port == MainOutputPort) return ChoicePort.Main;
            for (int i = 0; i < _optionPorts.Count; i++)
                if (_optionPorts[i] == port) return i;
            return 0;
        }

        /// <summary>取第 index 个选项端口（越界返回 null）。</summary>
        public Port GetOptionPort(int index)
        {
            return index >= 0 && index < _optionPorts.Count ? _optionPorts[index] : null;
        }

        /// <summary>全部出端口（主延续 + 各选项）——供连线兼容性枚举。</summary>
        public IEnumerable<Port> AllOutputPorts
        {
            get
            {
                if (MainOutputPort != null) yield return MainOutputPort;
                foreach (var p in _optionPorts) yield return p;
            }
        }

        // ---------------- 选项行 ----------------

        /// <summary>按 Data.ChoiceTexts 重建选项行（Inspector / + 号增删后调用）。</summary>
        public void RebuildOptions()
        {
            _optionsContainer.Clear();
            _optionPorts.Clear();
            _optionLabels.Clear();

            if (Data.ChoiceTexts == null) Data.ChoiceTexts = new List<string>();

            for (int i = 0; i < Data.ChoiceTexts.Count; i++)
            {
                _optionsContainer.Add(BuildOptionRow(i));
            }
        }

        private VisualElement BuildOptionRow(int index)
        {
            var row = new VisualElement();
            row.AddToClassList("vn-choice-row");

            var idx = new Label((index + 1).ToString());
            idx.AddToClassList("vn-choice-index");
            row.Add(idx);

            string text = Data.ChoiceTexts[index];
            var label = new Label(string.IsNullOrWhiteSpace(text) ? "(空描述)" : text);
            label.AddToClassList("vn-choice-text");
            label.tooltip = $"选项 {index + 1} 的描述。\n双击可在 Inspector 中编辑；空描述无法保存。";
            row.Add(label);

            var port = InstantiateChoicePort(Direction.Output, Port.Capacity.Single, "vn-port-choice");
            port.tooltip = $"选项 {index + 1} 的命令链出口——连出该选项点击后执行的命令链。";
            row.Add(port);

            _optionPorts.Add(port);
            _optionLabels.Add(label);
            return row;
        }

        /// <summary>刷新单行描述文本（Inspector 编辑后调用，避免整容器重建打断连线）。</summary>
        public void RefreshOptionLabel(int index)
        {
            if (index < 0 || index >= _optionLabels.Count) return;
            string text = Data.ChoiceTexts != null && index < Data.ChoiceTexts.Count
                ? Data.ChoiceTexts[index] : "";
            _optionLabels[index].text = string.IsNullOrWhiteSpace(text) ? "(空描述)" : text;
        }

        // ---------------- 增删选项 ----------------

        /// <summary>新增一个空选项（描述为空、端口未连），并触发 OptionAdded。</summary>
        public void AddOption()
        {
            if (Data.ChoiceTexts == null) Data.ChoiceTexts = new List<string>();
            Data.ChoiceTexts.Add("");
            RebuildOptions();
            OptionAdded?.Invoke(this);
            ChoiceChanged?.Invoke(this);
        }

        /// <summary>
        /// 移除第 index 个选项（仅数据 + 视图；出边级联删除由 Window 层完成后再调用）。
        /// </summary>
        public void RemoveOptionDataAt(int index)
        {
            if (Data.ChoiceTexts == null || index < 0 || index >= Data.ChoiceTexts.Count) return;
            Data.ChoiceTexts.RemoveAt(index);
            RebuildOptions();
            ChoiceChanged?.Invoke(this);
        }

        // ---------------- + 按钮 ----------------

        private void BuildAddButton()
        {
            var addRow = new VisualElement();
            addRow.AddToClassList("vn-choice-addrow");

            var btn = new Button(AddOption) { text = "+" };
            btn.AddToClassList("vn-choice-add");
            btn.tooltip = "新增一个选项（描述为空、命令链待连）。\n右侧 Inspector 同样可以增删选项。";
            addRow.Add(btn);

            mainContainer.Add(addRow);
        }

        public override bool IsCopiable() => false;
    }
}
