// Shared by the water body and the reflections drawn inside it: the shape of the hollow
// the water fills, and the noises both draw from.
#ifndef WEIZZA_WATER_COMMON
#define WEIZZA_WATER_COMMON

// Set per pond through a property block by WaterPond.cs.
float4 _WaterSpan;          // x left x, y right x, z surface height, w floor samples used
float _WaterFloor[64];      // ground height under the water, evenly spaced from left to right

float WaterHash(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

float2 WaterHash2(float2 p)
{
    p = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)));
    return frac(sin(p) * 43758.5453);
}

// Smooth value noise for soft painted shapes.
float WaterNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = WaterHash(i);
    float b = WaterHash(i + float2(1, 0));
    float c = WaterHash(i + float2(0, 1));
    float d = WaterHash(i + float2(1, 1));
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

// Distance to the nearest cell wall of a drifting Voronoi pattern (F2 minus F1).
float WaterVoronoiEdge(float2 p, float t)
{
    float2 i = floor(p);
    float2 f = frac(p);
    float f1 = 8.0, f2 = 8.0;
    [unroll] for (int y = -1; y <= 1; y++)
    [unroll] for (int x = -1; x <= 1; x++)
    {
        float2 g = float2(x, y);
        float2 o = WaterHash2(i + g);
        o = 0.5 + 0.45 * sin(t + 6.2831 * o);
        float d = length(g + o - f);
        if (d < f1) { f2 = f1; f1 = d; }
        else if (d < f2) { f2 = d; }
    }
    return f2 - f1;
}

// Ground height under the water at a world x, from the samples the pond baked.
float WaterFloorAt(float x)
{
    float n = max(_WaterSpan.w, 2.0);
    float u = saturate((x - _WaterSpan.x) / max(_WaterSpan.y - _WaterSpan.x, 1e-3)) * (n - 1.0);
    int i = (int)floor(u);
    int j = min(i + 1, (int)n - 1);
    return lerp(_WaterFloor[i], _WaterFloor[j], u - i);
}

#endif
