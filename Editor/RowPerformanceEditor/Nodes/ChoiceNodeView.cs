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
    /// 结构：标题栏左上角是总输入端口（主链上游连入，白色）、节点右下角
    /// 「+」按钮左侧是主延续端口（连 End 终端或另一个 choice 节点实现合并展示，
    /// 亮蓝高对比——与选项端口的橙色三色分明）；节点体内垂直列出全部选项——
    /// 每行「序号 + 描述 + 右侧出端口」，出端口连出该选项的命令链；
    /// 未连线的端口 = 空链（点击后直接进入下一行）。
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

            // 主延续端口：放节点右下角、「+」按钮左侧的独立区域。
            //
            // 之前在 titleContainer right:8（与「链尾」角标同侧、x 紧贴），
            // 鼠标按下易命中角标 Label 而非端口，且 outputContainer 为空导致
            // #top 高度塌陷（旧问题同 InputPort，但此处还有 badge 视觉挤压与
            // z-order 干扰），表现为「主延续端口拖不出线」。
            // 移到 mainContainer 右下后：
            //   · y 落在选项行之下、「+」按钮左侧，x 不与任何已有元素重叠
            //   · 与 InputPort（标题栏左上）形成「左上入 → 右下出」对角 flow，
            //     符合 GraphView 阅读直觉
            //   · 用 .vn-port-choice-main 的亮蓝色（与白/橙端口三色分明）突出
            //     「主链延续」语义，避免与选项端口（橙）或输入端口（白）混淆
            //   · Build 末尾 BringToFront 把它提到 mainContainer z-order 最上层，
            //     防止被后加的 optionsContainer/addRow 覆盖
            MainOutputPort = InstantiateChoicePort(Direction.Output, Port.Capacity.Single,
                "vn-port-choice-main");
            MainOutputPort.tooltip = "主链延续：通常连接 End 终端（执行完选项链后等待确认）；\n" +
                                     "连到另一个 choice 节点可实现「单行多选项合并展示」。";
            mainContainer.Add(MainOutputPort);
            MainOutputPort.style.position = Position.Absolute;
            MainOutputPort.style.right = 36;   // 「+」按钮在 right:4 宽 20，端口 right:36 + 宽 14 → x 与 + 间隔 4px
            MainOutputPort.style.bottom = 8;   // 与「+」按钮 bottom:3 大致对齐，独立于选项行
            MainOutputPort.style.left = StyleKeyword.Auto;
            MainOutputPort.style.top = StyleKeyword.Auto;
            MainOutputPort.style.marginTop = 0;
            // BringToFront 必须等 RebuildOptions/BuildAddButton 把 optionsContainer 与
            // addRow 都加入 mainContainer 之后再调（见 Build 末尾），否则 addRow 后加
            // 会反超 z-order 把端口盖住。
            // 阻止 Node 标题拖动手势截获 port 的 mouse down（TrickleDown 阶段，在 EdgeConnector 之后）
            MainOutputPort.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button == 0) evt.StopPropagation();
            }, TrickleDown.TrickleDown);

            // 输入端口（总入线）：与主延续端口对称，absolute 定位到标题栏左上角。
            //
            // 不能走标准 inputContainer：#top 高度由 input/output 容器内容决定，
            // choice 的 outputContainer 为空（Node.RefreshPorts 会把它移出层级）、
            // inputContainer 又是 absolute 不占流——#top 高度塌陷为 0。端口 top:50%
            // 相对 0 高的 #top 被推到标题栏底缘，下半截伸进 mainContainer 的选项行区
            // （选项行在 node-border 中 z-order 更高）被盖住，鼠标点不中端口、
            // EdgeConnector 无法启动——表现为「总输入引脚拖不出线」。
            // titleContainer 内 absolute 是 MainOutputPort 已验证的模式
            // （EdgeConnector 是 port 自身 manipulator，与所在容器无关）。
            InputPort = InstantiateChoicePort(Direction.Input, Port.Capacity.Single, null);
            InputPort.tooltip = "总输入：主链上游连入（Line Entry / 前序命令 / 上游 choice）。";
            InputPort.style.position = Position.Absolute;
            InputPort.style.top = 6;
            InputPort.style.left = 6;
            InputPort.style.right = StyleKeyword.Auto;
            InputPort.style.marginTop = 0;
            titleContainer.Add(InputPort);
            // 阻止 Node 标题拖动手势截获 port 的 mouse down（与 MainOutputPort 一致）
            InputPort.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button == 0) evt.StopPropagation();
            }, TrickleDown.TrickleDown);

            _optionsContainer = new VisualElement();
            _optionsContainer.AddToClassList("vn-choice-options");
            mainContainer.Add(_optionsContainer);

            RebuildOptions();
            BuildAddButton();

            // Build 末尾再 BringToFront——之前在 Add 之后立刻 BringToFront，但此时
            // optionsContainer / addRow 还没加，等它们加入 mainContainer 后 z-order
            // 会反超 MainOutputPort。.vn-choice-addrow 是个 flex 占满底部宽的容器，
            // 会把 right:36 bottom:8 的 MainOutputPort 整个压在下面，hit-test 命中
            // 透明的 addRow 而非端口，EdgeConnector 不启动。必须等所有 sibling
            // 落位后再提升到 mainContainer z-order 最上层。
            MainOutputPort.BringToFront();

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
