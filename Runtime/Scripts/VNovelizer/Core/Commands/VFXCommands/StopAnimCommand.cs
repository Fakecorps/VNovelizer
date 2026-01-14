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
                    string resPath = VNProjectConfig.Instance.ParticalEffectPath + "/Animation/" + animName;
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