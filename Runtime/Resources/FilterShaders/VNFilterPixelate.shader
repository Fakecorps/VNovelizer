Shader "VNovelizer/Filters/Pixelate"
{
    // ============================================================
    // 画面滤镜：像素化（ScreenFilterSystem 内置，filter_pixelate(pixelCount[, duration])）
    // _PixelCount：横向分块数 2..512（96 默认；分辨率无关——像素块大小随
    // 块数自动换算）。滤镜 Shader 契约见 Docs/VNScreenFilterSystem.md。
    // Built-in 兼容（无 RenderPipeline tag），存放于包内 Resources 保证进构建。
    // ============================================================
    Properties
    {
        _MainTex ("Input Texture", 2D) = "white" {}
        [HideInInspector] _Weight ("Filter Weight", Range(0,1)) = 0
        [HideInInspector] _Aspect ("Screen Aspect", Float) = 1.777778
        _PixelCount ("Pixel Count", Float) = 96
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
            float _PixelCount;

            v2f vert(appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed3 orig = tex2D(_MainTex, i.uv).rgb;

                float count = max(_PixelCount, 1.0);
                float2 q = floor(i.uv * count) / count;
                fixed3 pix = tex2D(_MainTex, q).rgb;

                fixed3 col = lerp(orig, pix, _Weight);
                return fixed4(col, 1.0);
            }
            ENDCG
        }
    }
}
