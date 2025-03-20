Shader "Custom/InvisibleShadow"
{
    Properties
    {
        _ShadowIntensity ("Shadow Intensity", Range(0, 1)) = 0.7
    }
    SubShader
    {
        Tags {"Queue" = "Transparent" "RenderType"="Transparent"}
        Pass
        {
            Tags {"LightMode" = "ForwardBase"}
            Cull Off
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha
            
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD1;
                float3 normal : TEXCOORD2;
                float shadowCoord : TEXCOORD3;
            };

            float _ShadowIntensity;

            v2f vert (appdata_t v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.shadowCoord = dot(o.normal, _WorldSpaceLightPos0.xyz); // Basic light direction shadowing
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float shadowStrength = saturate(i.shadowCoord) * _ShadowIntensity;
                return fixed4(0, 0, 0, 1.0 - shadowStrength); // Show only shadows
            }
            ENDCG
        }
    }
}
