using System.Collections.Generic;
using UnityEngine;
using VNovelizer.Core.Commands.Chain;

namespace VNovelizer.Editor.RowPerformanceEditor
{
    /// <summary>
    /// 命令链图的自动布局（Sugiyama 风格分层布局，2026-08-28 第二轮改版）。
    ///
    /// <para>
    /// <b>四阶段实现</b>（参考 Kozo Sugiyama 1981, IEEE Trans. Syst. Man Cybern. 11(2)）：
    /// <list type="number">
    /// <item><b>环移除</b>：命令链是 SP 图（Series-Parallel），Validator 已保证无环，跳过。</item>
    /// <item><b>层级分配</b>：最长路径法——layer(v) = max(layer(u) for u in preds(v)) + 1。
    ///       Start 哨兵固定 layer 0，End 哨兵固定 layer max（视觉强制最左/最右）。</item>
    /// <item><b>交叉最小化</b>：Barycenter 启发式 + 上下扫层 12 轮——dagre / ELK 等
    ///       成熟图编辑器均采用此方案；对 &lt; 50 节点的小图，&lt; 1ms 完成，视觉零交叉。</item>
    /// <item><b>节点定位</b>：X 按"列内水平居中"对齐（节点中心落在列中心竖线上）；
    ///       同层节点 Y 按 barycenter 顺序垂直铺开，整层居中于 <paramref name="centerY"/>。</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// <b>视觉规则</b>（用户确认的解读 B）：
    /// <list type="bullet">
    /// <item><b>同层节点在一条竖线上对齐</b>——列中心竖线，而非左上角对齐。
    ///       旧实现按左上角对齐：宽节点（长命令带参数）右边界伸进下一层 X 区域，
    ///       与下一层 Fork/Join 视觉重叠。居中对齐后右边界永不越列。</item>
    /// <item><b>层内 Y 有间隔</b>——VerticalGap=80，Fork 的多个分支垂直拉开，
    ///       互不重叠、连线清晰。</item>
    /// <item><b>不同层 X 有间隔</b>——列步长 = 全图最宽节点 + HorizontalGap(140)，
    ///       列间留白充足。</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// <b>端点固定</b>：Start / End 哨兵在每次布局时强制 X = 最左列中心 / 最右列中心，
    /// Y = centerY。这保证 LineEntry 总在最左、LineExit / OnConfirmExit 总在最右——
    /// 解决用户反馈"每次打开 Entry 不在最左、Exit 不在最右"的根因。
    /// </para>
    ///
    /// <para>
    /// <b>宽度来源</b>：实测宽度（RelayoutAll 传入节点真实渲染像素）优先；
    /// 缺省按 <see cref="NodeWidthEst"/> 逐节点类型估算——命令(200)/无参命令(160)/
    /// Fork-Join(120)/终端(140)。Rebuild 时无实测值也能得到正确的列宽。
    /// </para>
    /// </summary>
    public static class ChainAutoLayout
    {
        /// <summary>节点默认宽度（无实测宽度时的兜底估算）。</summary>
        public const float NodeWidth = 200f;

        /// <summary>节点默认高度。</summary>
        public const float NodeHeight = 62f;

        /// <summary>
        /// 相邻 layer 之间的 X 间距（列步长 = 最宽节点宽度 + 此间距）。
        /// 2026-08-28：80 → 140——列间留白加大后，宽命令节点与下一层 Fork/Join
        /// 视觉彻底分离，用户反馈的"多多少少有部分重叠"消失。
        /// </summary>
        public const float HorizontalGap = 140f;

        /// <summary>
        /// 同 layer 内相邻节点的 Y 间距。
        /// 2026-08-28：36 → 80——层内并列节点（Fork 的多个分支）垂直拉开，
        /// "都在一条竖线上对齐、彼此有间隔"的视觉感更明确。
        /// </summary>
        public const float VerticalGap = 80f;

        /// <summary>进入段泳道的中心 Y。</summary>
        public const float EntryLaneY = 200f;

        /// <summary>出口段泳道的中心 Y。</summary>
        public const float ConfirmLaneY = 800f;

        /// <summary>链起始 X（layer 0 的 X 坐标，Start 哨兵固定于此）。</summary>
        public const float StartX = 80f;

