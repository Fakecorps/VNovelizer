using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VNovelizer.Core.Theater;

namespace VNovelizer.Core.Commands
{
    /// <summary>
    /// 角色移动命令（剧场层实现）
    /// 格式：charmove(位置, 目标位置X, 目标位置Y, [移动时间])
    /// 坐标为剧本像素语义（1920x1080 参考，原点=画面中心）——与旧 anchoredPosition 语义一致。
    /// 注意：此命令不继承，执行下一行时会自动恢复到默认位置（OnShowCharacter 重置槽位基准）。
    ///
    /// 并发安全：token 列表登记活动动画（命令链 [charmove(L,...) & charmove(M,...)] 并行场景）。
    /// </summary>
    public class CharMoveCommand : VNCommand
    {
        public override string CommandName { get { return "charmove"; } }

        private float defaultDuration = 0.5f;

        private readonly List<string> _activeMoves = new List<string>();

        public override bool Execute(string args)
        {
            return true; // 异步命令，返回 true 表示已接受
        }

        public override IEnumerator ExecuteAsync(string args)
        {
            if (string.IsNullOrEmpty(args)) yield break;

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

            var theater = TheaterManager.GetInstance();
            var actor = theater.GetActor(posCode);
            if (actor == null)
            {
                Debug.LogError($"[CharMove] 找不到位置 {posCode} 的角色");
                yield break;
            }

            var target = new Vector2(targetX, targetY);

            _activeMoves.Add(posCode);
            try
            {
                yield return actor.MoveAsync(target, duration);
            }
            finally
            {
                _activeMoves.Remove(posCode);
            }

            Debug.Log($"[CharMove] 角色 {posCode} 已移动到位置: ({targetX}, {targetY})");
        }

        /// <summary>中断：全部活动移动瞬间到终态（actor.Interrupt 归位语义）</summary>
        public override void Interrupt()
        {
            var theater = TheaterManager.GetInstance();
            int count = _activeMoves.Count;
            foreach (string posCode in _activeMoves)
            {
                theater.GetActor(posCode)?.Interrupt();
            }
            if (count > 0)
                Debug.Log($"[CharMove] {count} 个移动动画被玩家中断，已瞬间完成。");
            _activeMoves.Clear();
        }

        public override void Simulate(string args)
        {
            // 预演语义与旧实现一致：位移是行内瞬态演出，下一行 OnShowCharacter 会重置槽位基准，
            // 快进后由 PlayCurrentLine 的 Execute 重新播放，故 Simulate 只记录不落状态。
            if (string.IsNullOrEmpty(args)) return;

            string[] parts = args.Split(',');
            if (parts.Length < 3) return;

            string posCode = parts[0].Trim();
            string charData = VNManager.GetInstance().GetCharacterData(posCode);
            if (string.IsNullOrEmpty(charData) || charData == "hide")
            {
                Debug.LogWarning($"[CharMove.Simulate] 位置 {posCode} 没有角色，跳过移动");
                return;
            }

            Debug.Log($"[CharMove.Simulate] 位置 {posCode} 将在运行时移动到 ({parts[1].Trim()}, {parts[2].Trim()})");
        }
    }
}
