Shader "Hidden/FullScreen/FOW/TextureSample"
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
            #pragma multi_compile_local PLANE_XZ PLANE_XY PLANE_ZY
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
            sampler2D _CameraDepthNormalsTexture;

            float4x4 _camToWorldMatrix;
            float4x4 _inverseProjectionMatrix;

            float _maxDistance;
            float2 _fowTiling;
            float2 _fowScrollSpeed;
            float4 _unKnownColor;

            sampler2D _fowTexture;
            bool _skipTriplanar;
            float3 _fowAxis;

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
                float2 uvSample = pos + (_Time.yy * _fowScrollSpeed);
                float4 fogColor = tex2D(_fowTexture, uvSample * _fowTiling);
#else
                const float2 p11_22 = float2(unity_CameraProjection._11, unity_CameraProjection._22);
                const float2 p13_31 = float2(unity_CameraProjection._13, unity_CameraProjection._23);
                const float isOrtho = unity_OrthoParams.w;
                const float near = _ProjectionParams.y;
                const float far = _ProjectionParams.z;

                float4 depthnormal = tex2D(_CameraDepthNormalsTexture, i.uv);
                float d;
                float3 normal;
                DecodeDepthNormal(depthnormal, d, normal);
                d = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, i.uv); // non-linear Z

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

                normal = mul((float3x3)_camToWorldMatrix, normal);
                float3 powResult = pow(abs(normal), 8);
                float dotResult = dot(powResult, float3(1, 1, 1));
                //float3 lerpVals = round(powResult / dotResult);
                float3 lerpVals = (powResult / dotResult);
                if (_skipTriplanar)
                    lerpVals = _fowAxis;
                //uvSample = lerp(lerp(worldPos.xz, worldPos.yz, lerpVals.x), worldPos.xy, lerpVals.z) + (_Time * _fowScrollSpeed);
                float2 uvSample1 = worldPos.yz + (_Time.yy * _fowScrollSpeed);
                float2 uvSample2 = worldPos.xz + (_Time.yy * _fowScrollSpeed);
                float2 uvSample3 = worldPos.xy + (_Time.yy * _fowScrollSpeed);

                float4 fogColor = tex2D(_fowTexture, uvSample1 * _fowTiling) * lerpVals.x;
                fogColor += tex2D(_fowTexture, uvSample2 * _fowTiling) * lerpVals.y;
                fogColor += tex2D(_fowTexture, uvSample3 * _fowTiling) * lerpVals.z;

                GetFowSpacePosition(worldPos, pos, height);
#endif

                float coneCheckOut = 0;
                FOW_Sample_float(pos, height, coneCheckOut);
                
                //fixed4 fogColor = tex2D(_fowTexture, uvSample * _fowTiling);
                fogColor = lerp(color, fogColor, _unKnownColor.w);
                OutOfBoundsCheck(pos, color);
                OutOfBoundsCheck(pos, fogColor);
                return float4(lerp(fogColor.rgb * _unKnownColor.rgb, color.rgb, coneCheckOut), color.a);
            }
            ENDCG
        }
    }
}