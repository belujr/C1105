Shader "InfiniteGrass/Modifiers/GrassSteppedTrailShader"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        // Increased the maximum range from 1 to 10 so you can push the grass down much harder
        _Strength ("Strength", Range(0, 10)) = 2.0
    }
    SubShader
    {
        Tags {
            "Queue" = "Transparent" 
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent" 
            "LightMode" = "GrassSlope"
        }

        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha
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
                float4 tangent : TANGENT;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float4 tangent : TANGENT;
                float4 color : COLOR;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _Strength;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.tangent = v.tangent;
                o.color = v.color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
               fixed4 col = tex2D(_MainTex, i.uv);

    // Use the texture's Red and Green channels to dictate the X and Z bend directions
    float2 encodeToSlope = 1.0 - col.rg;
    // Calculate strength using the texture's alpha and the particle's alpha
    // (We stop using col.r here so strength isn't tied to the X-axis bend direction)
    float strength = col.a * i.color.a * _Strength;

    return float4(encodeToSlope, 0, strength);
            }
            ENDCG
        }
    }
}