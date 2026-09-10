Shader "VNovelizer/BGTransition"
{
    // ============================================================
    // 背景切换过渡着色器（bgtrans 命令实现）
    //
    // 使用方式：临时 MeshActor 承载新背景图，主背景演员保持旧图，
    // 本 Shader 按 _Progress (0→1) 决定新图哪些像素可见，实现遮罩型过渡。
    //
    // _Mode 取值：
    //   0 = Fade     交叉淡化（新图 alpha = _Progress）
    //   1 = Blinds   竖向百叶窗（_BlindsCount 条，条带内从上到下、条带间左→右相位）
    //   2 = Wipe     从左到右擦除
    //   3 = Iris     圆心扩散（新图从画面中心的圆逐渐扩大）
    //   4 = Scroll   从下往上卷开
    //   5 = Dissolve 噪点阈值溶解
    //
    // UV 语义（MeshActor 网格契约）：uv(0,0)=画面左下，uv(1,1)=画面右上。
    //
    // 存放位置：Runtime/Resources/（包内 Resources 会被打进构建，
    // 加载顺序 Shader.Find 优先、Resources.Load 兜底，见 MeshActor.UseBgTransitionShader）
    // ============================================================
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        [HideInInspector] _Progress ("Transition Progress", Range(0,1)) = 0
        [HideInInspector] _Mode ("Transition Mode", Int) = 0
        [HideInInspector] _BlindsCount ("Blinds Band Count", Float) = 8
    }

    // ============================================================
    // 唯一 SubShader：Built-in 兼容版（无 RenderPipeline tag）。
    // URP 项目走 Built-in 兼容编译/渲染路径（官方支持；单个背景 quad
    // 不走 SRP Batcher 无性能损失），Built-in 项目原生可用。
    //
    // 注意：不要加 "RenderPipeline"="UniversalPipeline" tag——
    // 带该 tag 的 HLSL 块若不 include URP 的 Core.hlsl，unity_ObjectToWorld /
    // UNITY_MATRIX_MVP 等矩阵宏均未声明，会编译失败导致整个材质品红。
    // ============================================================
    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend One OneMinusSrcAlpha

        Pass
        {
            Name "BgTransition"
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
                float2 texcoord : TEXCOORD0;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float _Progress;
            int _Mode;
            float _BlindsCount;

            static const float _Aspect = 1.7777778;

            float BGTransHash21(float2 p)
            {
                p = frac(p * float2(234.34, 435.345));
                p += dot(p, p + 34.23);
                return frac(p.x * p.y);
            }

            float BGTransValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = BGTransHash21(i);
                float b = BGTransHash21(i + float2(1, 0));
                float c = BGTransHash21(i + float2(0, 1));
                float d = BGTransHash21(i + float2(1, 1));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            v2f vert(appdata_t v)
            {
                v2f OUT;
                OUT.vertex = UnityObjectToClipPos(v.vertex);
                OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                fixed4 tex = tex2D(_MainTex, IN.texcoord);
                fixed alpha = tex.a * _Color.a;
                fixed3 rgb = tex.rgb * _Color.rgb;

                float p = saturate(_Progress);

                if (_Mode == 1)
                {
                    float count = max(_BlindsCount, 1.0);
                    float band = floor(IN.texcoord.x * count);
                    float localP = saturate((p - band / count) * count);
                    if (IN.texcoord.y < 1.0 - localP) discard;
                }
                else if (_Mode == 2)
                {
                    if (IN.texcoord.x > p) discard;
                }
                else if (_Mode == 3)
                {
                    float2 d = IN.texcoord - 0.5;
                    d.x *= _Aspect;
                    float maxDist = length(float2(0.5 * _Aspect, 0.5));
                    if (length(d) > p * maxDist) discard;
                }
                else if (_Mode == 4)
                {
                    if (IN.texcoord.y > p) discard;
                }
                else if (_Mode == 5)
                {
                    float n = BGTransValueNoise(IN.texcoord * 40.0);
                    if (n > p) discard;
                }
                else
                {
                    alpha *= p;
                }

                rgb *= alpha;
                return fixed4(rgb, alpha);
            }
            ENDCG
        }
    }

    FallBack "Sprites/Default"
}
