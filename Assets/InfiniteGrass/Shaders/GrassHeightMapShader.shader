Shader "InfiniteGrass/GrassHeightMapShader"
{
    Properties { }
    SubShader
    {
        Tags { "RenderType"="Opaque" }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                half4 color : COLOR;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 color : TEXCOORD0;
            };

            float2 _BoundsYMinMax;
            float4 _SpawnAllowed; // Passed from the C# Renderer Feature

            float Remap(float In, float2 InMinMax, float2 OutMinMax)
            {
                return OutMinMax.x + (In - InMinMax.x) * (OutMinMax.y - OutMinMax.x) / (InMinMax.y - InMinMax.x);
            }

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex);
                
                float rChannel = Remap(worldPos.y, _BoundsYMinMax, float2(0, 1)); 
                float gChannel = v.color.r; 
                o.color = float2(rChannel, gChannel);

                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                // R = Altitude, G = Grass Allowed, B = Flower Allowed, A = 1
                return float4(i.color.x, i.color.y * _SpawnAllowed.x, i.color.y * _SpawnAllowed.y, 1.0);
            }
            ENDCG
        }
    }
}