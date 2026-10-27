using UnityEngine;
using Coffee.UIExtensions;

namespace VNovelizer.Core.Weather
{
    /// <summary>
    /// 天气粒子工厂：构建雪/雨 ParticleSystem 的初始参数（零美术资源依赖）。
    ///
    /// <para>
    /// 【VFX 注册表扩展 2026-10-23】本类承担双重角色：
    /// ① 编辑器侧显式烘焙菜单的**出厂参数构建源**（VNWeatherParticleGenerator 复用
    /// 本类构建 GameObject 后经 PrefabUtility 落盘成预制体资产；仅用户明确确认时
    /// 才覆盖包内模板。平时粒子参数以注册表引用的 prefab 为准，脚本绝不自动覆盖）；
    /// ② 运行时零配置兜底（VFX 注册表粒子条目 Prefab 为空时由
    /// WeatherParticleManager 直接构建）。运行时正常路径 = 注册表 + 预制体实例化。
    /// </para>
    ///
    /// <para>
    /// 输出结构：GameObject（全屏 RectTransform + ParticleSystem + UIParticle），
    /// 挂在 EffectLayer（VN_EffectCanvas，sortingOrder=5：滤镜层之上、对话框之下，
    /// VHS 等画面滤镜不会扭曲天气）。坐标系 = 画布参考分辨率 1920×1080
    /// （CanvasScaler 缩放，任意分辨率一致）；贴图 = 程序生成软圆点
    /// （雪=圆点；雨=同贴图经 startSize3D 拉成长条雨丝）。
    /// </para>
    ///
    /// <para>
    /// 初始参数策略（仅作内置模板出厂值）：速度完全由 velocityOverLifetime（Local 空间）
    /// 控制（startSpeed=0），发射形状 = 画布上沿附近的 Box（雪从屏幕外进入，雨在
    /// 顶部渐入）。密度/亮度由 VN_Particle.SetIntensity 按 intensity 实时调制发射率与
    /// startColor——这是命令语义，与 prefab 基准值相乘（改 prefab 基准同样生效）。
    /// </para>
    /// </summary>
    public static class WeatherParticleFactory
    {
        public const string SnowId = "snow";
        public const string RainId = "rain";

        // 1920×1080 参考画布中心为原点，上沿 Y=540；雪全部在屏外出生，雨在顶部渐入。
        private const float SnowEmitWidth = 2160f;
        private const float SnowEmitHeight = 160f;
        private const float SnowEmitTop = 700f;     // 出生 Y=620..780
        private const float RainEmitWidth = 2360f;
        private const float RainEmitHeight = 440f;
        private const float RainEmitTop = 720f;     // 出生 Y=500..940，尾段渐入画面
        private const float RainEmitRightOffset = 100f; // 抵消风吹向左产生的侧边稀疏

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
            // Hierarchy 缩放会让模拟尺寸乘 transform.lossyScale（Canvas scaleFactor 变化时
            // 粒子范围/尺寸双重缩放）——粒子按画布本地像素建模，恒用 Local 缩放模式
            var mainModule = ps.main;
            mainModule.scalingMode = ParticleSystemScalingMode.Local;

            // 软圆点贴图材质：不显式挂载时 UIParticle 烘焙取不到贴图，粒子渲染成方块
            // （运行时兜底 = 静态共享临时资源；编辑器烘焙 = VNWeatherParticleGenerator.PersistSoftDotAssets 落盘）
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = GetSoftDotMaterial();

            // Canvas 渲染桥。UIParticle 默认 m_Scale3D=(10,10,10)（为"世界空间小粒子放大到 UI"设计），
            // 会把粒子渲染放大 10 倍——发射区/雪片尺寸全部 ×10（粒子范围远超画布的根因）。
            // 我们的粒子按画布本地像素（1920×1080 参考）建模：scale3D 归一 + 关闭自动缩放，
            // 渲染恒 1:1（GetWorldScale = scale3DForCalc × parentScale = (1/sf) × sf = 1，任意分辨率正确）
            var uiParticle = go.AddComponent<UIParticle>();
            uiParticle.scale3D = Vector3.one;
            uiParticle.autoScalingMode = UIParticle.AutoScalingMode.None;
            uiParticle.raycastTarget = false;

