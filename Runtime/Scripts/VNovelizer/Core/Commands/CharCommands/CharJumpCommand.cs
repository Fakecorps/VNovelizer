using System.Collections;
using UnityEngine;

namespace VNovelizer.Core.Commands
{
    public class CharJumpCommand : VNCommand
    {
        public override string CommandName { get { return "charjump"; } }

        private float defaultDuration = 0.4f;
        private int defaultTimes = 1;
        private float defaultHeight = 30f;

        // --- 用于中断的数据 ---
        private RectTransform currentTarget;
        private Vector2 startPos;
        private Coroutine runningCoroutine; // 记录当前运行的协程

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

            currentTarget = panel.GetCharRect(posCode);

            // 【Bug修复】使用更可靠的检查方式，确保对象有效且激活
            if (currentTarget != null && currentTarget.gameObject != null && currentTarget.gameObject.activeSelf)
            {
                // 保存协程引用，以便中断
                runningCoroutine = MonoManager.GetInstance().StartCoroutine(JumpCoroutine(currentTarget, duration, times, height));
                // 等待协程结束
                yield return runningCoroutine;
            }
            else
            {
                // 如果对象无效，清理引用
                currentTarget = null;
            }
        }

        private IEnumerator JumpCoroutine(RectTransform rect, float durationPerJump, int times, float height)
        {
            // 【Bug修复】检查对象是否有效（使用更可靠的检查方式）
            if (rect == null || rect.gameObject == null)
            {
                Debug.LogWarning("[CharJumpCommand] RectTransform 为 null，无法执行跳跃动画");
                runningCoroutine = null;
                currentTarget = null;
                yield break;
            }
            
            // 记录原始位置，用于 Interrupt 恢复
            // 【Bug修复】使用 try-catch 保护，因为对象可能在检查后被销毁
            try
            {
                startPos = rect.anchoredPosition;
            }
            catch (MissingReferenceException)
            {
                Debug.LogWarning("[CharJumpCommand] RectTransform 在记录位置时已被销毁");
                runningCoroutine = null;
                currentTarget = null;
                yield break;
            }

            for (int i = 0; i < times; i++)
            {
                float elapsed = 0f;
                while (elapsed < durationPerJump)
                {
                    // 【Bug修复】每次循环都检查对象是否有效（使用更可靠的检查方式）
                    if (rect == null || rect.gameObject == null)
                    {
                        Debug.LogWarning("[CharJumpCommand] RectTransform 在动画过程中被销毁，中断跳跃动画");
                        runningCoroutine = null;
                        currentTarget = null;
                        yield break;
                    }
                    
                    elapsed += Time.deltaTime;
                    float t = elapsed / durationPerJump;
                    float yOffset = Mathf.Sin(t * Mathf.PI) * height;
                    
                    // 【Bug修复】使用 try-catch 捕获可能的异常
                    try
                    {
                        rect.anchoredPosition = new Vector2(startPos.x, startPos.y + yOffset);
                    }
                    catch (MissingReferenceException)
                    {
                        Debug.LogWarning("[CharJumpCommand] RectTransform 已被销毁，中断跳跃动画");
                        runningCoroutine = null;
                        currentTarget = null;
                        yield break;
                    }
                    
                    yield return null;
                }
                
                // 【Bug修复】在重置位置前也检查对象是否有效（使用更可靠的检查方式）
                if (rect == null || rect.gameObject == null)
                {
                    Debug.LogWarning("[CharJumpCommand] RectTransform 在动画过程中被销毁，中断跳跃动画");
                    runningCoroutine = null;
                    currentTarget = null;
                    yield break;
                }
                
                try
                {
                    rect.anchoredPosition = startPos;
                }
                catch (MissingReferenceException)
                {
                    Debug.LogWarning("[CharJumpCommand] RectTransform 已被销毁，中断跳跃动画");
                    runningCoroutine = null;
                    currentTarget = null;
                    yield break;
                }
            }

            // 执行完毕，清理引用
            runningCoroutine = null;
            currentTarget = null;
        }

        // === 核心：实现中断逻辑 ===
        public override void Interrupt()
        {
            // 1. 停止动画协程
            if (runningCoroutine != null)
            {
                MonoManager.GetInstance().StopCoroutine(runningCoroutine);
                runningCoroutine = null;
            }

            // 2. 强制复位角色位置 (把还在空中的角色按下来)
            // 【Bug修复】检查对象是否有效（使用更可靠的检查方式）
            if (currentTarget != null && currentTarget.gameObject != null)
            {
                try
                {
                    currentTarget.anchoredPosition = startPos;
                }
                catch (MissingReferenceException)
                {
                    // 对象已被销毁，忽略
                    Debug.LogWarning("[CharJumpCommand] 尝试中断时发现 RectTransform 已被销毁");
                }
            }
            currentTarget = null;
        }
    }
}