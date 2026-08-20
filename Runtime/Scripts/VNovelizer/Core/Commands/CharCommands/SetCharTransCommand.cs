using UnityEngine;
using VNovelizer.Core.Theater;

namespace VNovelizer.Core.Commands
{
    /// <summary>
    /// 设置角色 Transform 命令（剧场层实现）
    /// 格式：setchartrans(位置, Pos X, Pos Y, Scale)
    /// 坐标为剧本像素语义；缩放与翻转分离（scale 为正值大小，翻转符号独立保持）。
    /// 注意：此命令不继承，执行下一行时会自动恢复到默认 Transform（OnShowCharacter 重置）。
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

            var theater = TheaterManager.GetInstance();
            var state = theater.GetState(posCode);
            if (state == null)
            {
                Debug.LogError($"[SetCharTrans] 找不到角色: {posCode}");
                return false;
            }

            // 保持原有翻转符号，应用新缩放（与旧实现语义一致）
            bool flipped = state.scaleX < 0f;
            theater.SetPosition(posCode, new Vector2(posX, posY));
            theater.SetScale(posCode, Mathf.Abs(scale));
            theater.SetFlip(posCode, flipped);

            Debug.Log($"[SetCharTrans] 角色 {posCode} Transform 已设置: 位置=({posX}, {posY}), 缩放={scale}");
            return true;
        }

        public override void Simulate(string args)
        {
            // 预演语义与旧实现一致：Transform 是行内瞬态演出，不落状态
            if (string.IsNullOrEmpty(args)) return;

            string[] parts = args.Split(',');
            if (parts.Length < 4) return;

            string posCode = parts[0].Trim();
            string charData = VNManager.GetInstance().GetCharacterData(posCode);
            if (string.IsNullOrEmpty(charData) || charData == "hide")
            {
                Debug.LogWarning($"[SetCharTrans.Simulate] 位置 {posCode} 没有角色，跳过设置");
                return;
            }

            Debug.Log($"[SetCharTrans.Simulate] 位置 {posCode} 的 Transform 将在运行时设置");
        }
    }
}
