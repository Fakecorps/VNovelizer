using UnityEngine;
using Coffee.UIExtensions;

namespace VNovelizer.Core.Weather
{
    /// <summary>
    /// 天气粒子工厂：纯代码构建雪/雨 ParticleSystem（零美术资源依赖）。
    ///
    /// <para>
    /// 输出结构：GameObject（全屏 RectTransform + ParticleSystem + UIParticle），
    /// 挂在 EffectLayer（VN_EffectCanvas，sortingOrder=5：滤镜层之上、对话框之下，
    /// VHS 等画面滤镜不会扭曲天气）。坐标系 = 画布参考分辨率 1920×1080
    /// （CanvasScaler 缩放，任意分辨率一致）；贴图 = 引擎内置 Default-Particle
    /// 软圆点材质（雪=圆点；雨=同材质经 startSize3D 拉成长条雨丝）。
    /// </para>
    ///
    /// <para>
    /// 配置策略：速度完全由 velocityOverLifetime（Local 空间）控制（startSpeed=0），
    /// 发射形状 = Box 覆盖画布 + 四周溢出边距（粒子从画布外进入不露馅）。
    /// 密度/亮度由 WeatherParticleManager 按 intensity 实时调制发射率与 startColor。
    /// </para>
    /// </summary>
    public static class WeatherParticleFactory
    {
        public const string SnowId = "snow";
        public const string RainId = "rain";

        // 发射区域（1920×1080 参考画布 + 边距）
        private const float EmitWidth = 2048f;
        private const float SnowEmitHeight = 1536f;
        private const float SnowEmitTop = 250f;      // 发射区中心高于画布顶（上方落雪来源）
        private const float RainEmitHeight = 1400f;
        private const float RainEmitTop = 150f;

        /// <summary>创建下雪粒子系统（已开始播放）</summary>
        public static GameObject CreateSnow(Transform parent) => Build(parent, true);

        /// <summary>创建下雨粒子系统（已开始播放）</summary>
        public static GameObject CreateRain(Transform parent) => Build(parent, false);

        private static GameObject Build(Transform parent, bool isSnow)
        {
            string name = isSnow ? "VNEffect_WeatherSnow" : "VNEffect_WeatherRain";
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var ps = go.AddComponent<ParticleSystem>();
            if (isSnow) ConfigureSnow(ps);
            else ConfigureRain(ps);

            // Canvas 渲染桥（旧 SnowEffect/RainEffect prefab 同款挂载方式）
            go.AddComponent<UIParticle>();

            var em = ps.emission;
            em.enabled = true;
            ps.Play();
            return go;
        }

        private static void ConfigureSnow(ParticleSystem ps)
        {
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startSpeed = 0f;                                        // 速度由 velocityOverLifetime 控制
            main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 12f);
            main.startSize = new ParticleSystem.MinMaxCurve(9f, 18f);    // 参考像素直径
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 1f, 1f, 0.85f), new Color(0.88f, 0.93f, 1f, 0.55f));
            main.maxParticles = 1600;

            var em = ps.emission;
            em.rateOverTime = new ParticleSystem.MinMaxCurve(120f);      // 基准密度（× intensity）

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(EmitWidth, SnowEmitHeight, 1f);
            shape.position = new Vector3(0f, SnowEmitTop, 0f);

            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.Local;
            vel.y = new ParticleSystem.MinMaxCurve(-180f, -80f);         // 慢速飘落
            vel.x = new ParticleSystem.MinMaxCurve(-15f, 15f);           // 每片水平漂移差异
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);              // 与 x/y 保持同模式（TwoConstants）

            // 位置噪声 = 风摆（左右轻晃，飘雪感）
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 22f;
            noise.frequency = 0.45f;
            noise.scrollSpeed = 0.35f;
            noise.damping = true;

            // 出生淡入 / 消亡淡出（软边，不突兀）
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(FadeGradient(0.8f, 0.1f, 0.85f));
        }

        private static void ConfigureRain(ParticleSystem ps)
        {
            // 风斜角：≈10°（0.175 rad）±3° 随机 → 每粒 7°~13°
            const float slantRad = 0.175f;
            const float jitterRad = 0.05f;

            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startSpeed = 0f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.3f);
            main.startSize3D = true;
            main.startSizeX = new ParticleSystem.MinMaxCurve(3f, 4f);
            main.startSizeY = new ParticleSystem.MinMaxCurve(70f, 110f); // 长条雨丝
            main.startSizeZ = 1f;
            // startRotationZ 为弧度（startRotation 才是角度，勿混用）
            main.startRotationZ = new ParticleSystem.MinMaxCurve(slantRad - jitterRad, slantRad + jitterRad);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.78f, 0.85f, 1f, 0.7f), new Color(0.85f, 0.9f, 1f, 0.45f));
            main.maxParticles = 900;

            var em = ps.emission;
            em.rateOverTime = new ParticleSystem.MinMaxCurve(500f);      // 基准密度（× intensity）

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(EmitWidth, RainEmitHeight, 1f);
            shape.position = new Vector3(0f, RainEmitTop, 0f);

            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.Local;
            vel.y = new ParticleSystem.MinMaxCurve(-1500f, -950f);       // 高速下落
            vel.x = new ParticleSystem.MinMaxCurve(160f, 240f);          // 风斜水平速度（≈tan10°×vy）
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);              // 与 x/y 保持同模式（TwoConstants）

            // 丝尾淡出（消亡前柔和消失）
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(FadeGradient(0.9f, 0.8f, 1f));
        }

        /// <summary>alpha 淡入（0→holdIn 到 holdIn）保持到 holdOut 后淡出到 0</summary>
        private static Gradient FadeGradient(float peak, float holdIn, float holdOut)
        {
            var g = new Gradient();
            g.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 1f),
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(peak, holdIn),
                    new GradientAlphaKey(peak, holdOut),
                    new GradientAlphaKey(0f, 1f),
                });
            return g;
        }
    }
}
