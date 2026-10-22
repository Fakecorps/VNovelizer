Shader "VNovelizer/Filters/Vignette"
{
    // ============================================================
    // 画面滤镜：暗角（ScreenFilterSystem 内置，filter_vignette(strength[, radius[, softness[, duration]]])）
    // _Strength 0..1（强度）/ _Radius（暗角起点半径）/ _Softness（过渡软度）/
    // _Color（暗角颜色，默认黑）——暗角为圆形，经 _Aspect 校正保持圆形对称。
    // 滤镜 Shader 契约见 Docs/VNScreenFilterSystem.md（_MainTex/_Weight/_Aspect）。
    // Built-in 兼容（无 RenderPipeline tag），存放于包内 Resources 保证进构建。
    // ============================================================
    Properties
    {
        _MainTex ("Input Texture", 2D) = "white" {}
        [HideInInspector] _Weight ("Filter Weight", Range(0,1)) = 0
        [HideInInspector] _Aspect ("Screen Aspect", Float) = 1.777778
        _Strength ("Strength", Range(0, 1)) = 0.8
        _Radius ("Radius", Range(0, 1.5)) = 0.85
        _Softness ("Softness", Range(0.01, 1)) = 0.4
        _Color ("Vignette Color", Color) = (0, 0, 0, 1)
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
            float _Radius;
            float _Softness;
            fixed4 _Color;

            v2f vert(appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed3 col = tex2D(_MainTex, i.uv).rgb;

                float2 c = i.uv - 0.5;
                c.x *= _Aspect;
                float d = length(c);
                float v = smoothstep(_Radius, max(_Radius - _Softness, 0.001), d);

                col = lerp(col, _Color.rgb, v * _Strength * _Weight);
                return fixed4(col, 1.0);
            }
            ENDCG
        }
    }
}
