Shader "FOW/UI/Fog Of War Mini-Map"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255

        _ColorMask ("Color Mask", Float) = 15

        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

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

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
        CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #pragma multi_compile_local _ FRUSTUM_ENABLED
            #pragma multi_compile_local _ FRUSTUM_CLAMP

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord  : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            float4 _MainTex_ST;

            //camera frustum
            float4 _LineColor;
            float _LineWidth;
            float _Softness;
            float4 _TexSizeOverride;
            float2 _FrustumUV0, _FrustumUV1, _FrustumUV2, _FrustumUV3;
            float2 _FrustumUV4, _FrustumUV5, _FrustumUV6, _FrustumUV7;
            float _InsetX;
float _InsetY;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);

                OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);

                OUT.color = v.color * _Color;
                return OUT;
            }

         #if FRUSTUM_ENABLED

            float sdSegment(float2 p, float2 a, float2 b, float2 texSize)
            {
                p *= texSize;
                a *= texSize;
                b *= texSize;
                
                float2 pa = p - a, ba = b - a;
                float h = saturate(dot(pa, ba) / dot(ba, ba));
                return length(pa - ba * h);
            }
            
            float sdFrustum(float2 uv, float2 texSize)
            {
                float d = sdSegment(uv, _FrustumUV0, _FrustumUV1, texSize);
                d = min(d, sdSegment(uv, _FrustumUV1, _FrustumUV2, texSize));
                d = min(d, sdSegment(uv, _FrustumUV2, _FrustumUV3, texSize));
                d = min(d, sdSegment(uv, _FrustumUV3, _FrustumUV4, texSize));
                d = min(d, sdSegment(uv, _FrustumUV4, _FrustumUV5, texSize));
                d = min(d, sdSegment(uv, _FrustumUV5, _FrustumUV6, texSize));
                d = min(d, sdSegment(uv, _FrustumUV6, _FrustumUV7, texSize));
                d = min(d, sdSegment(uv, _FrustumUV7, _FrustumUV0, texSize)); // closes loop
                return d;
            }

        #endif

            fixed4 frag(v2f IN) : SV_Target
            {
                half4 color = IN.color;

                half alpha = (tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd).r;
                color.a = alpha;

            #if FRUSTUM_ENABLED
                float2 texSize = _TexSizeOverride.xy;
                float dist = sdFrustum(IN.texcoord, texSize);
                float lineAlpha = 1.0 - smoothstep(_LineWidth - _Softness, _LineWidth + _Softness, dist);
                color = lerp(color, _LineColor, lineAlpha * _LineColor.a);
            #endif

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip (color.a - 0.001);
                #endif

                return color;
            }
        ENDCG
        }
    }
}