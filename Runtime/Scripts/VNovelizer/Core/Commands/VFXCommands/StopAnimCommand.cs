using UnityEngine;
using VNovelizer.Core.API;

namespace VNovelizer.Core.Commands
{
    public class StopAnimCommand : VNCommand
    {
        public override string CommandName { get { return "stopanim"; } }

        public override bool Execute(string args)
        {
            if (string.IsNullOrEmpty(args)) return false;
            string animName = args.Trim();

            // 1. 注销状态
            VNManager.GetInstance().UnregisterEffect("VNAnim_" + animName);

            // 2. 查找并回收
            Transform parent = VNAPI.GetEffectLayer();
            if (parent != null)
            {
                Transform target = parent.Find("VNAnim_" + animName);
                if (target != null)
                {
                    // 【修复】回收路径必须与 PlayAnimCommand 的加载路径一致
                    // （AnimationPath，如 "VNovelizerRes/VFX/Animation"），
                    // 旧代码误用 ParticalEffectPath + "/Animation"，会把对象推进错误的池：
                    // 下次 playanim 按正确路径取不到（重复实例化），错误池对象永久闲置。
                    string resPath = VNProjectConfig.Instance.AnimationPath + "/" + animName;
                    PoolManager.GetInstance().PushObj(resPath, target.gameObject);
                }
            }

            return true;
        }

        public override void Simulate(string args)
        {
            if (!string.IsNullOrEmpty(args))
            {
                VNManager.GetInstance().UnregisterEffect("VNAnim_" + args.Trim());
            }
        }
    }
}