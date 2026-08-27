using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VNovelizer.Core.Commands;
using VNovelizer.Core.Commands.Chain;
using VNovelizer.Core.Diagnostics;

namespace VNovelizer.Editor
{
    /// <summary>
    /// 模板等价性回归测试（决策 s6）：验收「提升不改变演出」硬契约。
    ///
    /// <para>
    /// <b>验证方式</b>：对当前行分别执行
    /// ① 引擎隐式路径（<c>PlayCurrentLine</c> 的四步 Update）
    /// ② 默认模板命令链（<see cref="DefaultPerformanceTemplate"/>）
    /// 用 <see cref="PerformanceEventRecorder"/> 录制两次的 EventCenter 事件序列并比对。
    /// </para>
    ///
    /// <para>
    /// <b>判据是事件集合而非顺序</b>：模板是单层 Par 并行结构，全部系统命令同帧启动，
    /// 事件到达顺序由并行调度决定，与引擎的固定顺序不必相同。
    /// 「同帧内发生了相同的事件集合」才是等价性的实质。
    /// 勾选「显示顺序诊断」可查看具体次序差异——那是诊断视图，不是契约失败。
    /// </para>
    ///
    /// <para>
    /// <b>必须在播放模式下运行</b>：需要真实的 EventCenter、已加载的剧本数据、
    /// 以及可响应事件的 VNGameplayPanel。
    /// </para>
    /// </summary>
    public class TemplateEquivalenceTestWindow : EditorWindow
    {
        private bool _strictOrder;
        private Vector2 _scroll;
        private string _status = "";
        private bool _finished;

        private List<PerformanceEventRecorder.Entry> _engineTrace;
        private List<PerformanceEventRecorder.Entry> _templateTrace;

        /// <summary>正式判据：事件集合是否等价（顺序无关）</summary>
        private PerformanceEventRecorder.ComparisonResult _setComparison;

        /// <summary>辅助诊断：逐项顺序比对（顺序差异属预期，非契约失败）</summary>
        private PerformanceEventRecorder.ComparisonResult _orderComparison;

        [MenuItem("VNovelizer/模板等价性回归测试", false, 222)]
        public static void Open()
        {
            var win = GetWindow<TemplateEquivalenceTestWindow>("模板等价性");
            win.minSize = new Vector2(560, 460);
            win.Show();
        }

        private void OnGUI()
        {
            DrawToolbar();

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "本测试必须在播放模式下运行：需要真实的 EventCenter、已加载的剧本数据，" +
                    "以及可响应事件的 VNGameplayPanel。\n\n" +
                    "步骤：进入 Play 模式并让剧本播放到任意一行，然后点击「运行对比」。",
                    MessageType.Warning);
                return;
            }

            if (!string.IsNullOrEmpty(_status))
                EditorGUILayout.HelpBox(_status,
                    _finished && _setComparison != null && _setComparison.IsEquivalent
                        ? MessageType.Info : MessageType.Warning);

            if (!_finished) return;

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            DrawComparison();
            DrawTrace("引擎隐式路径", _engineTrace);
            DrawTrace("默认模板命令链", _templateTrace);

