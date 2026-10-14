// Holographic afterimage shader: glowing fresnel rim, translucent core, subtle scanlines.
// Unlit, glow/alpha mix (see _Additive). Works in Built-in and URP (URP renders passes with no LightMode tag).
Shader "Dash/GhostFresnel"
{
    Properties
    {
        [HDR] _Color ("Rim Color", Color) = (0.4, 0.9, 1, 1)
        [HDR] _CoreColor ("Core Color", Color) = (0.05, 0.25, 0.8, 0.35)
        _FresnelPower ("Fresnel Power", Range(0.5, 8)) = 2.2
        _Intensity ("Intensity", Float) = 2.5
        _Alpha ("Alpha", Range(0, 1)) = 1
        _ScanDensity ("Scanline Density", Float) = 40
        _ScanStrength ("Scanline Strength", Range(0, 1)) = 0.25
        _Additive ("Additive (1 = glow, 0 = solid-ish, use lower on bright ground)", Range(0, 1)) = 0.6
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend One OneMinusSrcAlpha
        ZWrite Off
        Cull Back

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldNormal : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
            };

            float4 _Color;
            float4 _CoreColor;
            float _FresnelPower;
            float _Intensity;
            float _Alpha;
            float _ScanDensity;
            float _ScanStrength;
            float _Additive;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                float3 n = normalize(i.worldNormal);

                // Direction toward the camera (orthographic cameras need the view axis instead of a position)
                float3 toCamPersp = normalize(_WorldSpaceCameraPos - i.worldPos);
                float3 toCamOrtho = normalize(UNITY_MATRIX_V[2].xyz);
                float3 v = unity_OrthoParams.w > 0.5 ? toCamOrtho : toCamPersp;

                float fres = pow(1.0 - saturate(dot(n, v)), _FresnelPower);
                float scan = 1.0 - _ScanStrength * (0.5 + 0.5 * sin(i.worldPos.y * _ScanDensity - _Time.y * 12.0));

                float3 col = lerp(_CoreColor.rgb, _Color.rgb, fres) * _Intensity * scan;
                float a = lerp(_CoreColor.a, 1.0, fres) * _Alpha;
                // Premultiplied: _Additive 1 = pure glow, lower = more opaque so it stays visible on bright ground
                return float4(col * a, a * (1.0 - _Additive));
            }
            ENDCG
        }
    }
}
