using System.Collections.Generic;
using UnityEngine;
using VNovelizer.Core.Commands.Chain;

namespace VNovelizer.Editor.RowPerformanceEditor
{
    /// <summary>
    /// 命令链图的自动布局（2026-08-27 重构：执行流从左到右）。
    ///
    /// <para>
    /// <b>方向语义</b>：
    /// </para>
    /// <list type="bullet">
    /// <item>执行深度 → <b>X 轴</b>（向右递增）。命令节点从左到右排列，符合阅读顺序。</item>
    /// <item>并行分支 → <b>Y 轴</b>（纵向均分）。Fork 的多条分支上下展开。</item>
    /// </list>
    /// <para>
    /// <b>双泳道</b>：进入段在上半区（<see cref="EntryLaneY"/>），出口段在下半区（<see cref="ConfirmLaneY"/>），
    /// 两条链各自从左到右流动。
    /// </para>
    /// </summary>
    public static class ChainAutoLayout
    {
        /// <summary>节点默认宽度</summary>
        public const float NodeWidth = 200f;

        /// <summary>节点默认高度（估算值，实际由 UIElements 测量）</summary>
        public const float NodeHeight = 62f;

        /// <summary>同一分支内相邻节点的水平间距（执行方向）</summary>
        public const float HorizontalGap = 60f;

        /// <summary>相邻并行分支的垂直间距</summary>
        public const float BranchGap = 40f;

        /// <summary>Fork/Join 胶囊与相邻分支节点的额外水平留白</summary>
        public const float ForkPadding = 20f;

        /// <summary>进入段泳道的中心 Y（上半区）</summary>
        public const float EntryLaneY = 200f;

        /// <summary>
        /// 出口段泳道的中心 Y（下半区）。
        /// 2026-08-27：620 → 800，与进入段拉开距离——模板展开后节点变高，
        /// 两条泳道过近会导致视觉重叠（用户需求 5）。
        /// </summary>
        public const float ConfirmLaneY = 800f;

        /// <summary>链起始 X</summary>
        public const float StartX = 80f;

        // 节点宽度估算（用于初始布局，实际宽度由 MeasureAndRelayout 修正）
        private const float W_COMMAND = 200f;
        private const float W_COMMAND_NOARGS = 160f;
        private const float W_TERMINAL = 120f;
        private const float W_FORKJOIN = 110f;
        private const float W_CAPSULE = 320f;

        /// <summary>
        /// 计算图中每个节点的坐标（估算版，用于初始布局）。
        /// </summary>
        /// <param name="graph">图数据</param>
        /// <param name="centerY">泳道中心 Y（该链沿此 Y 水平流动）</param>
        public static Dictionary<string, Vector2> Layout(ChainGraph graph, float centerY)
        {
            var positions = new Dictionary<string, Vector2>();
            if (graph == null || graph.NodeCount == 0) return positions;

            var sources = graph.FindSources();
            if (sources.Count != 1)
            {
                FallbackGrid(graph, centerY, positions);
                return positions;
            }

            LayoutChain(graph, sources[0].Id, null, StartX, centerY, positions, 0);
            return positions;
        }

        /// <summary>
        /// 测量并重布局：用实际渲染宽度重新计算节点 X 坐标。
        /// </summary>
        /// <param name="graph">图数据</param>
        /// <param name="centerY">泳道中心 Y</param>
        /// <param name="actualWidths">节点 ID → 实际渲染宽度（由视图层测量提供）</param>
        public static Dictionary<string, Vector2> MeasureAndRelayout(
            ChainGraph graph, float centerY, Dictionary<string, float> actualWidths)
        {
            var positions = new Dictionary<string, Vector2>();
            if (graph == null || graph.NodeCount == 0) return positions;

            var sources = graph.FindSources();
            if (sources.Count != 1)
            {
                FallbackGrid(graph, centerY, positions);
                return positions;
            }

            LayoutChainMeasured(graph, sources[0].Id, null, StartX, centerY,
                positions, 0, actualWidths);
            return positions;
        }

