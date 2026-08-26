using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VNovelizer.Core.API;
using VNovelizer.Core.Commands.Meta;
using VNovelizer.Core.Theater;

namespace VNovelizer.Core.Commands
{
    /// <summary>
    /// 震动命令（剧场层实现）
    /// 格式: shake(arg, shakeduration, shakeIntensity)
    /// arg = screen: 相机震动（剧场场景相机，UI 纹丝不动）
    /// arg = L/ML/M/MR/R: 对应位置的角色震动（剧场演员）
    /// arg = dialogue: 对话框震动（UI 层，保持旧实现）
    /// </summary>
    [VNCommandMeta(VNCommandCategory.Performance, "屏幕 / 角色 / 对话框震动")]
    public class ShakeCommand : VNCommand
    {
        [VNParam(0, "target", VNParamType.Enum, Options = "screen|dialogue|L|ML|M|MR|R",
            Description = "震动目标：screen=相机（UI 不动）/ dialogue=对话框 / 槽位码=角色")]
        [VNParam(1, "duration", VNParamType.Float, Min = 0f, Max = 10f, Default = "0.5",
            Optional = true, Description = "持续秒数")]
        [VNParam(2, "intensity", VNParamType.Float, Min = 0f, Max = 100f, Default = "10",
            Optional = true, Description = "强度（剧本像素）")]
        public override string CommandName { get { return "shake"; } }

        // 默认参数：强度单位为剧本像素（UI 与剧场演员统一像素语义，相机内部换算世界单位）
        private float defaultDuration = 0.5f;
        private float defaultIntensity = 10f;

        // --- 活动震动跟踪（支持多实例并行 + 点击跳过时正确中断归位） ---
        private bool _screenShakeActive;
        private readonly List<string> _activeActorShakes = new List<string>();

        // 对话框震动（UI 层）：协程句柄 + 原始位置
        private readonly List<(Coroutine co, RectTransform rect, Vector2 originalPos)> _activeUiShakes
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
                Debug.LogError("[ShakeCommand] 参数不足，至少需要指定震动目标（screen/L/ML/M/MR/R/dialogue）");
                return false;
            }

            string arg = parts[0].Trim().ToLower();
            float duration = defaultDuration;
            float intensity = defaultIntensity;

            if (parts.Length >= 2)
                float.TryParse(parts[1].Trim(), out duration);
            if (parts.Length >= 3)
                float.TryParse(parts[2].Trim(), out intensity);

            // 1) 相机震动：剧场场景相机（只震戏内画面，UI 稳定）
            if (arg == "screen")
            {
                SceneCameraManager.GetInstance().BeginShake(duration, intensity * MeshActor.PixelsToWorld);
                _screenShakeActive = true;
                Debug.Log($"[ShakeCommand] 开始相机震动: 持续时间={duration}, 强度={intensity}px");
                return true;
            }

            // 2) 角色震动：剧场演员
            string posCode = TheaterManager.NormalizePosCode(arg);
            if (posCode != null)
            {
                var theater = TheaterManager.GetInstance();
                if (theater.GetActor(posCode) == null)
                {
                    Debug.LogError($"[ShakeCommand] 找不到位置 {arg} 的角色");
                    return false;
                }
                theater.BeginActorShake(posCode, duration, intensity);
                if (!_activeActorShakes.Contains(posCode))
                    _activeActorShakes.Add(posCode);
                Debug.Log($"[ShakeCommand] 开始角色震动: 目标={arg}, 持续时间={duration}, 强度={intensity}px");
                return true;
            }

            // 3) 对话框震动：UI 层（保持旧实现）
            if (arg == "dialogue")
            {
                var panel = UIManager.GetInstance().Get<VNGameplayPanel>();
                if (panel == null)
                {
                    Debug.LogError("[ShakeCommand] 未找到 VNGameplayPanel，请确保该面板已打开。");
                    return false;
                }

                RectTransform dialogueBoxRect = panel.GetDialogueBoxRect();
                if (dialogueBoxRect == null)
                {
                    Debug.LogError("[ShakeCommand] 找不到对话框");
                    return false;
                }

                // 先记录原始位置（StartCoroutine 会同步执行到第一个 yield，须提前捕获）
                Vector2 originalPos = dialogueBoxRect.anchoredPosition;
                var co = MonoManager.GetInstance().StartCoroutine(ShakeUICoroutine(dialogueBoxRect, duration, intensity));
                _activeUiShakes.Add((co, dialogueBoxRect, originalPos));
                Debug.Log($"[ShakeCommand] 开始对话框震动: 持续时间={duration}, 强度={intensity}px");
                return true;
            }

            Debug.LogError($"[ShakeCommand] 未知的震动目标: {arg}。支持的目标: screen, L/ML/M/MR/R, dialogue");
            return false;
        }

        /// <summary>
        /// 中断全部震动：相机/演员归位 + 对话框强制归位
        /// </summary>
        public override void Interrupt()
        {
            // 相机震动
            if (_screenShakeActive)
            {
                SceneCameraManager.GetInstance().CancelShake();
                _screenShakeActive = false;
            }

            // 演员震动
            if (_activeActorShakes.Count > 0)
            {
                var theater = TheaterManager.GetInstance();
                foreach (string posCode in _activeActorShakes)
                    theater.CancelActorShake(posCode);
                _activeActorShakes.Clear();
            }

            // 对话框震动（UI 层）
            if (_activeUiShakes.Count > 0)
            {
                var mono = MonoManager.GetInstance();
                foreach (var entry in _activeUiShakes)
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
                _activeUiShakes.Clear();
            }
        }

        /// <summary>
        /// UI 震动协程（对话框分支专用）
        /// </summary>
        private IEnumerator ShakeUICoroutine(RectTransform rect, float duration, float intensity)
        {
            if (rect == null) yield break;

            Vector2 originalPos = rect.anchoredPosition;

            try
            {
                float elapsedTime = 0f;
                while (elapsedTime < duration)
                {
                    if (rect == null)
                    {
                        Debug.LogWarning("[ShakeCommand] RectTransform 在震动过程中被销毁，中断震动");
                        yield break;
                    }

                    float offsetX = Random.Range(-intensity, intensity);
                    float offsetY = Random.Range(-intensity, intensity);
                    rect.anchoredPosition = new Vector2(originalPos.x + offsetX, originalPos.y + offsetY);

                    elapsedTime += Time.deltaTime;
                    yield return null;
                }
            }
            finally
            {
                int idx = _activeUiShakes.FindIndex(e => e.rect == rect);
                if (idx >= 0) _activeUiShakes.RemoveAt(idx);
            }

            // 震动结束归位，防止偏移累积
            if (rect != null)
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
