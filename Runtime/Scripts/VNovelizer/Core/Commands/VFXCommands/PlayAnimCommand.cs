using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using VNovelizer.Core.API;

namespace VNovelizer.Core.Commands
{
    public class PlayAnimCommand : VNCommand
    {
        public override string CommandName { get { return "playanim"; } }

        // --- 多实例并行支持（活动列表） ---
        // 旧实现用单组实例字段，命令链 [playanim(a,L) & playanim(b,R)] 时第二个调用覆盖字段：
        // 第一个动画自然结束时不回收（引用比对失败 → 泄漏），Interrupt 只回收最后一个。
        // 改为列表登记全部活动动画。
        private class ActiveAnim
        {
            public GameObject Obj;
            public string ResPath;
            public string AnimName;
            public bool IsLoop;
            public Coroutine Co;
        }

        private readonly List<ActiveAnim> _activeAnims = new List<ActiveAnim>();

        public override bool Execute(string args)
        {
            MonoManager.GetInstance().StartCoroutine(ExecuteAsync(args));
            return true;
        }

        // 主入口（链式执行器与同步入口共用）：创建独立 entry 并登记
        public override IEnumerator ExecuteAsync(string args)
        {
            var entry = new ActiveAnim();
            _activeAnims.Add(entry);
            try
            {
                yield return ExecuteAsyncCore(args, entry);
            }
            finally
            {
                _activeAnims.Remove(entry);
            }
        }

        private IEnumerator ExecuteAsyncCore(string args, ActiveAnim entry)
        {
            if (string.IsNullOrEmpty(args)) yield break;

            string animName = "";
            string posArg = "M"; // 默认中间
            bool isLoop = false;

            int firstComma = args.IndexOf(',');
            if (firstComma != -1)
            {
                animName = args.Substring(0, firstComma).Trim();
                string rest = args.Substring(firstComma + 1).Trim();

                // 检查 Loop (如果在最后)
                if (rest.EndsWith(",loop", System.StringComparison.OrdinalIgnoreCase))
                {
                    isLoop = true;
                    // 去掉 ",loop"
                    rest = rest.Substring(0, rest.Length - 5).Trim();
                }

                // 剩下的就是位置参数
                posArg = rest;
            }
            else
            {
                animName = args.Trim();
            }

            // 加载资源
            string resPath = VNProjectConfig.Instance.AnimationPath + "/" + animName;
            GameObject animObj = null;
            PoolManager.GetInstance().GetObj(resPath, (go) => { animObj = go; });

            while (animObj == null) yield return null;

            // 保存引用（每实例独立，支持并行）
            entry.Obj = animObj;
            entry.ResPath = resPath;
            entry.AnimName = animName;
            entry.IsLoop = isLoop;

            // 初始化
            Transform parent = VNAPI.GetEffectLayer();
            animObj.name = "VNAnim_" + animName;
            animObj.transform.SetParent(parent, false);

            RectTransform rect = animObj.GetComponent<RectTransform>();
            rect.localScale = Vector3.one;

            // 设置位置 (核心逻辑)
            SetPositionSmart(rect, posArg);

            // 播放与回收
            Animator anim = animObj.GetComponent<Animator>();
            if (anim != null)
            {
                yield return null; // 等待 Animator 初始化

                if (!isLoop)
                {
                    float length = anim.GetCurrentAnimatorStateInfo(0).length;
                    // 如果长度为0 (可能是无限循环动画没 StateInfo)，给个默认值 1s
                    if (length <= 0) length = 1.0f;

                    yield return new WaitForSeconds(length);

                    // 检查对象是否仍然有效（可能已被中断：Interrupt 会回收并置空 entry.Obj）
                    if (entry.Obj != null && entry.Obj == animObj)
                    {
                        PoolManager.GetInstance().PushObj(resPath, animObj);
                        entry.Obj = null;
                    }
                }
                else
                {
                    VNManager.GetInstance().RegisterEffect("VNAnim_" + animName);
                    // 【修复】循环动画也需要保存引用，以便在中断时清理
                    // 注意：循环动画不会自动清理引用，需要在Interrupt中处理
                }
            }
            else
            {
                // 如果没有 Animator，默认停留 1 秒后回收（除非 loop）
                if (!isLoop)
                {
                    yield return new WaitForSeconds(1.0f);

                    if (entry.Obj != null && entry.Obj == animObj)
                    {
                        PoolManager.GetInstance().PushObj(resPath, animObj);
                        entry.Obj = null;
                    }
                }
                else
                {
                    // 循环动画也需要注册效果
                    VNManager.GetInstance().RegisterEffect("VNAnim_" + animName);
                }
            }
        }