        /// <summary>
        /// 沿链布局（估算宽度版），返回该段结束后的下一个 X。
        /// </summary>
        private static float LayoutChain(ChainGraph graph, string startId, string stopAtId,
            float x, float centerY, Dictionary<string, Vector2> positions, int depth)
        {
            if (depth > 32) return x;

            string current = startId;
            var visited = new HashSet<string>();

            while (!string.IsNullOrEmpty(current) && current != stopAtId && visited.Add(current))
            {
                var node = graph.GetNode(current);
                if (node == null) break;

                if (node.Kind == ChainGraphNodeKind.Fork)
                {
                    string joinId = FindJoin(graph, current);

                    // Fork 节点放在当前 X 位置，垂直居中于其分支区域
                    float forkWidth = NodeWidthEst(node);
                    positions[current] = new Vector2(x, centerY - NodeHeight / 2f);
                    x += forkWidth + ForkPadding + HorizontalGap;

                    var branches = graph.GetSuccessors(current);
                    var heights = new List<float>();
                    foreach (string branch in branches)
                        heights.Add(MeasureBranchHeight(graph, branch, joinId));

                    float totalHeight = 0f;
                    foreach (float h in heights) totalHeight += h;
                    totalHeight += BranchGap * (branches.Count - 1);

                    float cursorY = centerY - totalHeight / 2f;
                    float maxBranchRight = x;

                    for (int i = 0; i < branches.Count; i++)
                    {
                        float branchCenterY = cursorY + heights[i] / 2f;
                        float right = LayoutChain(graph, branches[i], joinId,
                            x, branchCenterY, positions, depth + 1);
                        if (right > maxBranchRight) maxBranchRight = right;

                        cursorY += heights[i] + BranchGap;
                    }

                    x = maxBranchRight + ForkPadding;

                    if (joinId != null)
                    {
                        float joinWidth = NodeWidthEst(graph.GetNode(joinId));
                        positions[joinId] = new Vector2(x, centerY - NodeHeight / 2f);
                        x += joinWidth + HorizontalGap;
                        visited.Add(joinId);

                        var afterJoin = graph.GetSuccessors(joinId);
                        current = afterJoin.Count > 0 ? afterJoin[0] : null;
                    }
                    else
                    {
                        current = null;
                    }
                    continue;
                }

                if (node.Kind == ChainGraphNodeKind.Join) break;

                positions[current] = new Vector2(x, centerY - NodeHeight / 2f);
                x += NodeWidthEst(node) + HorizontalGap;

                var successors = graph.GetSuccessors(current);
                current = successors.Count > 0 ? successors[0] : null;
            }

            return x;
        }

        /// <summary>沿链布局（实际宽度版），返回该段结束后的下一个 X。</summary>
        private static float LayoutChainMeasured(ChainGraph graph, string startId, string stopAtId,
            float x, float centerY, Dictionary<string, Vector2> positions, int depth,
            Dictionary<string, float> actualWidths)
        {
            if (depth > 32) return x;

            string current = startId;
            var visited = new HashSet<string>();

            while (!string.IsNullOrEmpty(current) && current != stopAtId && visited.Add(current))
            {
                var node = graph.GetNode(current);
                if (node == null) break;

                float nodeW = GetMeasuredWidth(current, actualWidths, node);

                if (node.Kind == ChainGraphNodeKind.Fork)
                {
                    string joinId = FindJoin(graph, current);

                    positions[current] = new Vector2(x, centerY - NodeHeight / 2f);
                    x += nodeW + ForkPadding + HorizontalGap;

                    var branches = graph.GetSuccessors(current);
                    var heights = new List<float>();
                    foreach (string branch in branches)
                        heights.Add(MeasureBranchHeight(graph, branch, joinId));

                    float totalHeight = 0f;
                    foreach (float h in heights) totalHeight += h;
                    totalHeight += BranchGap * (branches.Count - 1);

                    float cursorY = centerY - totalHeight / 2f;
                    float maxBranchRight = x;

                    for (int i = 0; i < branches.Count; i++)
                    {
                        float branchCenterY = cursorY + heights[i] / 2f;
                        float right = LayoutChainMeasured(graph, branches[i], joinId,
                            x, branchCenterY, positions, depth + 1, actualWidths);
                        if (right > maxBranchRight) maxBranchRight = right;

                        cursorY += heights[i] + BranchGap;
                    }

                    x = maxBranchRight + ForkPadding;

                    if (joinId != null)
                    {
                        float joinW = GetMeasuredWidth(joinId, actualWidths, graph.GetNode(joinId));
                        positions[joinId] = new Vector2(x, centerY - NodeHeight / 2f);
                        x += joinW + HorizontalGap;
                        visited.Add(joinId);

                        var afterJoin = graph.GetSuccessors(joinId);
                        current = afterJoin.Count > 0 ? afterJoin[0] : null;
                    }
                    else
                    {
                        current = null;
                    }
                    continue;
                }

                if (node.Kind == ChainGraphNodeKind.Join) break;

                positions[current] = new Vector2(x, centerY - NodeHeight / 2f);
                x += nodeW + HorizontalGap;

                var successors = graph.GetSuccessors(current);
                current = successors.Count > 0 ? successors[0] : null;
            }

            return x;
        }

