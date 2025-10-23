//----------------------------------------------------------------------------------------------------------------------
// Macro

// Custom variables
//#define LIL_CUSTOM_PROPERTIES \
//    float _CustomVariable;
#define LIL_CUSTOM_PROPERTIES \
    float _FrameRate; \
    float _Frequency; \
    float _AberrationR; \
    float _AberrationG; \
    float _AberrationB;

// Custom textures
#define LIL_CUSTOM_TEXTURES

// Add vertex shader input
#define LIL_REQUIRE_APP_POSITION
#define LIL_REQUIRE_APP_TEXCOORD0

// Add vertex copy
#define LIL_CUSTOM_VERT_COPY

// Inserting a process into pixel shader
//#define BEFORE_xx
//#define OVERRIDE_xx

#include <UnityCG.cginc>

#define LIL_CUSTOM_VERTEX_OS \
    positionOS = input.positionOS; \
    float2 noise = glitch_noise_calculate(float2(floor(positionOS.y), frac(positionOS.y)), _FrameRate, _Frequency); \
    positionOS.x = lerp(positionOS.x, positionOS.x + noise.x, noise.y);

#define BEFORE_OUTPUT \
    float2 gv = fd.uvMain; \
    float2 noise = glitch_noise_color_calculate(gv, _FrameRate); \
    gv.x = lerp(gv.x, gv.x + noise.x, noise.y); \
    float3 diff = fd.col.rgb - LIL_SAMPLE_2D(_MainTex, sampler_MainTex, gv) * _Color; \
    fd.col.r = LIL_SAMPLE_2D(_MainTex, sampler_MainTex, gv + float2(noise.x * _AberrationR, 0)).r * _Color.r + diff.r; \
    fd.col.g = LIL_SAMPLE_2D(_MainTex, sampler_MainTex, gv + float2(noise.x * _AberrationG, 0)).g * _Color.g + diff.g; \
    fd.col.b = LIL_SAMPLE_2D(_MainTex, sampler_MainTex, gv + float2(noise.x * _AberrationB, 0)).b * _Color.b + diff.b; \

float random(float2 seeds)
{
    return frac(sin(dot(seeds, float2(12.9898, 78.233))) * 43758.5453);
}

float perlinNoise(fixed2 st)
{
    fixed2 p = floor(st);
    fixed2 f = frac(st);
    fixed2 u = f * f * (3.0 - 2.0 * f);

    float v00 = random(p + fixed2(0, 0));
    float v10 = random(p + fixed2(1, 0));
    float v01 = random(p + fixed2(0, 1));
    float v11 = random(p + fixed2(1, 1));

    return lerp(lerp(dot(v00, f - fixed2(0, 0)), dot(v10, f - fixed2(1, 0)), u.x),
                lerp(dot(v01, f - fixed2(0, 1)), dot(v11, f - fixed2(1, 1)), u.x),
                u.y) + 0.5f;
}

float2 glitch_noise_calculate(float2 uv, float fr, float freq)
{
    float posterize = floor(frac(perlinNoise(frac(_Time)) * 10) / (1 / fr)) * (1 / fr);
    float noiseY = 2.0 * random(posterize) - 0.5;
    float glitchLine1 = step(uv.y - noiseY, random(uv));
    float glitchLine2 = step(uv.y - noiseY, 0);
    noiseY = saturate(glitchLine1 - glitchLine2);
    float noiseX = (2.0 * random(posterize) - 0.5) * 0.0008;
    float frequency = step(abs(noiseX), freq);
    noiseX *= frequency;
    return float2(noiseX, noiseY);
}

float2 glitch_noise_color_calculate(float2 uv, float fr)
{
    float posterize = floor(frac(perlinNoise(frac(_Time)) * 10) / (1 / fr)) * (1 / fr);
    float noiseY = 2.0 * random(posterize) - 0.5;
    float glitchLine1 = step(uv.y - noiseY, random(uv));
    float glitchLine2 = step(uv.y - noiseY, 0);
    noiseY = saturate(glitchLine1 - glitchLine2);
    float noiseX = (2.0 * random(posterize) - 0.5) * 0.001;
    float frequency = step(abs(noiseX), 0.01);
    noiseX *= frequency;
    return float2(noiseX, noiseY);
}

//----------------------------------------------------------------------------------------------------------------------
// Information about variables
//----------------------------------------------------------------------------------------------------------------------

