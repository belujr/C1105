#ifndef WATER_RIPPLES_INCLUDED
#define WATER_RIPPLES_INCLUDED

float4 _Ripples[16];   // x, z, spawnTime, strength

void WaterRipples_float(float3 WorldPos, float Speed, float Life, float Width,
                        float Frequency, out float Height, out float Foam)
{
    Height = 0;
    Foam = 0;

    [unroll]
    for (int i = 0; i < 16; i++)
    {
        float4 r = _Ripples[i];
        float age = _Time.y - r.z;
        if (age < 0 || age > Life) continue;

        float d = distance(WorldPos.xz, r.xy);
        float radius = age * Speed;
        float fade = (1 - age / Life) * r.w;

        // Gaussian band around the expanding radius
        float x = (d - radius) / Width;
        float band = exp(-x * x);

        Height += band * cos((d - radius) * Frequency) * fade;
        Foam   += band * fade;
    }
    Foam = saturate(Foam);
}
#endif