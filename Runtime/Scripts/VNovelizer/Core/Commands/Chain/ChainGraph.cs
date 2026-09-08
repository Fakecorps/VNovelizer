using System.Collections.Generic;

namespace VNovelizer.Core.Commands.Chain
{
    /// <summary>图节点的类别。</summary>
    public enum ChainGraphNodeKind
    {
        /// <summary>命令节点（对应 <see cref="CommandNode"/>）</summary>
        Command = 0,

        /// <summary>并行分流（对应 <see cref="ParNode"/> 的开始）</summary>
        Fork,

        /// <summary>并行汇合（对应 <see cref="ParNode"/> 的结束）</summary>
        Join,

        /// <summary>链起点（无入边的哨兵节点，不产生命令）</summary>
        Start,

        /// <summary>链终点（无出边的哨兵节点，不产生命令）</summary>
        End,

        /// <summary>
        /// R11：choice 节点（对应 <see cref="ChoiceNode"/>）。出边带端口语义：
        /// PortIndex ≥ 0 = 第 N 个选项的命令链首节点；PortIndex = <see cref="ChoiceMainPort"/>
        /// = 主链延续（连 End 终端或另一个 choice 节点，表达"单行多 choice 合并展示"）。
        /// </summary>
        Choice,
    }

    /// <summary>Choice 节点主延续边的端口号（连 End 终端或下一个 choice 节点）。</summary>
    public static class ChoicePort
    {
        public const int Main = -1;
    }

    /// <summary>
    /// 图中的一个节点。**与 GraphView 无关的纯数据模型**——
    /// 使 SP 分解、序列化、校验三个组件可脱离 UI 独立开发与测试，
    /// UI 层（<c>CommandNodeView</c> 等）只需持有本类型的引用。
    /// </summary>
    public class ChainGraphNode
    {
        /// <summary>图内唯一 ID（UI 层生成，用于连线引用与错误定位）</summary>
        public string Id;

        public ChainGraphNodeKind Kind;

        /// <summary>命令名（仅 <see cref="ChainGraphNodeKind.Command"/> 有效）</summary>
        public string CommandName;

        /// <summary>原始参数串（仅 Command 有效，与 <see cref="CommandNode.Args"/> 同语义）</summary>
        public string Args;

        /// <summary>
        /// 源文本偏移（Command 列文本内的起始位置）。由 <c>AstToGraph</c> 从
        /// <see cref="CommandNode.Position"/> / <see cref="ParNode.Position"/> 传入，
        /// 是运行时执行状态 ↔ 编辑器图节点的**映射 key**（运行时埋点记录同一文本
        /// 解析出的 Position，编辑器按此字段匹配高亮与双击重播）。
        /// -1 表示无源位置（哨兵/手工创建的节点）。
        /// </summary>
        public int SourcePosition = -1;

        /// <summary>
        /// R11：choice 选项描述列表（仅 <see cref="ChainGraphNodeKind.Choice"/> 有效）。
        /// 索引与出边 PortIndex 一一对应（0..Count-1）。null 表示非 choice 节点。
        /// </summary>
        public List<string> ChoiceTexts;

        public ChainGraphNode(string id, ChainGraphNodeKind kind,
            string commandName = null, string args = null, int sourcePosition = -1)
        {
            Id = id;
            Kind = kind;
            CommandName = commandName;
            Args = args;
            SourcePosition = sourcePosition;
        }

        public override string ToString()
        {
            return Kind == ChainGraphNodeKind.Command
                ? $"{Id}:{CommandName}({Args})"
                : $"{Id}:{Kind}";
        }
    }

    /// <summary>一条有向边（从 <see cref="FromId"/> 的出端口连到 <see cref="ToId"/> 的入端口）。</summary>
    public struct ChainGraphEdge
    {
        public string FromId;
        public string ToId;

        /// <summary>
        /// R11：出端口序号。仅 From 为 <see cref="ChainGraphNodeKind.Choice"/> 时有意义：
        /// ≥ 0 = 第 N 个选项；<see cref="ChoicePort.Main"/> = 主链延续。其余节点恒为 0。
        /// </summary>
        public int PortIndex;

