// A texture drawn flat on the ground, additive. By default it is hidden behind objects (trees, walls) like anything else;
// the script can set _ZTest to 8 (Always) to draw it over everything. Used for the glowing ring under the floating Ranged Minion.
// The color and the opacity are set by script (_Color, with alpha = opacity).
Shader "VFX/GroundOverlay"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        [HDR] _Color ("Color", Color) = (1, 1, 1, 1)
        // 4 = LessEqual (hidden behind objects, like anything else), 8 = Always (drawn over everything)
        [HideInInspector] _ZTest ("ZTest", Float) = 4
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+10" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Blend SrcAlpha One
        ZWrite Off
        ZTest [_ZTest]
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; };

            sampler2D _MainTex;
            float4 _Color;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                float t = tex2D(_MainTex, i.uv).r;
                return float4(_Color.rgb, t * _Color.a);
            }
            ENDCG
        }
    }
}
