// The danger zone a Ranged Minion shows while it aims: a fan (or a lane, or a full circle) on the floor.
// It is drawn over the grass (ZTest Always), so tall grass can never hide it.
//   - a faint area, brighter near the minion
//   - bright edge lines (constant width in meters) along the sides and the far rim
//   - a bright FRONT that moves outward while the minion charges (the area fills up as the shot gets ready)
//   - when the shot locks, everything flashes brighter
// The mesh is built by MinionShotVFX: uv0 = (distance 0..1, angle 0..1), uv1 = (meters to the nearest side edge, meters to the rim).
Shader "VFX/DangerZone"
{
    Properties
    {
        _Color ("Fill Color", Color) = (0.55, 0.5, 1, 1)
        [HDR] _EdgeColor ("Edge Color", Color) = (1.6, 1.4, 2.4, 1)
        _Fill ("Fill Amount", Range(0, 1)) = 0.35

        [HideInInspector] _Progress ("Progress", Range(0, 1)) = 0
        [HideInInspector] _Lock ("Lock", Range(0, 1)) = 0
        [HideInInspector] _Alpha ("Alpha", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+20" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Blend SrcAlpha One
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; float2 uv2 : TEXCOORD1; };
            struct v2f { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; float2 edge : TEXCOORD1; };

            float4 _Color;
            float4 _EdgeColor;
            float _Fill;
            float _Progress;
            float _Lock;
            float _Alpha;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.edge = v.uv2;
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                float r = i.uv.x;

                // edge lines along the sides and the far rim (constant width in meters)
                float edgeLine = saturate((1.0 - smoothstep(0.03, 0.09, i.edge.x)) + (1.0 - smoothstep(0.03, 0.09, i.edge.y)));

                // the area, brighter near the minion
                float area = _Fill * lerp(1.0, 0.45, r);

                // the charging front: everything behind it is brighter
                float behind = 1.0 - smoothstep(_Progress - 0.05, _Progress, r);
                float front = exp(-pow((r - _Progress) / 0.02, 2.0)) * step(0.001, _Progress);

                float alpha = area * (0.35 + 0.65 * behind)
                            + edgeLine * (0.55 + 0.45 * _Lock)
                            + front * 0.7
                            + _Lock * area * 0.6;

                float3 col = lerp(_Color.rgb, _EdgeColor.rgb, saturate(edgeLine + front + _Lock * 0.5));
                return float4(col, saturate(alpha) * _Alpha);
            }
            ENDCG
        }
    }
}
