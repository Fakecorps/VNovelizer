using UnityEngine;
using VNovelizer.Core.API;
using VNovelizer.Core.Commands.Meta;
using VNovelizer.Core.Theater;

namespace VNovelizer.Core.Commands.SystemCommands
{
    /// <summary>
    /// 【系统命令】按槽位显示/隐藏立绘。
    ///
    /// <para><b>格式</b>：<c>showChar(pos[, charRef])</c></para>
    ///
    /// <para>
    /// <b>隐式绑定</b>：第二参数留空 = 引用本行对应立绘列
    /// （L→CharLeft、ML→CharMid_Left、M→CharMid、MR→CharMid_Right、R→CharRight）。
    /// </para>
    ///
    /// <para>
    /// <b>空 = 隐藏</b>：立绘列**不继承**（这是框架的既定规则）。数据列为空时本命令
    /// 隐藏该槽位，与引擎 <c>UpdateCharacter</c> 的"空槽与 hide 等价"完全一致——
    /// 因此本命令**不会**像 showbg/playBGM 那样"无值即跳过"。
    /// </para>
    /// </summary>
    [VNCommandMeta(VNCommandCategory.System,
        "按槽位显示立绘（空值=隐藏该槽）。第二参数留空则引用本行对应立绘列")]
    public class ShowCharCommand : VNCommand
    {
        [VNParam(0, "pos", VNParamType.SlotCode,
            Description = "槽位：L / ML / M / MR / R（或全名）")]
        [VNParam(1, "charRef", VNParamType.CharacterRef,
            Optional = true, ImplicitBinding = true, BoundColumn = "(对应立绘列)",
            Description = "立绘引用 角色#分组#表情；留空则引用本行对应立绘列")]
        public override string CommandName => "showchar";

        /// <summary>解析出 (槽位内部ID, 立绘引用)；槽位非法时返回 false。</summary>
        private static bool ResolveArgs(string args, out string slotId, out string charRef)
        {
            slotId = null;
            charRef = null;

            if (string.IsNullOrWhiteSpace(args))
            {
                Debug.LogError("[ShowChar] 参数不能为空，格式: showChar(pos[, charRef])");
                return false;
            }

            string[] parts = args.Split(',');
            string posCode = TheaterManager.NormalizePosCode(parts[0]);
            if (posCode == null)
            {
                Debug.LogError($"[ShowChar] 未知槽位: {parts[0].Trim()}（可用 L/ML/M/MR/R 或全名）");
                return false;
            }

            slotId = SlotIdFromPosCode(posCode);

            if (parts.Length >= 2 && !string.IsNullOrWhiteSpace(parts[1]))
            {
                charRef = parts[1].Trim();
                return true;
            }

            // 隐式绑定：读本行对应立绘列（列为空 → charRef 为空 → 隐藏该槽，符合"不继承"规则）
            var ctx = VNAPI.GetCurrentLineContext();
            charRef = ctx?.GetCharBySlot(posCode) ?? "";
            return true;
        }

        /// <summary>
        /// 槽位代码 → VNManager 内部槽位 ID。
        /// VNManager 的立绘字典用 "Left"/"MidLeft"/"Mid"/"MidRight"/"Right" 作 key，
        /// 而 TheaterManager.NormalizePosCode 归一化为 "L"/"ML"/"M"/"MR"/"R"。
        /// </summary>
        private static string SlotIdFromPosCode(string posCode)
        {
            switch (posCode)
            {
                case "L":  return "Left";
                case "ML": return "MidLeft";
                case "M":  return "Mid";
                case "MR": return "MidRight";
                case "R":  return "Right";
                default:   return posCode;
            }
        }

        public override bool Execute(string args)
        {
            if (!ResolveArgs(args, out string slotId, out string charRef)) return false;

            VNManager.GetInstance().SysShowCharacter(slotId, charRef);
            return true;
        }

        public override void Simulate(string args)
        {
            if (!ResolveArgs(args, out string slotId, out string charRef)) return;

            VNManager.GetInstance().SysSimulateCharacter(slotId, charRef);
        }
    }
}
