using System;
using System.Collections.Generic;
using UnityEngine;
using VNovelizer.Core.Commands.Chain;

namespace VNovelizer.Editor.RowPerformanceEditor
{
    /// <summary>
    /// R13（2026-09-10）：节点位置的"分层匹配"恢复器——把旧位置（savedPositions，
    /// key 为 <c>entry/节点ID</c> / <c>confirm/节点ID</c> 的 PositionKey 格式）匹配到
    /// 新图节点上，实现"用户布局持久化 + 增量摆放"。
    ///
    /// <para>
    /// <b>三层匹配</b>（对应原始设计决策 s2a 与 R13 确认）：
    /// <list type="number">
    /// <item><b>ID 精确匹配</b>：新节点 ID（"{DFS序号}:{命令名}"）直接命中旧 key——
    ///       链尾追加等未变结构全部恢复；Fork/Join/Choice 用独立计数器，
    ///       链中插命令不改变其 ID，同样天然命中。</item>
    /// <item><b>签名对齐</b>：剩余命令节点按命令名（忽略大小写）与剩余旧位置
    ///       贪心对齐——链中插入后序号位移的节点据此找回原位置。
    ///       （旧 key 只含命令名不含参数，故按命令名对齐；同名错配仅是位置
    ///       继承，非破坏性，可点"整理布局"修复。）</item>
    /// <item><b>插位摆放</b>：再剩余的节点按拓扑序插到前驱右下方（
    ///       <see cref="ChainAutoLayout.PlaceAfterPredecessor"/>），不推动旧节点。</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// <b>退化为全量 Layout 的两种情形</b>（返回 null，调用方走首布局）：
    /// ① savedPositions 为空 / 本泳道无任何条目（无缓存）；
    /// ② 精确匹配 + 签名对齐零命中（行内容完全重写，插位无锚点）。
    /// </para>
    /// </summary>
    public static class ChainPositionMatcher
    {
        /// <summary>
        /// 为新图的普通节点（非哨兵）恢复位置。
        /// 返回 null 表示应退化为全量 Layout；否则返回的字典覆盖图中全部普通节点。
        /// </summary>
        public static Dictionary<string, Vector2> Resolve(
            ChainGraph graph, bool isConfirm,
            Dictionary<string, Vector2> savedPositions, float startX, float centerY)
        {
            if (graph == null || savedPositions == null || savedPositions.Count == 0)
                return null;

            string prefix = isConfirm ? "confirm/" : "entry/";

            // 1. 本泳道旧位置：剥前缀 → 旧节点 ID → 位置
            var oldPosById = new Dictionary<string, Vector2>();
            foreach (var pair in savedPositions)
            {
                if (pair.Key == null ||
                    !pair.Key.StartsWith(prefix, StringComparison.Ordinal)) continue;
                string id = pair.Key.Substring(prefix.Length);
                if (!string.IsNullOrEmpty(id) && !oldPosById.ContainsKey(id))
                    oldPosById[id] = pair.Value;
            }
            if (oldPosById.Count == 0) return null;

            var resolved = new Dictionary<string, Vector2>();
            var usedOld = new HashSet<string>();

            // 2a. ID 精确匹配
            foreach (var node in graph.Nodes)
            {
                if (IsSentinel(node)) continue;
                if (oldPosById.TryGetValue(node.Id, out var pos))
                {
                    resolved[node.Id] = pos;
                    usedOld.Add(node.Id);
                }
            }

            // 2b. 签名对齐（仅命令节点；旧 key 只含命令名不含参数，按命令名对齐）。
            //     新节点按 DFS 序号升序、旧位置按序号升序，保证贪心确定性。
            var remainingNew = new List<ChainGraphNode>();
            foreach (var node in graph.Nodes)
                if (!IsSentinel(node) && !resolved.ContainsKey(node.Id) &&
                    node.Kind == ChainGraphNodeKind.Command)
                    remainingNew.Add(node);
            remainingNew.Sort((a, b) => CompareByOrdinal(a.Id, b.Id));

            var remainingOld = new List<KeyValuePair<string, Vector2>>();
            foreach (var pair in oldPosById)
                if (!usedOld.Contains(pair.Key) && IsCommandId(pair.Key))
                    remainingOld.Add(pair);
            remainingOld.Sort((a, b) => CompareByOrdinal(a.Key, b.Key));

            foreach (var node in remainingNew)
            {
                int idx = remainingOld.FindIndex(p =>
                    CommandNameOf(p.Key) == CommandNameOf(node.Id));
                if (idx < 0) continue;
                resolved[node.Id] = remainingOld[idx].Value;
                usedOld.Add(remainingOld[idx].Key);
                remainingOld.RemoveAt(idx);
            }

            // 3. 零命中（行内容完全重写）→ 退化全量 Layout（首布局语义）
            if (resolved.Count == 0) return null;

            // 4. 剩余未匹配节点按拓扑序插位
            PlaceUnmatched(graph, resolved, startX, centerY);

            return resolved;
        }

