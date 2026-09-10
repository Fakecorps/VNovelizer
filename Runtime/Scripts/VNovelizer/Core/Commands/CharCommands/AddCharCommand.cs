using UnityEngine;
using VNovelizer.Core.Commands.Meta;
using VNovelizer.Core.Theater;

namespace VNovelizer.Core.Commands
{
    /// <summary>
    /// 自由角色注册命令（R14 新增）。
    /// 格式：addChar(Char#Group#Emo, x, y, scale, posID)
    ///
    /// <para>
    /// 仅登记不登台（visible=false）：注册后可用 showchar(posID) 展示，
    /// 之后 posID 可供全部角色槽位命令引用（charfadein/out、charmove、
    /// charjump、charflip、setchartrans、shake 的角色分支）。
    /// </para>
    ///
    /// <para>
    /// 生命周期：会话级（跨行存活）——直到 removeChar(posID) / 淡出隐藏 /
    /// 切换剧本清场 / 返回主菜单。
    /// </para>
    ///
    /// <para>
    /// 规则：同名 posID（大小写不敏感）覆盖更新；posID 不能与标准槽位
    /// L/ML/M/MR/R（及全名别名）冲突；不含 , ( ) 引号 | &amp; 空格。
    /// </para>
    /// </summary>
    [VNCommandMeta(VNCommandCategory.Performance,
        "注册自由角色（仅登记不显示，用 showchar(posID) 展示；posID 可供全部槽位命令引用）")]
    public class AddCharCommand : VNCommand
    {
        [VNParam(0, "charRef", VNParamType.CharacterRef,
            Description = "立绘引用 角色#分组#表情")]
        [VNParam(1, "x", VNParamType.Float, Min = -960f, Max = 960f, Default = "0",
            Description = "X 坐标（剧本像素，原点=画面中心）")]
        [VNParam(2, "y", VNParamType.Float, Min = -540f, Max = 540f, Default = "0",
            Description = "Y 坐标（剧本像素，原点=画面中心）")]
        [VNParam(3, "scale", VNParamType.Float, Min = 0.1f, Max = 5f, Default = "1",
            Description = "缩放（正值大小）")]
        [VNParam(4, "posID", VNParamType.String,
            Description = "自定义槽位 ID（供 showchar/charjump 等引用；保留名 L/ML/M/MR/R 不可用；大小写不敏感唯一）")]
        public override string CommandName { get { return "addchar"; } }

        public override bool Execute(string args)
        {
            return RegisterFromArgs(args);
        }

        /// <summary>
        /// 快进/读档预演：与 Execute 行为一致（注册是数据态操作，无动画）。
        /// FastForwardToLine 开头会 ClearFreeChars，随后 Simulate 到本行时重新注册，
        /// 保证"跳行目标之前没有 addChar"时自由角色不残留。
        /// </summary>
        public override void Simulate(string args)
        {
            RegisterFromArgs(args);
        }

        private static bool RegisterFromArgs(string args)
        {
            if (string.IsNullOrEmpty(args))
            {
                Debug.LogError("[AddChar] 参数不能为空，格式: addChar(Char#Group#Emo, x, y, scale, posID)");
                return false;
            }

            string[] parts = args.Split(',');
            if (parts.Length < 5)
            {
                Debug.LogError($"[AddChar] 参数不足（需 5 个：Char#Group#Emo, x, y, scale, posID），当前: \"{args}\"");
                return false;
            }

            string charRef = parts[0].Trim();
            if (charRef.Length == 0)
            {
                Debug.LogError("[AddChar] 立绘引用不能为空（格式 角色#分组#表情）");
                return false;
            }

            if (!float.TryParse(parts[1].Trim(), out float x) ||
                !float.TryParse(parts[2].Trim(), out float y) ||
                !float.TryParse(parts[3].Trim(), out float scale))
            {
                Debug.LogError($"[AddChar] 无法解析坐标/缩放参数: \"{args}\"");
                return false;
            }

            string posID = parts[4].Trim();
            return TheaterManager.GetInstance().RegisterFreeChar(posID, charRef, new Vector2(x, y), scale);
        }
    }
}