        /// <summary>获取节点的实际测量宽度，fallback 到估算。</summary>
        private static float GetMeasuredWidth(string nodeId,
            Dictionary<string, float> actualWidths, ChainGraphNode node)
        {
            if (actualWidths != null && actualWidths.TryGetValue(nodeId, out float w) && w > 0)
                return w;
            return NodeWidthEst(node);
        }

        /// <summary>
        /// 测量一条分支所需的垂直高度（用于 Fork 分支纵向均分）。
        /// </summary>
        private static float MeasureBranchHeight(ChainGraph graph, string startId, string stopAtId)
        {
            float maxHeight = NodeHeight;
            string current = startId;
            var visited = new HashSet<string>();

            while (!string.IsNullOrEmpty(current) && current != stopAtId && visited.Add(current))
            {
                var node = graph.GetNode(current);
                if (node == null) break;

                if (node.Kind == ChainGraphNodeKind.Fork)
                {
                    string joinId = FindJoin(graph, current);
                    var subBranches = graph.GetSuccessors(current);

                    float sum = 0f;
                    foreach (string sub in subBranches)
                        sum += MeasureBranchHeight(graph, sub, joinId);
                    sum += BranchGap * (subBranches.Count - 1);

                    if (sum > maxHeight) maxHeight = sum;

                    if (joinId == null) break;
                    visited.Add(joinId);
                    var after = graph.GetSuccessors(joinId);
                    current = after.Count > 0 ? after[0] : null;
                    continue;
                }

                if (node.Kind == ChainGraphNodeKind.Join) break;

                var succ = graph.GetSuccessors(current);
                current = succ.Count > 0 ? succ[0] : null;
            }

            return maxHeight;
        }

        /// <summary>沿第一条分支找出配对的 Join。</summary>
        private static string FindJoin(ChainGraph graph, string forkId)
        {
            var branches = graph.GetSuccessors(forkId);
            if (branches.Count == 0) return null;

            string current = branches[0];
            var visited = new HashSet<string>();
            int guard = 0;

            while (!string.IsNullOrEmpty(current) && visited.Add(current) && guard++ < 512)
            {
                var node = graph.GetNode(current);
                if (node == null) return null;
                if (node.Kind == ChainGraphNodeKind.Join) return current;

                var succ = graph.GetSuccessors(current);
                if (succ.Count == 0) return null;
                current = succ[0];
            }

            return null;
        }

        /// <summary>估算节点宽度（用于初始布局）。</summary>
        private static float NodeWidthEst(ChainGraphNode node)
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

        /// <summary>非法图的退化布局：网格排列。</summary>
        private static void FallbackGrid(ChainGraph graph, float centerY,
            Dictionary<string, Vector2> positions)
        {
            int i = 0;
            foreach (var node in graph.Nodes)
            {
                int row = i / 3;
                int col = i % 3;
                positions[node.Id] = new Vector2(
                    StartX + col * (W_COMMAND + HorizontalGap),
                    centerY - NodeHeight + row * (NodeHeight + BranchGap));
                i++;
            }
        }
    }
}
