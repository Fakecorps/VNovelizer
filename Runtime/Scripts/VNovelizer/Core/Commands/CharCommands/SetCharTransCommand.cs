using System.Collections;
using UnityEngine;
using VNovelizer.Core.API;

namespace VNovelizer.Core.Commands
{
    /// <summary>
    /// 设置角色 Transform 命令
    /// 格式：setchartrans(位置, Pos X, Pos Y, Scale)
    /// 示例：setchartrans(M, 100, 200, 1.5) -> 将中间位置的角色移动到 (100, 200)，缩放为 1.5
    /// 注意：此命令不继承，执行下一行时会自动恢复到默认 Transform
    /// </summary>
    public class SetCharTransCommand : VNCommand
    {
        public override string CommandName { get { return "setchartrans"; } }

        public override bool Execute(string args)
        {
            if (string.IsNullOrEmpty(args)) return false;

            string[] parts = args.Split(',');
            if (parts.Length < 4)
            {
                Debug.LogError($"[SetCharTrans] 参数不足，需要至少4个参数：位置, Pos X, Pos Y, Scale");
                return false;
            }

            // 解析参数
            string posCode = parts[0].Trim();
            if (!float.TryParse(parts[1].Trim(), out float posX))
            {
                Debug.LogError($"[SetCharTrans] 无法解析 Pos X: {parts[1]}");
                return false;
            }
            if (!float.TryParse(parts[2].Trim(), out float posY))
            {
                Debug.LogError($"[SetCharTrans] 无法解析 Pos Y: {parts[2]}");
                return false;
            }
            if (!float.TryParse(parts[3].Trim(), out float scale))
            {
                Debug.LogError($"[SetCharTrans] 无法解析 Scale: {parts[3]}");
                return false;
            }

            // 获取角色 RectTransform
            RectTransform target = VNAPI.GetCharRect(posCode);
            if (target == null)
            {
                Debug.LogError($"[SetCharTrans] 找不到角色: {posCode}");
                return false;
            }

            // 获取面板并保存默认值（如果还没有保存过）
            var panel = UIManager.GetInstance().GetPanel<VNGameplayPanel>("VNGameplayPanel");
            if (panel != null)
            {
                panel.SaveDefaultCharTransform(posCode);
            }

            // 设置位置
            target.anchoredPosition = new Vector2(posX, posY);
            
            // 设置缩放（保持原有的翻转状态，即 scale.x 的符号）
            Vector3 currentScale = target.localScale;
            float scaleX = Mathf.Sign(currentScale.x) * Mathf.Abs(scale); // 保持符号，应用新缩放值
            target.localScale = new Vector3(scaleX, scale, 1f);

            Debug.Log($"[SetCharTrans] 角色 {posCode} Transform 已设置: 位置=({posX}, {posY}), 缩放={scale}");
            return true;
        }

        public override void Simulate(string args)
        {
            // 预演模式下，只记录状态，不操作UI
            if (string.IsNullOrEmpty(args)) return;

            string[] parts = args.Split(',');
            if (parts.Length < 4) return;

            string posCode = parts[0].Trim();
            
            // 检查角色是否存在
            string charData = VNManager.GetInstance().GetCharacterData(posCode);
            if (string.IsNullOrEmpty(charData) || charData == "hide")
            {
                Debug.LogWarning($"[SetCharTrans.Simulate] 位置 {posCode} 没有角色，跳过设置");
                return;
            }

            // 预演模式下不操作UI，只记录日志
            Debug.Log($"[SetCharTrans.Simulate] 位置 {posCode} 的 Transform 将在运行时设置");
        }
    }
}