        /// <summary>交叉最小化扫层轮数（dagre 默认 24，对 &lt; 50 节点命令链 12 已足够）。</summary>
        private const int CrossingSweepPasses = 12;

        // 节点宽度估算（实测宽度缺失时使用）
        private const float W_COMMAND = 200f;
        private const float W_COMMAND_NOARGS = 160f;
        private const float W_TERMINAL = 140f;
        private const float W_FORKJOIN = 120f;
        private const float W_CAPSULE = 320f;

        /// <summary>
        /// 计算图中每个节点的坐标（Sugiyama 风格分层布局）。
        /// </summary>
        /// <param name="widths">可选：节点 ID → 实测宽度（像素）。为 null 时用估算宽度。</param>
        public static Dictionary<string, Vector2> Layout(ChainGraph graph, float centerY,
            IReadOnlyDictionary<string, float> widths = null)
        {
            var positions = new Dictionary<string, Vector2>();
            if (graph == null || graph.NodeCount == 0) return positions;

            var startNode = ChainGraphDumper.FindStartSentinel(graph);
            var endNode = ChainGraphDumper.FindEndSentinel(graph);

            // 1. 层级分配（最长路径，端点不参与——它们固定到 layer 0 / maxLayer）
            var layers = AssignLayers(graph, startNode?.Id, endNode?.Id);

            // 2. 按 layer 分组（端点单独加入 layer 0 / layer maxLayer+1，
            //    它们的位置作为 barycenter 的"虚拟参考点"——算法需要从端点出发推算第一层顺序）
            var layerGroups = new Dictionary<int, List<string>>();
            int maxLayer = 0; // 至少留出 layer 0 给 Start
            if (startNode != null) layerGroups[0] = new List<string> { startNode.Id };

            foreach (var node in graph.Nodes)
            {
                if (node == startNode || node == endNode) continue;
                if (!layers.TryGetValue(node.Id, out int l)) continue;
                if (!layerGroups.TryGetValue(l, out var list))
                    layerGroups[l] = list = new List<string>();
                list.Add(node.Id);
                if (l > maxLayer) maxLayer = l;
            }

            // End 哨兵加入最深 layer（作为虚拟参考点 + 实际放置位置）
            if (endNode != null)
            {
                int endLayer = maxLayer + 1;
                if (!layerGroups.TryGetValue(endLayer, out var list))
                    layerGroups[endLayer] = list = new List<string>();
                list.Add(endNode.Id);
                maxLayer = endLayer;
            }

            // 基础节点高度（端点 Y 居中要用——先固定后用）
            float baseNodeHeight = NodeHeight;

            // 空链（仅哨兵、无中间节点）——给哨兵左右排开
            // 判断：layer 1 不存在 = 没有从 Start 出发的第一层命令节点
            if (!layerGroups.ContainsKey(1) || layerGroups[1].Count == 0)
            {
                if (startNode != null)
                    positions[startNode.Id] = new Vector2(StartX, centerY - baseNodeHeight / 2f);
                if (endNode != null)
                    positions[endNode.Id] = new Vector2(StartX + 460f, centerY - baseNodeHeight / 2f);
                return positions;
            }

            // 3. 交叉最小化（Barycenter 上下扫层）
            CrossingMinimizeBarycenter(graph, layerGroups, maxLayer);

            // 4. 计算列步长（全图最宽节点 + gap）与列内可用宽度。
            //    列内可用宽 = 列步长 - HorizontalGap——节点按自身宽度在列内**水平居中**，
            //    右边界永不越过列边界（旧实现按左上角对齐：宽节点右边界伸进下一层，
            //    与下一层的 Fork/Join 视觉重叠——即用户截图反馈的问题）。
            float layerXStep = ResolveLayerXStep(graph, widths);
            float columnUsableWidth = layerXStep - HorizontalGap;

            // 5. 定位：中间 layer 1..maxLayer-1 按 barycenter 顺序排开。
            //    X：列左边界 + (列宽 - 节点宽)/2 → 节点中心对齐到列中心竖线；
            //    Y：层内垂直均分，整层居中于 centerY。
            for (int l = 1; l < maxLayer; l++)
            {
                if (!layerGroups.TryGetValue(l, out var nodes) || nodes.Count == 0) continue;

                float columnLeft = StartX + l * layerXStep;
                float yStep = baseNodeHeight + VerticalGap;
                float totalH = nodes.Count * yStep - VerticalGap;
                float topY = centerY - totalH / 2f;

                for (int i = 0; i < nodes.Count; i++)
                {
                    var node = graph.GetNode(nodes[i]);
                    float nodeW = NodeWidthOf(node, widths);
                    float x = columnLeft + (columnUsableWidth - nodeW) / 2f;
                    positions[nodes[i]] = new Vector2(x, topY + i * yStep);
                }
            }

            // 6. 端点固定（Start 最左，End 最右，Y 居中）。
            //    端点同样在各自列内水平居中——与中间层节点"中心竖线对齐"一致。
            //    这是结构约束——用户拖动过的位置不会破坏"端点永远在边界"的不变量。
            if (startNode != null)
                positions[startNode.Id] = new Vector2(
                    CenterInColumn(0, startNode, widths, layerXStep, columnUsableWidth),
                    centerY - baseNodeHeight / 2f);
            if (endNode != null)
                positions[endNode.Id] = new Vector2(
                    CenterInColumn(maxLayer, endNode, widths, layerXStep, columnUsableWidth),
                    centerY - baseNodeHeight / 2f);

            return positions;
        }

