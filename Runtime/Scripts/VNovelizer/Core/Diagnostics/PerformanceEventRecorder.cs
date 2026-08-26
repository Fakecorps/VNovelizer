using System.Collections.Generic;
using System.Text;
using UnityEngine.Events;
using VNovelizer.Core;

namespace VNovelizer.Core.Diagnostics
{
    /// <summary>
    /// 演出事件时序录制器：按发生顺序记录 <see cref="EventCenter"/> 上的演出事件。
    ///
    /// <para>
    /// <b>用途</b>：验收「提升不改变演出」硬契约（决策 s6）。对同一行分别跑
    /// 引擎隐式路径与模板命令链，逐项比对事件序列——两者必须完全一致。
    /// 这个护栏的价值在于防**静默漂移**：日后改引擎或改模板时，若两条路径
    /// 不再等价，编译不会报错、运行也不会崩，只有玩家会发现"我只改了一个参数，
    /// 整行演出却变了"。
    /// </para>
    ///
    /// <para>
    /// <b>实现约束</b>：<see cref="EventCenter.EventTrigger{T}"/> 只在已有监听者时
    /// 才调用委托，因此录制器必须在被测路径执行**之前**完成注册；
    /// 且泛型事件按载荷类型分派，需按事件名逐个注册匹配类型的监听。
    /// </para>
    /// </summary>
    public class PerformanceEventRecorder
    {
        /// <summary>一条事件记录。</summary>
        public struct Entry
        {
            /// <summary>事件名（<see cref="VNGameEvents"/> 常量）</summary>
            public string EventName;

            /// <summary>载荷摘要（用于比对——字典型载荷已展开为稳定顺序的字符串）</summary>
            public string Payload;

            public override string ToString()
            {
                return string.IsNullOrEmpty(Payload) ? EventName : EventName + "(" + Payload + ")";
            }
        }

        private readonly List<Entry> _entries = new List<Entry>();
        private bool _recording;

        // 保存委托引用，用于精确注销（EventCenter 按委托实例移除）
        private UnityAction<Dictionary<string, string>> _onUpdateDialogue;
        private UnityAction<Dictionary<string, string>> _onUpdateHeadProfile;
        private UnityAction<Dictionary<string, string>> _onShowCharacter;
        private UnityAction<string> _onChangeBackground;
        private UnityAction<string> _onHideCharacter;
        private UnityAction _onHideBackground;
        private UnityAction _onDisplayAllText;
        private UnityAction _onTypingFinished;

        public IReadOnlyList<Entry> Entries => _entries;
        public bool IsRecording => _recording;

        /// <summary>开始录制（会清空既有记录）。</summary>
        public void Start()
        {
            if (_recording) Stop();

            _entries.Clear();
            var ec = EventCenter.GetInstance();

            _onUpdateDialogue = info => Record(VNGameEvents.UpdateDialogue, Describe(info));
            _onUpdateHeadProfile = info => Record(VNGameEvents.UpdateHeadProfile, Describe(info));
            _onShowCharacter = info => Record(VNGameEvents.ShowCharacter, Describe(info));
            _onChangeBackground = bg => Record(VNGameEvents.ChangeBackground, bg);
            _onHideCharacter = pos => Record(VNGameEvents.HideCharacter, pos);
            _onHideBackground = () => Record(VNGameEvents.HideBackground, null);
            _onDisplayAllText = () => Record(VNGameEvents.DisplayAllText, null);
            _onTypingFinished = () => Record(VNGameEvents.TypingFinished, null);

            ec.AddEventListener(VNGameEvents.UpdateDialogue, _onUpdateDialogue);
            ec.AddEventListener(VNGameEvents.UpdateHeadProfile, _onUpdateHeadProfile);
            ec.AddEventListener(VNGameEvents.ShowCharacter, _onShowCharacter);
            ec.AddEventListener(VNGameEvents.ChangeBackground, _onChangeBackground);
            ec.AddEventListener(VNGameEvents.HideCharacter, _onHideCharacter);
            ec.AddEventListener(VNGameEvents.HideBackground, _onHideBackground);
            ec.AddEventListener(VNGameEvents.DisplayAllText, _onDisplayAllText);
            ec.AddEventListener(VNGameEvents.TypingFinished, _onTypingFinished);

            _recording = true;
        }

