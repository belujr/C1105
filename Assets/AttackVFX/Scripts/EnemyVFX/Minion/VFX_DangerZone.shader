// The danger zone a Ranged Minion shows while it aims: a fan (or a lane, or a full circle) on the floor.
// It is drawn over the grass (ZTest Always), so tall grass can never hide it.
//   - a faint area, brighter near the minion
//   - bright edge lines (constant width in meters) along the sides and the far rim, with a slight flicker
//   - CHEVRONS: V-shaped bands that flow outward, the way the bullets will fly
//   - range marks at every quarter of the distance
//   - a bright FRONT that moves outward while the minion charges (the area fills up as the shot gets ready)
//   - when the shot LOCKS, a bright wave races across the zone and everything glows brighter
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
        [HideInInspector] _LockT ("Lock Time", Range(0, 1)) = 0
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
            float _LockT;
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
                float ang = abs(i.uv.y - 0.5) * 2.0;   // 0 in the middle, 1 at the side edges

                // edge lines along the sides and the far rim (constant width in meters), with a slight flicker
                float edgeLine = saturate((1.0 - smoothstep(0.03, 0.09, i.edge.x)) + (1.0 - smoothstep(0.03, 0.09, i.edge.y)));
                edgeLine *= 0.85 + 0.15 * sin(_Time.y * 18.0);

                // the area, brighter near the minion
                float area = _Fill * lerp(1.0, 0.4, r);

                // the charging front: everything behind it is brighter
                float behind = 1.0 - smoothstep(_Progress - 0.05, _Progress, r);
                float front = exp(-pow((r - _Progress) / 0.02, 2.0)) * step(0.001, _Progress);

                // chevrons: V-shaped bands that flow outward, the direction the bullets will fly
                float chev = frac(r * 9.0 - _Time.y * 1.6 + ang * 0.7);
                float chevMask = smoothstep(0.0, 0.06, chev) * (1.0 - smoothstep(0.06, 0.2, chev));
                float chevAlpha = chevMask * 0.3 * (0.25 + 0.75 * behind) * (1.0 - 0.6 * r);

                // range marks at every quarter of the distance
                float tick = 1.0 - smoothstep(0.0, 0.012, abs(frac(r * 4.0 + 0.5) - 0.5));
                float tickAlpha = tick * 0.2 * (0.4 + 0.6 * behind);

                // when the aim locks, a bright wave races across the zone
                float lockFront = exp(-pow((r - _LockT * 1.15) / 0.035, 2.0)) * step(0.001, _LockT) * (1.0 - _LockT);

                float alpha = area * (0.35 + 0.65 * behind)
                            + chevAlpha + tickAlpha
                            + edgeLine * (0.55 + 0.45 * _Lock)
                            + front * 0.7
                            + lockFront * 0.9
                            + _Lock * area * 0.5;

                float3 col = lerp(_Color.rgb, _EdgeColor.rgb, saturate(edgeLine + front + lockFront + _Lock * 0.5 + chevMask * 0.3));
                return float4(col, saturate(alpha) * _Alpha);
            }
            ENDCG
        }
    }
}
