using UnityEngine;
using VNovelizer.Core.API;

namespace VNovelizer.Core.Commands
{
    public class SnowCommand : VNCommand
    {
        public override string CommandName { get { return "snow"; } }

        public override bool Execute(string args)
        {
            // 1. 注册状态 (记账) - 必须要有！
            VNManager.GetInstance().RegisterEffect("Snow");

            // 2. 获取挂点
            Transform parent = VNAPI.GetEffectLayer();
            if (parent == null) return false;

            // 3. 检查是否已经有了
            if (parent.Find("SnowEffect") != null)
            {
                // 已经有了就不重复生成，但状态必须注册(防止读档后状态丢失)
                return true;
            }

            string path = VNProjectConfig.Instance.ParticalEffectPath + "/SnowEffect";
            PoolManager.GetInstance().GetObj(path, (go) =>
            {
                if (go == null)
                {
                    Debug.LogError($"[Snow] 无法从对象池加载: {path}");
                    return;
                }

                go.name = "SnowEffect"; // 必须和 StopSnow 查找的名字一致
                go.transform.SetParent(parent, false);

                RectTransform rect = go.GetComponent<RectTransform>();
                if (rect != null)
                {
                    rect.anchorMin = Vector2.zero;
                    rect.anchorMax = Vector2.one;
                    rect.offsetMin = Vector2.zero;
                    rect.offsetMax = Vector2.zero;
                    rect.localScale = Vector3.one;
                }

                var ps = go.GetComponent<ParticleSystem>();
                if (ps != null)
                {
                    var emission = ps.emission;
                    emission.enabled = true; // 重新开启发射
                    ps.Play();
                }

                var uiParticle = go.GetComponent<Coffee.UIExtensions.UIParticle>();
                if (uiParticle != null) uiParticle.Play();
            });

            return true;
        }

        // 模拟逻辑：只记账，不生成物体
        public override void Simulate(string args)
        {
            VNManager.GetInstance().RegisterEffect("snow");
        }
    }
}