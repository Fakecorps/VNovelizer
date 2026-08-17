using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VNovelizer.Core.API;

namespace VNovelizer.Core.Commands
{
    /// <summary>
    /// 震动命令
    /// 格式: shake(arg, shakeduration, shakeIntensity)
    /// arg = screen: 相机震动（整个面板）
    /// arg = L/M/R: 对应位置的角色震动
    /// arg = dialogue: 对话框震动
    /// </summary>
    public class ShakeCommand : VNCommand
    {
        public override string CommandName { get { return "shake"; } }

        // 默认参数：震动 UI 时，强度通常需要大一点，因为 UI 坐标单位是像素
        private float defaultDuration = 0.5f;
        private float defaultIntensity = 10f; // UI像素偏移量

        // --- 活动震动协程跟踪（支持多实例并行 + 点击跳过时正确中断） ---
        // 记录 (协程句柄, 目标 rect, 原始位置)，中断时全部停止并强制归位
        private readonly List<(Coroutine co, RectTransform rect, Vector2 originalPos)> _activeShakes
            = new List<(Coroutine, RectTransform, Vector2)>();

        public override bool Execute(string args)
        {
            if (string.IsNullOrEmpty(args))
            {
                Debug.LogError("[ShakeCommand] 参数不能为空，格式: shake(arg, shakeduration, shakeIntensity)");
                return false;
            }

            // 解析参数
            string[] parts = args.Split(',');
            if (parts.Length < 1)
            {
                Debug.LogError("[ShakeCommand] 参数不足，至少需要指定震动目标（screen/L/M/R/dialogue）");
                return false;
            }

            string arg = parts[0].Trim().ToLower();
            float duration = defaultDuration;
            float intensity = defaultIntensity;

            if (parts.Length >= 2)
                float.TryParse(parts[1].Trim(), out duration);
            if (parts.Length >= 3)
                float.TryParse(parts[2].Trim(), out intensity);

            // 使用泛型方法获取 VNGameplayPanel
            var panel = UIManager.GetInstance().GetPanel<VNGameplayPanel>("VNGameplayPanel");
            if (panel == null)
            {
                Debug.LogError("[ShakeCommand] 未找到 VNGameplayPanel，请确保该面板已打开。");
                return false;
            }

            Transform targetTransform = null;

            // 根据 arg 参数确定震动目标
            if (arg == "screen")
            {
                // 屏幕震动（整个面板）
                targetTransform = panel.transform;
            }
            else if (arg == "l" || arg == "m" || arg == "r" || 
                     arg == "left" || arg == "mid" || arg == "middle" || arg == "right")
            {
                // 角色震动
                string posCode = NormalizePositionCode(arg);
                RectTransform charRect = VNAPI.GetCharRect(posCode);
                if (charRect != null)
                {
                    targetTransform = charRect;
                }
                else
                {
                    Debug.LogError($"[ShakeCommand] 找不到位置 {arg} 的角色");
                    return false;
                }
            }
            else if (arg == "dialogue")
            {
                // 对话框震动
                RectTransform dialogueBoxRect = panel.GetDialogueBoxRect();
                if (dialogueBoxRect != null)
                {
                    targetTransform = dialogueBoxRect;
                }
                else
                {
                    Debug.LogError("[ShakeCommand] 找不到对话框");
                    return false;
                }
            }
            else
            {
                Debug.LogError($"[ShakeCommand] 未知的震动目标: {arg}。支持的目标: screen, L/M/R, dialogue");
                return false;
            }

            if (targetTransform != null)
            {
                // 先记录原始位置（StartCoroutine 会同步执行到第一个 yield，
                // 届时 anchoredPosition 已被首个偏移改变，必须提前捕获）
                var rect = targetTransform.GetComponent<RectTransform>();
                Vector2 originalPos = rect != null ? rect.anchoredPosition : Vector2.zero;

                // 启动震动协程（登记句柄，支持并行实例与中断归位）
                var co = MonoManager.GetInstance().StartCoroutine(ShakeUICoroutine(targetTransform, duration, intensity));
                if (rect != null)
                    _activeShakes.Add((co, rect, originalPos));
                Debug.Log($"[ShakeCommand] 开始震动: 目标={arg}, 持续时间={duration}, 强度={intensity}");
                return true;
            }

            return false;
        }

        /// <summary>
        /// 中断全部震动：停止协程并强制归位，防止跳过后 UI 元素持续震动/偏移残留
        /// </summary>
        public override void Interrupt()
        {
            if (_activeShakes.Count == 0) return;

            var mono = MonoManager.GetInstance();
            foreach (var entry in _activeShakes)
            {
                mono.StopCoroutine(entry.co);
                if (entry.rect != null && entry.rect.gameObject != null)
                {
                    try
                    {
                        entry.rect.anchoredPosition = entry.originalPos;
                    }
                    catch (MissingReferenceException)
                    {
                        // 对象已销毁，无需归位
                    }
                }
            }
            _activeShakes.Clear();
            Debug.Log("[ShakeCommand] 震动被玩家中断，已全部归位。");
        }

        /// <summary>
        /// 标准化位置代码（L/M/R）
        /// </summary>
        private string NormalizePositionCode(string posCode)
        {
            if (string.IsNullOrEmpty(posCode)) return posCode;
            string lower = posCode.ToLower();
            if (lower == "left" || lower == "l") return "L";
            if (lower == "mid" || lower == "middle" || lower == "m") return "M";
            if (lower == "right" || lower == "r") return "R";
            return posCode;
        }

        /// <summary>
        /// UI 震动协程
        /// </summary>
        private IEnumerator ShakeUICoroutine(Transform targetTransform, float duration, float intensity)
        {
            if (targetTransform == null) yield break;

            // 获取 RectTransform 以便操作 UI 坐标
            RectTransform rect = targetTransform.GetComponent<RectTransform>();
            if (rect == null) yield break;

            // 记录原始坐标 (AnchoredPosition 是相对于父物体的坐标)
            Vector2 originalPos = rect.anchoredPosition;

            try
            {
            float elapsedTime = 0f;

            while (elapsedTime < duration)
            {
                // 【Bug修复】每次循环都检查对象是否有效
                if (rect == null || targetTransform == null)
                {
                    Debug.LogWarning("[ShakeCommand] RectTransform 在震动过程中被销毁，中断震动");
                    yield break;
                }

                // 生成随机偏移 (UI 坐标系)
                float offsetX = Random.Range(-intensity, intensity);
                float offsetY = Random.Range(-intensity, intensity);

                // 应用偏移
                try
                {
                    rect.anchoredPosition = new Vector2(
                        originalPos.x + offsetX,
                        originalPos.y + offsetY
                    );
                }
                catch (MissingReferenceException)
                {
                    Debug.LogWarning("[ShakeCommand] RectTransform 已被销毁，中断震动");
                    yield break;
                }

                elapsedTime += Time.deltaTime;
                yield return null;
            }
            }
            finally
            {
                // 自然结束（或被外部 StopCoroutine）时从活动列表移除
                int idx = _activeShakes.FindIndex(e => e.rect == rect);
                if (idx >= 0) _activeShakes.RemoveAt(idx);
            }

            // 震动结束，强制归位，防止偏移累积
            if (rect != null && targetTransform != null)
            {
                try
                {
                    rect.anchoredPosition = originalPos;
                }
                catch (MissingReferenceException)
                {
                    Debug.LogWarning("[ShakeCommand] RectTransform 在震动结束时已被销毁");
                }
            }
        }
    }
}