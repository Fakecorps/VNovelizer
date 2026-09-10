using UnityEngine;
using VNovelizer.Core.Commands.Meta;
using VNovelizer.Core.Theater;

namespace VNovelizer.Core.Commands
{
    /// <summary>
    /// 移除并注销自由角色（R14 新增，与 addChar 配套）。
    /// 格式：removeChar(posID)
    /// 立即中断该角色的活动动画并移除演员；未注册的 posID 打警告不报错（容错）。
    /// 兼容语义：charfadeout(posID) 淡出隐藏、showchar(posID, 空) 亦可隐藏，均不注销注册。
    /// </summary>
    [VNCommandMeta(VNCommandCategory.Performance,
        "移除并注销自由角色（addChar 注册的角色；立即生效）")]
    public class RemoveCharCommand : VNCommand
    {
        [VNParam(0, "posID", VNParamType.String,
            Description = "addChar 注册的自定义槽位 ID")]
        public override string CommandName { get { return "removechar"; } }

        public override bool Execute(string args)
        {
            return RemoveFromArgs(args);
        }

        /// <summary>快进/读档预演：与 Execute 行为一致（移除是数据态操作，无动画）。</summary>
        public override void Simulate(string args)
        {
            RemoveFromArgs(args);
        }

        private static bool RemoveFromArgs(string args)
        {
            string posID = args?.Trim();
            if (string.IsNullOrEmpty(posID))
            {
                Debug.LogError("[RemoveChar] 参数不能为空，格式: removeChar(posID)");
                return false;
            }
            return TheaterManager.GetInstance().UnregisterFreeChar(posID);
        }
    }
}
