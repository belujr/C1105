// Clears grass (and flowers) under a soft disc. It draws into the grass / flower MASK textures that
// GrassDataRendererFeature renders every frame, exactly like your FlowerMaskShader.
// It never draws in the normal camera view (its passes use the GrassMask / FlowerMask light modes).
Shader "InfiniteGrass/Modifiers/HealAuraGrassMask"
{
    Properties
    {
        _Opacity ("Opacity", Range(0, 1)) = 1
        _SolidRadius ("Fully cleared radius (0-1 of the disc)", Range(0, 1)) = 0.85
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Cull Off
        ZWrite Off
        ZTest Always
        Blend One One
        BlendOp Max

        CGINCLUDE
        #include "UnityCG.cginc"

        struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
        struct v2f { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; };

        float _Opacity;
        float _SolidRadius;

        v2f vert (appdata v)
        {
            v2f o;
            o.vertex = UnityObjectToClipPos(v.vertex);
            o.uv = v.uv;
            return o;
        }

        float4 frag (v2f i) : SV_Target
        {
            float r = length(i.uv - 0.5) * 2.0;
            float v = 1.0 - smoothstep(_SolidRadius, 1.0, r);
            return float4(v * _Opacity, 0, 0, 1);
        }
        ENDCG

        Pass
        {
            Tags { "LightMode" = "GrassMask" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            ENDCG
        }

        Pass
        {
            Tags { "LightMode" = "FlowerMask" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            ENDCG
        }
    }
}
