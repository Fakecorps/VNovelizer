using System.Collections;
using UnityEngine;
using VNovelizer.Core.API;
using VNovelizer.Core.Commands.Meta;

namespace VNovelizer.Core.Commands
{
    [VNCommandMeta(VNCommandCategory.Performance,
        "停止粒子特效并延迟回收（停止发射，已发射粒子 5 秒飘完）")]
    public class StopParticleCommand : VNCommand
    {
        [VNParam(0, "effect", VNParamType.ParticleName,
            Description = "特效资源名（与 playparticle 参数一致）")]
        public override string CommandName { get { return "stopparticle"; } }

        public override bool Execute(string args)
        {
            if (string.IsNullOrEmpty(args)) return false;
            string effectName = args.Trim();

            // 1. 注销状态
            VNAPI.UnregisterEffect(effectName);

            // 2. 【Fix-8】快进/skip 语境：同步立即写终态（停止发射），
            //    仅"延迟回收"保留异步（清理性质、不污染演出画面）。
            Transform parent = VNAPI.GetEffectLayer();
            if (parent != null)
            {
                Transform target = parent.Find("VNEffect_" + effectName);
                if (target != null)
                {
                    ParticleSystem ps = target.GetComponentInChildren<ParticleSystem>();
                    if (ps != null)
                    {
                        var emission = ps.emission;
                        emission.enabled = false;
                    }
                    MonoManager.GetInstance().StartCoroutine(RecycleDelayed(target.gameObject, effectName, 5.0f));
                }
            }
            return true;
        }

        public override IEnumerator ExecuteAsync(string effectName)
        {
            if (string.IsNullOrEmpty(effectName)) yield break;

            // 【Fix-12】异步路径（命令链直接调用 ExecuteAsync）也必须注销注册状态：
            // 否则 VNManager.activeEffects 仍认为粒子在播 → 存档写入 ActiveEffects →
            // 读档恢复时把已停止的特效"复活"。
            VNAPI.UnregisterEffect(effectName.Trim());

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