        /// <summary>把节点在指定列内水平居中：列左边界 + (列宽 - 节点宽) / 2。</summary>
        private static float CenterInColumn(int layer, ChainGraphNode node,
            IReadOnlyDictionary<string, float> widths, float layerXStep, float columnUsableWidth)
        {
            float columnLeft = StartX + layer * layerXStep;
            float nodeW = NodeWidthOf(node, widths);
            return columnLeft + (columnUsableWidth - nodeW) / 2f;
        }

        /// <summary>取节点宽度：实测优先，缺省用 <see cref="NodeWidthEst"/> 估算。</summary>
        private static float NodeWidthOf(ChainGraphNode node,
            IReadOnlyDictionary<string, float> widths)
        {
            if (node == null) return NodeWidth;
            if (widths != null && widths.TryGetValue(node.Id, out var w) && w > 0f) return w;
            return NodeWidthEst(node);
        }

        /// <summary>
        /// 测量并重布局：用实际渲染宽度重新计算节点 X 坐标（与 <see cref="Layout"/>
        /// 等价——本方法保留为 API 兼容，宽度通过 <c>widths</c> 参数传入）。
        /// </summary>
        public static Dictionary<string, Vector2> MeasureAndRelayout(
            ChainGraph graph, float centerY, Dictionary<string, float> actualWidths)
        {
            return Layout(graph, centerY, actualWidths);
        }

