Shader "UI/GlowShader"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        [HDR] _Color ("Color Tint", Color) = (1,1,1,1)
        [HDR] _EmissionColor ("Emission Color (HDR)", Color) = (0,0,0,0)
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        LOD 200

        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            half4 _EmissionColor;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 baseCol = tex2D(_MainTex, i.uv) * _Color * i.color;
                fixed3 rgb = baseCol.rgb + (_EmissionColor.rgb * i.color.rgb);
                return fixed4(rgb, baseCol.a);
            }
            ENDCG
        }
    }
}
