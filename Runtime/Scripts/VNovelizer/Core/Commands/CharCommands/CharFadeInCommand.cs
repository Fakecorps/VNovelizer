using System.Collections;
using UnityEngine;
using VNovelizer.Core.API;
using PrimeTween; // 引用 PrimeTween

namespace VNovelizer.Core.Commands
{
    /// <summary>
    /// 角色淡入命令 (PrimeTween 高性能版)
    /// </summary>
    public class CharFadeInCommand : VNCommand
    {
        public override string CommandName { get { return "charfadein"; } }

        private float defaultDuration = 0.5f;

        // --- 运行时状态 ---
        private CanvasGroup _targetCG;
        private Tween _fadeTween; // 保存 Tween 结构体

        // 异步命令不需要重写 Execute，只需重写 ExecuteAsync
        // (基类 Execute 默认会警告，这里我们覆盖为空即可)
        public override bool Execute(string args)
        {
            return true;
        }

        public override IEnumerator ExecuteAsync(string args)
        {
            if (string.IsNullOrEmpty(args)) yield break;

            // 1. 解析参数
            string[] parts = args.Split(',');
            string posCode = parts[0].Trim();
            float duration = defaultDuration;
            if (parts.Length > 1) float.TryParse(parts[1].Trim(), out duration);

            // 2. 获取目标
            RectTransform targetRect = VNAPI.GetCharRect(posCode);
            if (targetRect == null)
            {
                Debug.LogError($"[CharFadeIn] 找不到位置 {posCode} 的角色");
                yield break;
            }

            // 3. 获取或添加 CanvasGroup
            _targetCG = targetRect.GetComponent<CanvasGroup>();
            if (_targetCG == null) _targetCG = targetRect.gameObject.AddComponent<CanvasGroup>();

            // 4. 初始状态设置
            _targetCG.alpha = 0;
            targetRect.gameObject.SetActive(true);

            // 5. 【核心优化】使用 PrimeTween
            // 这里的 Tween 是结构体，零 GC
            _fadeTween = Tween.Alpha(_targetCG, 1f, duration, Ease.OutQuad);

            // 6. 等待完成
            // ToYieldInstruction 会返回一个对象，让协程挂起直到 Tween 结束
            yield return _fadeTween.ToYieldInstruction();

            // 【Bug修复】检查对象是否仍然有效
            if (_targetCG != null && _targetCG.gameObject != null)
            {
                // 对象仍然有效，正常清理
            }
            else
            {
                Debug.LogWarning("[CharFadeIn] CanvasGroup 在动画过程中被销毁");
            }

            // 7. 清理引用
            _targetCG = null;
            _fadeTween = default;
        }

        // 中断逻辑：玩家点击屏幕跳过动画
        public override void Interrupt()
        {
            // 检查 Tween 是否还在运行
            if (_fadeTween.isAlive)
            {
                // 瞬间完成动画 (Alpha 变 1) 并停止
                _fadeTween.Complete();

                Debug.Log("[CharFadeIn] 动画被玩家中断，已瞬间完成。");
            }

            // 确保状态正确 (双重保险)
            // 【Bug修复】检查对象是否仍然有效
            if (_targetCG != null)
            {
                try
                {
                    if (_targetCG.gameObject != null)
                    {
                        _targetCG.alpha = 1f;
                    }
                }
                catch (MissingReferenceException)
                {
                    Debug.LogWarning("[CharFadeIn] 尝试中断时发现 CanvasGroup 已被销毁");
                }
                _targetCG = null;
            }
        }
    }
}