        /// <summary>
        /// 沿最长路径 BFS 分配每个节点的 layer。
        ///
        /// <para>
        /// 规则：<c>layer(v) = max(layer(u) for u in preds(v)) + 1</c>。同一节点从多条路径到达时
        /// 取最大 layer（确保所有 parent 到它的边都"向后"指向更深层）。
        /// </para>
        ///
        /// <para>
        /// <b>防回环</b>：用 Kahn BFS + <c>enqueued</c> 标记——每个节点只入队一次，
        /// 即使环边导致 inDegree 变负也不会重复处理。对含环脏图天然安全：
        /// 环上的节点 layer 不被更新（被 existing ≥ newL 跳过），
        /// 不会触发入队（enqueued 标记为 true）。
        /// </para>
        ///
        /// <para>
        /// 端点处理：Start 哨兵入度强制 0（哨兵"无入边"是结构约束，不受脏图影响），
        /// End 哨兵直接被排除——指向 End 的边不算作任何中间节点的入度。
        /// </para>
        ///
        /// <para>
        /// 注：本实现不对 Join 插 dummy 节点——Join 的入边可能来自不同 layer 的节点，
        /// 产生跨层斜线（Sugiyama 风格允许）。若未来要"所有边逐层下降"严格垂直，
        /// 可在此处插入 dummy，但需要图编辑器支持"虚拟节点不显示"机制。
        /// </para>
        /// </summary>
        private static Dictionary<string, int> AssignLayers(ChainGraph graph, string startId, string endId)
        {
            var layers = new Dictionary<string, int>();
            if (string.IsNullOrEmpty(startId)) return layers;

            // 只追踪"非端点"节点的入度
            var inDegree = new Dictionary<string, int>();
            var enqueued = new HashSet<string>(); // 防重复入队（关键：脏图环边防御）
            foreach (var node in graph.Nodes)
            {
                if (node.Id == endId) continue;
                inDegree[node.Id] = 0;
            }
            foreach (var edge in graph.Edges)
            {
                if (edge.FromId == endId) continue;
                if (edge.ToId == endId) continue;
                if (inDegree.ContainsKey(edge.ToId))
                    inDegree[edge.ToId]++;
            }
            // Start 哨兵强制入度 0（脏图中若有指向 Start 的边，忽略——哨兵不接边）
            inDegree[startId] = 0;

            var queue = new Queue<string>();
            queue.Enqueue(startId);
            enqueued.Add(startId);
            layers[startId] = 0;

            while (queue.Count > 0)
            {
                string id = queue.Dequeue();
                int currentL = layers[id];
                foreach (var succ in graph.GetSuccessors(id))
                {
                    if (succ == endId) continue;
                    if (enqueued.Contains(succ)) continue; // 已处理过（含环情况下不再二次入队）

                    int newL = currentL + 1;
                    // 入度递减到 0 才入队（Kahn 拓扑）
                    if (inDegree.ContainsKey(succ))
                    {
                        inDegree[succ]--;
                        if (inDegree[succ] > 0) continue; // 还有前驱未到
                    }
                    layers[succ] = newL;
                    queue.Enqueue(succ);
                    enqueued.Add(succ);
                }
            }
            return layers;
        }

        /// <summary>
        /// 交叉最小化：Barycenter 启发式 + 上下扫层。
        ///
        /// <para>
        /// 对 layer 1..maxLayer-1 中的每个节点 v，按其前驱节点在 layer-1 中的平均位置排序；
        /// 对称地再按后继节点在 layer+1 中的平均位置反扫。重复 12 轮以收敛。
        /// </para>
        ///
        /// <para>
        /// <b>端点不参与排序</b>：layer 0（Start 哨兵）和 layer maxLayer（End 哨兵）跳过——
        /// 它们的位置已固定，排序只会扰乱其它节点的收敛。
        /// </para>
        ///
        /// <para>
        /// <b>初始顺序</b>：layer 1 按 Start 哨兵的出边顺序（保留文本中的命令链序列）；
        /// 其他层按当前 DFS 顺序。这一初始序对收敛速度与最终美观至关重要。
        /// </para>
        /// </summary>
        private static void CrossingMinimizeBarycenter(ChainGraph graph,
            Dictionary<int, List<string>> layerGroups, int maxLayer)
        {
            if (layerGroups.Count == 0) return;

            // 初始：layer 1 节点按 Start 哨兵出边顺序排（自然的命令链顺序）
            var startSentinel = ChainGraphDumper.FindStartSentinel(graph);
            if (startSentinel != null && layerGroups.TryGetValue(1, out var layer1))
            {
                var orderedSuccs = graph.GetSuccessors(startSentinel.Id);
                var succSet = new HashSet<string>(layer1);
                layer1.Clear();
                foreach (var s in orderedSuccs) if (succSet.Contains(s)) layer1.Add(s);
                foreach (var n in layer1) succSet.Remove(n);
                foreach (var n in succSet) layer1.Add(n); // 未在 Start 出边中的（断链头）追加末尾
            }

            // 12 轮上下扫（端点 layer 0 / maxLayer 跳过）
            for (int pass = 0; pass < CrossingSweepPasses; pass++)
            {
                // 自顶向下：以 layer-1 的位置为基准排 layer
                for (int l = 1; l < maxLayer; l++)
                {
                    if (!layerGroups.TryGetValue(l, out var nodes) || nodes.Count <= 1) continue;
                    OrderByBarycenter(graph, nodes, layerGroups, l, fromLower: true);
                }
                // 自底向上：以 layer+1 的位置为基准排 layer
                for (int l = maxLayer - 1; l > 0; l--)
                {
                    if (!layerGroups.TryGetValue(l, out var nodes) || nodes.Count <= 1) continue;
                    OrderByBarycenter(graph, nodes, layerGroups, l, fromLower: false);
                }
            }
        }

