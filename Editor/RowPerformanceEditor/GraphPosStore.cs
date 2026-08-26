using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace VNovelizer.Editor.RowPerformanceEditor
{
    /// <summary>
    /// 节点位置的 sidecar 持久化（决策 s2b）：<c>{csv}.graphpos.json</c>。
    ///
    /// <para>
    /// <b>为何独立于 <c>.csv.cmdmap.json</c></b>：后者是 Excel↔CSV 三方合并的**基准快照**，
    /// 其正确性直接决定"会不会丢数据"，必须进版本控制；而位置是拖拽产生的**高频纯缓存**，
    /// 丢失只导致重新自动布局。两者生命周期语义完全不同——若同居一个文件，
    /// 一次拖拽的写入失误就可能破坏合并基准。
    /// </para>
    ///
    /// <para>
    /// 本文件建议加入 <c>.gitignore</c>：团队协作时各人的布局偏好不同，
    /// 纳入版本控制只会产生无意义的冲突。
    /// </para>
    /// </summary>
    public static class GraphPosStore
    {
        [Serializable]
        private class NodePos
        {
            public string key;
            public float x;
            public float y;
        }

        [Serializable]
        private class RowEntry
        {
            public string id;
            public bool templateCollapsed = true;
            public List<NodePos> nodes = new List<NodePos>();
        }

        [Serializable]
        private class Store
        {
            public List<RowEntry> rows = new List<RowEntry>();
        }

        private static string GetPath(string csvPath)
        {
            return string.IsNullOrEmpty(csvPath) ? null : csvPath + ".graphpos.json";
        }

        /// <summary>读取指定行的节点位置。文件缺失 / 损坏时返回空字典（退化为自动布局）。</summary>
        public static Dictionary<string, Vector2> LoadPositions(string csvPath, string rowId)
        {
            var result = new Dictionary<string, Vector2>();
            var row = FindRow(Load(csvPath), rowId);
            if (row == null) return result;

            foreach (var node in row.nodes)
                if (!string.IsNullOrEmpty(node.key))
                    result[node.key] = new Vector2(node.x, node.y);

            return result;
        }

        /// <summary>读取指定行的模板折叠状态（默认折叠）。</summary>
        public static bool LoadTemplateCollapsed(string csvPath, string rowId)
        {
            var row = FindRow(Load(csvPath), rowId);
            return row?.templateCollapsed ?? true;
        }

        /// <summary>写入指定行的节点位置与折叠状态。</summary>
        public static void Save(string csvPath, string rowId,
            Dictionary<string, Vector2> positions, bool templateCollapsed)
        {
            string path = GetPath(csvPath);
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(rowId)) return;

            var store = Load(csvPath) ?? new Store();
            var row = FindRow(store, rowId);

            if (row == null)
            {
                row = new RowEntry { id = rowId };
                store.rows.Add(row);
            }

            row.templateCollapsed = templateCollapsed;
            row.nodes.Clear();

            if (positions != null)
            {
                foreach (var pair in positions)
                    row.nodes.Add(new NodePos
                    {
                        key = pair.Key,
                        x = pair.Value.x,
                        y = pair.Value.y
                    });
            }

            try
            {
                // 位置数据是纯缓存：写失败不能影响编辑流程，静默降级即可
                File.WriteAllText(path, JsonUtility.ToJson(store, prettyPrint: true));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[GraphPosStore] 写入节点位置失败（不影响编辑）：{e.Message}");
            }
        }

        private static Store Load(string csvPath)
        {
            string path = GetPath(csvPath);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return new Store();

            try
            {
                var store = JsonUtility.FromJson<Store>(File.ReadAllText(path));
                return store ?? new Store();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[GraphPosStore] 节点位置文件损坏，将使用自动布局：{e.Message}");
                return new Store();
            }
        }

        private static RowEntry FindRow(Store store, string rowId)
        {
            if (store == null || string.IsNullOrEmpty(rowId)) return null;
            foreach (var row in store.rows)
                if (row.id == rowId) return row;
            return null;
        }
    }
}