        public ChainGraphEdge(string fromId, string toId, int portIndex = 0)
        {
            FromId = fromId;
            ToId = toId;
            PortIndex = portIndex;
        }

        public override string ToString() =>
            PortIndex == 0 ? $"{FromId} -> {ToId}" : $"{FromId} -[{PortIndex}]-> {ToId}";
    }

    /// <summary>
    /// 一条命令链的图表示（单条链；进入段与出口段各自是一张独立的图）。
    ///
    /// <para>
    /// <b>不变式</b>：合法的图必须是 SP 图（Series-Parallel，串并联图）——
    /// 这是命令链文本能表达的全部结构。校验由 <c>ChainGraphValidator</c> 负责，
    /// 分解由 <c>GraphToAst</c> 负责，本类型只管数据与邻接查询。
    /// </para>
    /// </summary>
    public class ChainGraph
    {
        private readonly Dictionary<string, ChainGraphNode> _nodes = new Dictionary<string, ChainGraphNode>();
        private readonly List<ChainGraphEdge> _edges = new List<ChainGraphEdge>();

        /// <summary>出边邻接表（保持添加顺序——Fork 的分支顺序即序列化后的分支顺序）</summary>
        private readonly Dictionary<string, List<string>> _outgoing = new Dictionary<string, List<string>>();

        /// <summary>入边邻接表</summary>
        private readonly Dictionary<string, List<string>> _incoming = new Dictionary<string, List<string>>();

        public IEnumerable<ChainGraphNode> Nodes => _nodes.Values;
        public IReadOnlyList<ChainGraphEdge> Edges => _edges;
        public int NodeCount => _nodes.Count;

        public ChainGraphNode AddNode(ChainGraphNode node)
        {
            if (node == null || string.IsNullOrEmpty(node.Id)) return null;
            _nodes[node.Id] = node;
            if (!_outgoing.ContainsKey(node.Id)) _outgoing[node.Id] = new List<string>();
            if (!_incoming.ContainsKey(node.Id)) _incoming[node.Id] = new List<string>();
            return node;
        }

        public ChainGraphNode AddNode(string id, ChainGraphNodeKind kind,
            string commandName = null, string args = null, int sourcePosition = -1)
        {
            return AddNode(new ChainGraphNode(id, kind, commandName, args, sourcePosition));
        }

        /// <summary>
        /// 添加边。重复边（同 from/to/port 三元组）会被忽略——
        /// 非 Choice 节点间同两点平行边在 SP 图中无意义；
        /// Choice 节点靠 <paramref name="portIndex"/> 区分不同出边。
        /// </summary>
        public void AddEdge(string fromId, string toId, int portIndex = 0)
        {
            if (string.IsNullOrEmpty(fromId) || string.IsNullOrEmpty(toId)) return;
            if (!_nodes.ContainsKey(fromId) || !_nodes.ContainsKey(toId)) return;

            for (int i = 0; i < _edges.Count; i++)
            {
                if (_edges[i].FromId == fromId && _edges[i].ToId == toId &&
                    _edges[i].PortIndex == portIndex)
                    return;
            }

            _edges.Add(new ChainGraphEdge(fromId, toId, portIndex));
            _outgoing[fromId].Add(toId);
            _incoming[toId].Add(fromId);
        }

        /// <summary>
        /// R11：choice 删除第 <paramref name="removedIndex"/> 个选项后，
        /// 把该节点 PortIndex 大于 <paramref name="removedIndex"/> 的出边前移一位
        /// （与 ChoiceTexts 的索引前移保持对齐）。
        /// </summary>
        public void ShiftChoicePorts(string choiceId, int removedIndex)
        {
            if (string.IsNullOrEmpty(choiceId) || removedIndex < 0) return;
            if (!_nodes.ContainsKey(choiceId)) return;

            var toShift = new List<ChainGraphEdge>();
            for (int i = _edges.Count - 1; i >= 0; i--)
            {
                var e = _edges[i];
                if (e.FromId != choiceId || e.PortIndex <= removedIndex) continue;

                toShift.Add(e);
                _edges.RemoveAt(i);
                if (_outgoing.TryGetValue(choiceId, out var outs)) outs.Remove(e.ToId);
                if (_incoming.TryGetValue(e.ToId, out var ins)) ins.Remove(choiceId);
            }

            // 按新索引重新添加（AddEdge 维护邻接表；注意先删后加避免重复边误判）
            foreach (var e in toShift)
                AddEdge(e.FromId, e.ToId, e.PortIndex - 1);
        }

