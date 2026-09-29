Shader "InfiniteGrass/FlowerGardenShader"
{
    Properties
    {
        [MainTexture] _BaseColorTexture("BaseColor Texture", 2D) = "white" {}
        _AlphaClip("Alpha Clip Threshold", Range(0, 1)) = 0.5

        [Header(Colors)][Space]
        _Color1("Color 1", Color) = (1, 0, 0, 1)
        _Color2("Color 2", Color) = (1, 1, 0, 1)
        _Color3("Color 3", Color) = (1, 0, 1, 1)

        [Header(Placement and Size)][Space]
        _FlowerDensity("Flower Density", Range(0, 1)) = 0.5
        _Spread("XZ Scatter Distance", Float) = 0.5
        _ScaleMin("Min Size", Float) = 0.8
        _ScaleMax("Max Size", Float) = 1.2

        [Header(Wind)][Space]
        _WindTexture("Wind Texture", 2D) = "white" {}
        _WindScroll("Wind Scroll", Vector) = (1, 1, 0, 0)
        _WindStrength("Wind Strength", Float) = 1

        [Header(Lighting)][Space]
        _RandomNormal("Random Normal", Range(0, 1)) = 0.1

        [Header(Fog)][Space]
        _FogResistance("Fog Resistance", Range(0, 1)) = 0.4
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue"="Geometry"}

        Pass
        {
            Cull Off 
            ZTest Less
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile _ _FORWARD_PLUS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float3 normalOS     : NORMAL;
                float2 uv           : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS  : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float2 uv          : TEXCOORD1;
                half3 instanceColor: COLOR;
                float3 normalWS    : TEXCOORD2;
                float3 rootWS      : TEXCOORD3;
                float localHeight  : TEXCOORD4;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColorTexture_ST;
                float _AlphaClip;

                half3 _Color1;
                half3 _Color2;
                half3 _Color3;
                
                float _FlowerDensity;
                float _Spread;
                float _ScaleMin;
                float _ScaleMax;

                float4 _WindTexture_ST;
                float _WindStrength;
                float2 _WindScroll;
                half _RandomNormal;
                half _FogResistance;

                float2 _CenterPos;
                float _DrawDistance;
                float _TextureUpdateThreshold;

                int _FlowerIndex;
                int _FlowerCount;
                float _FlowerYOffset;

                StructuredBuffer<float3> _GrassPositions;
            CBUFFER_END

            sampler2D _BaseColorTexture;
            sampler2D _WindTexture;
            sampler2D _GrassColorRT;
            sampler2D _GrassSlopeRT;

            half3 ApplySingleDirectLight(Light light, half3 N, half3 V, half3 albedo, half mask, half positionY)
            {
                half3 H = normalize(light.direction + V);
                half directDiffuse = saturate(dot(N, light.direction));

                float directSpecular = saturate(dot(N,H));
                directSpecular *= directSpecular;
                directSpecular *= directSpecular;
                directSpecular *= directSpecular;
                directSpecular *= directSpecular;
                directSpecular *= positionY * 0.12;

                half3 lighting = light.color * (light.shadowAttenuation * light.distanceAttenuation);
                return (albedo * directDiffuse + directSpecular * (1-mask)) * lighting;
            }

            uint murmurHash3(float input) {
                uint h = abs(input);
                h ^= h >> 16;
                h *= 0x85ebca6b;
                h ^= h >> 13;
                h *= 0xc2b2ae3d;
                h ^= h >> 16;
                return h;
            }

            float srandom(float input) {
                return (murmurHash3(input) / 4294967295.0) * 2 - 1;
            }

            float3 CalculateLighting(float3 albedo, float3 positionWS, float3 N, float3 V, float mask, float positionY){
                half3 result = SampleSH(N) * albedo;

                Light mainLight = GetMainLight(TransformWorldToShadowCoord(positionWS));
                result += ApplySingleDirectLight(mainLight, N, V, albedo, mask, positionY);

                int additionalLightsCount = GetAdditionalLightsCount();
                for (int i = 0; i < additionalLightsCount; ++i)
                {
                    Light light = GetAdditionalLight(i, positionWS);
                    result += ApplySingleDirectLight(light, N, V, albedo, mask, positionY);
                }

                return result;
            }

            Varyings vert(Attributes IN, uint instanceID : SV_InstanceID)
{
    Varyings OUT;
    float3 pivot = _GrassPositions[instanceID];

    // Create a stable seed based purely on world coordinates to prevent jittering
    float stableSeed = pivot.x * 832.14 + pivot.z * 391.72;

    // 1. Procedural Density Culling
    float densityCheck = abs(srandom(stableSeed * 1.5));
    if (densityCheck > _FlowerDensity) {
        OUT.positionCS = float4(1e9, 1e9, 1e9, 1); // valid w (no NaN on divide), far outside the clip volume so it's discarded reliably on every GPU
        OUT.positionWS = float3(0,0,0);
        OUT.uv = float2(0,0);
        OUT.instanceColor = half3(1,1,1);
        OUT.normalWS = float3(0,1,0);
        OUT.rootWS = float3(0,0,0);
        OUT.localHeight = 0;
        return OUT;
    }

    // 2. Mesh Type Culling
    uint randomType = murmurHash3(stableSeed) % max(1, (uint)_FlowerCount);
    if (randomType != (uint)_FlowerIndex) {
        OUT.positionCS = float4(1e9, 1e9, 1e9, 1); // valid w (no NaN on divide), far outside the clip volume so it's discarded reliably on every GPU
        OUT.positionWS = float3(0,0,0);
        OUT.uv = float2(0,0);
        OUT.instanceColor = half3(1,1,1);
        OUT.normalWS = float3(0,1,0);
        OUT.rootWS = float3(0,0,0);
        OUT.localHeight = 0;
        return OUT;
    }

    // 3. XZ Spread Scatter (using stable Seed)
    pivot.x += srandom(stableSeed * 2.2) * _Spread;
    pivot.z += srandom(stableSeed * 3.3) * _Spread;
    OUT.rootWS = pivot; // stable per-instance root, used later for the color RT lookup

    // 4. Random Color Assignment
    float randColor = abs(srandom(stableSeed * 4.4));
    if (randColor < 0.333) OUT.instanceColor = _Color1;
    else if (randColor < 0.666) OUT.instanceColor = _Color2;
    else OUT.instanceColor = _Color3;

    // 5. Scale Variation
    float randomScale = lerp(_ScaleMin, _ScaleMax, srandom(stableSeed * 5.5) * 0.5 + 0.5);
    float3 positionOS = IN.positionOS.xyz * randomScale;

    // 6. Random Y Rotation 
    float randomRot = srandom(stableSeed * 6.6) * 3.14159265;
    float s, c;
    sincos(randomRot, s, c);
    float2x2 rotMat = float2x2(c, -s, s, c);
    positionOS.xz = mul(rotMat, positionOS.xz);

    // Rotate the real mesh normal by the same Y rotation, so lighting actually
    // follows the flower's petals/leaves instead of a fixed up-vector.
    float3 normalOS = IN.normalOS;
    normalOS.xz = mul(rotMat, normalOS.xz);
    OUT.normalWS = normalize(normalOS);
    OUT.localHeight = saturate(IN.positionOS.y); // clamped to 0-1: the flower mesh's Y range is arbitrary, unlike grass's guaranteed 0-1 mesh

    // 7. Wind Displacement ONLY (Interactivity / Slope RT removed)
    half3 windTex = tex2Dlod(_WindTexture, float4(TRANSFORM_TEX(pivot.xz, _WindTexture) + _WindScroll * _Time.y, 0, 0));
    float2 wind = (windTex.rg * 2.0 - 1.0) * _WindStrength;

    // Apply wind directly based on height
    positionOS.xz += wind * positionOS.y * 0.7; 

    // 8. Move to World Space and Apply Y-Offset
    float3 positionWS = positionOS + pivot;
    positionWS.y += _FlowerYOffset;
    
    OUT.positionCS = TransformWorldToHClip(positionWS);
    OUT.positionWS = positionWS;
    OUT.uv = IN.uv;

    return OUT;
}
            half4 frag(Varyings IN, bool isFrontFace : SV_IsFrontFace) : SV_Target
            {
                float4 texColor = tex2D(_BaseColorTexture, IN.uv);
                clip(texColor.a - _AlphaClip);

                // Base color multiplied by random instance color 
                half3 albedo = texColor.rgb * IN.instanceColor;

                float2 rtUV = (IN.rootWS.xz - _CenterPos) / (_DrawDistance + _TextureUpdateThreshold);
                rtUV = rtUV * 0.5 + 0.5;
                float4 colorRT = tex2D(_GrassColorRT, rtUV);

                albedo = lerp(albedo, colorRT.rgb, colorRT.a);

                // Real mesh normal (rotated with the flower) drives shading, with a touch of
                // per-instance jitter for variety. Flip it on backfaces since this pass is Cull Off,
                // otherwise the undersides of petals/leaves read as unlit.
                float3 baseNormal = IN.normalWS * (isFrontFace ? 1 : -1);
                float3 N = normalize(baseNormal + float3(srandom(IN.rootWS.x * 314 + IN.rootWS.z * 10), 0, srandom(IN.rootWS.z * 677 + IN.rootWS.x * 10)) * _RandomNormal);
                half3 V = normalize(_WorldSpaceCameraPos - IN.positionWS);

                float3 lighting = CalculateLighting(albedo, IN.positionWS, N, V, colorRT.a, IN.localHeight);
                
                float fogFactor = ComputeFogFactor(IN.positionCS.z);
                // The same fog blend % reads as a much bigger color shift on dark areas (stem, shadowed
                // leaves) than on bright petals, since dark colors have little of their own "signal" to
                // begin with. Give darker areas extra resistance so the stem doesn't wash out to fog color
                // while petals still read naturally.
                float luminance = dot(lighting, half3(0.299, 0.587, 0.114));
                float darknessBoost = (1 - saturate(luminance)) * _FogResistance;
                float effectiveResistance = saturate(_FogResistance + darknessBoost);
                fogFactor = saturate(lerp(fogFactor, 1, effectiveResistance));
                return half4(MixFog(lighting, fogFactor), 1);
            }
            ENDHLSL
        }
    }
}