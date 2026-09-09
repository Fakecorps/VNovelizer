using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;
using VNovelizer.Core.Commands.Chain;
using VNovelizer.Core.Commands.Meta;

namespace VNovelizer.Editor.RowPerformanceEditor
{
    /// <summary>
    /// 行演出编辑器的画布（2026-08-27 重构：执行流从左到右）。
    ///
    /// <para>
    /// <b>双泳道</b>：进入段在上半区（<see cref="ChainAutoLayout.EntryLaneY"/>），
    /// 出口段在下半区（<see cref="ChainAutoLayout.ConfirmLaneY"/>），两条链各自从左到右流动。
    /// </para>
    ///
    /// <para>
    /// <b>2026-08-28：终端哨兵数据驱动</b>。Start/End 哨兵节点常驻图数据
    /// （<see cref="ChainGraphDumper.EnsureSentinels"/>），终端视图与锚点边全部由
    /// graph.Nodes / graph.Edges 渲染——用户连「终端 → 节点」的边是真实图数据：
    /// 单命令链不再被误判孤立、空泳道也有终端可连、断开锚点边即断开链头/链尾。
    /// </para>
    ///
    /// <para>
    /// <b>2026-08-28：删除时序修复</b>。Unity GraphView 的删除是「先回调后应用」——
    /// 回调时被删元素仍在 view 中。同步必须显式排除 change.elementsToRemove，
    /// 否则删除在数据层不生效（删了节点还报它的错、保存后命令复活）。
    /// </para>
    /// </summary>
    public class RowGraphView : GraphView
    {
        public event Action<VNNodeViewBase> OnNodeSelected;
        public event Action OnGraphChanged;
        /// <summary>仅节点被拖动（图数据未变）。Window 侧用于位置快照，避免 Undo 栈被拖动灌满。</summary>
        public event Action OnNodesMoved;

        /// <summary>R10：节点双击（Play 模式下 = 从该节点重播）。</summary>
        public event Action<VNNodeViewBase> OnNodeDoubleClicked;

        /// <summary>
        /// 节点拖动手势开始（左键在节点上按下）。Window 侧据此压入「移动前」快照——
        /// 每个拖动手势 = 一条独立 Undo 记录（业界标准），替代旧的 TopLabel 粘性合并
        /// （两次独立拖动被合并成一条、且快照记录的是移动中位置，均不符合直觉）。
        /// </summary>
        public event Action OnNodeDragGestureStarted;

        public event Action OnRequestPromotion;

        /// <summary>请求在画布指定位置创建命令节点（命令名, 是否出口段, 画布坐标）</summary>
        public event Action<string, bool, Vector2> OnRequestCreateNodeAt;

        public ChainGraph EntryGraph { get; private set; } = new ChainGraph();
        public ChainGraph ConfirmGraph { get; private set; } = new ChainGraph();
        public VNLineContext LineContext { get; set; }

        private readonly Dictionary<string, VNNodeViewBase> _nodeViews =
            new Dictionary<string, VNNodeViewBase>();

        private readonly List<VisualElement> _decorations = new List<VisualElement>();

        private bool _suppressChangeEvents;

        /// <summary>拖拽悬停提示</summary>
        private Label _dragHint;

        public RowGraphView()
        {
            AddToClassList("vn-graph");

            SetupZoom(0.35f, 2.0f);
            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new ClickSelector());
            // 用户偏好：保留自实现 DragBoxSelector（坐标系稳定）
            this.AddManipulator(new DragBoxSelector());

            Insert(0, new GridBackground());

            graphViewChanged = OnGraphViewChanged;

            // 节点拖动手势检测（Capture 阶段——赶在 SelectionDragger 之前记录）
            RegisterCallback<PointerDownEvent>(OnPointerDownCapture, TrickleDown.TrickleDown);

            RegisterCallback<DragUpdatedEvent>(OnDragUpdated);
            RegisterCallback<DragPerformEvent>(OnDragPerform);
            RegisterCallback<DragExitedEvent>(OnDragExited);

            _dragHint = new Label("松开创建命令节点");
            _dragHint.AddToClassList("vn-drag-hint");
            _dragHint.style.display = DisplayStyle.None;
            _dragHint.pickingMode = PickingMode.Ignore;
            Add(_dragHint);
        }

        // ---------------- 拖拽接收 ----------------

