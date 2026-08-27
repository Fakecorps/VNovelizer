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
    /// </summary>
    public class RowGraphView : GraphView
    {
        public event Action<VNNodeViewBase> OnNodeSelected;
        public event Action OnGraphChanged;
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

            // 2026-08-27：端口完全外置（left/right:-14）——确保画布任何上层都不裁剪节点溢出区域
            // 2026-08-27（用户需求 1 修复）：移除画布 overflow:visible——
            // 否则节点会渲染到上层兄弟元素（如左侧命令面板）上方造成视觉重叠。
            // 端口不可见的根因是 #node-border 的 overflow:hidden，已在 VNNodeViewBase 修；
            // 端口只需溢出 #node-border（节点自身 overflow:visible），无需溢出整个画布。

            SetupZoom(0.35f, 2.0f);
            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new ClickSelector());
            this.AddManipulator(new DragBoxSelector());

            Insert(0, new GridBackground());

            graphViewChanged = OnGraphViewChanged;

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
        /// 重建画布。
        /// 2026-08-27 决策（用户 Q1）：彻底删除折叠胶囊——默认演出直接展开为
        /// 独立节点（由 RowPerfEditorWindow 合成完整模板图后传入）。
        /// savedPositions 有值时恢复保存位置；无值时做一次基础布局，
        /// 之后完全由用户拖拽掌控（不再有任何自动重排）。
        /// </summary>
        public void Rebuild(ChainGraph entryGraph, ChainGraph confirmGraph,
            Dictionary<string, Vector2> savedPositions = null,
            bool templateCollapsed = true, bool showTemplate = false)
        {
            _suppressChangeEvents = true;

            ClearCanvas();

            EntryGraph = entryGraph ?? new ChainGraph();
            ConfirmGraph = confirmGraph ?? new ChainGraph();

            float entryStartX = ChainAutoLayout.StartX;

            BuildLane(EntryGraph, isConfirm: false, centerY: ChainAutoLayout.EntryLaneY,
                savedPositions: savedPositions, startX: entryStartX);

            BuildLane(ConfirmGraph, isConfirm: true, centerY: ChainAutoLayout.ConfirmLaneY,
                savedPositions: savedPositions, startX: ChainAutoLayout.StartX);

            _suppressChangeEvents = false;

            schedule.Execute(FrameAllIfNeeded).ExecuteLater(50);
            // 2026-08-27 决策（用户 Q2）：不再自动 MeasureAndRelayout。
            // 自动重排是"拖一下节点全图乱跑"的元凶之一；
            // 现在：无保存位置时 BuildLane 内做一次基础布局，之后完全由用户掌控，
            // "整理布局"工具栏按钮才触发完整重排（RelayoutAll → MeasureAndRelayout）。
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
            if (graph.NodeCount == 0)
            {
                if (isConfirm)
                {
                    // 2026-08-27（用户需求 4）：出口段为空时也显示默认结构——
                    // OnConfirmEntry → [NextLine 引擎隐式影子] → OnConfirmExit。
                    // 影子节点与连线纯视图层（不写入 ChainGraph），序列化天然不含。
                    BuildEmptyConfirmLane(centerY, startX);
                    return;
                }
                BuildTerminalsOnly(graph, isConfirm, centerY, startX);
                return;
            }

            var layout = ChainAutoLayout.Layout(graph, centerY);

            foreach (var node in graph.Nodes)
            {
                var view = CreateNodeView(node, isConfirm);
                if (view == null) continue;

                Vector2 pos = Vector2.zero;
                bool restored = savedPositions != null &&
                                savedPositions.TryGetValue(PositionKey(isConfirm, node.Id), out pos);
                if (!restored)
                {
                    if (layout.TryGetValue(node.Id, out var layoutPos)) pos = layoutPos;
                    pos.x += (startX - ChainAutoLayout.StartX);
                }

                view.SetPosition(new Rect(pos, new Vector2(0f, 0f)));
                AddElement(view);
                _nodeViews[NodeViewKey(isConfirm, node.Id)] = view;
            }

            foreach (var edge in graph.Edges)
            {
                var from = GetNodeView(isConfirm, edge.FromId);
                var to = GetNodeView(isConfirm, edge.ToId);
                if (from?.OutputPort == null || to?.InputPort == null) continue;

                var e = from.OutputPort.ConnectTo(to.InputPort);
                AddElement(e);
            }

            // 传 layout 给终端定位（同步可得；view.GetPosition().xMax 不可靠——SetPosition 时 size=(0,0)）
            BuildTerminalsOnly(graph, isConfirm, centerY, startX, layout);
        }

        /// <summary>
        /// 测量所有节点的实际渲染宽度并重新布局。
        /// </summary>
        private void MeasureAndRelayout()
        {
            if (EntryGraph.NodeCount == 0 && ConfirmGraph.NodeCount == 0) return;

            var measured = new Dictionary<string, float>();
            foreach (var pair in _nodeViews)
            {
                var view = pair.Value;
                if (view == null || view.Data == null) continue;
                float w = view.localBound.width;
                if (w > 0) measured[view.Data.Id] = w;
            }

            float entryStartX = ChainAutoLayout.StartX;

            if (EntryGraph.NodeCount > 0)
                ApplyMeasuredLayout(EntryGraph, isConfirm: false,
                    ChainAutoLayout.EntryLaneY, measured, entryStartX);

            if (ConfirmGraph != null && ConfirmGraph.NodeCount > 0)
                ApplyMeasuredLayout(ConfirmGraph, isConfirm: true,
                    ChainAutoLayout.ConfirmLaneY, measured, ChainAutoLayout.StartX);
        }

        private void ApplyMeasuredLayout(ChainGraph graph, bool isConfirm, float centerY,
            Dictionary<string, float> measured, float startX)
        {
            var positions = ChainAutoLayout.MeasureAndRelayout(graph, centerY, measured);
            foreach (var pair in positions)
            {
                var view = GetNodeView(isConfirm, pair.Key);
                if (view == null) continue;
                var rect = view.GetPosition();
                view.SetPosition(new Rect(
                    pair.Value.x + (startX - ChainAutoLayout.StartX),
                    pair.Value.y, rect.width, rect.height));
            }
        }

        private void BuildTerminalsOnly(ChainGraph graph, bool isConfirm, float centerY,
            float startX, Dictionary<string, Vector2> layout = null)
        {
            var sources = graph.FindSources();
            var sinks = graph.FindSinks();

            if (sources.Count == 1)
            {
                var kind = isConfirm ? TerminalKind.ConfirmStart : TerminalKind.LineStart;
                // 起始终端在第一个命令节点左侧——用 layout 字典算位置（同步可知）
                float firstX = layout != null && layout.TryGetValue(sources[0].Id, out var sp)
                    ? sp.x : startX;
                var startView = AddTerminalView(kind, isConfirm,
                    firstX - TerminalReserved, centerY);

                var first = GetNodeView(isConfirm, sources[0].Id);
                if (startView?.OutputPort != null && first?.InputPort != null)
                {
                    var anchor = startView.OutputPort.ConnectTo(first.InputPort);
                    anchor.capabilities &= ~Capabilities.Deletable;
                    AddElement(anchor);
                }
            }

            if (sinks.Count == 1)
            {
                // 结束终端在链最右端——用 layout 字典的 sink 节点位置 + 估算宽度。
                // view.GetPosition().xMax 不可靠（SetPosition 时 size=(0,0)，GraphView 异步测量还没跑）。
                float sinkX = layout != null && layout.TryGetValue(sinks[0].Id, out var lp)
                    ? lp.x
                    : ComputeMaxRight(graph, startX);
                float sinkWidth = EstimateNodeWidth(sinks[0]);
                float maxRight = sinkX + sinkWidth;

                var kind = isConfirm ? TerminalKind.ChainEnd : TerminalKind.WaitConfirm;
                var endView = AddTerminalView(kind, isConfirm,
                    maxRight + ChainAutoLayout.HorizontalGap, centerY);

                var last = GetNodeView(isConfirm, sinks[0].Id);
                if (endView?.InputPort != null && last?.OutputPort != null)
                {
                    var anchor = last.OutputPort.ConnectTo(endView.InputPort);
                    anchor.capabilities &= ~Capabilities.Deletable;
                    AddElement(anchor);
                }
            }
        }

        private float ComputeMaxRight(ChainGraph graph, float startX)
        {
            float maxRight = startX;
            foreach (var pair in _nodeViews)
            {
                if (pair.Value == null || pair.Value.Data == null) continue;
                var view = pair.Value;
                if (graph.Nodes.Any(n => n.Id == view.Data.Id))
                {
                    // 用 pos.x + 估算宽度——view.GetPosition().xMax 此时 size=(0,0) 不可靠
                    float right = view.GetPosition().x + EstimateNodeWidth(view.Data);
                    if (right > maxRight) maxRight = right;
                }
            }
            return maxRight;
        }

        /// <summary>同步估算节点宽度（不依赖 GraphView 异步测量）。</summary>
        private static float EstimateNodeWidth(ChainGraphNode node)
        {
            if (node == null) return 200f;
            switch (node.Kind)
            {
                case ChainGraphNodeKind.Fork:
                case ChainGraphNodeKind.Join:
                    return 120f;
                case ChainGraphNodeKind.Start:
                case ChainGraphNodeKind.End:
                    return 150f;
                default:
                    return string.IsNullOrWhiteSpace(node.Args) ? 160f : 200f;
            }
        }

        private const float TerminalReserved = 140f;

        private TerminalNodeView AddTerminalView(TerminalKind kind, bool isConfirm,
            float x, float centerY)
        {
            bool isStart = kind == TerminalKind.LineStart || kind == TerminalKind.ConfirmStart;
            string id = "__terminal_" + kind + "__";

            var data = new ChainGraphNode(id, isStart ? ChainGraphNodeKind.Start : ChainGraphNodeKind.End);
            var view = new TerminalNodeView(data, kind, isConfirm);
            view.SetPosition(new Rect(x, centerY - 15f, 130f, 30f));
            AddElement(view);
            _nodeViews[NodeViewKey(isConfirm, id)] = view;
            return view;
        }

        private VNNodeViewBase CreateNodeView(ChainGraphNode node, bool isConfirm)
        {
            switch (node.Kind)
            {
                case ChainGraphNodeKind.Command:
                    return new CommandNodeView(node, isConfirm);

                case ChainGraphNodeKind.Fork:
                case ChainGraphNodeKind.Join:
                    return new ForkJoinNodeView(node, isConfirm);

                case ChainGraphNodeKind.Start:
                    return new TerminalNodeView(node,
                        isConfirm ? TerminalKind.ConfirmStart : TerminalKind.LineStart, isConfirm);

                case ChainGraphNodeKind.End:
                    return new TerminalNodeView(node,
                        isConfirm ? TerminalKind.ChainEnd : TerminalKind.WaitConfirm, isConfirm);

                default:
                    return null;
            }
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

            ports.ForEach(port =>
            {
                if (port == startPort) return;
                if (port.direction == startPort.direction) return;
                if (port.node == startPort.node) return;

                var portView = port.node as VNNodeViewBase;
                if (portView == null) return;
                if (portView.IsConfirmChain != startView.IsConfirmChain) return;

                compatible.Add(port);
            });

            return compatible;
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

            if (structural)
            {
                SyncGraphDataFromView(change);
                OnGraphChanged?.Invoke();
                // 2026-08-27：删除连线/删节点后的自动重排——任何结构变化都不许移动用户摆好的节点。
                // 需要重排时点工具栏「整理布局」。
            }
            else if (moved)
            {
                OnGraphChanged?.Invoke();
            }

            return change;
        }

        private void SyncGraphDataFromView(GraphViewChange change)
        {
            RebuildEdgesFromView(EntryGraph, isConfirm: false);
            RebuildEdgesFromView(ConfirmGraph, isConfirm: true);
        }

        private void RebuildEdgesFromView(ChainGraph graph, bool isConfirm)
        {
            var rebuilt = new ChainGraph();
            var validIds = new HashSet<string>();
            foreach (var node in graph.Nodes)
            {
                if (GetNodeView(isConfirm, node.Id) == null) continue;
                rebuilt.AddNode(node);
                validIds.Add(node.Id);
            }

            edges.ForEach(edge =>
            {
                var from = edge.output?.node as VNNodeViewBase;
                var to = edge.input?.node as VNNodeViewBase;
                if (from == null || to == null) return;
                if (from.IsConfirmChain != isConfirm || to.IsConfirmChain != isConfirm) return;
                if (from.Data == null || to.Data == null) return;

                // 引擎隐式影子（NextLine 等）与终端锚点不进图数据
                if (!validIds.Contains(from.Data.Id) || !validIds.Contains(to.Data.Id)) return;

                rebuilt.AddEdge(from.Data.Id, to.Data.Id);
            });

            if (isConfirm) ConfirmGraph = rebuilt;
            else EntryGraph = rebuilt;
        }

        /// <summary>
        /// 出口段为空时的默认结构（2026-08-27 用户需求 4）：
        /// OnConfirmEntry → [NextLine 影子] → OnConfirmExit。
        ///
        /// <para>
        /// NextLine 是引擎隐式行为（出口段执行完自动推进下一行），此处显式画出
        /// 仅为可读性——影子节点与全部连线均为<b>纯视图层</b>（不进 ChainGraph，
        /// 序列化天然不含；<see cref="RebuildEdgesFromView"/> 亦会过滤）。
        /// 用户一旦往出口段拖入真实命令，Rebuild 后走正常链渲染路径。
        /// </para>
        /// </summary>
        private void BuildEmptyConfirmLane(float centerY, float startX)
        {
            // OnConfirmEntry：橙色弹头（左直右圆，输出端口）
            var entryView = AddTerminalView(TerminalKind.ConfirmStart, isConfirm: true,
                x: startX, centerY: centerY);

            // NextLine 影子命令节点
            const string shadowId = "__implicit_nextline__";
            var shadowData = new ChainGraphNode(shadowId, ChainGraphNodeKind.Command,
                "nextline", "");
            var shadowView = new CommandNodeView(shadowData, isConfirmChain: true)
            {
                tooltip = "引擎隐式行为：出口段执行完毕后自动推进到下一行。\n" +
                          "此节点为只读影子——不会写入 Command 列。\n" +
                          "往出口段拖入命令后，它会替换为你的真实编排。"
            };
            shadowView.MarkAsTemplateGhost();
            shadowView.SetTitle("NextLine（引擎默认）");
            // 只读影子：禁止删除/复制（删除会误触提升流程；它是引擎行为不可移除）
            shadowView.capabilities &= ~Capabilities.Deletable;
            shadowView.capabilities &= ~Capabilities.Copiable;
            float entryWidth = 150f;
            shadowView.SetPosition(new Rect(
                startX + entryWidth + ChainAutoLayout.HorizontalGap, centerY - 30f, 0f, 0f));
            AddElement(shadowView);
            _nodeViews[NodeViewKey(true, shadowId)] = shadowView;

            // OnConfirmExit：橙色弹头（左圆右直，输入端口）
            float shadowRight = startX + entryWidth + ChainAutoLayout.HorizontalGap + 190f;
            var exitView = AddTerminalView(TerminalKind.ChainEnd, isConfirm: true,
                x: shadowRight + ChainAutoLayout.HorizontalGap, centerY: centerY);

            // 视图连线：entry → nextline → exit（锚点边不可删）
            if (entryView?.OutputPort != null && shadowView.InputPort != null)
            {
                var e1 = entryView.OutputPort.ConnectTo(shadowView.InputPort);
                e1.capabilities &= ~Capabilities.Deletable;
                AddElement(e1);
            }
            if (shadowView.OutputPort != null && exitView?.InputPort != null)
            {
                var e2 = shadowView.OutputPort.ConnectTo(exitView.InputPort);
                e2.capabilities &= ~Capabilities.Deletable;
                AddElement(e2);
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
        }

        public override void ClearSelection()
        {
            base.ClearSelection();
            OnNodeSelected?.Invoke(null);
        }

        /// <summary>在指定画布坐标处创建一个命令节点。</summary>
        public CommandNodeView CreateCommandNode(string commandName, string args,
            Vector2 canvasPosition, bool isConfirm)
        {
            var graph = isConfirm ? ConfirmGraph : EntryGraph;
            string id = GenerateNodeId(graph, commandName);

            var data = graph.AddNode(id, ChainGraphNodeKind.Command, commandName, args ?? "");
            var view = new CommandNodeView(data, isConfirm);
            view.SetPosition(new Rect(canvasPosition, new Vector2(0f, 0f)));

            AddElement(view);
            _nodeViews[NodeViewKey(isConfirm, id)] = view;

            OnGraphChanged?.Invoke();
            // 2026-08-27：新建节点不再触发全图重排（节点已在用户指定位置）
            return view;
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
            // 2026-08-27：新建 Fork/Join 不再触发全图重排（固定 300px 水平间隔已足够）
        }

        /// <summary>
        /// 整理布局：按执行顺序重新排布全部节点。
        /// **唯一**触发全图自动布局的入口（工具栏按钮 / 右键菜单）。
        /// </summary>
        public void RelayoutAll()
        {
            ApplyLayout(EntryGraph, isConfirm: false, centerY: ChainAutoLayout.EntryLaneY);
            ApplyLayout(ConfirmGraph, isConfirm: true, centerY: ChainAutoLayout.ConfirmLaneY);
            MeasureAndRelayout();
        }

        private void ApplyLayout(ChainGraph graph, bool isConfirm, float centerY)
        {
            var layout = ChainAutoLayout.Layout(graph, centerY);
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
                result[PositionKey(view.IsConfirmChain, view.Data.Id)] =
                    view.GetPosition().position;
            }
            return result;
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
    /// </summary>
    public class DragBoxSelector : Manipulator
    {
        private VisualElement _box;
        private Vector2 _startLocal;
        private bool _active;

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
