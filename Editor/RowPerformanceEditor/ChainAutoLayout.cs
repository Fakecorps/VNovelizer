using System.Collections.Generic;
using UnityEngine;
using VNovelizer.Core.Commands.Chain;

namespace VNovelizer.Editor.RowPerformanceEditor
{
    /// <summary>
    /// 命令链图的自动布局：按**执行深度**分层定 Y，按并行分支均分定 X。
    ///
    /// <para>
    /// 布局质量直接决定第一印象——用户打开编辑器看到的第一眼若是节点堆叠或错乱，
    /// 后面的功能再好也会被判定为"不好用"。因此布局遵循三条原则：
    /// </para>
    ///
    /// <list type="number">
    /// <item><b>执行顺序 = 视觉顺序</b>。同一深度的节点在同一水平线上，自上而下即执行流。</item>
    /// <item><b>并行分支左右对称展开</b>，以 Fork 为中轴，视觉上立刻能看出"这几条同时跑"。</item>
    /// <item><b>分支宽度自适应</b>：分支内节点多则该分支占更宽的通道，避免相邻分支挤在一起。</item>
    /// </list>
    /// </summary>
    public static class ChainAutoLayout
    {
        /// <summary>节点默认宽度（用于计算分支通道间距）</summary>
        public const float NodeWidth = 200f;

        /// <summary>同一分支内相邻节点的垂直间距</summary>
        public const float VerticalGap = 34f;

        /// <summary>相邻并行分支的水平间距</summary>
        public const float BranchGap = 56f;

        /// <summary>Fork/Join 胶囊与相邻分支节点的额外垂直留白</summary>
        public const float ForkPadding = 12f;

        /// <summary>进入段泳道的中心 X</summary>
        public const float EntryLaneX = 300f;

        /// <summary>出口段泳道的中心 X（与进入段保持 ≥100px 通道供点击虚线走线）</summary>
        public const float ConfirmLaneX = 860f;

        /// <summary>链起始 Y</summary>
        public const float StartY = 60f;

        /// <summary>
        /// 计算图中每个节点的坐标。
        /// </summary>
        /// <param name="graph">待布局的图（必须是合法 SP 图）</param>
        /// <param name="centerX">泳道中心 X</param>
        /// <returns>节点 ID → 坐标</returns>
        public static Dictionary<string, Vector2> Layout(ChainGraph graph, float centerX)
        {
            var positions = new Dictionary<string, Vector2>();
            if (graph == null || graph.NodeCount == 0) return positions;

            var sources = graph.FindSources();
            if (sources.Count != 1)
            {
                // 非法图（多起点/有环）：退化为网格排列，保证节点不重叠可见
                FallbackGrid(graph, centerX, positions);
                return positions;
            }

            LayoutChain(graph, sources[0].Id, null, centerX, StartY, positions, 0);
            return positions;
        }

        /// <summary>
        /// 沿链布局，返回该段结束后的下一个 Y。
        /// </summary>
        private static float LayoutChain(ChainGraph graph, string startId, string stopAtId,
            float centerX, float y, Dictionary<string, Vector2> positions, int depth)
        {
            if (depth > 32) return y; // 防御畸形图

            string current = startId;
            var visited = new HashSet<string>();

            while (!string.IsNullOrEmpty(current) && current != stopAtId && visited.Add(current))
            {
                var node = graph.GetNode(current);
                if (node == null) break;

                if (node.Kind == ChainGraphNodeKind.Fork)
                {
                    string joinId = FindJoin(graph, current);

                    positions[current] = new Vector2(centerX - 65f, y);
                    y += NodeHeight(node) + ForkPadding + VerticalGap;

                    // 先测量每条分支的宽度需求，再据此分配水平通道
                    var branches = graph.GetSuccessors(current);
                    var widths = new List<float>();
                    foreach (string branch in branches)
                        widths.Add(MeasureBranchWidth(graph, branch, joinId));

                    float totalWidth = 0f;
                    foreach (float w in widths) totalWidth += w;
                    totalWidth += BranchGap * (branches.Count - 1);

                    float cursorX = centerX - totalWidth / 2f;
                    float maxBranchBottom = y;

                    for (int i = 0; i < branches.Count; i++)
                    {
                        float branchCenterX = cursorX + widths[i] / 2f;
                        float bottom = LayoutChain(graph, branches[i], joinId,
                            branchCenterX, y, positions, depth + 1);
                        if (bottom > maxBranchBottom) maxBranchBottom = bottom;

                        cursorX += widths[i] + BranchGap;
                    }

                    y = maxBranchBottom + ForkPadding;

                    if (joinId != null)
                    {
                        positions[joinId] = new Vector2(centerX - 65f, y);
                        y += NodeHeight(graph.GetNode(joinId)) + VerticalGap;
                        visited.Add(joinId);

                        var afterJoin = graph.GetSuccessors(joinId);
                        current = afterJoin.Count > 0 ? afterJoin[0] : null;
                    }
                    else
                    {
                        current = null;
                    }
                    continue; // Fork 段已处理完毕，不可落入下方的普通节点分支
                }

                if (node.Kind == ChainGraphNodeKind.Join) break; // 由外层 Fork 处理

                positions[current] = new Vector2(centerX - NodeWidth / 2f, y);
                y += NodeHeight(node) + VerticalGap;

                var successors = graph.GetSuccessors(current);
                current = successors.Count > 0 ? successors[0] : null;
            }

            return y;
        }

        /// <summary>
        /// 测量一条分支所需的水平宽度：分支内若有嵌套 Fork，宽度需容纳其全部子分支。
        /// 这使"分支内还有分支"的情形不会挤压相邻分支。
        /// </summary>
        private static float MeasureBranchWidth(ChainGraph graph, string startId, string stopAtId)
        {
            float maxWidth = NodeWidth;
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
                        sum += MeasureBranchWidth(graph, sub, joinId);
                    sum += BranchGap * (subBranches.Count - 1);

                    if (sum > maxWidth) maxWidth = sum;

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

            return maxWidth;
        }

        /// <summary>沿第一条分支找出配对的 Join（与 GraphToAst 同规则）。</summary>
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

        // 节点高度估算（真实高度由 UIElements 布局后才确定，此处用类型近似）
        private const float H_COMMAND = 58f;
        private const float H_COMMAND_NOARGS = 30f;
        private const float H_TERMINAL = 30f;
        private const float H_FORKJOIN = 30f;
        private const float H_CAPSULE = 112f;

        private static float NodeHeight(ChainGraphNode node)
        {
            switch (node.Kind)
            {
                case ChainGraphNodeKind.Fork:
                case ChainGraphNodeKind.Join:
                    return H_FORKJOIN;

                case ChainGraphNodeKind.Start:
                case ChainGraphNodeKind.End:
                    return H_TERMINAL;

                default:
                    return string.IsNullOrWhiteSpace(node.Args) ? H_COMMAND_NOARGS : H_COMMAND;
            }
        }

        /// <summary>模板折叠胶囊的高度（供窗口层布局参考）。</summary>
        public static float CapsuleHeight => H_CAPSULE;

        /// <summary>非法图的退化布局：网格排列，保证全部节点可见不重叠。</summary>
        private static void FallbackGrid(ChainGraph graph, float centerX,
            Dictionary<string, Vector2> positions)
        {
            int i = 0;
            foreach (var node in graph.Nodes)
            {
                int row = i / 3;
                int col = i % 3;
                positions[node.Id] = new Vector2(
                    centerX - NodeWidth + col * (NodeWidth + BranchGap),
                    StartY + row * (H_COMMAND + VerticalGap));
                i++;
            }
        }
    }
}
