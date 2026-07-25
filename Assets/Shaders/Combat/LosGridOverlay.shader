Shader "IronKingdoms/Combat/LosGridOverlay"
{
    Properties
    {
        _Color ("Color", Color) = (0.82, 0.12, 0.1, 0.42)
        _GridColor ("Grid Color", Color) = (1.0, 0.28, 0.18, 0.75)
        _GridLineWidth ("Grid Line Width", Range(0.01, 0.25)) = 0.08
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            Offset -1, -1

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;
            fixed4 _GridColor;
            float _GridLineWidth;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 edge = min(i.uv, 1.0 - i.uv);
                float lineMask = 1.0 - smoothstep(0.0, _GridLineWidth, min(edge.x, edge.y));
                fixed4 fill = _Color * i.color;
                fixed4 grid = _GridColor;
                fill.rgb = lerp(fill.rgb, grid.rgb, lineMask * grid.a);
                fill.a = max(fill.a, lineMask * grid.a * 0.85);
                return fill;
            }
            ENDCG
        }
    }

    FallBack Off
}