        private void OnDragUpdated(DragUpdatedEvent evt)
        {
            if (CommandPalette.TryGetDragCommand(out _))
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                _dragHint.style.display = DisplayStyle.Flex;
                _dragHint.style.left = evt.mousePosition.x + 12;
                _dragHint.style.top = evt.mousePosition.y + 12;
            }
        }

        private void OnDragPerform(DragPerformEvent evt)
        {
            if (!CommandPalette.TryGetDragCommand(out string commandName)) return;

            Vector2 canvasPos = contentViewContainer.WorldToLocal(evt.mousePosition);
            // 上半区=进入段，下半区=出口段
            bool isConfirm = canvasPos.y >
                             (ChainAutoLayout.EntryLaneY + ChainAutoLayout.ConfirmLaneY) / 2f;

            OnRequestCreateNodeAt?.Invoke(commandName, isConfirm, canvasPos);
            CommandPalette.ClearDragData();
            _dragHint.style.display = DisplayStyle.None;
            evt.StopPropagation();
        }

        private void OnDragExited(DragExitedEvent evt)
        {
            _dragHint.style.display = DisplayStyle.None;
        }

        // ---------------- 图重建 ----------------

        /// <summary>
        /// 重建画布。终端哨兵常驻图数据（由本方法兜底装配），终端视图与锚点边
        /// 全部由 graph 渲染——不再有视图层自建的"幽灵终端"。
        /// savedPositions 有值时恢复保存位置；无值时做一次基础布局，
        /// 之后完全由用户拖拽掌控。
        /// </summary>
        /// <param name="frameAll">重建后是否自动 FrameAll（切行时 true；Undo/文本编辑传 false 保持视野）</param>
        public void Rebuild(ChainGraph entryGraph, ChainGraph confirmGraph,
            Dictionary<string, Vector2> savedPositions = null,
            bool templateCollapsed = true, bool showTemplate = false,
            bool frameAll = true)
        {
            _suppressChangeEvents = true;

            ClearCanvas();

            EntryGraph = entryGraph ?? new ChainGraph();
            ConfirmGraph = confirmGraph ?? new ChainGraph();

            // 哨兵兜底：调用方（文本解析 / 快照恢复 / 粘贴）应已装配锚点边，
            // 这里只保证节点存在，不重复连边。
            ChainGraphDumper.EnsureSentinels(EntryGraph, isConfirm: false, linkAnchors: false);
            ChainGraphDumper.EnsureSentinels(ConfirmGraph, isConfirm: true, linkAnchors: false);

            BuildLane(EntryGraph, isConfirm: false, centerY: ChainAutoLayout.EntryLaneY,
                savedPositions: savedPositions, startX: ChainAutoLayout.StartX);

            BuildLane(ConfirmGraph, isConfirm: true, centerY: ChainAutoLayout.ConfirmLaneY,
                savedPositions: savedPositions, startX: ChainAutoLayout.StartX);

            _suppressChangeEvents = false;

            // 重建后 selection 已清空，刷新一次确保旧视图残留的范围高亮类不遗留
            RefreshRangeHighlight();

            if (frameAll)
                schedule.Execute(FrameAllIfNeeded).ExecuteLater(50);
        }

        private void ClearCanvas()
        {
            foreach (var deco in _decorations) deco.RemoveFromHierarchy();
            _decorations.Clear();

            DeleteElements(graphElements.ToList());
            _nodeViews.Clear();
        }

        private void BuildLane(ChainGraph graph, bool isConfirm, float centerY,
            Dictionary<string, Vector2> savedPositions, float startX)
        {
            // 2026-08-28 R8：savedPositions 参数当前不被读取——所有节点统一走 Layout。
            // 参数保留：Undo 快照（Positions 字段）可能仍需向后兼容；将来若加
            // "用户主动保存布局" 快捷键，可在此处恢复读取逻辑。
            bool hasContent = ChainGraphDumper.HasContent(graph);

            // 端点 ID（RowGraphView 视图层与图数据通过固定 ID 约定）
            string startId = ChainGraphDumper.SentinelId(isConfirm, true);
            string endId = ChainGraphDumper.SentinelId(isConfirm, false);

            // 空链（仅哨兵、无连接）时 Layout 无从展开——哨兵用固定站位
            var layout = hasContent ? ChainAutoLayout.Layout(graph, centerY) : null;

            // 位置双重缺失（快照/保存位置都没有 + 自动布局也没覆盖到——如断链孤儿节点）时，
            // 按索引错开散布，至少可读可拖。
            int unplacedIndex = 0;

            foreach (var node in graph.Nodes)
            {
                var view = CreateNodeView(node, isConfirm);
                if (view == null) continue;

                bool isStartSentinel = node.Id == startId;
                bool isEndSentinel = node.Id == endId;

                Vector2 pos = Vector2.zero;

                if (isStartSentinel || isEndSentinel)
                {
                    // 2026-08-28 端点固定：哨兵永远由 Layout 算（保证 LineEntry/Exit 总在
                    // 最左/最右）。savedPositions 即使有哨兵键也忽略——用户拖动过的位置不
                    // 应破坏结构约束。
                    if (layout != null && layout.TryGetValue(node.Id, out var sentinelPos))
                        pos = sentinelPos;
                    else
                    {
                        // 空链 fallback：哨兵左右站位（与 BuildEmptyConfirmShadow 风格一致）
                        pos = isStartSentinel
                            ? new Vector2(startX, centerY - 15f)
                            : new Vector2(startX + 460f, centerY - 15f);
                    }
                }
                else
                {
                    // 2026-08-28 R8：所有非端点节点统一走 Layout，savedPositions 不再覆盖。
                    // 旧实现让用户上次拖动（或旧算法）保存的位置直接套用——既不保证
                    // "每次打开都整齐"（R8 核心需求），又会出现"Line Entry 不在最左"、
                    // "Fork 跑到了 LineEntry 左边"等结构错位（用户 2026-08-28 14:32 截图）。
                    // 用户拖动仍然立即生效（OnNodesMoved 实时更新位置），只是切行/重开
                    // 会被 Layout 重排——R8 优先级高于 R5 的"自由拖动保留"。
                    if (layout != null && layout.TryGetValue(node.Id, out var layoutPos))
                    {
                        pos = layoutPos;
                        pos.x += (startX - ChainAutoLayout.StartX);
                    }
                    else
                    {
                        // 双重缺失：错开散布防重叠（2 列瀑布）
                        int row = unplacedIndex / 2;
                        int col = unplacedIndex % 2;
                        pos = new Vector2(
                            startX + col * 240f,
                            centerY + 180f + row * 90f);
                        unplacedIndex++;
                    }
                }

                view.SetPosition(new Rect(pos, new Vector2(0f, 0f)));
                AddElement(view);
                _nodeViews[NodeViewKey(isConfirm, node.Id)] = view;
            }

            // 全部连线由图数据渲染——含哨兵锚点边（Start→链头、链尾→End）。
            // 锚点边是真实图数据：断开即断链，校验会提示。
            // R11：choice 节点按边 PortIndex 选择对应出端口（选项端口 / 主延续端口）。
            foreach (var edge in graph.Edges)
            {
                var from = GetNodeView(isConfirm, edge.FromId);
                var to = GetNodeView(isConfirm, edge.ToId);
                if (from == null || to == null || to.InputPort == null) continue;

                Port outPort = null;
                if (from is ChoiceNodeView choiceFrom)
                {
                    outPort = edge.PortIndex == ChoicePort.Main
                        ? choiceFrom.MainOutputPort
                        : choiceFrom.GetOptionPort(edge.PortIndex);
                }
                else
                {
                    outPort = from.OutputPort;
                }

                if (outPort == null) continue;

                var e = outPort.ConnectTo(to.InputPort);
                AddElement(e);
            }

            // [NextLine 显式化 2026-08-31] 出口段默认插入 nextline 模板节点。
            // 与系统命令族规则一致：nextline 作为流程命令默认出现在链尾（OnConfirm Entry
            // 与 OnConfirm Exit 之间），标记为 vn-node--template（半透明、不可连线）。
            // 用户删除它 → 触发 OnRequestPromotion → 提升为定制行（写入 Command 列）。
            if (isConfirm)
            {
                EnsureTemplateNextLine(graph, isConfirm, startX, centerY);
                // 初始化时评估已有 nextline 节点的可见性（与模板判断条件一致）
                RefreshNextLineVisibility(graph, isConfirm: true);
            }
        }

        /// <summary>
        /// 测量所有节点当前渲染宽度（id → 像素）。供 RelayoutAll 使用，让 Layer 间距
        /// 反映真实节点尺寸——长命令节点不会挤压邻居、也不会与 Fork/Join 重叠。
        /// </summary>
        private Dictionary<string, float> MeasureAllWidths()
        {
            var widths = new Dictionary<string, float>();
            foreach (var pair in _nodeViews)
            {
                var view = pair.Value;
                if (view == null || view.Data == null) continue;
                float w = view.localBound.width;
                if (w > 0) widths[view.Data.Id] = w;
            }
            return widths;
        }

        private VNNodeViewBase CreateNodeView(ChainGraphNode node, bool isConfirm)
        {
            VNNodeViewBase view;
            switch (node.Kind)
            {
                case ChainGraphNodeKind.Command:
                    view = new CommandNodeView(node, isConfirm);
                    break;

                case ChainGraphNodeKind.Fork:
                case ChainGraphNodeKind.Join:
                    view = new ForkJoinNodeView(node, isConfirm);
                    break;

                case ChainGraphNodeKind.Choice:
                    view = new ChoiceNodeView(node, isConfirm);
                    break;

                case ChainGraphNodeKind.Start:
                    view = new TerminalNodeView(node,
                        isConfirm ? TerminalKind.ConfirmStart : TerminalKind.LineStart, isConfirm);
                    break;

                case ChainGraphNodeKind.End:
                    view = new TerminalNodeView(node,
                        isConfirm ? TerminalKind.ChainEnd : TerminalKind.WaitConfirm, isConfirm);
                    break;

                default:
                    return null;
            }

            // R10：节点双击统一转发给 Window（Play 模式 = 重播入口）
            view.DoubleClicked += v => OnNodeDoubleClicked?.Invoke(v);
            return view;
        }

        // ---------------- 泳道标签 ----------------
        // 2026-08-27（用户需求 1）：进入段/出口段文字标签已移除——泳道通过
        // 出口段的绿色配色与 OnConfirmEntry 弹头自然区分，无需文字说明。

        // ---------------- 连线规则 ----------------

        public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter)
        {
            var compatible = new List<Port>();
            var startView = startPort.node as VNNodeViewBase;
            if (startView == null) return compatible;

            // R11：choice 的选项端口不在 input/outputContainer 中（跟随选项行定位），
            // 需并入枚举。
            ports.ForEach(port => CheckPort(startPort, startView, port, compatible));
            foreach (var n in nodes.ToList())
            {
                if (!(n is ChoiceNodeView cv)) continue;
                foreach (var p in cv.AllOutputPorts)
                    CheckPort(startPort, startView, p, compatible);
            }

            return compatible;
        }

        private static void CheckPort(Port startPort, VNNodeViewBase startView,
            Port port, List<Port> compatible)
        {
            if (port == startPort) return;
            if (port.direction == startPort.direction) return;
            if (port.node == startPort.node) return;

            var portView = port.node as VNNodeViewBase;
            if (portView == null) return;
            if (portView.IsConfirmChain != startView.IsConfirmChain) return;

            // 影子节点（如空出口段的 NextLine 提示）是纯视图装饰——
            // 连到它身上的边永远进不了图数据，干脆禁止连线避免误导。
            if (portView.ClassListContains("vn-node--template")) return;

            compatible.Add(port);
        }

        private GraphViewChange OnGraphViewChanged(GraphViewChange change)
        {
            if (_suppressChangeEvents) return change;

            bool structural = (change.edgesToCreate != null && change.edgesToCreate.Count > 0) ||
                              (change.elementsToRemove != null && change.elementsToRemove.Count > 0);
            bool moved = change.movedElements != null && change.movedElements.Count > 0;

            if (change.elementsToRemove != null)
            {
                var ghosts = change.elementsToRemove
                    .OfType<VNNodeViewBase>()
                    .Where(v => v.ClassListContains("vn-node--template") ||
                                v is TemplateCapsuleNodeView)
                    .ToList();

                if (ghosts.Count > 0)
                {
                    foreach (var ghost in ghosts) change.elementsToRemove.Remove(ghost);
                    OnRequestPromotion?.Invoke();
                }
            }

            // 插入式连线：必须在 Unity 应用 edgesToCreate 之前处理（此时旧边仍在 view 中）。
            // 返回 false 的连线从 edgesToCreate 移除——拒绝应用（Single 容量端口被占用等）。
            if (change.edgesToCreate != null && change.edgesToCreate.Count > 0)
            {
                for (int i = change.edgesToCreate.Count - 1; i >= 0; i--)
                {
                    if (!RewriteAsInsertion(change.edgesToCreate[i]))
                        change.edgesToCreate.RemoveAt(i);
                }
            }

            if (structural)
            {
                // 删除节点自动桥接：pred → succ（在同步前做——graph 中前驱后继仍可查）。
                // 没有桥接时删链中节点会留下断链，立报"2 起点 2 终点"的错误。
                BridgeRemovedNodes(change);

                SyncGraphDataFromView(change);
                OnGraphChanged?.Invoke();

                // 图结构已变（删节点/改连线），选中 Fork/Join 的影响范围可能变化，重算高亮
                RefreshRangeHighlight();

                // R11+：图数据变化时重新评估 nextline 节点的可见性——
                // 主链尾已是 FlowCommand 时隐藏（不删数据，仅 UI）。
                // 避免命令列里有 nextline 但执行永远到不了时仍显示节点误导作者。
                RefreshNextLineVisibility(EntryGraph, isConfirm: false);
                RefreshNextLineVisibility(ConfirmGraph, isConfirm: true);
            }
            else if (moved)
            {
                // 拖动只影响位置不影响图数据——走独立事件，Window 侧做手势级快照。
                OnNodesMoved?.Invoke();
            }

            return change;
        }

        /// <summary>
        /// 插入式连线与容量拦截。
        ///
        /// <para>
        /// <b>插入语义</b>：新边 A→B 且 B（命令节点）的 Input 已被 C→B 占用时，
        /// 自动改写为 C→A→B（断 C→B，桥接 C→A，Unity 随后应用 A→B）。
        /// </para>
        ///
        /// <para>
        /// <b>容量拦截</b>（2026-08-28）：GraphView 对 Single 容量端口**不做强制**——
        /// 命令节点的第二条入边、终端锚点的第二条连线都会被静默接受，随后校验报
        /// "多路汇合/多起点"但用户不明所以。现在在源头拒绝：返回 false 的边不应用。
        /// </para>
        ///
        /// <para>
        /// <b>顺序纪律</b>：一切可行性检查必须先于任何断边操作——旧实现把
        /// 「A 入边已占用」检查放在断开 C→B 之后，中途放弃会留下旧边已断、
        /// 桥没搭上的断链（用户随手一连图就坏）。
        /// </para>
        /// </summary>
        /// <returns>false = 拒绝该连线（从 edgesToCreate 移除，不应用）</returns>
        private bool RewriteAsInsertion(Edge newEdge)
        {
            var a = newEdge.output?.node as VNNodeViewBase;  // 新边起点（通常是新拖入节点）
            var b = newEdge.input?.node as VNNodeViewBase;   // 新边终点
            if (a == null || b == null) return false;
            if (ReferenceEquals(a, b)) return false;
            if (a.IsConfirmChain != b.IsConfirmChain) return false;

            // 终端锚点 Single 容量：锚点边（Start→链头 / 链尾→End）只能有一条，
            // 已占用时拒绝新连线（用户应先断开旧的）。
            if (a is TerminalNodeView && a.OutputPort != null && a.OutputPort.connected) return false;
            if (b is TerminalNodeView && b.InputPort != null && b.InputPort.connected) return false;

            // R11：choice 节点连线。
            // 起点是 choice：每个选项端口/主延续端口只容一条边；choice 不参与插入改写。
            if (a is ChoiceNodeView)
            {
                if (newEdge.output != null && newEdge.output.connected) return false;
                if (b is CommandNodeView && b.InputPort != null && b.InputPort.connected) return false;
                return true;
            }
            // 终点是 choice：入端口只能一条（已占用则拒绝）。
            if (b is ChoiceNodeView)
            {
                return b.InputPort == null || !b.InputPort.connected;
            }

            // Fork 的 Multi 出口 / Join 的 Multi 入口：多连线合法，直连放行。
            bool aOutMulti = a is ForkJoinNodeView forkA && forkA.IsFork;
            bool bInMulti = b is ForkJoinNodeView joinB && !joinB.IsFork;
            if (bInMulti) return true;

            // A 输出侧容量：非 Fork 的输出端口已被占用 → 新边会让 A 出度变 2，拒绝。
            if (!aOutMulti && a.OutputPort != null && a.OutputPort.connected) return false;

            // B 输入侧：入边空闲 → 直连放行。
            if (!(b is CommandNodeView)) return b.InputPort == null || !b.InputPort.connected;
            if (b.InputPort == null || !b.InputPort.connected) return true;

            // ---- B（命令节点）入边被占 → 尝试插入改写 C→A→B ----

            // 可行性全部先于断边（顺序纪律）：
            // A 必须能承接 C 的输出（有入端口且空闲）——终端无入端口、链上节点入边已占，
            // 均无法承接，直接拒绝（旧边 C→B 保持原状）。
            if (a is TerminalNodeView) return false;
            if (a.InputPort == null || a.InputPort.connected) return false;

            Edge existing = FindExistingInputEdge(newEdge);
            if (existing == null) return false; // connected 与可见边不一致（防御）

            var c = existing.output?.node as VNNodeViewBase;
            if (c == null || ReferenceEquals(c, a)) return false;
            if (c.OutputPort == null) return false;

            // ---- 检查全部通过，执行改写 ----
            // 断旧边 C→B
            existing.output?.Disconnect(existing);
            existing.input?.Disconnect(existing);
            RemoveElement(existing);

            // 桥 C→A（立即加入 view，与新边 A→B 一起构成插入）
            var bridge = c.OutputPort.ConnectTo(a.InputPort);
            AddElement(bridge);
            return true;
        }

        private Edge FindExistingInputEdge(Edge newEdge)
        {
            foreach (var e in edges)
            {
                if (e.input == newEdge.input && e != newEdge)
                    return e;
            }
            return null;
        }

        /// <summary>
        /// 删除节点自动桥接：被删节点若"单入单出"，把前驱直连后继。
        /// 删除链头/链尾时前驱/后继是哨兵终端，同样成立（自动接回终端）。
        /// 邻居也在本批删除中则不桥接（整段删除无意义）。
        /// </summary>
        private void BridgeRemovedNodes(GraphViewChange change)
        {
            if (change.elementsToRemove == null) return;

            var removed = change.elementsToRemove.OfType<VNNodeViewBase>().ToList();
            if (removed.Count == 0) return;

            foreach (var node in removed)
            {
                if (node.Data == null) continue;
                if (node is TerminalNodeView) continue; // 哨兵不可删（防御）

                var graph = node.IsConfirmChain ? ConfirmGraph : EntryGraph;
                var preds = graph.GetPredecessors(node.Data.Id);
                var succs = graph.GetSuccessors(node.Data.Id);
                if (preds.Count != 1 || succs.Count != 1) continue;

                var predView = GetNodeView(node.IsConfirmChain, preds[0]);
                var succView = GetNodeView(node.IsConfirmChain, succs[0]);
                if (predView == null || succView == null) continue;
                if (removed.Contains(predView) || removed.Contains(succView)) continue;
                if (predView.OutputPort == null || succView.InputPort == null) continue;

                // pred 的现有出边必须指向本批删除的节点（否则出度另有归属，不能占用）
                Edge predOut = null;
                foreach (var e in edges)
                    if (e.output == predView.OutputPort) { predOut = e; break; }
                if (predOut != null)
                {
                    var target = predOut.input?.node as VNNodeViewBase;
                    if (target == null || !removed.Contains(target)) continue;
                }

                // succ 的现有入边必须来自本批删除的节点
                Edge succIn = null;
                foreach (var e in edges)
                    if (e.input == succView.InputPort) { succIn = e; break; }
                if (succIn != null)
                {
                    var source = succIn.output?.node as VNNodeViewBase;
                    if (source == null || !removed.Contains(source)) continue;
                }

                var bridge = predView.OutputPort.ConnectTo(succView.InputPort);
                AddElement(bridge);
            }
        }

        private void SyncGraphDataFromView(GraphViewChange change)
        {
            // 2026-08-28 修复（删除时序）：Unity GraphView 的删除是「先回调后应用」——
            // 回调时被删元素仍在 view 中。旧实现把"即将被删除的边/节点"重新同步回
            // graph，导致删除在数据层不生效：删了节点校验还报它的错、保存后命令复活。
            // 现在显式排除 elementsToRemove，并在 _nodeViews 中清理被删条目。
            var pendingNew = change.edgesToCreate;
            var removedEdges = change.elementsToRemove?.OfType<Edge>().ToList();
            var removedNodes = change.elementsToRemove?.OfType<VNNodeViewBase>()
                .Where(v => v.Data != null).ToList();

            if (removedNodes != null && removedNodes.Count > 0)
            {
                foreach (var v in removedNodes)
                    _nodeViews.Remove(NodeViewKey(v.IsConfirmChain, v.Data.Id));
            }

            RebuildEdgesFromView(EntryGraph, isConfirm: false, pendingNew, removedEdges, removedNodes);
            RebuildEdgesFromView(ConfirmGraph, isConfirm: true, pendingNew, removedEdges, removedNodes);
        }

        private void RebuildEdgesFromView(ChainGraph graph, bool isConfirm,
            List<Edge> pendingNewEdges, List<Edge> removedEdges,
            List<VNNodeViewBase> removedNodes)
        {
            var rebuilt = new ChainGraph();
            var validIds = new HashSet<string>();

            var removedIds = new HashSet<string>();
            if (removedNodes != null)
            {
                foreach (var v in removedNodes)
                    if (v.IsConfirmChain == isConfirm && v.Data != null)
                        removedIds.Add(v.Data.Id);
            }

            foreach (var node in graph.Nodes)
            {
                if (removedIds.Contains(node.Id)) continue;      // 即将被删除
                if (GetNodeView(isConfirm, node.Id) == null) continue; // 视图已不存在
                rebuilt.AddNode(node);
                validIds.Add(node.Id);
            }

            // 先从 view edges 重建（排除即将被删除的边）
            edges.ForEach(edge =>
            {
                if (removedEdges != null && removedEdges.Contains(edge)) return;
                TryAddEdge(rebuilt, edge, isConfirm, validIds);
            });

            // 再合并尚未被 Unity 加入 view 的待处理新边（创建方向时序：先回调后应用）
            if (pendingNewEdges != null)
            {
                foreach (var edge in pendingNewEdges)
                {
                    if (removedEdges != null && removedEdges.Contains(edge)) continue;
                    TryAddEdge(rebuilt, edge, isConfirm, validIds);
                }
            }

            if (isConfirm) ConfirmGraph = rebuilt;
            else EntryGraph = rebuilt;
        }

        private void TryAddEdge(ChainGraph rebuilt, Edge edge, bool isConfirm, HashSet<string> validIds)
        {
            var from = edge.output?.node as VNNodeViewBase;
            var to = edge.input?.node as VNNodeViewBase;
            if (from == null || to == null) return;
            if (from.IsConfirmChain != isConfirm || to.IsConfirmChain != isConfirm) return;
            if (from.Data == null || to.Data == null) return;

            // 引擎隐式影子（NextLine 等）不进图数据；终端哨兵已在 validIds 中——
            // 用户连「终端 → 节点」的锚点边是真实图数据。
            if (!validIds.Contains(from.Data.Id) || !validIds.Contains(to.Data.Id)) return;

            // R11：choice 出边携带端口号
            int portIndex = 0;
            if (from is ChoiceNodeView cv) portIndex = cv.GetPortIndex(edge.output);

            rebuilt.AddEdge(from.Data.Id, to.Data.Id, portIndex);
        }

        /// <summary>
        /// [NextLine 显式化 2026-08-31] 出口段默认插入 nextline 模板节点。
        ///
        /// <para>
        /// 与系统命令族规则一致：nextline 作为流程命令默认出现在链尾
        /// （OnConfirm Entry → [nextline] → OnConfirm Exit），标记为
        /// <c>vn-node--template</c>（半透明、不可连线到它身上、删除时触发提升）。
        /// </para>
        ///
        /// <para>
        /// 插入条件：
        /// </para>
        /// <list type="bullet">
        /// <item>出口段（<c>isConfirm == true</c>）；</item>
        /// <item>链尾没有已有的 nextline 节点（避免重复插入）；</item>
        /// </list>
        ///
        /// <para>
        /// 模板节点与真实节点的区别（通过 USS class 区分）：
        /// </para>
        /// <list type="bullet">
        /// <item><c>.vn-node--template</c>：半透明 + 灰色边框；</item>
        /// <item>禁止被连线（<see cref="GetCompatiblePorts"/> 拦截）；</item>
        /// <item>删除时触发 <see cref="OnRequestPromotion"/>（<see cref="OnGraphViewChanged"/> 拦截）；</item>
        /// <item>不持久化到 Command 列 —— 它是视图合成的，用户提升后才写入。</item>
        /// </list>
        /// </summary>
        private void EnsureTemplateNextLine(ChainGraph graph, bool isConfirm, float startX, float centerY)
        {
            if (graph == null || !isConfirm) return;

            // 检查图数据中是否已有 nextline（用户已显式写或已提升）
            foreach (var node in graph.Nodes)
                if (node.Kind == ChainGraphNodeKind.Command &&
                    string.Equals((node.CommandName ?? "").Trim(), "nextline",
                        StringComparison.OrdinalIgnoreCase))
                    return;

            // R11 修订：主链尾已是 FlowCommand（jump / jumpif / loadscript / choice /
            // loadscene 等）时，nextline 永远不会被执行——隐藏模板节点避免误导作者
            // 「删 nextline 提升」结果写进 Command 列后实际用不上。
            // 判定：主链 BFS 后的所有 sink（普通命令节点 + 主延续缺失的 choice）全部是 FlowCommand。
            if (!MainChainTailHasNonFlowCommand(graph)) return;

            string startId = ChainGraphDumper.SentinelId(isConfirm, true);
            string endId = ChainGraphDumper.SentinelId(isConfirm, false);
            var startView = GetNodeView(isConfirm, startId);
            var endView = GetNodeView(isConfirm, endId);

            if (startView?.OutputPort == null || endView?.InputPort == null) return;

            const string templateId = "__template_nextline__";
            var templateData = new ChainGraphNode(templateId,
                ChainGraphNodeKind.Command, "nextline", "");
            var templateView = new CommandNodeView(templateData, isConfirmChain: true)
            {
                tooltip = "默认流程命令：本行演出完毕后推进到下一行。\n" +
                          "这是模板节点——不会写入 Command 列。\n" +
                          "删除此节点可将其提升为定制行（写入 Command 列）。"
            };
            templateView.MarkAsTemplateGhost();
            templateView.SetTitle("nextline");
            templateView.capabilities &= ~Capabilities.Deletable;  // 删除走提升路径
            templateView.capabilities &= ~Capabilities.Copiable;
            templateView.SetPosition(new Rect(
                startX + 170f, centerY - 30f, 0f, 0f));
            AddElement(templateView);
            _nodeViews[NodeViewKey(isConfirm, templateId)] = templateView;

            // 视图连线：entry → nextline → exit（模板边不可删）
            var e1 = startView.OutputPort.ConnectTo(templateView.InputPort);
            e1.capabilities &= ~Capabilities.Deletable;
            AddElement(e1);

            var e2 = templateView.OutputPort.ConnectTo(endView.InputPort);
            e2.capabilities &= ~Capabilities.Deletable;
            AddElement(e2);
        }

        /// <summary>
        /// R11+：根据当前主链尾 FlowCommand 状态，重新评估 nextline 节点的可见性。
        /// 主链尾全是 FlowCommand（jump / choice / loadscript / ...）时，nextline 永远不会被执行——
        /// 隐藏视图（不删数据，Command 列文本保留，Validator 规则 12 仍按数据判断）。
        ///
        /// <para>
        /// 调用时机：<see cref="OnGraphViewChanged"/> structural 路径（图数据变化后）、
        /// <see cref="BuildLane"/> 末尾（初始化时）。
        /// </para>
        /// </summary>
        private void RefreshNextLineVisibility(ChainGraph graph, bool isConfirm)
        {
            if (graph == null) return;

            bool hide = !MainChainTailHasNonFlowCommand(graph);

            foreach (var view in _nodeViews.Values)
            {
                if (view == null) continue;
                if (view.IsConfirmChain != isConfirm) continue;
                if (!(view is CommandNodeView cmdView)) continue;
                if (cmdView.Data == null) continue;
                if (!string.Equals((cmdView.Data.CommandName ?? "").Trim(), "nextline",
                    StringComparison.OrdinalIgnoreCase)) continue;
                if (cmdView.ClassListContains("vn-node--template")) continue; // 模板节点由 EnsureTemplateNextLine 控制

                // 隐藏：style.display = None（不触发重建）
                cmdView.style.display = hide ? DisplayStyle.None : DisplayStyle.Flex;
            }
        }

        /// <summary>
        /// R11：判断出口段主链尾是否**还有非 FlowCommand 的可达节点**——
        /// 若所有主链 sink（普通命令 + 主延续缺失的 choice）都是 FlowCommand
        /// （jump / jumpif / loadscript / choice / loadscene 等），nextline 永远不会被执行。
        ///
        /// <para>
        /// 主链 BFS 复用 <see cref="GraphToAst.CollectMainChainNodes"/>：
        /// choice 节点只沿主延续边（Port=Main）扩展，选项链内节点不进入主链。
        /// </para>
        /// </summary>
        private static bool MainChainTailHasNonFlowCommand(ChainGraph graph)
        {
            if (graph == null) return true; // 防御：空图 → 显示（提示用户）
            var startSentinel = ChainGraphDumper.FindStartSentinel(graph);
            if (startSentinel == null) return true;
            var mainChain = GraphToAst.CollectMainChainNodes(graph, startSentinel.Id);
            if (mainChain.Count == 0) return true; // 防御

            // 空出口段（无任何业务命令）→ 显示 nextline 提示作者补 nextline
            bool hasAnyCommand = false;
            foreach (var n in graph.Nodes)
            {
                if (n.Kind == ChainGraphNodeKind.Command) { hasAnyCommand = true; break; }
                if (n.Kind == ChainGraphNodeKind.Choice) { hasAnyCommand = true; break; }
            }
            if (!hasAnyCommand) return true;

            foreach (var s in graph.FindSinks())
            {
                if (s.Kind == ChainGraphNodeKind.Choice) continue;
                // 哨兵（Start/End）不是命令节点——End 的 CommandName 为 null 会被
                // IsFlowCommand 误判为"非流程命令"从而错误显示 nextline
                if (s.Kind == ChainGraphNodeKind.Start || s.Kind == ChainGraphNodeKind.End) continue;
                if (!mainChain.Contains(s.Id)) continue;
                if (!ChainParser.IsFlowCommand((s.CommandName ?? "").Trim()))
                    return true;
            }
            foreach (var n in graph.Nodes)
            {
                if (n.Kind != ChainGraphNodeKind.Choice) continue;
                if (!mainChain.Contains(n.Id)) continue;
                bool hasMain = false;
                foreach (var pair in graph.GetOrderedSuccessors(n.Id))
                {
                    if (pair.Key == ChoicePort.Main) { hasMain = true; break; }
                }
                if (!hasMain) return true; // 主链 choice（无主延续）也算 sink
            }
            return false;
        }

        // ---------------- R10 运行时执行状态可视化 ----------------

        /// <summary>
        /// 按运行时状态给节点上三态 + 执行指针样式。key = 节点 <see cref="ChainGraphNode.SourcePosition"/>
        /// （与运行时埋点记录的 Command 列源偏移一致）。state 为 null（该行未播放过）时全部复位。
        /// Fork/Join/哨兵的 SourcePosition 不在命令 Position 集合中，保持默认外观。
        /// </summary>
        public void SetRuntimeNodeStates(VNovelizer.Core.Diagnostics.LineNodeState state)
        {
            foreach (var view in _nodeViews.Values)
            {
                if (view?.Data == null) continue;

                int pos = view.Data.SourcePosition;
                if (pos < 0 || state == null)
                {
                    view.SetRuntimeState(RuntimeNodeState.None);
                    continue;
                }

                var running = view.IsConfirmChain ? state.ConfirmRunning : state.EntryRunning;
                var executed = view.IsConfirmChain ? state.ConfirmExecuted : state.EntryExecuted;
                int pointer = view.IsConfirmChain ? state.ConfirmPointer : state.EntryPointer;

                // 优先级：正在运行 > 执行指针 > 运行过
                RuntimeNodeState s = RuntimeNodeState.None;
                if (running.Contains(pos)) s = RuntimeNodeState.Running;
                else if (pointer == pos) s = RuntimeNodeState.Pointer;
                else if (executed.Contains(pos)) s = RuntimeNodeState.Executed;

                view.SetRuntimeState(s);
            }
        }

        // ---------------- 校验状态可视化 ----------------

        public void ApplyValidation(ChainGraphValidationResult entryResult,
            ChainGraphValidationResult confirmResult)
        {
            foreach (var view in _nodeViews.Values) view.ApplyValidationState(null);

            ApplyIssues(entryResult, isConfirm: false);
            ApplyIssues(confirmResult, isConfirm: true);
        }

        private void ApplyIssues(ChainGraphValidationResult result, bool isConfirm)
        {
            if (result == null) return;

            foreach (var issue in result.Issues)
            {
                foreach (string nodeId in issue.NodeIds)
                {
                    var view = GetNodeView(isConfirm, nodeId);
                    view?.ApplyValidationState(issue.Level, issue.Message);
                }
            }
        }

        // ---------------- 选中与节点操作 ----------------

        public override void AddToSelection(ISelectable selectable)
        {
            base.AddToSelection(selectable);
            OnNodeSelected?.Invoke(selectable as VNNodeViewBase);
            RefreshRangeHighlight();
        }

        public override void RemoveFromSelection(ISelectable selectable)
        {
            base.RemoveFromSelection(selectable);
            // 注意：不触发 OnNodeSelected——取消单个选中不应把 Inspector 切到刚取消的节点
            RefreshRangeHighlight();
        }

        public override void ClearSelection()
        {
            base.ClearSelection();
            OnNodeSelected?.Invoke(null);
            RefreshRangeHighlight();
        }

        /// <summary>
        /// Fork/Join 范围高亮（2026-09-03 新增）：选中 Fork/Join 时，其配对范围
        /// （中间节点 + 配对端点，含嵌套内部）加「vn-node--range」弱化高亮，
        /// 帮助用户看清并行组的影响范围。多选取并集；被选中的节点本身保持选中
        /// 样式、不叠加范围高亮；取消全部选中即全部清除。
        /// </summary>
        private void RefreshRangeHighlight()
        {
            if (_suppressChangeEvents) return;

            // 1. 收集当前选中中的 Fork/Join，按 (泳道, 节点ID) 求范围并集
            var rangeKeys = new HashSet<string>();
            foreach (var sel in selection)
            {
                if (!(sel is ForkJoinNodeView fj)) continue;
                var graph = fj.IsConfirmChain ? ConfirmGraph : EntryGraph;
                if (!GraphToAst.TryResolveForkJoinRange(graph, fj.Data.Id, out _, out _, out var ids))
                    continue;
                foreach (var id in ids)
                    rangeKeys.Add(NodeViewKey(fj.IsConfirmChain, id));
            }

            // 2. 应用 / 清除高亮类
            foreach (var kvp in _nodeViews)
            {
                bool inRange = rangeKeys.Contains(kvp.Key) && !selection.Contains(kvp.Value);
                if (inRange) kvp.Value.AddToClassList("vn-node--range");
                else kvp.Value.RemoveFromClassList("vn-node--range");
            }
        }

        /// <summary>在指定画布坐标处创建一个命令节点。</summary>
        /// <param name="notifyChange">是否触发 OnGraphChanged（批量操作如粘贴传 false，由调用方统一收尾）</param>
        public CommandNodeView CreateCommandNode(string commandName, string args,
            Vector2 canvasPosition, bool isConfirm, bool notifyChange = true)
        {
            var graph = isConfirm ? ConfirmGraph : EntryGraph;
            string id = GenerateNodeId(graph, commandName);

            var data = graph.AddNode(id, ChainGraphNodeKind.Command, commandName, args ?? "");
            var view = new CommandNodeView(data, isConfirm);
            view.SetPosition(new Rect(canvasPosition, new Vector2(0f, 0f)));

            AddElement(view);
            _nodeViews[NodeViewKey(isConfirm, id)] = view;

            if (notifyChange) OnGraphChanged?.Invoke();
            return view;
        }

        /// <summary>
        /// R11：在指定画布坐标处创建 choice 节点（默认 2 个空选项）。
        /// 创建后自动把主延续端口连到 End 哨兵（"选项执行完进入等待确认"的默认出口）。
        /// </summary>
        /// <param name="notifyChange">是否触发 OnGraphChanged（批量操作如粘贴传 false，由调用方统一收尾）</param>
        public ChoiceNodeView CreateChoiceNode(Vector2 canvasPosition, bool isConfirm,
            bool notifyChange = true)
        {
            var graph = isConfirm ? ConfirmGraph : EntryGraph;
            string id = GenerateNodeId(graph, "choice");

            var data = graph.AddNode(id, ChainGraphNodeKind.Choice);
            data.ChoiceTexts = new List<string> { "", "" };

            var view = new ChoiceNodeView(data, isConfirm);
            view.SetPosition(new Rect(canvasPosition, new Vector2(0f, 0f)));

            AddElement(view);
            _nodeViews[NodeViewKey(isConfirm, id)] = view;

            // 主延续边自动连 End 哨兵（默认出口；多 choice 合并展示时用户可改连另一 choice）
            string endId = ChainGraphDumper.SentinelId(isConfirm, false);
            var endView = GetNodeView(isConfirm, endId);
            if (endView?.InputPort != null && view.MainOutputPort != null)
            {
                graph.AddEdge(id, endId, ChoicePort.Main);
                var e = view.MainOutputPort.ConnectTo(endView.InputPort);
                AddElement(e);
            }

            if (notifyChange) OnGraphChanged?.Invoke();
            return view;
        }

        /// <summary>
        /// R11：给 choice 节点新增一个空选项（描述为空、端口未连）。
        /// </summary>
        public void AddChoiceOption(ChoiceNodeView view)
        {
            if (view == null) return;
            view.AddOption();
            OnGraphChanged?.Invoke();
        }

        /// <summary>
        /// R11：删除 choice 的第 <paramref name="index"/> 个选项——
        /// 级联删除该端口连出的整条链（含分支下游全部非哨兵节点，哨兵/其他泳道保留）。
        /// 删除后其余选项端口前移一位，出边按新索引重新渲染。
        /// </summary>
        public void RemoveChoiceOption(ChoiceNodeView view, int index)
        {
            if (view == null || view.Data == null) return;
            var graph = view.IsConfirmChain ? ConfirmGraph : EntryGraph;

            _suppressChangeEvents = true;
            try
            {
                // 1. 收集下游节点（从该选项端口 BFS，哨兵除外）
                var toDelete = new HashSet<string>();
                var queue = new Queue<string>();
                foreach (var pair in graph.GetOrderedSuccessors(view.Data.Id))
                {
                    if (pair.Key != index) continue;
                    var target = graph.GetNode(pair.Value);
                    if (target == null) continue;
                    if (target.Kind == ChainGraphNodeKind.Start ||
                        target.Kind == ChainGraphNodeKind.End) continue;
                    if (toDelete.Add(pair.Value)) queue.Enqueue(pair.Value);
                }

                while (queue.Count > 0)
                {
                    string id = queue.Dequeue();
                    var n = graph.GetNode(id);
                    if (n == null) continue;
                    foreach (string s in graph.GetSuccessors(id))
                    {
                        var sn = graph.GetNode(s);
                        if (sn == null) continue;
                        if (sn.Kind == ChainGraphNodeKind.Start ||
                            sn.Kind == ChainGraphNodeKind.End) continue;
                        if (toDelete.Add(s)) queue.Enqueue(s);
                    }
                }

                // 2. 视图删除：choice 的全部出边（端口重编号后需按新索引重建）
                //    + 下游节点及其间连线。
                var viewEdgesToRemove = edges.ToList().Where(e =>
                {
                    var f = e.output?.node as VNNodeViewBase;
                    var t = e.input?.node as VNNodeViewBase;
                    if (f == view) return true;
                    if (t != null && t.Data != null && toDelete.Contains(t.Data.Id)) return true;
                    if (f != null && f.Data != null && toDelete.Contains(f.Data.Id)) return true;
                    return false;
                }).ToList();
                if (viewEdgesToRemove.Count > 0) DeleteElements(viewEdgesToRemove);

                foreach (var id in toDelete)
                {
                    var v = GetNodeView(view.IsConfirmChain, id);
                    if (v != null)
                    {
                        RemoveElement(v);
                        _nodeViews.Remove(NodeViewKey(view.IsConfirmChain, id));
                    }
                }

                // 3. 数据层：删选项端口边 → 端口重编号 → 级联删节点
                foreach (var pair in graph.GetOrderedSuccessors(view.Data.Id))
                    if (pair.Key == index)
                        graph.RemoveEdge(view.Data.Id, pair.Value, index);

                graph.ShiftChoicePorts(view.Data.Id, index);

                foreach (var id in toDelete)
                    graph.RemoveNode(id);

                // 4. 视图：移除选项数据 + 重建行（端口实例随 RebuildOptions 更新）
                view.RemoveOptionDataAt(index);

                // 5. 按新端口索引重新渲染该 choice 的全部出边
                foreach (var edge in graph.Edges)
                {
                    if (edge.FromId != view.Data.Id) continue;
                    var to = GetNodeView(view.IsConfirmChain, edge.ToId);
                    if (to?.InputPort == null) continue;
                    var outPort = edge.PortIndex == ChoicePort.Main
                        ? view.MainOutputPort
                        : view.GetOptionPort(edge.PortIndex);
                    if (outPort == null) continue;
                    AddElement(outPort.ConnectTo(to.InputPort));
                }
            }
            finally
            {
                _suppressChangeEvents = false;
            }

            OnGraphChanged?.Invoke();
        }

        // ---------------- 节点级复制 / 粘贴 ----------------

        /// <summary>
        /// 收集选中命令节点的复制载荷：命令文本 + 相对第一个节点的位置偏移。
        /// 粘贴时按偏移还原布局（吸收 GraphView 内置 serializeGraphElements 的核心体验）。
        /// Fork/Join/终端不参与节点级复制（复制粘贴链可覆盖该场景）。
        /// </summary>
        public List<NodePastePayload> CopySelectedNodes()
        {
            var payloads = new List<NodePastePayload>();
            Vector2 anchor = Vector2.zero;
            bool first = true;

            foreach (var selectable in selection)
            {
                if (!(selectable is CommandNodeView view)) continue;
                if (view.Data == null || string.IsNullOrEmpty(view.Data.CommandName)) continue;

                var pos = view.GetPosition().position;
                if (first) { anchor = pos; first = false; }
                payloads.Add(new NodePastePayload
                {
                    Command = view.Data.CommandName + "(" + (view.Data.Args ?? "") + ")",
                    Offset = pos - anchor,
                });
            }
            return payloads;
        }

        /// <summary>
        /// 在画布指定位置粘贴节点载荷——按复制的相对位置还原布局。
        /// 粘贴的节点孤立——由用户连线（或用插入式连线接到链上）。
        /// </summary>
        /// <param name="notifyChange">false 时不触发 OnGraphChanged（批量操作统一收尾）</param>
        public List<CommandNodeView> PasteNodesAt(List<NodePastePayload> payloads,
            Vector2 canvasPosition, bool isConfirm, bool notifyChange = true)
        {
            var created = new List<CommandNodeView>();
            if (payloads == null) return created;

            foreach (var payload in payloads)
            {
                // 解析 cmd(args)
                string cmd = (payload.Command ?? "").Trim();
                if (string.IsNullOrEmpty(cmd)) continue;
                string name = cmd;
                string args = "";
                int paren = cmd.IndexOf('(');
                if (paren > 0 && cmd.EndsWith(")"))
                {
                    name = cmd.Substring(0, paren);
                    args = cmd.Substring(paren + 1, cmd.Length - paren - 2);
                }
                if (string.IsNullOrEmpty(name)) continue;

                var view = CreateCommandNode(name, args,
                    canvasPosition + payload.Offset, isConfirm, notifyChange: false);
                created.Add(view);
            }

            if (created.Count > 0)
            {
                ClearSelection();
                foreach (var v in created) AddToSelection(v);
                if (notifyChange) OnGraphChanged?.Invoke();
            }
            return created;
        }

        /// <summary>
        /// 把 <paramref name="newNode"/> 插入到 <paramref name="source"/> 链中后续位置：
        /// 断开 source → down，改建 source → newNode + newNode → down。
        /// 同一泳道、同为命令节点，且 source 有下游时才执行。
        /// 粘贴单节点时若复制源仍选中，调用此方法避免节点变成孤立（无法保存）。
        /// </summary>
        /// <param name="notifyChange">false 时不触发 OnGraphChanged（批量操作统一收尾）</param>
        public bool InsertAfter(CommandNodeView source, CommandNodeView newNode, bool notifyChange = true)
        {
            if (source == null || newNode == null) return false;
            if (source.IsConfirmChain != newNode.IsConfirmChain) return false;
            if (source == newNode) return false;
            if (source.Data == null || newNode.Data == null) return false;
            if (source.OutputPort == null || newNode.InputPort == null || newNode.OutputPort == null) return false;

            var graph = source.IsConfirmChain ? ConfirmGraph : EntryGraph;

            // 找 source 当前下游边（Single 容量，最多 1 条）
            VNNodeViewBase downView = null;
            Edge oldEdge = null;
            foreach (var e in edges)
            {
                if (e.output == source.OutputPort)
                {
                    oldEdge = e;
                    downView = e.input?.node as VNNodeViewBase;
                    break;
                }
            }

            _suppressChangeEvents = true;
            try
            {
                // 断旧边 source → down
                if (oldEdge != null && downView != null && downView.Data != null)
                {
                    source.OutputPort.Disconnect(oldEdge);
                    RemoveElement(oldEdge);
                    graph.RemoveEdge(source.Data.Id, downView.Data.Id);
                }

                // 新边 source → newNode
                var e1 = source.OutputPort.ConnectTo(newNode.InputPort);
                AddElement(e1);
                graph.AddEdge(source.Data.Id, newNode.Data.Id);

                // 新边 newNode → down（如果有原下游）
                if (downView != null && downView.Data != null && downView.InputPort != null)
                {
                    var e2 = newNode.OutputPort.ConnectTo(downView.InputPort);
                    AddElement(e2);
                    graph.AddEdge(newNode.Data.Id, downView.Data.Id);
                }
            }
            finally
            {
                _suppressChangeEvents = false;
            }

            if (notifyChange) OnGraphChanged?.Invoke();
            return true;
        }

        /// <summary>画布可视区域中心的画布坐标（粘贴落点）。</summary>
        public Vector2 GetViewCenterCanvas()
        {
            return contentViewContainer.WorldToLocal(worldBound.center);
        }

        /// <summary>节点级复制的载荷：命令文本 + 相对锚点的位置偏移。</summary>
        public class NodePastePayload
        {
            public string Command;
            public Vector2 Offset;
        }

        public void CreateForkJoinPair(Vector2 canvasPosition, bool isConfirm)
        {
            var graph = isConfirm ? ConfirmGraph : EntryGraph;

            string forkId = GenerateNodeId(graph, "fork");
            string joinId = GenerateNodeId(graph, "join");

            var forkData = graph.AddNode(forkId, ChainGraphNodeKind.Fork);
            var joinData = graph.AddNode(joinId, ChainGraphNodeKind.Join);

            var forkView = new ForkJoinNodeView(forkData, isConfirm);
            var joinView = new ForkJoinNodeView(joinData, isConfirm);

            // Fork 在左，Join 在右（水平间隔）
            forkView.SetPosition(new Rect(canvasPosition, new Vector2(0f, 0f)));
            joinView.SetPosition(new Rect(
                canvasPosition + new Vector2(300f, 0f), new Vector2(0f, 0f)));

            AddElement(forkView);
            AddElement(joinView);
            _nodeViews[NodeViewKey(isConfirm, forkId)] = forkView;
            _nodeViews[NodeViewKey(isConfirm, joinId)] = joinView;

            OnGraphChanged?.Invoke();
        }

        /// <summary>
        /// 整理布局：按执行顺序重新排布全部节点（含端点约束、实测宽度）。
        /// **唯一**触发全图自动布局的入口（工具栏按钮 / 右键菜单）。
        ///
        /// <para>
        /// 2026-08-28：RelayoutAll 现在会通知 Window 清空对应行的位置缓存——
        /// 否则下次 Rebuild 仍会读回用户拖过的旧位置，"整理布局"只生效一次。
        /// </para>
        /// </summary>
        public void RelayoutAll()
        {
            // 用实测宽度跑一次 layout（更精确——长命令节点不会挤压邻居）
            var widths = MeasureAllWidths();
            ApplyLayout(EntryGraph, isConfirm: false,
                ChainAutoLayout.EntryLaneY, widths);
            ApplyLayout(ConfirmGraph, isConfirm: true,
                ChainAutoLayout.ConfirmLaneY, widths);
            OnPositionsRelayouted?.Invoke();
        }

        /// <summary>布局被强制重排时通知——Window 侧据此清空 GraphPosStore 中该行缓存。</summary>
        public event Action OnPositionsRelayouted;

        private void ApplyLayout(ChainGraph graph, bool isConfirm, float centerY,
            IReadOnlyDictionary<string, float> widths = null)
        {
            var layout = ChainAutoLayout.Layout(graph, centerY, widths);
            foreach (var pair in layout)
            {
                var view = GetNodeView(isConfirm, pair.Key);
                if (view == null) continue;
                var rect = view.GetPosition();
                view.SetPosition(new Rect(pair.Value, rect.size));
            }
        }

        public Dictionary<string, Vector2> CollectPositions()
        {
            var result = new Dictionary<string, Vector2>();
            foreach (var pair in _nodeViews)
            {
                var view = pair.Value;
                if (view.Data == null) continue;

                var pos = view.GetPosition().position;
                // 防御：跳过零位——未完成布局的节点记录 (0,0) 会污染快照，
                // Undo 恢复时全部节点叠在原点。
                if (pos.x == 0f && pos.y == 0f) continue;

                result[PositionKey(view.IsConfirmChain, view.Data.Id)] = pos;
            }
            return result;
        }

        // ---------------- 手势检测 ----------------

        /// <summary>
        /// Capture 阶段捕获节点上的左键按下——拖动手势开始信号。
        /// 无论最终是否发生移动（点击不算），Window 侧的暂存快照都会被覆盖或丢弃。
        /// </summary>
        private void OnPointerDownCapture(PointerDownEvent evt)
        {
            if (evt.button != 0) return;

            var ve = evt.target as VisualElement;
            if (ve?.GetFirstAncestorOfType<Node>() == null) return;

            OnNodeDragGestureStarted?.Invoke();
        }

        // ---------------- 工具 ----------------

        private static string NodeViewKey(bool isConfirm, string nodeId)
            => (isConfirm ? "c:" : "e:") + nodeId;

        private static string PositionKey(bool isConfirm, string nodeId)
            => (isConfirm ? "confirm/" : "entry/") + nodeId;

        private VNNodeViewBase GetNodeView(bool isConfirm, string nodeId)
        {
            if (nodeId == null) return null;
            _nodeViews.TryGetValue(NodeViewKey(isConfirm, nodeId), out var view);
            return view;
        }

        /// <summary>
        /// 按命令签名查找命令节点视图（文本编辑器单击命令 → 细节栏展示用，2026-09-03）。
        /// 匹配：泳道 + 命令名（忽略大小写）+ 参数（两端 trim 后精确比较）。
        /// 多个同名同参节点取第一个；找不到返回 null（调用方静默忽略）。
        /// </summary>
        public CommandNodeView FindCommandNode(bool isConfirm, string commandName, string args)
        {
            if (string.IsNullOrEmpty(commandName)) return null;
            string a = args?.Trim() ?? "";

            foreach (var kvp in _nodeViews)
            {
                if (!(kvp.Value is CommandNodeView cmd)) continue;
                if (kvp.Value.IsConfirmChain != isConfirm) continue;
                if (!string.Equals(cmd.Data.CommandName, commandName, StringComparison.OrdinalIgnoreCase)) continue;
                if ((cmd.Data.Args ?? "").Trim() != a) continue;
                return cmd;
            }
            return null;
        }

        /// <summary>
        /// 仅按 id 查找节点 view（不区分泳道）——用于错误对话框等仅知 id 的场景。
        /// 找不到返回 null（节点可能已被删除或尚未创建）。
        /// </summary>
        public VNNodeViewBase GetNodeViewForValidation(string nodeId)
        {
            if (nodeId == null) return null;
            foreach (var v in _nodeViews.Values)
                if (v != null && v.Data != null && v.Data.Id == nodeId)
                    return v;
            return null;
        }

        private static string GenerateNodeId(ChainGraph graph, string prefix)
        {
            int i = graph.NodeCount;
            string id;
            do
            {
                id = i + ":" + (prefix ?? "node").ToLower();
                i++;
            } while (graph.GetNode(id) != null);
            return id;
        }

        private void FrameAllIfNeeded()
        {
            if (_nodeViews.Count == 0) return;
            FrameAll();
        }

        public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            Vector2 canvasPos = contentViewContainer.WorldToLocal(evt.mousePosition);
            bool isConfirm = canvasPos.y >
                             (ChainAutoLayout.EntryLaneY + ChainAutoLayout.ConfirmLaneY) / 2f;

            string laneName = isConfirm ? "出口段" : "进入段";

            evt.menu.AppendAction("添加 FORK / JOIN 并行组 (" + laneName + ")",
                _ => CreateForkJoinPair(canvasPos, isConfirm));
            evt.menu.AppendSeparator();
            evt.menu.AppendAction("整理布局", _ => RelayoutAll());
            evt.menu.AppendAction("聚焦全部节点", _ => FrameAll());

            base.BuildContextualMenu(evt);
        }
    }

    /// <summary>
    /// 自实现拖拽框选器。选框绘制到 contentViewContainer，坐标系与画布缩放/平移同步。
    ///
    /// <para>
    /// <b>2026-09-03：兼任「点空白取消选中」</b>。取消选中原依赖 Unity 内置
    /// <c>ClickSelector</c>，其激活条件要求 <c>clickCount == 1</c>——编辑节奏快时
    /// （选中节点后紧接着快速点空白）系统把该次点击报成双击序列（clickCount &gt; 1），
    /// 条件不满足 → 不清除，表现为「点空白取消选中不那么灵敏」。本选择器在空白处
    /// 按下时已捕获指针，故由它直接处理：抬起时若从未拖动（纯点击）则显式清除选中，
    /// 彻底摆脱 clickCount 依赖。ClickSelector 保留作装饰元素等未捕获场景的兜底。
    /// </para>
    /// </summary>
    public class DragBoxSelector : Manipulator
    {
        /// <summary>位移超过该阈值视为拖拽框选而非点击（画布坐标像素）</summary>
        private const float DragThreshold = 3f;

        private VisualElement _box;
        private Vector2 _startLocal;
        private bool _active;
        private bool _dragged;

        protected override void RegisterCallbacksOnTarget()
        {
            target.RegisterCallback<PointerDownEvent>(OnPointerDown);
            target.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            target.RegisterCallback<PointerUpEvent>(OnPointerUp);
            target.RegisterCallback<PointerCancelEvent>(OnPointerCancel);
            target.RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            target.UnregisterCallback<PointerDownEvent>(OnPointerDown);
            target.UnregisterCallback<PointerMoveEvent>(OnPointerMove);
            target.UnregisterCallback<PointerUpEvent>(OnPointerUp);
            target.UnregisterCallback<PointerCancelEvent>(OnPointerCancel);
            target.UnregisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0) return;

            var graphView = target as GraphView;
            if (graphView == null) return;

            // 仅当点在空白区域时启动框选
            if (evt.target != target) return;

            _startLocal = graphView.contentViewContainer.WorldToLocal(evt.position);

            _box = new VisualElement();
            _box.AddToClassList("vn-rect-box");
            _box.pickingMode = PickingMode.Ignore;
            _box.style.left = _startLocal.x;
            _box.style.top = _startLocal.y;
            _box.style.width = 0;
            _box.style.height = 0;
            graphView.contentViewContainer.Add(_box);

            target.CapturePointer(evt.pointerId);
            _active = true;
            _dragged = false;
            evt.StopPropagation();
        }

        private void OnPointerCancel(PointerCancelEvent evt)
        {
            EndBoxSelection();
        }

        private void OnPointerCaptureOut(PointerCaptureOutEvent evt)
        {
            EndBoxSelection();
        }

        private void EndBoxSelection()
        {
            if (!_active) return;
            _active = false;
            if (_box != null)
            {
                _box.RemoveFromHierarchy();
                _box = null;
            }
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (!_active || _box == null) return;

            var graphView = target as GraphView;
            if (graphView == null) return;

            Vector2 curLocal = graphView.contentViewContainer.WorldToLocal(evt.position);

            // 位移超过阈值 → 判定为拖拽框选（抬起时不再当作"点击清除"）
            if (!_dragged && (curLocal - _startLocal).magnitude > DragThreshold)
                _dragged = true;

            float x = Mathf.Min(_startLocal.x, curLocal.x);
            float y = Mathf.Min(_startLocal.y, curLocal.y);
            float w = Mathf.Abs(curLocal.x - _startLocal.x);
            float h = Mathf.Abs(curLocal.y - _startLocal.y);

            _box.style.left = x;
            _box.style.top = y;
            _box.style.width = w;
            _box.style.height = h;

            var selection = new Rect(x, y, w, h);
            UpdateSelection(graphView, selection, evt.shiftKey);
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (!_active) return;
            _active = false;

            if (_box != null)
            {
                _box.RemoveFromHierarchy();
                _box = null;
            }

            // 纯点击（未拖动）空白 → 显式清除选中。不依赖内置 ClickSelector 的
            // clickCount == 1 条件（快速连击时 clickCount > 1 导致时灵时不灵）。
            // shift+点击空白保留多选，与 ClickSelector 语义一致。
            if (!_dragged && !evt.shiftKey)
                (target as GraphView)?.ClearSelection();

            target.ReleasePointer(evt.pointerId);
            evt.StopPropagation();
        }

        private void UpdateSelection(GraphView graphView, Rect rect, bool additive)
        {
            if (!additive) graphView.ClearSelection();

            foreach (var node in graphView.nodes.ToList())
            {
                var ve = node as VisualElement;
                if (ve == null) continue;
                var worldRect = ve.worldBound;
                var localTopLeft = graphView.contentViewContainer.WorldToLocal(
                    new Vector2(worldRect.x, worldRect.y));
                var localBottomRight = graphView.contentViewContainer.WorldToLocal(
                    new Vector2(worldRect.xMax, worldRect.yMax));
                var nodeRect = new Rect(localTopLeft.x, localTopLeft.y,
                    localBottomRight.x - localTopLeft.x, localBottomRight.y - localTopLeft.y);

                if (rect.Overlaps(nodeRect) || rect.Contains(nodeRect.center))
                {
                    graphView.AddToSelection(node);
                }
            }
        }
    }
}
