using UnityEngine;
using VNovelizer.Core.API;

namespace VNovelizer.Core.Commands
{
    /// <summary>
    /// 角色水平翻转命令
    /// 格式：charflip(位置, [可选]方向)
    /// 示例1：charflip(M) -> 切换翻转状态 (左变右，右变左)
    /// 示例2：charflip(M, -1) -> 强制面朝左
    /// 示例3：charflip(M, 1) -> 强制面朝右
    /// </summary>
    public class CharFlipCommand : VNCommand
    {
        public override string CommandName { get { return "charflip"; } }

        public override bool Execute(string args)
        {
            if (string.IsNullOrEmpty(args)) return false;

            string[] parts = args.Split(',');
            string posCode = parts[0].Trim();

            // 1. 获取角色
            RectTransform target = VNAPI.GetCharRect(posCode);
            if (target == null)
            {
                Debug.LogError($"[CharFlip] 找不到角色: {posCode}");
                return false;
            }

            // 2. 确定翻转方向
            float targetScaleX;

            if (parts.Length > 1)
            {
                // 强制指定：填 1 或 -1
                if (float.TryParse(parts[1].Trim(), out float val))
                {
                    // 只要符号，保留原有大小（防止缩放被重置）
                    targetScaleX = Mathf.Sign(val) * Mathf.Abs(target.localScale.x);
                }
                else
                {
                    // 也可以支持 "left", "right" 字符串
                    string dir = parts[1].Trim().ToLower();
                    if (dir == "left") targetScaleX = -1f * Mathf.Abs(target.localScale.x);
                    else targetScaleX = 1f * Mathf.Abs(target.localScale.x);
                }
            }
            else
            {
                // 默认模式：切换方向 (取反)
                targetScaleX = -target.localScale.x;
            }

            // 3. 应用翻转到UI
            Vector3 scale = target.localScale;
            scale.x = targetScaleX;
            target.localScale = scale;

            // 4. 同步更新内部状态（保持状态一致性）
            VNManager.GetInstance().SetCharacterScaleX(posCode, targetScaleX);

            Debug.Log($"[CharFlip] 角色 {posCode} 翻转至 X={scale.x}");
            return true;
        }
        public override void Simulate(string args)
        {
            if (string.IsNullOrEmpty(args)) return;

            string[] parts = args.Split(',');
            string posCode = parts[0].Trim();

            // 1. 获取当前角色状态
            string charData = VNManager.GetInstance().GetCharacterData(posCode); // 获取 "CharID_Emotion"
            if (string.IsNullOrEmpty(charData) || charData == "hide") 
            {
                Debug.LogWarning($"[CharFlip.Simulate] 位置 {posCode} 没有角色，跳过翻转");
                return; // 没角色就跳过
            }

            // 2. 解析新的翻转方向 (只改变 scale.x 的符号)
            float targetScaleX;
            if (parts.Length > 1)
            {
                // 强制指定方向
                if (float.TryParse(parts[1].Trim(), out float val))
                {
                    targetScaleX = Mathf.Sign(val); // 只需要符号，绝对值设为1
                }
                else
                {
                    string dir = parts[1].Trim().ToLower();
                    if (dir == "left") targetScaleX = -1f;
                    else targetScaleX = 1f;
                }
            }
            else
            {
                // 默认切换：获取当前 scale.x，然后取反
                float currentScaleX = VNManager.GetInstance().GetCharacterScaleX(posCode);
                targetScaleX = currentScaleX * -1f;
            }

            // 3. 更新内部状态（不操作UI，因为UI还没创建）
            VNManager.GetInstance().SetCharacterScaleX(posCode, targetScaleX);
            Debug.Log($"[CharFlip.Simulate] 位置 {posCode} 翻转状态更新为: {targetScaleX}");
        }
    }
}