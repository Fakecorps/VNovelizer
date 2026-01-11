using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using VNovelizer.Core.API;
using PrimeTween;

namespace VNovelizer.Core.Commands
{
    /// <summary>
    /// 背景淡化切换命令 (基于 PrimeTween 实现)
    /// </summary>
    public class BgFadeCommand : VNCommand
    {
        public override string CommandName { get { return "bgfade"; } }

        private float defaultDuration = 1.0f;

        private Image _front;
        private Image _back;
        private Sprite _newSprite;
        private bool isRunning = false; 
        private Tween _fadeTween; 


        public override bool Execute(string args)
        {
            return true;
        }


        public override IEnumerator ExecuteAsync(string args)
        {
            if (string.IsNullOrEmpty(args)) yield break;

            isRunning = true; 

            //解析参数
            string[] parts = args.Split(',');
            string bgName = parts[0].Trim();
            float duration = defaultDuration;
            if (parts.Length > 1) float.TryParse(parts[1].Trim(), out duration);

            // 等待UI面板准备好
            float waitTime = 0f;
            const float maxWaitTime = 1f;
            _front = VNAPI.GetBG_F();
            _back = VNAPI.GetBG_B();
            
            while ((_front == null || _back == null) && waitTime < maxWaitTime)
            {
                yield return null;
                waitTime += Time.deltaTime;
                _front = VNAPI.GetBG_F();
                _back = VNAPI.GetBG_B();
            }

            if (_front == null || _back == null)
            {
                Debug.LogWarning("[BgFade] 面板缺少背景 Image 组件，可能UI还未完全初始化。已跳过该命令。");
                isRunning = false;
                yield break;
            }

            
            VNManager.GetInstance().UpdateCurrentBG_OnlyData(bgName);

            //异步加载新图片
            string fullPath = VNProjectConfig.Instance.BackgroundResPath + "/" + bgName;
            ResourceRequest request = Resources.LoadAsync<Sprite>(fullPath);
            yield return request;

            // 检查是否被中断（在加载过程中可能就被点了）
            if (!isRunning)
            {
                yield break;
            }

            _newSprite = request.asset as Sprite;
            if (_newSprite == null)
            {
                Debug.LogError($"[BgFade] 图片加载失败: {bgName} (路径: {fullPath})");
                isRunning = false;
                yield break;
            }
   
            _back.sprite = _newSprite;
            _back.color = Color.white;
            _back.gameObject.SetActive(true);

            //使用 PrimeTween 执行淡出动画
            if (_front != null)
            {
                Color c = _front.color;
                c.a = 1f;
                _front.color = c;
            }

            // 使用 PrimeTween 创建淡出动画
            bool animationCompleted = false;
            if (_front != null)
            {
                _fadeTween = Tween.Alpha(_front, startValue: 1f, endValue: 0f, duration: duration)
                    .OnComplete(() =>
                    {
                        animationCompleted = true;
                    });
            }
            else
            {
                // 如果 _front 为空，直接完成
                animationCompleted = true;
            }

            // 等待动画完成（或中断）
            while (!animationCompleted && isRunning)
            {
                yield return null;
            }

            // 如果动画被中断，_fadeTween 会在 Interrupt() 中被停止
            // 这里只需要检查是否完成
            if (isRunning)
            {
                // 7. 自然结束
                Finish();
            }
            
            isRunning = false;
            _fadeTween = default; 
        }

        /// <summary>
        /// 强制完成逻辑：瞬间切换到最终状态
        /// </summary>
        private void Finish()
        {
            // 停止 PrimeTween 动画（如果正在运行）
            if (_fadeTween.isAlive)
            {
                _fadeTween.Stop();
            }

            // 将 Front 变成新图，且不透明
            if (_front != null && _newSprite != null)
            {
                _front.sprite = _newSprite;
                Color c = _front.color;
                c.a = 1f;
                _front.color = c;
            }

            // 隐藏 Back
            if (_back != null)
            {
                _back.gameObject.SetActive(false);
            }

            // 清理引用
            _front = null;
            _back = null;
            _newSprite = null;
            _fadeTween = default;
        }

        public override void Simulate(string args)
        {
            string[] parts = args.Split(',');
            string bgName = parts[0].Trim();
            // 预演时直接更新数据状态，不播放动画
            VNAPI.UpdateBGData(bgName);
        }

        /// <summary>
        /// 被 VNManager 中断时调用
        /// </summary>
        public override void Interrupt()
        {
            if (isRunning)
            {
                Debug.Log("[BgFade] 切换被玩家中断，瞬间完成。");

                isRunning = false;

                // 停止 PrimeTween 动画
                if (_fadeTween.isAlive)
                {
                    _fadeTween.Stop();
                }

                Finish();
            }
        }
    }
}