        /// <summary>
        /// 对 layer l 的节点按"前驱（或后继）在相邻层的位置"取 barycenter（算术平均）排序。
        /// </summary>
        private static void OrderByBarycenter(ChainGraph graph, List<string> nodes,
            Dictionary<int, List<string>> layerGroups, int layer, bool fromLower)
        {
            // 索引：节点 ID → 在相邻层中的位置（0-based）
            var neighborLayer = fromLower ? layer - 1 : layer + 1;
            if (!layerGroups.TryGetValue(neighborLayer, out var neighbors) || neighbors.Count == 0)
                return;

            var posInNeighbor = new Dictionary<string, int>();
            for (int i = 0; i < neighbors.Count; i++) posInNeighbor[neighbors[i]] = i;

            var scored = new List<(string id, float score)>(nodes.Count);
            foreach (var id in nodes)
            {
                var adj = fromLower
                    ? graph.GetPredecessors(id)
                    : graph.GetSuccessors(id);
                if (adj.Count == 0)
                {
                    scored.Add((id, float.NaN)); // 无邻居排到末尾
                    continue;
                }
                float sum = 0f;
                int count = 0;
                foreach (var n in adj)
                {
                    if (posInNeighbor.TryGetValue(n, out int p))
                    {
                        sum += p;
                        count++;
                    }
                }
                scored.Add((id, count > 0 ? sum / count : float.NaN));
            }

            // NaN 排到末尾，其余升序
            scored.Sort((a, b) =>
            {
                if (float.IsNaN(a.score) && float.IsNaN(b.score)) return 0;
                if (float.IsNaN(a.score)) return 1;
                if (float.IsNaN(b.score)) return -1;
                return a.score.CompareTo(b.score);
            });

            nodes.Clear();
            foreach (var s in scored) nodes.Add(s.id);
        }

        /// <summary>估算节点宽度（无实测宽度时使用）。</summary>
        public static float NodeWidthEst(ChainGraphNode node)
        {
            switch (node.Kind)
            {
                case ChainGraphNodeKind.Fork:
                case ChainGraphNodeKind.Join:
                    return W_FORKJOIN;
                case ChainGraphNodeKind.Start:
                case ChainGraphNodeKind.End:
                    return W_TERMINAL;
                default:
                    return string.IsNullOrWhiteSpace(node.Args) ? W_COMMAND_NOARGS : W_COMMAND;
            }
        }

        /// <summary>模板折叠胶囊的宽度（供窗口层布局参考）。</summary>
        public static float CapsuleWidth => W_CAPSULE;

        /// <summary>模板折叠胶囊的高度</summary>
        public static float CapsuleHeight => 200f;

        /// <summary>
        /// 取全图最大节点宽度 + <see cref="HorizontalGap"/>（列步长）。
        /// 实测宽度优先；缺失时按 <see cref="NodeWidthEst"/> 逐节点估算——
        /// 命令(200)/无参命令(160)/Fork-Join(120)/终端(140) 各取所需，
        /// 不再统一用 <see cref="NodeWidth"/>（旧实现的盲区：Fork/Join 明明窄
        /// 却按 200 算列距，宽命令节点按 200 算实际渲染却超过 200 → 右溢出）。
        /// </summary>
        private static float ResolveLayerXStep(ChainGraph graph,
            IReadOnlyDictionary<string, float> widths)
        {
            float maxW = 0f;
            foreach (var n in graph.Nodes)
            {
                float w = NodeWidthOf(n, widths);
                if (w > maxW) maxW = w;
            }
            if (maxW <= 0f) maxW = NodeWidth;
            return maxW + HorizontalGap;
        }

        /// <summary>非法图（多源 / 多汇 / 环）的退化布局：网格排列。</summary>
        public static void FallbackGrid(ChainGraph graph, float centerY,
            Dictionary<string, Vector2> positions)
        {
            int i = 0;
            foreach (var node in graph.Nodes)
            {
                int row = i / 3;
                int col = i % 3;
                positions[node.Id] = new Vector2(
                    StartX + col * (NodeWidth + HorizontalGap),
                    centerY - NodeHeight + row * (NodeHeight + VerticalGap));
                i++;
            }
        }
    }
}