        /// <summary>停止录制并注销全部监听。</summary>
        public void Stop()
        {
            if (!_recording) return;

            var ec = EventCenter.GetInstance();
            ec.RemoveEventListener(VNGameEvents.UpdateDialogue, _onUpdateDialogue);
            ec.RemoveEventListener(VNGameEvents.UpdateHeadProfile, _onUpdateHeadProfile);
            ec.RemoveEventListener(VNGameEvents.ShowCharacter, _onShowCharacter);
            ec.RemoveEventListener(VNGameEvents.ChangeBackground, _onChangeBackground);
            ec.RemoveEventListener(VNGameEvents.HideCharacter, _onHideCharacter);
            ec.RemoveEventListener(VNGameEvents.HideBackground, _onHideBackground);
            ec.RemoveEventListener(VNGameEvents.DisplayAllText, _onDisplayAllText);
            ec.RemoveEventListener(VNGameEvents.TypingFinished, _onTypingFinished);

            _recording = false;
        }

        private void Record(string eventName, string payload)
        {
            if (!_recording) return;
            _entries.Add(new Entry { EventName = eventName, Payload = payload ?? "" });
        }

        /// <summary>
        /// 字典载荷 → 稳定字符串。按键名排序，确保同内容不同插入顺序也得到相同摘要
        /// （引擎与命令链两条路径填充字典的顺序未必一致，但语义相同即应视为等价）。
        /// </summary>
        private static string Describe(Dictionary<string, string> info)
        {
            if (info == null || info.Count == 0) return "";

            var keys = new List<string>(info.Keys);
            keys.Sort();

            var sb = new StringBuilder();
            for (int i = 0; i < keys.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(keys[i]).Append('=').Append(info[keys[i]] ?? "");
            }
            return sb.ToString();
        }

        /// <summary>
        /// 比对两次录制的事件序列。
        /// </summary>
        /// <param name="expected">基准序列（引擎隐式路径）</param>
        /// <param name="actual">待验证序列（模板命令链）</param>
        /// <param name="ignoreOrder">
        /// 是否忽略顺序。默认 false（严格逐项比对）。
        /// 对默认模板应设为 <b>true</b>——模板是单层 Par，全部系统命令同帧并行启动，
        /// 其事件到达顺序由并行调度决定，与引擎的固定顺序不必相同；
        /// 「同帧完成」才是等价性的实质，顺序不是。
        /// </param>
        public static ComparisonResult Compare(
            IReadOnlyList<Entry> expected, IReadOnlyList<Entry> actual, bool ignoreOrder = false)
        {
            var result = new ComparisonResult();

            if (ignoreOrder)
            {
                var expectedBag = ToBag(expected);
                var actualBag = ToBag(actual);

                foreach (var pair in expectedBag)
                {
                    actualBag.TryGetValue(pair.Key, out int actualCount);
                    if (actualCount < pair.Value)
                        result.Differences.Add(
                            $"缺少事件 {pair.Key}（期望 {pair.Value} 次，实际 {actualCount} 次）");
                }

                foreach (var pair in actualBag)
                {
                    expectedBag.TryGetValue(pair.Key, out int expectedCount);
                    if (pair.Value > expectedCount)
                        result.Differences.Add(
                            $"多余事件 {pair.Key}（期望 {expectedCount} 次，实际 {pair.Value} 次）");
                }

                return result;
            }

            int max = expected.Count > actual.Count ? expected.Count : actual.Count;
            for (int i = 0; i < max; i++)
            {
                string e = i < expected.Count ? expected[i].ToString() : "(无)";
                string a = i < actual.Count ? actual[i].ToString() : "(无)";
                if (e != a) result.Differences.Add($"#{i}: 期望 {e}，实际 {a}");
            }

            return result;
        }

        private static Dictionary<string, int> ToBag(IReadOnlyList<Entry> entries)
        {
            var bag = new Dictionary<string, int>();
            foreach (var e in entries)
            {
                string key = e.ToString();
                bag.TryGetValue(key, out int n);
                bag[key] = n + 1;
            }
            return bag;
        }

        /// <summary>比对结果。</summary>
        public class ComparisonResult
        {
            public List<string> Differences = new List<string>();
            public bool IsEquivalent => Differences.Count == 0;
        }

        /// <summary>把录制序列格式化为可读文本（每行一条）。</summary>
        public string Dump()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < _entries.Count; i++)
                sb.Append('#').Append(i).Append(' ').Append(_entries[i]).Append('\n');
            return sb.ToString();
        }
    }
}