            // 挂 VN_Particle 契约组件（运行时兜底由管理器消费接口；编辑器烘焙随 prefab 序列化）
            if (go.GetComponent<VN_Particle>() == null) go.AddComponent<VN_Particle>();

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
            main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 9f);
            main.startSize = new ParticleSystem.MinMaxCurve(5f, 14f);    // 参考像素直径
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.84f, 0.92f, 1f, 0.42f), new Color(1f, 1f, 1f, 0.82f));
            main.maxParticles = 1600;

            var em = ps.emission;
            em.rateOverTime = new ParticleSystem.MinMaxCurve(88f);       // 基准密度（× intensity）

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(SnowEmitWidth, SnowEmitHeight, 1f);
            shape.position = new Vector3(0f, SnowEmitTop, 0f);

            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.Local;
            vel.y = new ParticleSystem.MinMaxCurve(-185f, -95f);         // 慢速飘落
            vel.x = new ParticleSystem.MinMaxCurve(-24f, 24f);           // 每片水平漂移差异
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);              // 与 x/y 保持同模式（TwoConstants）

            var rotation = ps.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(0.43633232f);

            // 位置噪声 = 轻柔风摆（左右轻晃，飘雪感；强度/频率克制，避免雪花横跳鬼畜）
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 7f;
            noise.frequency = 0.22f;
            noise.scrollSpeed = 0.18f;
            noise.damping = true;

            // 出生淡入 / 消亡淡出（软边，不突兀）
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(FadeGradient(0.8f, 0.1f, 0.85f));
        }

        private static void ConfigureRain(ParticleSystem ps)
        {
            // 向左微倾（弧度），与负 X 的水平风速一致。
            const float slantMin = -0.19f;
            const float slantMax = -0.11f;

            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startSpeed = 0f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.25f);
            main.startSize3D = true;
            main.startSizeX = new ParticleSystem.MinMaxCurve(5f, 8f);
            main.startSizeY = new ParticleSystem.MinMaxCurve(160f, 250f);
            main.startSizeZ = 1f;
            // startRotationZ 为弧度（startRotation 才是角度，勿混用）。
            main.startRotationZ = new ParticleSystem.MinMaxCurve(slantMin, slantMax);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.8f, 0.88f, 1f, 0.58f), new Color(0.94f, 0.97f, 1f, 0.88f));
            main.maxParticles = 900;

            var em = ps.emission;
            em.rateOverTime = new ParticleSystem.MinMaxCurve(520f);      // 基准密度（× intensity）

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(RainEmitWidth, RainEmitHeight, 1f);
            shape.position = new Vector3(RainEmitRightOffset, RainEmitTop, 0f);

            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.Local;
            vel.y = new ParticleSystem.MinMaxCurve(-1280f, -900f);       // 高速下落
            vel.x = new ParticleSystem.MinMaxCurve(-195f, -110f);        // 风斜水平速度（向左）
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);              // 与 x/y 保持同模式（TwoConstants）

            // 前 8% 淡入、70% 前保持亮度，再于消亡前柔和淡出。
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(FadeGradient(0.95f, 0.08f, 0.7f));
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

        // ==================== 软圆点贴图（零美术资源依赖） ====================

        private static Texture2D _sharedSoftDot;
        private static Material _sharedSoftDotMaterial;

        /// <summary>
        /// 软圆点材质（进程级共享缓存）。贴图 = 程序生成 64×64 径向渐变
        /// （核心实心 → 边缘平滑衰减到 0，Bilinear 采样）。
        /// 运行时兜底直接使用（HideAndDontSave，进程级缓存无泄漏）；
        /// 编辑器烘焙 prefab 时由 VNWeatherParticleGenerator.PersistSoftDotAssets
        /// 落盘为 PNG + 材质资产并替换引用（临时对象无法序列化进 prefab）。
        /// </summary>
        public static Material GetSoftDotMaterial()
        {
            if (_sharedSoftDotMaterial == null)
            {
                _sharedSoftDot = CreateSoftDotTexture();
                var shader = Shader.Find("Unlit/Transparent");
                if (shader == null) shader = Shader.Find("Sprites/Default");
                _sharedSoftDotMaterial = new Material(shader)
                {
                    name = "VN_SoftDotMaterial",
                    hideFlags = HideFlags.HideAndDontSave,
                };
                _sharedSoftDotMaterial.mainTexture = _sharedSoftDot;
            }
            return _sharedSoftDotMaterial;
        }

        /// <summary>程序生成软圆点贴图（白色，alpha 径向衰减；HideAndDontSave 临时对象）</summary>
        private static Texture2D CreateSoftDotTexture()
        {
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "VN_SoftDot",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };

            float half = size * 0.5f;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - half) / half;
                    float dy = (y + 0.5f - half) / half;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    // 核心 0~0.55 实心，0.55~1 平滑（smoothstep）衰减到 0
                    float t = Mathf.Clamp01(1f - Mathf.Max(d - 0.55f, 0f) / 0.45f);
                    t = t * t * (3f - 2f * t);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(t * 255f));
                }
            }
            tex.SetPixels32(pixels);
            // 【Fix-SoftDot-Readable 2026-10-23】绝不能用 Apply(false, true) 释放 CPU 内存：
            // 编辑器烘焙（VNWeatherParticleGenerator.PersistSoftDotAssets）要用 EncodeToPNG
            // 读回像素写盘，GPU-only（makeNoLongerReadable）纹理会抛 "not readable"。
            // 64×64 RGBA32 仅 16KB，保留 CPU 副本零成本。
            tex.Apply(false, false);
            return tex;
        }
    }
}
