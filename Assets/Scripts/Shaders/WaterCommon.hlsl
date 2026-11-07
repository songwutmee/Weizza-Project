// Shared by the pond surface and the reflections laid over it: where the water is,
// and the noises both draw from. Kept in one place so the reflection is clipped to
// exactly the same painted shore the surface draws.
#ifndef WEIZZA_WATER_COMMON
#define WEIZZA_WATER_COMMON

// Set per pond through a property block by WaterPond.cs.
float4 _WaterRect;   // x centre x, y half width, z centre z, w half depth (world metres)
float4 _WaterLevel;  // x surface height, y shore wobble (metres), z corner radius, w seed

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
// Small near the walls, which is where caustic light gathers.
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

// Metres inside the painted shore at a point on the water plane. Negative is outside.
// A rounded box in (x, z), with depth stretched because a metre of depth reads much
// thinner than a metre across on a camera pitched only sixteen degrees, then a slow
// wobble so the edge reads drawn, not cut.
float WaterInside(float x, float z)
{
    const float depthStretch = 1.8;
    float r = _WaterLevel.z;
    float2 p = float2(x - _WaterRect.x, (z - _WaterRect.z) * depthStretch);
    float2 b = float2(_WaterRect.y, _WaterRect.w * depthStretch) - r;
    float2 q = abs(p) - b;
    float sdf = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
    float wobble = (WaterNoise(float2(x * 1.3 + _WaterLevel.w, z * 2.1)) - 0.5) * 2.0 * _WaterLevel.y
                 + (WaterNoise(float2(x * 4.7, z * 6.0 + _WaterLevel.w)) - 0.5) * 0.6 * _WaterLevel.y;
    return -sdf + wobble;
}

#endif
