using System.Collections;
using System.Text.RegularExpressions;
using UnityEngine;
using VNovelizer.Core.API;

namespace VNovelizer.Core.Commands
{
    public class PlayAnimCommand : VNCommand
    {
        public override string CommandName { get { return "playanim"; } }

        public override bool Execute(string args)
        {
            MonoManager.GetInstance().StartCoroutine(ExecuteAsync(args));
            return true;
        }

        public override IEnumerator ExecuteAsync(string args)
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
                    PoolManager.GetInstance().PushObj(resPath, animObj);
                }
                else
                {
                    VNManager.GetInstance().RegisterEffect("VNAnim_" + animName);
                }
            }
            else
            {
                // 如果没有 Animator，默认停留 1 秒后回收（除非 loop）
                if (!isLoop)
                {
                    yield return new WaitForSeconds(1.0f);
                    PoolManager.GetInstance().PushObj(resPath, animObj);
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
                if (charCode.StartsWith("L")) defaultX = -400;
                if (charCode.StartsWith("R")) defaultX = 400;

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