// Procedural particle shader: soft glowing disc, or a ring when _RingWidth > 0.
// No textures needed. Uses premultiplied blending with a fixed blend state:
// _Additive = 1 -> pure additive glow, _Additive = 0 -> normal alpha blend (used for dust).
Shader "Dash/GlowParticle"
{
    Properties
    {
        _Intensity ("Intensity", Float) = 2
        _Softness ("Softness", Range(0.2, 6)) = 1.6
        _RingWidth ("Ring Width (0 = soft disc)", Range(0, 0.5)) = 0
        _Additive ("Additive (1 = glow, 0 = alpha blend)", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "PreviewType"="Plane" }
        Blend One OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            float _Intensity;
            float _Softness;
            float _RingWidth;
            float _Additive;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.uv = v.uv;
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                float2 d = i.uv * 2.0 - 1.0;
                float r = length(d);

                float disc = pow(saturate(1.0 - r), _Softness);
                float ring = pow(saturate(1.0 - abs(r - 0.7) / max(_RingWidth, 0.001)), 2.0) * step(r, 1.0);
                float a = lerp(disc, ring, step(0.001, _RingWidth));

                float alpha = i.color.a * a;
                // Premultiplied output: alpha channel is reduced by _Additive, so 1 = pure additive
                return float4(i.color.rgb * _Intensity * alpha, alpha * (1.0 - _Additive));
            }
            ENDCG
        }
    }
}
