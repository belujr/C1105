// A pool of blood that spreads across the floor under the dying player (alpha blended, unlit).
// Drawn on a flat quad by PlayerDeathFX. Runtime values (_Progress, _Alpha, _Pulse, _RT) are set by the script.
//   - the edge is pushed in and out by noise, so the pool has tendrils and bulges (never a perfect circle)
//   - dark blood with brighter wet streaks and a few holes, like thick liquid
Shader "VFX/DeathPool"
{
    Properties
    {
        _NoiseTex ("Noise (tileable, Wrap Mode = Repeat)", 2D) = "gray" {}
        _ColorDark ("Dark Blood", Color) = (0.22, 0.0, 0.012, 1)
        _ColorMid ("Blood", Color) = (0.55, 0.01, 0.03, 1)
        [HDR] _ColorShine ("Wet Shine", Color) = (1.8, 0.1, 0.08, 1)
        _Scale ("Noise Scale", Float) = 1.4
        _NoiseAmount ("Edge Noise", Float) = 0.6

        [HideInInspector] _Progress ("Progress", Range(0, 1)) = 0
        [HideInInspector] _Alpha ("Alpha", Range(0, 1)) = 1
        [HideInInspector] _Pulse ("Pulse", Float) = 0
        [HideInInspector] _RT ("Real Time", Float) = 0
        [HideInInspector] _Seed ("Seed", Float) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; };

            sampler2D _NoiseTex;
            float4 _ColorDark;
            float4 _ColorMid;
            float4 _ColorShine;
            float _Scale;
            float _NoiseAmount;
            float _Progress;
            float _Alpha;
            float _Pulse;
            float _RT;
            float _Seed;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                float2 p = i.uv * 2.0 - 1.0;
                float r = length(p);

                float n1 = tex2D(_NoiseTex, i.uv * _Scale + float2(0.13, 0.71) + _Seed).r;                         // shape of the pool
                float n2 = tex2D(_NoiseTex, i.uv * _Scale * 2.3 + float2(0.50, 0.20) + _Seed * 1.7 + _RT * 0.012).r;       // wet streaks
                float n3 = tex2D(_NoiseTex, i.uv * _Scale * 0.8 + float2(0.90, 0.40) + _Seed * 2.3 - _RT * 0.008).r;       // holes

                // the edge is pushed in and out by noise. Max radius = 0.68 + 0.3, so it always stays inside the quad
                float edgeR = r + (n1 - 0.5) * _NoiseAmount;
                float reach = _Progress * 0.68;
                float inside = 1.0 - smoothstep(reach - 0.05, reach, edgeR);

                float hole = smoothstep(0.28, 0.40, n3);
                float alpha = inside * lerp(0.6, 1.0, hole);

                float3 col = lerp(_ColorDark.rgb, _ColorMid.rgb, smoothstep(0.30, 0.70, n2));

                // thin bright wet streaks, they pulse with the heartbeat
                float shine = pow(saturate(1.0 - abs(n2 - 0.55) * 7.0), 3.0);
                col += _ColorShine.rgb * shine * (0.7 + 0.6 * _Pulse);

                // the rim of the pool is darker
                float rim = smoothstep(reach - 0.25, reach - 0.02, edgeR);
                col = lerp(col, _ColorDark.rgb * 0.7, rim * 0.7);

                return float4(col, alpha * _Alpha);
            }
            ENDCG
        }
    }
}
