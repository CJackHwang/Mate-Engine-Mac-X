Shader "UI/GrabGaussianBlur2D_PoissonFixed"
{
    Properties
    {
        _MainTex ("Mask", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        _Radius ("Blur Radius (px)", Float) = 3
        _Sigma ("Sigma (px)", Float) = 2.5
        _Tint ("Tint", Color) = (0,0,0,0.35)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "CanUseSpriteAtlas"="True" }
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        GrabPass {}

        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;

            sampler2D _GrabTexture;
            float4 _GrabTexture_TexelSize;
            float _Radius;
            float _Sigma;
            float4 _Tint;

            struct appdata_t { float4 vertex:POSITION; float2 uv:TEXCOORD0; };
            struct v2f { float4 pos:SV_POSITION; float4 grabPos:TEXCOORD0; float2 uv:TEXCOORD1; };

            v2f vert(appdata_t v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.grabPos = ComputeGrabScreenPos(o.pos);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            float2 rot(float2 p, float c, float s) { return float2(p.x*c - p.y*s, p.x*s + p.y*c); }
            float gauss(float r, float s) { return exp(-0.5 * (r*r) / (s*s)); }

            fixed4 frag(v2f i):SV_Target
            {
                float2 uv01 = i.grabPos.xy / i.grabPos.w;
                float angle = 0.78539816339;
                float ca = cos(angle), sa = sin(angle);

                float2 taps[12] = {
                    float2( 0.1305,  0.9914),
                    float2(-0.9777,  0.2099),
                    float2( 0.7936, -0.6084),
                    float2(-0.4513, -0.8924),
                    float2( 0.9886,  0.1506),
                    float2(-0.0043, -0.9999),
                    float2(-0.7931,  0.6091),
                    float2( 0.4447,  0.8957),
                    float2(-0.9309, -0.3652),
                    float2( 0.2706, -0.9627),
                    float2( 0.9996, -0.0282),
                    float2(-0.6258,  0.7799)
                };

                float3 acc = 0;
                float wsum = 0;

                float4 coord = i.grabPos;
                float3 c0 = tex2Dproj(_GrabTexture, UNITY_PROJ_COORD(coord)).rgb;
                float w0 = gauss(0, max(_Sigma, 1e-3));
                acc += c0 * w0;
                wsum += w0;

                [unroll]
                for (int k = 0; k < 12; k++)
                {
                    float2 d = rot(taps[k], ca, sa) * _Radius;
                    float rpx = length(d);
                    float w = gauss(rpx, max(_Sigma, 1e-3));

                    float4 c1 = coord;
                    float4 c2 = coord;
                    float2 delta = d * _GrabTexture_TexelSize.xy;
                    c1.xy += delta * c1.w;
                    c2.xy -= delta * c2.w;

                    float3 s1 = tex2Dproj(_GrabTexture, UNITY_PROJ_COORD(c1)).rgb;
                    float3 s2 = tex2Dproj(_GrabTexture, UNITY_PROJ_COORD(c2)).rgb;

                    acc += s1 * w;
                    acc += s2 * w;
                    wsum += 2.0 * w;
                }

                float3 col = acc / max(wsum, 1e-5);
                col = lerp(col, _Tint.rgb, saturate(_Tint.a));

                fixed4 mask = tex2D(_MainTex, i.uv) * _Color;
                return fixed4(col, mask.a);
            }
            ENDCG
        }
    }
    Fallback "UI/Default"
}
