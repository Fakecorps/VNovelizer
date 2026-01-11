using System.Collections;
using UnityEngine;
using VNovelizer.Core.API;

namespace VNovelizer.Core.Commands
{
    public class StopParticleCommand : VNCommand
    {
        public override string CommandName { get { return "stopparticle"; } }

        public override bool Execute(string args)
        {
            if (string.IsNullOrEmpty(args)) return false;
            string effectName = args.Trim();

            // 1. 注销状态
            VNAPI.UnregisterEffect(effectName);

            // 2. 执行销毁流程
            MonoManager.GetInstance().StartCoroutine(ExecuteAsync(effectName));
            return true;
        }

        public override IEnumerator ExecuteAsync(string effectName)
        {
            Transform parent = VNAPI.GetEffectLayer();
            if (parent == null) yield break;

            string objName = "VNEffect_" + effectName;
            Transform target = parent.Find(objName);

            if (target != null)
            {
                // 停止发射
                ParticleSystem ps = target.GetComponentInChildren<ParticleSystem>();
                if (ps != null)
                {
                    var emission = ps.emission;
                    emission.enabled = false;
                }

                // 延迟回收 (给它 5秒飘完)
                MonoManager.GetInstance().StartCoroutine(RecycleDelayed(target.gameObject, effectName, 5.0f));
            }
            yield break;
        }

        private IEnumerator RecycleDelayed(GameObject go, string effectName, float delay)
        {
            yield return new WaitForSeconds(delay);
            if (go != null)
            {
                string path = VNProjectConfig.Instance.ParticalEffectPath + "/" + effectName;
                PoolManager.GetInstance().PushObj(path, go);
            }
        }

        public override void Simulate(string args)
        {
            if (!string.IsNullOrEmpty(args))
            {
                VNAPI.UnregisterEffect(args.Trim());
            }
        }
    }
}