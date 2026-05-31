Shader "Hidden/FullScreen/FOW/GrayScale"
{
    Properties
    {
        _MainTex("Texture", 2D) = "white" {}
    }
    SubShader
    {
        // No culling or depth
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma multi_compile _ FOW_IS_2D

            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"
            #include_with_pragmas "../FogOfWarLogic.hlsl"

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
            float4 _MainTex_TexelSize;
            sampler2D _CameraDepthTexture;
            
            float4x4 _camToWorldMatrix;
            float4x4 _inverseProjectionMatrix;

            float3 _unKnownColor;
            float _maxDistance;
            
            float _saturationStrength;

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 color = tex2D(_MainTex, i.uv);
                
                float2 pos;
                float height;
#if FOW_IS_2D
                pos = (i.uv * float2(2,2) - float2(1,1)) * _cameraSize * float2(_MainTex_TexelSize.z/ _MainTex_TexelSize.w,1);
                pos+= _cameraPosition;
                FOW_Rotate_Degrees_float(pos, _cameraPosition, -_cameraRotation, pos);
                height = 0;
#else
                const float2 p11_22 = float2(unity_CameraProjection._11, unity_CameraProjection._22);
                const float2 p13_31 = float2(unity_CameraProjection._13, unity_CameraProjection._23);
                const float isOrtho = unity_OrthoParams.w;
                const float near = _ProjectionParams.y;
                const float far = _ProjectionParams.z;

                float d = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, i.uv);
        #if defined(UNITY_REVERSED_Z)
                d = 1 - d;
        #endif
                float zOrtho = lerp(near, far, d);
                float zPers = near * far / lerp(far, near, d);
                float vz = lerp(zPers, zOrtho, isOrtho);

                if (vz > _maxDistance)
                    return color;

                float3 vpos = float3((i.uv * 2 - 1 - p13_31) / p11_22 * lerp(vz, 1, isOrtho), -vz);
                float4 worldPos = mul(_camToWorldMatrix, float4(vpos, 1));

                GetFowSpacePosition(worldPos, pos, height);
#endif

                float coneCheckOut = 0;
                FOW_Sample_float(pos, height, coneCheckOut);

                OutOfBoundsCheck(pos, color);
                float luma = dot(color.rgb * _unKnownColor, float3(0.2126729, 0.7151522, 0.0721750));
                float3 saturatedColor = luma.xxx + _saturationStrength.xxx * (color.rgb - luma.xxx);
                return float4(lerp(saturatedColor, color.rgb, coneCheckOut), color.a);
            }
            ENDCG
        }
    }
}
