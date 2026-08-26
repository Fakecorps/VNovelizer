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
    /// 行演出编辑器的画布：双泳道（进入段 / 出口段）+ 自由拖拽 + 实时校验。
    ///
    /// <para>
    /// <b>双泳道是平级关系而非主从</b>：进入段是"进入本行时执行"，
    /// 出口段是"玩家点击后执行"，两者都是本行演出的一部分，因此同起始高度并排展示。
    /// 若把出口段做成折叠的次要区域，作者容易忘记它的存在而漏掉转场编排。
    /// </para>
    /// </summary>
    public class RowGraphView : GraphView
    {
        /// <summary>选中节点变化（null 表示取消选中）</summary>
        public event Action<VNNodeViewBase> OnNodeSelected;

        /// <summary>图结构发生变更（连线/删除/移动/参数修改）——用于触发校验与脏标记</summary>
        public event Action OnGraphChanged;

        /// <summary>请求把模板提升为定制行（用户触碰了影子节点）</summary>
        public event Action OnRequestPromotion;

        /// <summary>徽章点击请求跳转数据列</summary>
        public event Action<string> OnRequestJumpToColumn;

        /// <summary>进入段图数据</summary>
        public ChainGraph EntryGraph { get; private set; } = new ChainGraph();

        /// <summary>出口段图数据</summary>
        public ChainGraph ConfirmGraph { get; private set; } = new ChainGraph();

        /// <summary>当前行的解析后上下文（模板胶囊显示数据列值用）</summary>
        public VNLineContext LineContext { get; set; }

        private readonly Dictionary<string, VNNodeViewBase> _nodeViews =
            new Dictionary<string, VNNodeViewBase>();

        private readonly List<VisualElement> _decorations = new List<VisualElement>();

        /// <summary>抑制 graphViewChanged 回调（重建图期间避免误触发脏标记）</summary>
        private bool _suppressChangeEvents;

        public RowGraphView()
        {
            AddToClassList("vn-graph");

            // 画布交互：缩放 / 平移 / 框选 / 拖拽
            SetupZoom(0.35f, 2.0f);
            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());

            Insert(0, new GridBackground());

            graphViewChanged = OnGraphViewChanged;

            RegisterCallback<GeometryChangedEvent>(_ => UpdateDecorationPositions());
        }

        // ---------------- 图重建 ----------------

        /// <summary>
        /// 用给定的两段图重建画布。
        /// </summary>
        /// <param name="entryGraph">进入段</param>
        /// <param name="confirmGraph">出口段（可为空图）</param>
        /// <param name="savedPositions">
        /// 已保存的节点位置（节点身份 → 坐标）。命中的节点恢复位置，
        /// 未命中的走 AutoLayout——位置永不阻塞编辑，丢了只是重排。
        /// </param>
        /// <param name="templateCollapsed">模板是否折叠显示</param>
        /// <param name="showTemplate">是否显示模板影子（普通行/增强行为 true）</param>
        public void Rebuild(ChainGraph entryGraph, ChainGraph confirmGraph,
            Dictionary<string, Vector2> savedPositions = null,
            bool templateCollapsed = true, bool showTemplate = false)
        {
            _suppressChangeEvents = true;

            ClearCanvas();

            EntryGraph = entryGraph ?? new ChainGraph();
            ConfirmGraph = confirmGraph ?? new ChainGraph();

            BuildLane(EntryGraph, isConfirm: false, centerX: ChainAutoLayout.EntryLaneX,
                savedPositions: savedPositions,
                showTemplate: showTemplate, templateCollapsed: templateCollapsed);

            BuildLane(ConfirmGraph, isConfirm: true, centerX: ChainAutoLayout.ConfirmLaneX,
                savedPositions: savedPositions,
                showTemplate: false, templateCollapsed: true);

            BuildLaneLabels();

            _suppressChangeEvents = false;

            // 重建后延后一帧再自动聚焦，等 UIElements 完成实际布局
            schedule.Execute(FrameAllIfNeeded).ExecuteLater(50);
        }

        private void ClearCanvas()
        {
            foreach (var deco in _decorations) deco.RemoveFromHierarchy();
            _decorations.Clear();

            DeleteElements(graphElements.ToList());
            _nodeViews.Clear();
        }

        private void BuildLane(ChainGraph graph, bool isConfirm, float centerX,
            Dictionary<string, Vector2> savedPositions, bool showTemplate, bool templateCollapsed)
        {
            // 出口段为空 → 整条泳道不画（避免空荡荡的"出口开始/结束"孤儿终端）
            if (graph.NodeCount == 0 && isConfirm) return;

            var layout = graph.NodeCount > 0
                ? ChainAutoLayout.Layout(graph, centerX)
                : new Dictionary<string, Vector2>();

            float templateOffset = 0f;

            // 模板影子（折叠胶囊）——普通行的主视觉，**即使无命令也必须显示**
            // （普通行 = Command 列为空，画布上唯一的内容就是它）
            if (showTemplate && templateCollapsed)
            {
                var capsuleData = new ChainGraphNode("__template__", ChainGraphNodeKind.Command,
                    "默认演出", "");
                var capsule = new TemplateCapsuleNodeView(capsuleData, LineContext);
                capsule.OnRequestExpand += () => OnRequestPromotion?.Invoke();
                capsule.OnColumnClicked += col => OnRequestJumpToColumn?.Invoke(col);
                capsule.SetPosition(new Rect(
                    centerX - 160f, ChainAutoLayout.StartY, 320f, ChainAutoLayout.CapsuleHeight));
                AddElement(capsule);
                _nodeViews["__template__"] = capsule;

                templateOffset = ChainAutoLayout.CapsuleHeight + ChainAutoLayout.VerticalGap * 2f;
            }

            // 进入段空（普通行且未展开模板）：只有胶囊，无节点无终端
            if (graph.NodeCount == 0) return;

            // 起点终端占位：整条链下移，避免与首节点重叠
            float contentOffset = templateOffset + TerminalReserved + 14f;

            // 建节点（数据图节点：命令 / Fork / Join）
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
                    pos.y += contentOffset;
                }

                view.SetPosition(new Rect(pos, new Vector2(ChainAutoLayout.NodeWidth, 0f)));
                AddElement(view);
                _nodeViews[NodeViewKey(isConfirm, node.Id)] = view;
            }

            // 连边（数据图边）
            foreach (var edge in graph.Edges)
            {
                var from = GetNodeView(isConfirm, edge.FromId);
                var to = GetNodeView(isConfirm, edge.ToId);
                if (from?.OutputPort == null || to?.InputPort == null) continue;

                var e = from.OutputPort.ConnectTo(to.InputPort);
                AddElement(e);
            }

            // 视觉终端（纯视图装饰，**不入数据图**——校验/序列化/边同步都不感知它们，
            // 否则起点终端会与真实首节点构成"双起点"致命错误）
            BuildTerminals(graph, isConfirm, centerX, layout, contentOffset);
        }

        /// <summary>
        /// 构建泳道的起点 / 终点终端与锚点边。
        /// 仅在图结构合法（唯一 source / 唯一 sink）时渲染；畸形图交给校验器报错，
        /// 这里不添乱。
        /// </summary>
        private void BuildTerminals(ChainGraph graph, bool isConfirm, float centerX,
            Dictionary<string, Vector2> layout, float contentOffset)
        {
            var sources = graph.FindSources();
            var sinks = graph.FindSinks();

            // ---- 起点终端 + 锚点边 ----
            if (sources.Count == 1)
            {
                var kind = isConfirm ? TerminalKind.ConfirmStart : TerminalKind.LineStart;
                var startView = AddTerminalView(kind, isConfirm, centerX,
                    ChainAutoLayout.StartY + contentOffset - TerminalReserved);

                var first = GetNodeView(isConfirm, sources[0].Id);
                if (startView?.OutputPort != null && first?.InputPort != null)
                {
                    var anchor = startView.OutputPort.ConnectTo(first.InputPort);
                    anchor.capabilities &= ~Capabilities.Deletable; // 锚点边不可删（删了链就"无头"）
                    AddElement(anchor);
                }
            }

            // ---- 终点终端 + 锚点边 ----
            if (sinks.Count == 1)
            {
                // 用布局结果推末端 Y（此时命令节点刚定位完，取最大 y + 节点高 + 间距）
                float maxBottom = ChainAutoLayout.StartY + contentOffset;
                foreach (var p in layout.Values)
                    if (p.y + 66f > maxBottom) maxBottom = p.y + 66f;

                var kind = isConfirm ? TerminalKind.ChainEnd : TerminalKind.WaitConfirm;
                var endView = AddTerminalView(kind, isConfirm, centerX,
                    maxBottom + ChainAutoLayout.VerticalGap);

                var last = GetNodeView(isConfirm, sinks[0].Id);
                if (endView?.InputPort != null && last?.OutputPort != null)
                {
                    var anchor = last.OutputPort.ConnectTo(endView.InputPort);
                    anchor.capabilities &= ~Capabilities.Deletable;
                    AddElement(anchor);
                }
            }
        }

        /// <summary>起点终端占位高度（终端 30 + 间距 14），BuildLane 用它给整条链下移让位。</summary>
        private const float TerminalReserved = 44f;

        /// <summary>创建一个纯视图终端（不写入任何 ChainGraph）。</summary>
        private TerminalNodeView AddTerminalView(TerminalKind kind, bool isConfirm,
            float centerX, float y)
        {
            bool isStart = kind == TerminalKind.LineStart || kind == TerminalKind.ConfirmStart;
            string id = "__terminal_" + kind + "__"; // 每种形态独立 ID，位置持久化互不覆盖

            var data = new ChainGraphNode(id, isStart ? ChainGraphNodeKind.Start : ChainGraphNodeKind.End);
            var view = new TerminalNodeView(data, kind, isConfirm);
            view.SetPosition(new Rect(centerX - 75f, y, 150f, 30f));
            AddElement(view);
            // 记入视图表仅供位置持久化与选中高亮；其 ID 不在数据图中，边同步天然忽略
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

        private void BuildLaneLabels()
        {
            AddLaneLabel("进入段 · 进入本行时自动执行",
                "vn-lane-entry", ChainAutoLayout.EntryLaneX);

            // 出口段标签只在出口链非空时显示——空泳道配标签会误导"这里有东西可编辑"
            if (ConfirmGraph != null && ConfirmGraph.NodeCount > 0)
            {
                AddLaneLabel("出口段 · 玩家点击后执行",
                    "vn-lane-confirm", ChainAutoLayout.ConfirmLaneX);
            }
        }

        private void AddLaneLabel(string text, string styleClass, float centerX)
        {
            var label = new Label(text);
            label.AddToClassList("vn-lane-label");
            label.AddToClassList(styleClass);
            label.pickingMode = PickingMode.Ignore;
            label.style.left = centerX - 100f;
            label.style.top = 10f;

            contentViewContainer.Add(label);
            _decorations.Add(label);
        }

        private void UpdateDecorationPositions()
        {
            // 泳道标签固定在内容坐标系，随画布平移缩放——无需额外处理
        }

        // ---------------- 连线规则 ----------------

        /// <summary>
        /// 端口兼容性：只允许"输出 → 输入"、不同节点、且不跨泳道。
        ///
        /// <b>跨泳道禁止</b>是必要的：进入段与出口段是两条独立的命令链，
        /// 分别序列化为 <c>Command</c> 与 <c>@Confirm:</c> 两段文本。
        /// 若允许跨接，图就无法拆回两段文本。
        /// 两段之间唯一的连接是「等待确认 → 出口开始」的点击关系，由结构固定表达。
        /// </summary>
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
                if (portView.IsConfirmChain != startView.IsConfirmChain) return; // 禁止跨泳道

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

            // 拦截模板影子节点的删除——应走"提升"确认流程而非直接移除
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
            }
            else if (moved)
            {
                OnGraphChanged?.Invoke(); // 位置变更也算脏（需写 sidecar）
            }

            return change;
        }

        /// <summary>
        /// 把视图侧的连线/删除变更同步回 <see cref="ChainGraph"/> 数据模型。
        /// 数据模型是保存链路的输入，必须与视图一致。
        /// </summary>
        private void SyncGraphDataFromView(GraphViewChange change)
        {
            // 重建两段图的边集：直接从当前视图的全部 Edge 反推，
            // 比逐条增删更不易出错（图规模是行级，全量重建开销可忽略）
            RebuildEdgesFromView(EntryGraph, isConfirm: false);
            RebuildEdgesFromView(ConfirmGraph, isConfirm: true);
        }

        private void RebuildEdgesFromView(ChainGraph graph, bool isConfirm)
        {
            var rebuilt = new ChainGraph();
            foreach (var node in graph.Nodes)
            {
                // 视图中已被删除的节点不再纳入
                if (GetNodeView(isConfirm, node.Id) == null) continue;
                rebuilt.AddNode(node);
            }

            edges.ForEach(edge =>
            {
                var from = edge.output?.node as VNNodeViewBase;
                var to = edge.input?.node as VNNodeViewBase;
                if (from == null || to == null) return;
                if (from.IsConfirmChain != isConfirm || to.IsConfirmChain != isConfirm) return;
                if (from.Data == null || to.Data == null) return;

                rebuilt.AddEdge(from.Data.Id, to.Data.Id);
            });

            if (isConfirm) ConfirmGraph = rebuilt;
            else EntryGraph = rebuilt;
        }

        // ---------------- 校验状态可视化 ----------------

        /// <summary>把校验结果映射到节点视觉状态（标红 / 高亮）。</summary>
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

        /// <summary>在指定画布坐标处创建一个命令节点（命令面板拖拽 / 右键菜单调用）。</summary>
        public CommandNodeView CreateCommandNode(string commandName, string args,
            Vector2 canvasPosition, bool isConfirm)
        {
            var graph = isConfirm ? ConfirmGraph : EntryGraph;
            string id = GenerateNodeId(graph, commandName);

            var data = graph.AddNode(id, ChainGraphNodeKind.Command, commandName, args ?? "");
            var view = new CommandNodeView(data, isConfirm);
            view.SetPosition(new Rect(canvasPosition, new Vector2(ChainAutoLayout.NodeWidth, 0f)));

            AddElement(view);
            _nodeViews[NodeViewKey(isConfirm, id)] = view;

            OnGraphChanged?.Invoke();
            return view;
        }

        /// <summary>创建一对 FORK / JOIN 胶囊（并行编排的入口）。</summary>
        public void CreateForkJoinPair(Vector2 canvasPosition, bool isConfirm)
        {
            var graph = isConfirm ? ConfirmGraph : EntryGraph;

            string forkId = GenerateNodeId(graph, "fork");
            string joinId = GenerateNodeId(graph, "join");

            var forkData = graph.AddNode(forkId, ChainGraphNodeKind.Fork);
            var joinData = graph.AddNode(joinId, ChainGraphNodeKind.Join);

            var forkView = new ForkJoinNodeView(forkData, isConfirm);
            var joinView = new ForkJoinNodeView(joinData, isConfirm);

            forkView.SetPosition(new Rect(canvasPosition, new Vector2(130f, 0f)));
            joinView.SetPosition(new Rect(
                canvasPosition + new Vector2(0f, 190f), new Vector2(130f, 0f)));

            AddElement(forkView);
            AddElement(joinView);
            _nodeViews[NodeViewKey(isConfirm, forkId)] = forkView;
            _nodeViews[NodeViewKey(isConfirm, joinId)] = joinView;

            OnGraphChanged?.Invoke();
        }

        /// <summary>重新自动布局（工具栏"整理布局"按钮）。</summary>
        public void RelayoutAll()
        {
            ApplyLayout(EntryGraph, isConfirm: false, centerX: ChainAutoLayout.EntryLaneX);
            ApplyLayout(ConfirmGraph, isConfirm: true, centerX: ChainAutoLayout.ConfirmLaneX);
            OnGraphChanged?.Invoke();
        }

        private void ApplyLayout(ChainGraph graph, bool isConfirm, float centerX)
        {
            var layout = ChainAutoLayout.Layout(graph, centerX);
            foreach (var pair in layout)
            {
                var view = GetNodeView(isConfirm, pair.Key);
                if (view == null) continue;
                var rect = view.GetPosition();
                view.SetPosition(new Rect(pair.Value, rect.size));
            }
        }

        /// <summary>收集当前全部节点位置（写 sidecar 用）。</summary>
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

        /// <summary>位置持久化的键：与 <see cref="AstToGraph"/> 生成的节点身份一致。</summary>
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

        /// <summary>画布右键菜单。</summary>
        public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            Vector2 canvasPos = contentViewContainer.WorldToLocal(evt.mousePosition);
            bool isConfirm = canvasPos.x >
                             (ChainAutoLayout.EntryLaneX + ChainAutoLayout.ConfirmLaneX) / 2f;

            string laneName = isConfirm ? "出口段" : "进入段";

            evt.menu.AppendAction($"添加 FORK / JOIN 并行组（{laneName}）",
                _ => CreateForkJoinPair(canvasPos, isConfirm));
            evt.menu.AppendSeparator();
            evt.menu.AppendAction("整理布局", _ => RelayoutAll());
            evt.menu.AppendAction("聚焦全部节点", _ => FrameAll());

            base.BuildContextualMenu(evt);
        }
    }
}
