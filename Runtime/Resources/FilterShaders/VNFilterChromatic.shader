Shader "VNovelizer/Filters/Chromatic"
{
    // ============================================================
    // 画面滤镜：色差（ScreenFilterSystem 内置，filter_chromatic(amount[, duration])）
    // _Amount：径向色散量 0..0.05（0.012 默认）——R/B 沿「像素到画面中心」
    // 方向向外/向内偏移，中心无色差、边缘最强（经 _Aspect 校正保持圆形对称）。
    // 滤镜 Shader 契约见 Docs/VNScreenFilterSystem.md（_MainTex/_Weight/_Aspect）。
    // Built-in 兼容（无 RenderPipeline tag），存放于包内 Resources 保证进构建。
    // ============================================================
    Properties
    {
        _MainTex ("Input Texture", 2D) = "white" {}
        [HideInInspector] _Weight ("Filter Weight", Range(0,1)) = 0
        [HideInInspector] _Aspect ("Screen Aspect", Float) = 1.777778
        _Amount ("Amount", Range(0, 0.05)) = 0.012
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
            float _Amount;

            v2f vert(appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 c = i.uv - 0.5;
                c.x *= _Aspect;
                float2 off = c * (_Amount * _Weight);
                off.x /= _Aspect;

                fixed r = tex2D(_MainTex, i.uv + off).r;
                fixed g = tex2D(_MainTex, i.uv).g;
                fixed b = tex2D(_MainTex, i.uv - off).b;
                return fixed4(fixed3(r, g, b), 1.0);
            }
            ENDCG
        }
    }
}