            EditorGUILayout.EndScrollView();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                if (GUILayout.Button("运行对比", EditorStyles.toolbarButton, GUILayout.Width(80)))
                    RunComparison();
            }

            _strictOrder = GUILayout.Toggle(_strictOrder, "显示顺序诊断",
                EditorStyles.toolbarButton, GUILayout.Width(90));

            GUILayout.FlexibleSpace();

            // 正式判据始终是「集合等价」：模板是并行结构，事件到达顺序由调度决定，
            // 顺序差异属预期而非契约失败。顺序诊断只作辅助视图。
            if (_finished && _setComparison != null)
            {
                var prev = GUI.color;
                GUI.color = _setComparison.IsEquivalent
                    ? new Color(0.6f, 0.9f, 0.55f) : new Color(1f, 0.55f, 0.5f);
                GUILayout.Label(_setComparison.IsEquivalent
                        ? "[OK] 契约成立（集合等价）"
                        : $"[X] 契约破裂：{_setComparison.Differences.Count} 处事件差异",
                    EditorStyles.miniLabel);
                GUI.color = prev;
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawComparison()
        {
            if (_setComparison == null) return;

            // ---- 正式判据：事件集合 ----
            EditorGUILayout.LabelField("契约判据：事件集合等价", EditorStyles.boldLabel);

            if (_setComparison.IsEquivalent)
            {
                EditorGUILayout.HelpBox(
                    "两条路径产生了完全相同的事件集合——「提升不改变演出」硬契约成立。",
                    MessageType.Info);
            }
            else
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                foreach (string diff in _setComparison.Differences)
                    EditorGUILayout.LabelField(diff, EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.Space(6);

            // ---- 辅助视图：顺序诊断 ----
            if (_strictOrder && _orderComparison != null)
            {
                EditorGUILayout.LabelField(
                    $"顺序诊断（{_orderComparison.Differences.Count} 处次序不同 · 非失败）",
                    EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(
                    "模板是单层 Par：全部系统命令同帧并行启动，到达顺序由调度决定，" +
                    "与引擎的固定顺序不必相同。此处仅用于诊断具体命令的执行位置。",
                    MessageType.None);

                if (_orderComparison.Differences.Count > 0)
                {
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    foreach (string diff in _orderComparison.Differences)
                        EditorGUILayout.LabelField(diff, EditorStyles.wordWrappedMiniLabel);
                    EditorGUILayout.EndVertical();
                }

                EditorGUILayout.Space(6);
            }
        }

        private void DrawTrace(string title, List<PerformanceEventRecorder.Entry> trace)
        {
            if (trace == null) return;

            EditorGUILayout.LabelField($"{title}（{trace.Count} 条事件）", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            for (int i = 0; i < trace.Count; i++)
                EditorGUILayout.LabelField($"#{i}  {trace[i]}", EditorStyles.miniLabel);
            if (trace.Count == 0)
                EditorGUILayout.LabelField("（无事件——请确认剧本已播放到有效行）", EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(6);
        }

        private void RunComparison()
        {
            _finished = false;
            _status = "运行中…";

            var manager = VNManager.GetInstance();
            if (manager.StoryLines == null || manager.StoryLines.Count == 0 ||
                manager.CurrentLineIndex < 0)
            {
                _status = "没有已加载的剧本或当前行无效。请先让剧本播放到任意一行。";
                _finished = false;
                Repaint();
                return;
            }

            MonoManager.GetInstance().StartCoroutine(RunCoroutine(manager));
        }

        private IEnumerator RunCoroutine(VNManager manager)
        {
            var line = manager.StoryLines[manager.CurrentLineIndex];
            string lineId = line.ID;

            // ---- ① 录制引擎隐式路径 ----
            var engineRecorder = new PerformanceEventRecorder();
            engineRecorder.Start();
            manager.ReplayImplicitPerformanceForTest();
            yield return null; // 让同帧事件全部落地
            engineRecorder.Stop();
            _engineTrace = new List<PerformanceEventRecorder.Entry>(engineRecorder.Entries);

            // ---- ② 录制默认模板命令链 ----
            // 用 Instant 执行：模板的系统命令同步实现即瞬时呈现终态，
            // 与引擎四步的"同帧同步"语义一致；异步执行会引入打字机等待，
            // 使事件跨帧到达而无法在单帧内比对。
            string templateText = DefaultPerformanceTemplate.BuildText();

            var templateRecorder = new PerformanceEventRecorder();
            templateRecorder.Start();
            CommandManager.GetInstance().ExecuteCommandsInstant(templateText);
            yield return null;
            templateRecorder.Stop();
            _templateTrace = new List<PerformanceEventRecorder.Entry>(templateRecorder.Entries);

            // ---- ③ 比对 ----
            // 正式判据 = 事件集合等价（顺序无关）；顺序比对仅作辅助诊断
            _setComparison = PerformanceEventRecorder.Compare(
                _engineTrace, _templateTrace, ignoreOrder: true);
            _orderComparison = PerformanceEventRecorder.Compare(
                _engineTrace, _templateTrace, ignoreOrder: false);

            _status = $"已对比行 {lineId}：引擎 {_engineTrace.Count} 条事件，" +
                      $"模板 {_templateTrace.Count} 条事件。" +
                      $"模板文本：{templateText}";
            _finished = true;
            Repaint();
        }
    }
}
