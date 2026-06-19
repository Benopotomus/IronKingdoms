Shader "Hidden/FullScreen/FOW/FOW_RT"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        // No culling or depth
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma multi_compile_local _ FOW_USE_REGROW

            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"
            #define IS_FOW_RENDERTEXTURE
            #include_with_pragmas "../FogOfWarLogic.hlsl"
            //#include "../FogOfWarLogic.hlsl"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            sampler2D _MainTex;
            sampler2D _CameraDepthTexture;

            float4x4 _camToWorldMatrix;
            float4x4 _inverseProjectionMatrix;

            float _FowRT_FadeOutSpeed;
            float _FowRT_FadeInSpeed;
            float _FowRT_MaxRegrowAmount;


            fixed4 frag (v2f i) : SV_Target
            {
                //fixed4 color = tex2D(_MainTex, i.uv);

                float revealerSample = 0;
				float2 pos = float2(((i.uv.x-.5) * _worldBounds.x) + _worldBounds.y, ((i.uv.y-.5) * _worldBounds.z) + _worldBounds.w);

                //return float4(pos.x, pos.y, 0,1);

                FOW_Sample_Raw_float(pos, 0, revealerSample);

                #if FOW_USE_REGROW
                    //return tex2D(_MainTex, i.uv);
                    float opacitySample = 1 - tex2D(_MainTex, i.uv).r;

                    if (revealerSample >= opacitySample)
                    {
                        if (_FowRT_FadeInSpeed > 1000)
                            revealerSample = saturate(revealerSample);
                        else
                            revealerSample = min(revealerSample, opacitySample + _FowRT_FadeInSpeed * unity_DeltaTime.x);
                    }
                    else
                    {
                        float targetValue = opacitySample;
                        if (opacitySample >= _FowRT_MaxRegrowAmount) 
                        {
                            targetValue = _FowRT_FadeOutSpeed > 1000 ? 0 : targetValue - unity_DeltaTime.x * _FowRT_FadeOutSpeed;
                            targetValue = max(targetValue, _FowRT_MaxRegrowAmount);
                        }
                        revealerSample = max(revealerSample, targetValue);
                    }


                    revealerSample = saturate(revealerSample);

                #endif

                float outSample = (1 - revealerSample);
                return float4(outSample,outSample,outSample,outSample);
            }
            ENDCG
        }
        Pass
        {
            CGPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            sampler2D _MainTex;
            float _regrowSpeed;
            float _FowRT_MaxRegrowAmount;

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 color = tex2D(_MainTex, i.uv);
                color.r = 1 - color.r;
                return color;
                //color.r-= unity_DeltaTime.z;
                if (color.r > _FowRT_MaxRegrowAmount)
                {
                    color.r -= unity_DeltaTime.x * _regrowSpeed;
                    color.r = clamp(color.r, _FowRT_MaxRegrowAmount, 1);
                }
                
                return color;
            }
            ENDCG
        }
    }
}
