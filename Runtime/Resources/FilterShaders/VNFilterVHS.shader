Shader "VNovelizer/Filters/VHS"
{
    // ============================================================
    // 画面滤镜：VHS 模拟（ScreenFilterSystem 内置，filter_vhs(intensity[, duration])）
    // 要素：横向行带抖动 + 色差 RGB 分离 + 扫描线 + 胶片噪点颗粒，
    // 强度统一由 _Intensity（0..1）缩放、_Weight 全局混合。
    // 滤镜 Shader 契约见 Docs/VNScreenFilterSystem.md（_MainTex/_Weight/_Aspect）。
    // Built-in 兼容（无 RenderPipeline tag），存放于包内 Resources 保证进构建。
    // ============================================================
    Properties
    {
        _MainTex ("Input Texture", 2D) = "white" {}
        [HideInInspector] _Weight ("Filter Weight", Range(0,1)) = 0
        [HideInInspector] _Aspect ("Screen Aspect", Float) = 1.777778
        _Intensity ("Intensity", Range(0, 1)) = 0.6
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
            float _Intensity;

            float VNFHash21(float2 p)
            {
                p = frac(p * float2(234.34, 435.345));
                p += dot(p, p + 34.23);
                return frac(p.x * p.y);
            }

            float VNFHash1(float n)
            {
                return frac(sin(n) * 43758.5453);
            }

            v2f vert(appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.uv;
                float a = _Intensity * _Weight;

                // ① 横向行带抖动（8 行一条带，时间量化成块状跳变）
                float row = floor(uv.y * 300.0);
                float band = floor(row / 8.0);
                float n = VNFHash1(band + floor(_Time.y * 12.0) * 17.0);
                float jump = (n - 0.5) * 0.06 * a * step(0.6, frac(n * 7.31));
                uv.x += jump;

                // ② 色差 RGB 分离（高频正弦摆动）
                float shift = a * 0.006 * sin(_Time.y * 30.0);
                fixed r = tex2D(_MainTex, uv + float2(shift, 0.0)).r;
                fixed g = tex2D(_MainTex, uv).g;
                fixed b = tex2D(_MainTex, uv - float2(shift, 0.0)).b;
                fixed3 col = fixed3(r, g, b);

                // ③ 扫描线
                float scan = 0.82 + 0.18 * sin(uv.y * 620.0);
                col *= lerp(1.0, scan, a * 0.7);

                // ④ 胶片噪点颗粒（24fps 量化的逐像素闪烁）
                float grain = VNFHash21(floor(uv * 480.0) + floor(_Time.y * 24.0));
                col += (grain - 0.5) * 0.14 * a;

                return fixed4(col, 1.0);
            }
            ENDCG
        }
    }
}
