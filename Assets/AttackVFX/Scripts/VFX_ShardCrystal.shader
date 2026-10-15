// Faceted ice-crystal look for the Imp's projectile (URP, unlit).
//   - every flat face is shaded differently, so the shard glitters as it spins
//   - glowing rim, bright glints and per-face sparkle flashes
//   - brighter toward the pointed front tip
// Colors are set on the MATERIAL (Deep / Light / Rim). Works on a mesh renderer and on mesh particles.
Shader "VFX/ShardCrystal"
{
    Properties
    {
        [HDR] _ColorDeep ("Deep Color (dark facets)", Color) = (0.12, 0.28, 0.85, 1)
        [HDR] _ColorLight ("Light Color (bright facets)", Color) = (0.7, 0.88, 1.0, 1)
        [HDR] _ColorRim ("Rim Color", Color) = (0.62, 0.45, 1.0, 1)
        _Glow ("Tip Glow", Float) = 1.2
        _FresnelPower ("Rim Sharpness", Range(0.5, 6)) = 2.2
        _SparkleSpeed ("Sparkle Speed", Float) = 14
        _Intensity ("Intensity", Float) = 1.6
        _Additive ("Additive (1 = glow, 0 = solid)", Range(0, 1)) = 0.35
        _LightDir ("Light Direction", Vector) = (0.4, 0.8, 0.3, 0)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "ShardCrystal"
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ColorDeep;
                half4 _ColorLight;
                half4 _ColorRim;
                float _Glow;
                float _FresnelPower;
                float _SparkleSpeed;
                float _Intensity;
                float _Additive;
                float4 _LightDir;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 n = normalize(IN.normalWS);
                float3 L = normalize(_LightDir.xyz);

                // direction toward the camera (orthographic cameras use the view axis instead of a position)
                float3 toCamPersp = normalize(_WorldSpaceCameraPos - IN.positionWS);
                float3 toCamOrtho = normalize(UNITY_MATRIX_V[2].xyz);
                float3 v = unity_OrthoParams.w > 0.5 ? toCamOrtho : toCamPersp;

                float facet = saturate(dot(n, L) * 0.5 + 0.5);
                float fres = pow(1.0 - saturate(abs(dot(n, v))), _FresnelPower);

                float3 H = normalize(L + v);
                float spec = pow(saturate(abs(dot(n, H))), 48.0);

                // each face flashes on its own, a little out of step with the others
                float id = frac(sin(dot(round(n * 7.0), float3(12.9898, 78.233, 37.719))) * 43758.5453);
                float sparkle = pow(saturate(sin(_Time.y * _SparkleSpeed + id * 6.2831853)), 12.0);

                float tip = smoothstep(0.35, 1.0, IN.uv.x);   // uv.x: 0 at the tail, 1 at the pointed front

                half3 col = lerp(_ColorDeep.rgb, _ColorLight.rgb, facet);
                col += _ColorRim.rgb * fres * 1.2;
                col += spec * 1.5;
                col += _ColorLight.rgb * sparkle * 0.8;
                col += _ColorLight.rgb * tip * _Glow;

                float alpha = saturate(0.55 + fres * 0.4 + spec);

                // premultiplied: _Additive 1 = pure glow, 0 = solid
                return half4(col * _Intensity * alpha, alpha * (1.0 - _Additive));
            }
            ENDHLSL
        }
    }
}