        // --- 核心：位置解析器 ---
        private void SetPositionSmart(RectTransform rect, string posArg)
        {
            // 去除空格
            posArg = posArg.Replace(" ", "");

            // 模式 1: 绝对坐标 "(x,y)"
            // 正则: ^\((-?\d+),(-?\d+)\)$
            if (posArg.StartsWith("(") && posArg.EndsWith(")"))
            {
                Vector2 offset = ParseVector2(posArg);
                rect.anchoredPosition = offset;
                return;
            }

            // 模式 2: 角色跟随 "M" 或 "M(x,y)"
            string charCode = "";
            Vector2 charOffset = Vector2.zero;

            int openParen = posArg.IndexOf('(');
            if (openParen != -1)
            {
                // 有偏移量: "M(0,300)"
                charCode = posArg.Substring(0, openParen); // "M"
                string vectorPart = posArg.Substring(openParen); // "(0,300)"
                charOffset = ParseVector2(vectorPart);
            }
            else
            {
                // 无偏移量: "M"
                charCode = posArg;
            }

            // 获取角色位置
            RectTransform charRect = VNAPI.GetCharRect(charCode);
            if (charRect != null)
            {
                rect.anchoredPosition = charRect.anchoredPosition + charOffset;
            }
            else
            {
                // 找不到角色时的默认位置
                float defaultX = 0;
                if (charCode.StartsWith("L") || charCode.StartsWith("Left", System.StringComparison.OrdinalIgnoreCase)) defaultX = -400;
                if (charCode.StartsWith("ML") || charCode.StartsWith("MidLeft", System.StringComparison.OrdinalIgnoreCase)) defaultX = -200;
                if (charCode.StartsWith("MR") || charCode.StartsWith("MidRight", System.StringComparison.OrdinalIgnoreCase)) defaultX = 200;
                if (charCode.StartsWith("R") || charCode.StartsWith("Right", System.StringComparison.OrdinalIgnoreCase)) defaultX = 400;

                rect.anchoredPosition = new Vector2(defaultX, 0) + charOffset;
            }
        }

        // 辅助：解析 "(x,y)" 字符串
        private Vector2 ParseVector2(string s)
        {
            // 去掉括号
            s = s.Trim('(', ')');
            string[] nums = s.Split(',');

            float x = 0, y = 0;
            if (nums.Length >= 1) float.TryParse(nums[0], out x);
            if (nums.Length >= 2) float.TryParse(nums[1], out y);

            return new Vector2(x, y);
        }

        /// <summary>
        /// 【修复】中断命令：当玩家进入下一行时，立即回收全部活动动画对象（含并行实例）
        /// </summary>
        public override void Interrupt()
        {
            if (_activeAnims.Count == 0) return;

            var snapshot = new List<ActiveAnim>(_activeAnims);
            foreach (var entry in snapshot)
            {
                if (entry.Obj == null) continue;

                Debug.Log($"[PlayAnimCommand] 动画被中断，立即回收: {entry.Obj.name} (循环: {entry.IsLoop})");

                // 循环动画需要取消注册
                if (entry.IsLoop && !string.IsNullOrEmpty(entry.AnimName))
                {
                    VNManager.GetInstance().UnregisterEffect("VNAnim_" + entry.AnimName);
                }

                // 回收动画对象
                if (!string.IsNullOrEmpty(entry.ResPath))
                {
                    PoolManager.GetInstance().PushObj(entry.ResPath, entry.Obj);
                }
                else
                {
                    GameObject.Destroy(entry.Obj);
                }

                // 置空引用（核心协程的比对检查会发现对象已被回收）
                entry.Obj = null;
            }

            _activeAnims.Clear();
        }

        public override void Simulate(string args)
        {
            if (args.Contains("loop"))
            {
                string animName = args.Split(',')[0].Trim();
                VNManager.GetInstance().RegisterEffect("VNAnim_" + animName);
            }
        }
    }
}