        /// <summary>剩余未匹配节点按拓扑序（前驱先于后继）插位摆放。</summary>
        private static void PlaceUnmatched(ChainGraph graph,
            Dictionary<string, Vector2> resolved, float startX, float centerY)
        {
            var anchorUsage = new Dictionary<string, int>();
            int fallback = 0;

            foreach (var id in TopologicalOrder(graph))
            {
                if (resolved.ContainsKey(id)) continue;
                var node = graph.GetNode(id);
                if (node == null || IsSentinel(node)) continue;

                if (ChainAutoLayout.PlaceAfterPredecessor(
                    graph, id, resolved, anchorUsage, startX, centerY, out var pos))
                {
                    resolved[id] = pos;
                }
                else
                {
                    // 无锚点（断链孤儿等）：2 列瀑布兜底，至少可读可拖
                    int row = fallback / 2;
                    int col = fallback % 2;
                    resolved[id] = new Vector2(
                        startX + col * 240f,
                        centerY + 180f + row * 90f);
                    fallback++;
                }
            }
        }

        private static bool IsSentinel(ChainGraphNode node)
            => node.Kind == ChainGraphNodeKind.Start || node.Kind == ChainGraphNodeKind.End;

        /// <summary>拓扑序（BFS，防环）。断链孤儿追加末尾。</summary>
        private static List<string> TopologicalOrder(ChainGraph graph)
        {
            var order = new List<string>();
            var visited = new HashSet<string>();
            var queue = new Queue<string>();
            foreach (var s in graph.FindSources()) queue.Enqueue(s.Id);

            while (queue.Count > 0)
            {
                string id = queue.Dequeue();
                if (!visited.Add(id)) continue;
                order.Add(id);
                foreach (var succ in graph.GetSuccessors(id))
                    if (!visited.Contains(succ)) queue.Enqueue(succ);
            }

            foreach (var n in graph.Nodes)
                if (visited.Add(n.Id)) order.Add(n.Id);
            return order;
        }

        /// <summary>是否为命令节点 ID（"{序号}:{命令名}"，冒号前是纯数字）。</summary>
        private static bool IsCommandId(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            int colon = id.IndexOf(':');
            if (colon <= 0) return false;
            for (int i = 0; i < colon; i++)
                if (id[i] < '0' || id[i] > '9') return false;
            return true;
        }

        /// <summary>ID 中的命令名部分（冒号后，小写）；无冒号则整体小写。</summary>
        private static string CommandNameOf(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            int colon = id.IndexOf(':');
            return (colon >= 0 ? id.Substring(colon + 1) : id).ToLowerInvariant();
        }

        private static long ParseLeadingNumber(string id)
        {
            long v = 0;
            if (string.IsNullOrEmpty(id)) return 0;
            foreach (var c in id)
            {
                if (c < '0' || c > '9') break;
                v = v * 10 + (c - '0');
            }
            return v;
        }

        private static int CompareByOrdinal(string a, string b)
            => ParseLeadingNumber(a).CompareTo(ParseLeadingNumber(b));
    }
}
