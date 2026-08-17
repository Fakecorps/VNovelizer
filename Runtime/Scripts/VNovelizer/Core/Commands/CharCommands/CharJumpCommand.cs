using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace VNovelizer.Core.Commands
{
    public class CharJumpCommand : VNCommand
    {
        public override string CommandName { get { return "charjump"; } }

        private float defaultDuration = 0.4f;
        private int defaultTimes = 1;
        private float defaultHeight = 30f;

        // --- 多实例并行支持（token 列表模式） ---
        // 旧实现用单组实例字段（currentTarget/startPos/runningCoroutine），
        // 命令链 [charjump(L) & charjump(M)] 时第二个调用覆盖字段：
        // 第一个动画的归位基准被篡改（L 会被拉到 M 的起始位置），
        // 且 Interrupt 只能中断最后一个。改为列表登记全部活动跳跃。
        private struct ActiveJump
        {
            public int Token;
            public RectTransform Rect;
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
            string posCode = parts[0].Trim();
            float duration = defaultDuration;
            int times = defaultTimes;
            float height = defaultHeight;

            if (parts.Length >= 2) float.TryParse(parts[1].Trim(), out duration);
            if (parts.Length >= 3) int.TryParse(parts[2].Trim(), out times);
            if (parts.Length >= 4) float.TryParse(parts[3].Trim(), out height);

            var panel = UIManager.GetInstance().GetPanel<VNGameplayPanel>("VNGameplayPanel");
            if (panel == null) yield break;

            // 局部变量捕获（并行实例互不干扰）
            RectTransform targetRect = panel.GetCharRect(posCode);
            if (targetRect == null || targetRect.gameObject == null) yield break;

            // 确保对象激活（即使角色当前不可见，跳跃时也应能操作）
            if (!targetRect.gameObject.activeInHierarchy)
                targetRect.gameObject.SetActive(true);

            Vector2 startPos = targetRect.anchoredPosition;

            // 登记（token 管理）
            int token = ++_nextJumpToken;
            _activeJumps.Add(new ActiveJump { Token = token, Rect = targetRect, StartPos = startPos });

            try
            {
                yield return JumpCoroutine(targetRect, startPos, duration, times, height);
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

        private IEnumerator JumpCoroutine(RectTransform rect, Vector2 startPos, float durationPerJump, int times, float height)
        {
            if (rect == null || rect.gameObject == null)
            {
                Debug.LogWarning("[CharJumpCommand] RectTransform 为 null，无法执行跳跃动画");
                yield break;
            }

            for (int i = 0; i < times; i++)
            {
                float elapsed = 0f;
                while (elapsed < durationPerJump)
                {
                    if (rect == null || rect.gameObject == null)
                    {
                        Debug.LogWarning("[CharJumpCommand] RectTransform 在动画过程中被销毁，中断跳跃动画");
                        yield break;
                    }

                    elapsed += Time.deltaTime;
                    float t = elapsed / durationPerJump;
                    float yOffset = Mathf.Sin(t * Mathf.PI) * height;

                    try
                    {
                        rect.anchoredPosition = new Vector2(startPos.x, startPos.y + yOffset);
                    }
                    catch (MissingReferenceException)
                    {
                        Debug.LogWarning("[CharJumpCommand] RectTransform 已被销毁，中断跳跃动画");
                        yield break;
                    }

                    yield return null;
                }

                if (rect == null || rect.gameObject == null)
                {
                    Debug.LogWarning("[CharJumpCommand] RectTransform 在动画过程中被销毁，中断跳跃动画");
                    yield break;
                }

                try
                {
                    rect.anchoredPosition = startPos;
                }
                catch (MissingReferenceException)
                {
                    Debug.LogWarning("[CharJumpCommand] RectTransform 已被销毁，中断跳跃动画");
                    yield break;
                }
            }
        }

        /// <summary>
        /// 中断全部跳跃：把空中的角色按回起始位置（中断全部并行实例）
        /// </summary>
        public override void Interrupt()
        {
            if (_activeJumps.Count == 0) return;

            var snapshot = new List<ActiveJump>(_activeJumps);
            foreach (var aj in snapshot)
            {
                if (aj.Rect != null && aj.Rect.gameObject != null)
                {
                    try
                    {
                        aj.Rect.anchoredPosition = aj.StartPos;
                    }
                    catch (MissingReferenceException)
                    {
                        Debug.LogWarning("[CharJumpCommand] 尝试中断时发现 RectTransform 已被销毁");
                    }
                }
            }

            Debug.Log($"[CharJumpCommand] {snapshot.Count} 个跳跃动画被玩家中断，已全部归位。");
            _activeJumps.Clear();
        }
    }
}