//----------------------------------------------------------------------------------------------------------------------
// Vertex shader inputs (appdata structure)
//
// Type     Name                    Description
// -------- ----------------------- --------------------------------------------------------------------
// float4   input.positionOS        POSITION
// float2   input.uv0               TEXCOORD0
// float2   input.uv1               TEXCOORD1
// float2   input.uv2               TEXCOORD2
// float2   input.uv3               TEXCOORD3
// float2   input.uv4               TEXCOORD4
// float2   input.uv5               TEXCOORD5
// float2   input.uv6               TEXCOORD6
// float2   input.uv7               TEXCOORD7
// float4   input.color             COLOR
// float3   input.normalOS          NORMAL
// float4   input.tangentOS         TANGENT
// uint     vertexID                SV_VertexID

//----------------------------------------------------------------------------------------------------------------------
// Vertex shader outputs or pixel shader inputs (v2f structure)
//
// The structure depends on the pass.
// Please check lil_pass_xx.hlsl for details.
//
// Type     Name                    Description
// -------- ----------------------- --------------------------------------------------------------------
// float4   output.positionCS       SV_POSITION
// float2   output.uv01             TEXCOORD0 TEXCOORD1
// float2   output.uv23             TEXCOORD2 TEXCOORD3
// float3   output.positionOS       object space position
// float3   output.positionWS       world space position
// float3   output.normalWS         world space normal
// float4   output.tangentWS        world space tangent

//----------------------------------------------------------------------------------------------------------------------
// Variables commonly used in the forward pass
//
// These are members of `lilFragData fd`
//
// Type     Name                    Description
// -------- ----------------------- --------------------------------------------------------------------
// float4   col                     lit color
// float3   albedo                  unlit color
// float3   emissionColor           color of emission
// -------- ----------------------- --------------------------------------------------------------------
// float3   lightColor              color of light
// float3   indLightColor           color of indirectional light
// float3   addLightColor           color of additional light
// float    attenuation             attenuation of light
// float3   invLighting             saturate((1.0 - lightColor) * sqrt(lightColor));
// -------- ----------------------- --------------------------------------------------------------------
// float2   uv0                     TEXCOORD0
// float2   uv1                     TEXCOORD1
// float2   uv2                     TEXCOORD2
// float2   uv3                     TEXCOORD3
// float2   uvMain                  Main UV
// float2   uvMat                   MatCap UV
// float2   uvRim                   Rim Light UV
// float2   uvPanorama              Panorama UV
// float2   uvScn                   Screen UV
// bool     isRightHand             input.tangentWS.w > 0.0;
// -------- ----------------------- --------------------------------------------------------------------
// float3   positionOS              object space position
// float3   positionWS              world space position
// float4   positionCS              clip space position
// float4   positionSS              screen space position
// float    depth                   distance from camera
// -------- ----------------------- --------------------------------------------------------------------
// float3x3 TBN                     tangent / bitangent / normal matrix
// float3   T                       tangent direction
// float3   B                       bitangent direction
// float3   N                       normal direction
// float3   V                       view direction
// float3   L                       light direction
// float3   origN                   normal direction without normal map
// float3   origL                   light direction without sh light
// float3   headV                   middle view direction of 2 cameras
// float3   reflectionN             normal direction for reflection
// float3   matcapN                 normal direction for reflection for MatCap
// float3   matcap2ndN              normal direction for reflection for MatCap 2nd
// float    facing                  VFACE
// -------- ----------------------- --------------------------------------------------------------------
// float    vl                      dot(viewDirection, lightDirection);
// float    hl                      dot(headDirection, lightDirection);
// float    ln                      dot(lightDirection, normalDirection);
// float    nv                      saturate(dot(normalDirection, viewDirection));
// float    nvabs                   abs(dot(normalDirection, viewDirection));
// -------- ----------------------- --------------------------------------------------------------------
// float4   triMask                 TriMask (for lite version)
// float3   parallaxViewDirection   mul(tbnWS, viewDirection);
// float2   parallaxOffset          parallaxViewDirection.xy / (parallaxViewDirection.z+0.5);
// float    anisotropy              strength of anisotropy
// float    smoothness              smoothness
// float    roughness               roughness
// float    perceptualRoughness     perceptual roughness
// float    shadowmix               this variable is 0 in the shadow area
// float    audioLinkValue          volume acquired by AudioLink
// -------- ----------------------- --------------------------------------------------------------------
// uint     renderingLayers         light layer of object (for URP / HDRP)
// uint     featureFlags            feature flags (for HDRP)
// uint2    tileIndex               tile index (for HDRP)