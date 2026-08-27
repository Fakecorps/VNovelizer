using UnityEngine;
using VNovelizer.Core.Theater;
using VNovelizer.Core.Commands.Meta;

namespace VNovelizer.Core.Commands
{
    /// <summary>
    /// 角色水平翻转命令（剧场层实现）
    /// 格式：charflip(位置, [可选]方向)
    /// 示例1：charflip(M) -> 切换翻转状态 (左变右，右变左)
    /// 示例2：charflip(M, -1) -> 强制面朝左
    /// 示例3：charflip(M, 1) -> 强制面朝右
    /// 翻转状态源仍为 VNManager.currentCharactersScaleX（纯数据，随存档持久化）。
    /// </summary>
    [VNCommandMeta(VNCommandCategory.Performance,
        "角色立绘水平翻转（要求同行对应立绘列已填；翻转状态随存档持久化）")]
    public class CharFlipCommand : VNCommand
    {
        [VNParam(0, "pos", VNParamType.SlotCode,
            Description = "槽位（L/ML/M/MR/R 或全名），要求同行对应立绘列非空")]
        [VNParam(1, "direction", VNParamType.String, Optional = true,
            Description = "缺省=切换翻转；-1 或 left=强制朝左；1 或 right=强制朝右")]
        public override string CommandName { get { return "charflip"; } }

        public override bool Execute(string args)
        {
            if (string.IsNullOrEmpty(args)) return false;

            string[] parts = args.Split(',');
            string posCode = TheaterManager.NormalizePosCode(parts[0]);
            if (posCode == null)
            {
                Debug.LogError($"[CharFlip] 未知位置: {parts[0].Trim()}（可用 L/ML/M/MR/R 或全名）");
                return false;
            }

            var vnManager = VNManager.GetInstance();
            var theater = TheaterManager.GetInstance();

            // 1. 确定翻转方向（基于当前状态符号，与旧实现语义一致）
            float currentScaleX = vnManager.GetCharacterScaleX(posCode);
            float targetScaleX;

            if (parts.Length > 1)
            {
                if (float.TryParse(parts[1].Trim(), out float val))
                {
                    targetScaleX = Mathf.Sign(val);
                }
                else
                {
                    string dir = parts[1].Trim().ToLower();
                    targetScaleX = dir == "left" ? -1f : 1f;
                }
            }
            else
            {
                targetScaleX = currentScaleX * -1f;
            }

            // 2. 更新数据状态（Simulate 与 Execute 共享的唯一事实源）
            vnManager.SetCharacterScaleX(posCode, targetScaleX);

            // 3. 应用到剧场（演员不在台时只更新数据，登台时 OnShowCharacter 会读取）
            var state = theater.GetState(posCode);
            if (state != null)
            {
                theater.SetFlip(posCode, targetScaleX < 0f);
                Debug.Log($"[CharFlip] 角色 {posCode} 翻转至 X={targetScaleX}");
                return true;
            }

            Debug.Log($"[CharFlip] 角色 {posCode} 不在台上，翻转状态已记录: X={targetScaleX}");
            return true;
        }

        public override void Simulate(string args)
        {
            if (string.IsNullOrEmpty(args)) return;

            string[] parts = args.Split(',');
            string posCode = TheaterManager.NormalizePosCode(parts[0]);
            if (posCode == null) return;

            string charData = VNManager.GetInstance().GetCharacterData(posCode);
            if (string.IsNullOrEmpty(charData) || charData == "hide")
            {
                Debug.LogWarning($"[CharFlip.Simulate] 位置 {posCode} 没有角色，跳过翻转");
                return;
            }

            float currentScaleX = VNManager.GetInstance().GetCharacterScaleX(posCode);
            float targetScaleX;
            if (parts.Length > 1)
            {
                if (float.TryParse(parts[1].Trim(), out float val))
                    targetScaleX = Mathf.Sign(val);
                else
                    targetScaleX = parts[1].Trim().ToLower() == "left" ? -1f : 1f;
            }
            else
            {
                targetScaleX = currentScaleX * -1f;
            }

            VNManager.GetInstance().SetCharacterScaleX(posCode, targetScaleX);
            Debug.Log($"[CharFlip.Simulate] 位置 {posCode} 翻转状态更新为: {targetScaleX}");
        }
    }
}
