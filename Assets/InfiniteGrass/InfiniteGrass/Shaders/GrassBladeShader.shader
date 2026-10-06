Shader "InfiniteGrass/GrassBladeShader"
{

    Properties
    {
        [MainTexture] _BaseColorTexture("BaseColor Texture", 2D) = "white" {}
        _ColorA("ColorA", Color) = (0,0,0,1)
        _ColorB("ColorB", Color) = (1,1,1,1)
        _AOColor("AO Color", Color) = (0.5,0.5,0.5)

        [Header(Grass Shape)][Space]
        _GrassWidth("Grass Width", Float) = 1
        _GrassHeight("Grass Height", Float) = 1
        _GrassWidthRandomness("Grass Width Randomness", Range(0, 1)) = 0.25
        _GrassHeightRandomness("Grass Height Randomness", Range(0, 1)) = 0.5

        _GrassCurving("Grass Curving", Float) = 0.1
        [Space]
        _ExpandDistantGrassWidth("Expand Distant Grass Width", Float) = 1
        _ExpandDistantGrassRange("Expand Distant Grass Range", Vector) = (50, 200, 0, 0)

        [Header(Wind)][Space]
        _WindTexture("Wind Texture", 2D) = "white" {}
        _WindScroll("Wind Scroll", Vector) = (1, 1, 0, 0)
        _WindStrength("Wind Strength", Float) = 1

        [Header(Lighting)][Space]
        _RandomNormal("Random Normal", Range(0, 1)) = 0.1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue"="Geometry"}

        Pass
        {
            Cull Back
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
            // Unity 6 Forward+ Support Added Below
            #pragma multi_compile _ _FORWARD_PLUS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS   : POSITION;
            };

            struct Varyings
            {
                float4 positionCS  : SV_POSITION;
                half3 color        : COLOR;
            };

            CBUFFER_START(UnityPerMaterial)
                half3 _ColorA;
                half3 _ColorB;
                float4 _BaseColorTexture_ST;
                half3 _AOColor;

                float _GrassWidth;
                float _GrassHeight;
                float _GrassCurving;
                float _GrassWidthRandomness;
                float _GrassHeightRandomness;

                float _ExpandDistantGrassWidth;
                float2 _ExpandDistantGrassRange;

                float4 _WindTexture_ST;
                float _WindStrength;
                float2 _WindScroll;

                half _RandomNormal;

                float2 _CenterPos;

                float _DrawDistance;
                float _TextureUpdateThreshold;

                StructuredBuffer<float4> _GrassPositions; // xyz = world position, w = height scale (for edge fading)

            CBUFFER_END

            // AOE grass cut data (set globally by GrassCutManager)
            float4 _GrassCutA[8];          // xy = origin XZ, zw = forward direction XZ
            float4 _GrassCutB[8];          // x = radius, y = cos(half cone angle), z = age in seconds
            float4 _GrassCutTimings;       // x = sweep time, y = stay-cut time, z = regrow time, w = blow time
            float4 _GrassCutVisual;        // x = shockwave bend, y = glow intensity, z = ragged edge amount, w = fly distance
            float4 _GrassCutGlowColor;
            float _GrassCutCountF;

            sampler2D _BaseColorTexture;
            sampler2D _WindTexture;

            sampler2D _GrassColorRT;
            sampler2D _GrassSlopeRT;

            half3 ApplySingleDirectLight(Light light, half3 N, half3 V, half3 albedo, half mask, half positionY)
            {
                half3 H = normalize(light.direction + V);

                // Changed to standard Lambert for realistic shadows and contrast
                half directDiffuse = saturate(dot(N, light.direction));

                float directSpecular = saturate(dot(N,H));
                directSpecular *= directSpecular;
                directSpecular *= directSpecular;
                directSpecular *= directSpecular;
                directSpecular *= directSpecular;

                directSpecular *= positionY * 0.12;

                half3 lighting = light.color * (light.shadowAttenuation * light.distanceAttenuation);
                half3 result = (albedo * directDiffuse + directSpecular * (1-mask)) * lighting;

                return result; 
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

            float random(float input) {
                return murmurHash3(input) / 4294967295.0;
            }

            float srandom(float input) {
                return (murmurHash3(input) / 4294967295.0) * 2 - 1;
            }

            float Remap(float In, float2 InMinMax, float2 OutMinMax)
            {
                return OutMinMax.x + (In - InMinMax.x) * (OutMinMax.y - OutMinMax.x) / (InMinMax.y - InMinMax.x);
            }

            float3 CalculateLighting(float3 albedo, float3 positionWS, float3 N, float3 V, float mask, float positionY){

                // Changed from SampleSH(0) to SampleSH(N) to get directional skybox lighting
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

            float CutRand(float2 p)
            {
                return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453);
            }

            Varyings vert(Attributes IN, uint instanceID : SV_InstanceID)
            {
                Varyings OUT;

                float4 grassData = _GrassPositions[instanceID];
                float3 pivot = grassData.xyz;
                float heightFadeScale = grassData.w;

                float2 uv = (pivot.xz - _CenterPos) / (_DrawDistance + _TextureUpdateThreshold);
                uv = uv * 0.5 + 0.5;

                float grassWidth = _GrassWidth * (1 - random(pivot.x * 950 + pivot.z * 10) * _GrassWidthRandomness);

                float distanceFromCamera = length(_WorldSpaceCameraPos - pivot);
                //Expand the grass width based on the distance from camera
                grassWidth += saturate(Remap(distanceFromCamera, float2(_ExpandDistantGrassRange.x, _ExpandDistantGrassRange.y), float2(0, 1))) * _ExpandDistantGrassWidth;
                grassWidth *= (1 - IN.positionOS.y);

                //Grass Height
                float grassHeight = _GrassHeight * (1 - random(pivot.x * 230 + pivot.z * 10) * _GrassHeightRandomness) * heightFadeScale;
                
                //Billboard Logic
                float3 cameraTransformRightWS = UNITY_MATRIX_V[0].xyz;
                float3 cameraTransformUpWS = UNITY_MATRIX_V[1].xyz;
                float3 cameraTransformForwardWS = -UNITY_MATRIX_V[2].xyz;

                float4 slope = tex2Dlod(_GrassSlopeRT, float4(uv, 0, 0));
                float xSlope = slope.r * 2 - 1;
                float zSlope = slope.g * 2 - 1;

                float3 slopeDirection = normalize(float3(xSlope, 1 - (max(abs(xSlope), abs(zSlope)) * 0.5), zSlope));//Direction reconstructed from the slope texture
                float3 bladeDirection = normalize(lerp(float3(0, 1, 0), slopeDirection, slope.a));//The original direction is upward

                half3 windTex = tex2Dlod(_WindTexture, float4(TRANSFORM_TEX(pivot.xz, _WindTexture) + _WindScroll * _Time.y,0,0));
                float2 wind = (windTex.rg * 2 - 1) * _WindStrength * (1-slope.a);

                bladeDirection.xz += wind * IN.positionOS.y;//Adding wind and multiplying with the Y position to affect the tip only

                // ---- AOE grass cut: when the front reaches a blade it whips outward, flies off and shrinks away ----
                float cutWave = 0.0;                 // flash
                float cutBend = 0.0;                 // how far the blade is blown over
                float cutFly = 0.0;                  // how far the blade has flown off
                float cutDirStrength = 0.0;
                float2 cutPushDir = float2(0, 0);
                int cutCount = (int)(_GrassCutCountF + 0.5);

                [loop]
                for (int ci = 0; ci < cutCount; ci++)
                {
                    float2 toBlade = pivot.xz - _GrassCutA[ci].xy;
                    float cDist = length(toBlade);
                    float cRadius = _GrassCutB[ci].x;
                    if (cDist > cRadius) continue;

                    float cosA = cDist > 0.001 ? dot(toBlade / cDist, _GrassCutA[ci].zw) : 1.0;
                    float cone = saturate((cosA - _GrassCutB[ci].y) / 0.1 + 0.5);
                    if (cone <= 0.0) continue;

                    // same timeline as the compute shader: cP goes 0 -> 1 over the blow time, starting when the front reaches this blade
                    float cAge = _GrassCutB[ci].z;
                    float cD = cDist + (CutRand(pivot.xz) - 0.5) * _GrassCutVisual.z;
                    float cEased = saturate(cD / (cRadius + 0.5 * _GrassCutVisual.z));
                    float cHit = (1.0 - sqrt(1.0 - cEased)) * _GrassCutTimings.x;
                    float cP = saturate((cAge - cHit) / max(_GrassCutTimings.w, 0.001));

                    float cRegrow = saturate((cAge - _GrassCutTimings.x - _GrassCutTimings.w - _GrassCutTimings.y) / max(_GrassCutTimings.z, 0.001));
                    cRegrow = cRegrow * cRegrow * (3.0 - 2.0 * cRegrow);

                    float bend = cone * smoothstep(0.0, 0.3, cP) * (1.0 - smoothstep(0.85, 1.0, cP)) * (1.0 - cRegrow);
                    float g = (cP - 0.12) / 0.18;
                    float glow = cone * smoothstep(0.0, 0.04, cP) * exp(-g * g);
                    float fly = cone * smoothstep(0.25, 1.0, cP) * (1.0 - cRegrow);

                    float strength = max(bend, fly);
                    if (strength > cutDirStrength)
                    {
                        cutDirStrength = strength;
                        cutPushDir = toBlade / max(cDist, 0.001);
                    }
                    cutBend = max(cutBend, bend);
                    cutWave = max(cutWave, glow);
                    cutFly = max(cutFly, fly);
                }
                bladeDirection.xz += cutPushDir * (cutBend * _GrassCutVisual.x * IN.positionOS.y);

                bladeDirection = normalize(bladeDirection);
                
                float3 rightTangent = normalize(cross(bladeDirection, cameraTransformForwardWS));//The direction we gonna stretch the blade

                float3 positionOS = bladeDirection * IN.positionOS.y * grassHeight 
                                    + rightTangent * IN.positionOS.x * grassWidth;//This insures that the blade is always facing the camera

                positionOS.xz += (IN.positionOS.y * IN.positionOS.y) * float2(srandom(pivot.x * 851 + pivot.z * 10), srandom(pivot.z * 647 + pivot.x * 10)) * _GrassCurving;
                //Adds a bit of curving to grass blade

                //posOS -> posWS
                float3 positionWS = positionOS + pivot;

                // blades flung outward and upward by the AOE cut
                positionWS += float3(cutPushDir.x, 0.7, cutPushDir.y) * (cutFly * _GrassCutVisual.w);
                
                //posWS -> posCS
                OUT.positionCS = TransformWorldToHClip(positionWS);


                half3 baseColor = lerp(_ColorA, _ColorB, tex2Dlod(_BaseColorTexture, float4(TRANSFORM_TEX(pivot.xz, _BaseColorTexture),0,0)).r);
                
                half3 albedo = lerp(_AOColor, baseColor, IN.positionOS.y);

                float4 color = tex2Dlod(_GrassColorRT, float4(uv, 0, 0));
                albedo = lerp(albedo, color.rgb, color.a);

                //Lighting Stuff
                half3 N = normalize(bladeDirection + cameraTransformForwardWS * -0.5 + _RandomNormal * half3(srandom(pivot.x * 314 + pivot.z * 10), 0, srandom(pivot.z * 677 + pivot.x * 10)));
                //The normal vector is just the blade direction tilted a bit towards the camera with a bit of randomness
                half3 V = normalize(_WorldSpaceCameraPos - positionWS);

                float3 lighting = CalculateLighting(albedo, positionWS, N, V, color.a, IN.positionOS.y);

                // glowing band along the leading edge of the grass cut (stronger toward the blade tips)
                lighting += _GrassCutGlowColor.rgb * (cutWave * _GrassCutVisual.y * lerp(0.4, 1.0, IN.positionOS.y));
                //I'm also passing the Alpha Channel of the Color Map cause I dont want the blades that are affected with color to receive specular light 
                //The main use of the color map for me is burning the grass and the burned grass should not receive specular light
                
                float fogFactor = ComputeFogFactor(OUT.positionCS.z);
                OUT.color.rgb = MixFog(lighting, fogFactor);

                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                return half4(IN.color.rgb,1);
            }
            ENDHLSL
        }
    }
}