using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VNovelizer.Core.API;
using VNovelizer.Core.Compat;

namespace VNovelizer.Core.Commands
{
    /// <summary>
    /// 角色移动命令
    /// 格式：charmove(位置, 目标位置X, 目标位置Y, 移动时间)
    /// 示例：charmove(M, 100, 200, 1.0) -> 将中间位置的角色在1秒内移动到 (100, 200)
    /// 注意：此命令不继承，执行下一行时会自动恢复到默认位置
    ///
    /// 并发安全：支持同一命令多实例并行（命令链 [charmove(L,...) & charmove(M,...)] 场景）。
    /// 采用 token 列表管理活动动画，闭包捕获局部变量而非实例字段，
    /// 避免并行实例互相覆盖目标引用（旧实现的确定性 Bug）。
    /// </summary>
    public class CharMoveCommand : VNCommand
    {
        public override string CommandName { get { return "charmove"; } }

        private float defaultDuration = 0.5f;

        // --- 多实例并行支持（仿 CharFadeIn 的 token 模式） ---
        private struct ActiveMove
        {
            public int Token;
            public RectTransform Rect;
            public CompatTween Tween;
        }

        private readonly List<ActiveMove> _activeMoves = new List<ActiveMove>();
        private int _nextMoveToken;

        public override bool Execute(string args)
        {
            return true; // 异步命令，返回 true 表示已接受
        }

        public override IEnumerator ExecuteAsync(string args)
        {
            if (string.IsNullOrEmpty(args)) yield break;

            // 1. 解析参数
            string[] parts = args.Split(',');
            if (parts.Length < 3)
            {
                Debug.LogError($"[CharMove] 参数不足，需要至少3个参数：位置, 目标位置X, 目标位置Y, [移动时间]");
                yield break;
            }

            string posCode = parts[0].Trim();
            if (!float.TryParse(parts[1].Trim(), out float targetX))
            {
                Debug.LogError($"[CharMove] 无法解析目标位置X: {parts[1]}");
                yield break;
            }
            if (!float.TryParse(parts[2].Trim(), out float targetY))
            {
                Debug.LogError($"[CharMove] 无法解析目标位置Y: {parts[2]}");
                yield break;
            }
            float duration = defaultDuration;
            if (parts.Length >= 4) float.TryParse(parts[3].Trim(), out duration);

            // 2. 获取目标 RectTransform（局部变量：闭包捕获局部，并行实例互不干扰）
            RectTransform targetRect = VNAPI.GetCharRect(posCode);
            if (targetRect == null)
            {
                Debug.LogError($"[CharMove] 找不到位置 {posCode} 的角色");
                yield break;
            }

            // 3. 获取面板并保存默认值（如果还没有保存过）
            // 注意：必须在移动前保存，此时位置还是原始位置
            var panel = UIManager.GetInstance().GetPanel<VNGameplayPanel>("VNGameplayPanel");
            if (panel != null)
            {
                panel.SaveDefaultCharTransform(posCode);
            }

            // 4. 记录起始位置和目标位置
            Vector2 startPos = targetRect.anchoredPosition;
            Vector2 targetPos = new Vector2(targetX, targetY);

            // 5. 使用 PrimeTween 的 Custom 方法实现平滑移动
            CompatTween moveTween = AnimationCompat.CustomVector2(startPos, targetPos, duration,
                onValueChange: (Vector2 newPos) =>
                {
                    if (targetRect != null && targetRect.gameObject != null)
                    {
                        targetRect.anchoredPosition = newPos;
                    }
                },
                ease: Ease.OutQuad);

            // 6. 登记活动动画（token 管理，支持并行实例）
            int token = ++_nextMoveToken;
            _activeMoves.Add(new ActiveMove { Token = token, Rect = targetRect, Tween = moveTween });

            // 7. 等待动画完成
            try
            {
                yield return moveTween.ToYieldInstruction();
            }
            finally
            {
                UnregisterMove(token);
            }

            Debug.Log($"[CharMove] 角色 {posCode} 已移动到位置: ({targetX}, {targetY})");
        }

        private void UnregisterMove(int token)
        {
            for (int i = _activeMoves.Count - 1; i >= 0; i--)
            {
                if (_activeMoves[i].Token == token)
                    _activeMoves.RemoveAt(i);
            }
        }

        // 中断逻辑：玩家点击屏幕跳过动画（中断全部并行实例）
        public override void Interrupt()
        {
            var snapshot = new List<ActiveMove>(_activeMoves);
            foreach (var am in snapshot)
            {
                if (am.Tween.isAlive)
                {
                    // 瞬间完成动画并停止（角色直接到达目标位置）
                    am.Tween.Complete();
                }
            }

            if (snapshot.Count > 0)
                Debug.Log($"[CharMove] {snapshot.Count} 个移动动画被玩家中断，已瞬间完成。");

            _activeMoves.Clear();
        }

        public override void Simulate(string args)
        {
            // 预演模式下，只记录状态，不操作UI
            if (string.IsNullOrEmpty(args)) return;

            string[] parts = args.Split(',');
            if (parts.Length < 3) return;

            string posCode = parts[0].Trim();

            // 检查角色是否存在
            string charData = VNManager.GetInstance().GetCharacterData(posCode);
            if (string.IsNullOrEmpty(charData) || charData == "hide")
            {
                Debug.LogWarning($"[CharMove.Simulate] 位置 {posCode} 没有角色，跳过移动");
                return;
            }

            // 预演模式下不操作UI，只记录日志
            if (parts.Length >= 4 && float.TryParse(parts[3].Trim(), out float duration))
            {
                Debug.Log($"[CharMove.Simulate] 位置 {posCode} 将在 {duration} 秒内移动到 ({parts[1].Trim()}, {parts[2].Trim()})");
            }
            else
            {
                Debug.Log($"[CharMove.Simulate] 位置 {posCode} 将在运行时移动到 ({parts[1].Trim()}, {parts[2].Trim()})");
            }
        }
    }
}
