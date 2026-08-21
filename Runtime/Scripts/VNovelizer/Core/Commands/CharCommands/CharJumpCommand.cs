using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VNovelizer.Core.Theater;

namespace VNovelizer.Core.Commands
{
    /// <summary>
    /// 角色跳跃命令（剧场层实现）
    /// 格式：charjump(位置, [时长], [次数], [高度px])
    /// 高度为剧本像素语义。多实例并行：token 列表登记全部活动跳跃。
    /// </summary>
    public class CharJumpCommand : VNCommand
    {
        public override string CommandName { get { return "charjump"; } }

        private float defaultDuration = 0.4f;
        private int defaultTimes = 1;
        private float defaultHeight = 30f;

        // --- 多实例并行支持（登记 posCode，归位经 TheaterManager 状态） ---
        private struct ActiveJump
        {
            public int Token;
            public string PosCode;
            public Vector2 StartPos;
        }

        private readonly List<ActiveJump> _activeJumps = new List<ActiveJump>();
        private int _nextJumpToken;

        public override bool Execute(string args)
        {
            MonoManager.GetInstance().StartCoroutine(ExecuteAsync(args));
            return true;
        }

        public override IEnumerator ExecuteAsync(string args)
        {
            if (string.IsNullOrEmpty(args)) yield break;

            string[] parts = args.Split(',');
            string posCode = TheaterManager.NormalizePosCode(parts[0]);
            if (posCode == null)
            {
                Debug.LogError($"[CharJump] 未知位置: {parts[0].Trim()}（可用 L/ML/M/MR/R 或全名）");
                yield break;
            }
            float duration = defaultDuration;
            int times = defaultTimes;
            float height = defaultHeight;

            if (parts.Length >= 2) float.TryParse(parts[1].Trim(), out duration);
            if (parts.Length >= 3) int.TryParse(parts[2].Trim(), out times);
            if (parts.Length >= 4) float.TryParse(parts[3].Trim(), out height);

            var theater = TheaterManager.GetInstance();
            var actor = theater.GetActor(posCode);
            if (actor == null) yield break;

            var state = theater.GetState(posCode);
            if (state == null) yield break;

            // 确保可见（即使角色当前不可见，跳跃时也应能操作——与旧实现一致）
            if (!state.visible) theater.SetVisible(posCode, true);

            Vector2 startPos = state.position;

            int token = ++_nextJumpToken;
            _activeJumps.Add(new ActiveJump { Token = token, PosCode = posCode, StartPos = startPos });

            try
            {
                for (int i = 0; i < times; i++)
                {
                    float elapsed = 0f;
                    while (elapsed < duration)
                    {
                        if (theater.GetActor(posCode) == null) yield break; // 演员被移除
                        elapsed += Time.deltaTime;
                        float t = elapsed / duration;
                        float yOffset = Mathf.Sin(t * Mathf.PI) * height;
                        theater.SetPosition(posCode, new Vector2(startPos.x, startPos.y + yOffset));
                        yield return null;
                    }

                    if (theater.GetActor(posCode) == null) yield break;
                    theater.SetPosition(posCode, startPos);
                }
            }
            finally
            {
                UnregisterJump(token);
            }
        }

        private void UnregisterJump(int token)
        {
            for (int i = _activeJumps.Count - 1; i >= 0; i--)
            {
                if (_activeJumps[i].Token == token)
                    _activeJumps.RemoveAt(i);
            }
        }

        /// <summary>中断全部跳跃：把空中的角色按回起始位置</summary>
        public override void Interrupt()
        {
            if (_activeJumps.Count == 0) return;

            var theater = TheaterManager.GetInstance();
            var snapshot = new List<ActiveJump>(_activeJumps);
            foreach (var aj in snapshot)
            {
                theater.GetActor(aj.PosCode)?.SetPosition(aj.StartPos);
            }

            Debug.Log($"[CharJumpCommand] {snapshot.Count} 个跳跃动画被玩家中断，已全部归位。");
            _activeJumps.Clear();
        }
    }
}
