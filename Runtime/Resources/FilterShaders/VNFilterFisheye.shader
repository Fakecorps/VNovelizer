Shader "VNovelizer/Filters/Fisheye"
{
    // ============================================================
    // 画面滤镜：鱼眼（ScreenFilterSystem 内置，filter_fisheye(strength[, duration])）
    //
    // 滤镜 Shader 契约（见 Docs/VNScreenFilterSystem.md）：
    //   _MainTex  输入纹理（剧场相机 RT，管理器经 Graphics.Blit 传入）
    //   _Weight   全局混合权重 0..1（0=无效果 1=满效果；管理器每帧写入，
    //             命令的渐变淡入淡出/中断到终态全部依赖它）
    //   _Aspect   屏幕宽高比（管理器每帧写入，供圆形效果校正）
    //   _Strength 鱼眼强度（0..2，0.6 默认；桶形畸变 r' = r(1 + k·r²)）
    //
    // 存放位置：Runtime/Resources/FilterShaders/（包内 Resources 保证进构建；
    // 加载顺序 Shader.Find 优先、Resources.Load 兜底）。
    // Built-in 兼容（无 RenderPipeline tag）：URP 项目走官方兼容编译路径。
    // 注意：切勿加 "RenderPipeline"="UniversalPipeline" tag——不 include URP
    // Core.hlsl 时矩阵宏未声明会编译失败导致材质品红（BGTransition 实测教训）。
    // ============================================================
    Properties
    {
        _MainTex ("Input Texture", 2D) = "white" {}
        [HideInInspector] _Weight ("Filter Weight", Range(0,1)) = 0
        [HideInInspector] _Aspect ("Screen Aspect", Float) = 1.777778
        _Strength ("Strength", Range(0, 2)) = 0.6
    }

    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _Weight;
            float _Aspect;
            float _Strength;

            v2f vert(appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.texcoord, _MainTex); // Blit 平台翻转校正
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed3 orig = tex2D(_MainTex, i.uv).rgb;

                // 桶形畸变（aspect 校正坐标下保持圆形对称）
                float2 c = i.uv - 0.5;
                c.x *= _Aspect;
                float r = length(c);
                float k = _Strength * _Weight;
                float2 dir = c / max(r, 0.0001);
                float r2 = r * (1.0 + k * r * r);
                float2 suv = dir * r2;
                suv.x /= _Aspect;
                suv += 0.5;

                fixed3 dist = tex2D(_MainTex, suv).rgb; // 越界采样被 Clamp 边缘吸收
                return fixed4(lerp(orig, dist, _Weight), 1.0);
            }
            ENDCG
        }
    }
}