        /// <summary>
        /// 移除节点及其全部出入边（R11：删除 choice 选项时级联删除整条选项链）。
        /// 不存在则忽略。
        /// </summary>
        public void RemoveNode(string id)
        {
            if (string.IsNullOrEmpty(id) || !_nodes.ContainsKey(id)) return;

            for (int i = _edges.Count - 1; i >= 0; i--)
            {
                if (_edges[i].FromId == id || _edges[i].ToId == id)
                {
                    var e = _edges[i];
                    _edges.RemoveAt(i);
                    if (_outgoing.TryGetValue(e.FromId, out var outs)) outs.Remove(e.ToId);
                    if (_incoming.TryGetValue(e.ToId, out var ins)) ins.Remove(e.FromId);
                }
            }

            _outgoing.Remove(id);
            _incoming.Remove(id);
            _nodes.Remove(id);
        }

        /// <summary>移除边（2026-08-27：支持链中插入操作）。不存在则忽略。</summary>
        public void RemoveEdge(string fromId, string toId, int portIndex = 0)
        {
            if (string.IsNullOrEmpty(fromId) || string.IsNullOrEmpty(toId)) return;
            if (!_outgoing.TryGetValue(fromId, out var outs)) return;

            // 找到并移除 _edges 中对应记录（R11：按三元组精确匹配，避免误删 choice 的其他端口边）
            for (int i = _edges.Count - 1; i >= 0; i--)
            {
                if (_edges[i].FromId == fromId && _edges[i].ToId == toId &&
                    _edges[i].PortIndex == portIndex)
                {
                    _edges.RemoveAt(i);
                    break;
                }
            }
            outs.Remove(toId);
            if (_incoming.TryGetValue(toId, out var ins)) ins.Remove(fromId);
        }

        public ChainGraphNode GetNode(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            _nodes.TryGetValue(id, out var node);
            return node;
        }

        /// <summary>后继节点 ID 列表（顺序 = 添加顺序）</summary>
        public List<string> GetSuccessors(string id)
        {
            if (id != null && _outgoing.TryGetValue(id, out var list)) return list;
            return new List<string>();
        }

        /// <summary>
        /// R11：按端口号升序返回后继（主延续边 PortIndex=-1 排最前）。
        /// 用于 Choice 节点选项链的稳定遍历（用户连线顺序无关）。
        /// </summary>
        public List<KeyValuePair<int, string>> GetOrderedSuccessors(string id)
        {
            var result = new List<KeyValuePair<int, string>>();
            if (string.IsNullOrEmpty(id)) return result;

            for (int i = 0; i < _edges.Count; i++)
            {
                var e = _edges[i];
                if (e.FromId == id)
                    result.Add(new KeyValuePair<int, string>(e.PortIndex, e.ToId));
            }
            result.Sort((a, b) => a.Key.CompareTo(b.Key));
            return result;
        }

        /// <summary>前驱节点 ID 列表</summary>
        public List<string> GetPredecessors(string id)
        {
            if (id != null && _incoming.TryGetValue(id, out var list)) return list;
            return new List<string>();
        }

        public int OutDegree(string id) => GetSuccessors(id).Count;
        public int InDegree(string id) => GetPredecessors(id).Count;

        /// <summary>入度为 0 的节点（合法图恰有一个）</summary>
        public List<ChainGraphNode> FindSources()
        {
            var result = new List<ChainGraphNode>();
            foreach (var node in _nodes.Values)
                if (InDegree(node.Id) == 0) result.Add(node);
            return result;
        }

        /// <summary>出度为 0 的节点（合法图恰有一个）</summary>
        public List<ChainGraphNode> FindSinks()
        {
            var result = new List<ChainGraphNode>();
            foreach (var node in _nodes.Values)
                if (OutDegree(node.Id) == 0) result.Add(node);
            return result;
        }
    }
}
