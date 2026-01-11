using System.Collections;
using UnityEngine;
using VNovelizer.Core.API;

namespace VNovelizer.Core.Commands
{
    public class StopSnowCommand : VNCommand
    {
        public override string CommandName { get { return "stopsnow"; } }

        public override bool Execute(string args)
        {
            // 1. 注销状态 (记账) - 必须要有！
            VNManager.GetInstance().UnregisterEffect("Snow");

            // 2. 启动销毁协程
            MonoManager.GetInstance().StartCoroutine(ExecuteAsync(args));
            return true;
        }

        public override IEnumerator ExecuteAsync(string args)
        {
            Transform parent = VNAPI.GetEffectLayer();
            if (parent == null) yield break;

            var targets = new System.Collections.Generic.List<Transform>();
            foreach (Transform child in parent)
            {
                if (child.name == "SnowEffect") targets.Add(child);
            }

            foreach (Transform snowObj in targets)
            {
                ParticleSystem ps = snowObj.GetComponent<ParticleSystem>();
                if (ps != null)
                {
                    var emission = ps.emission;
                    emission.enabled = false;
                }

                // 延迟回收
                MonoManager.GetInstance().StartCoroutine(RecycleDelayed(snowObj.gameObject, 10.0f));
            }
            yield break;
        }

        private IEnumerator RecycleDelayed(GameObject go, float delay)
        {
            yield return new WaitForSeconds(delay);
            if (go != null)
            {
                string path = VNProjectConfig.Instance.ParticalEffectPath + "/SnowEffect"; // 注意路径拼接
                PoolManager.GetInstance().PushObj(path, go);
            }
        }

        // 【新增】模拟逻辑：只注销状态
        public override void Simulate(string args)
        {
            VNManager.GetInstance().UnregisterEffect("snow");
        }
    }
}