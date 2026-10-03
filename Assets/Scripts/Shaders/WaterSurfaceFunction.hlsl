// Custom Function Node source for Weizza/Water2D_Graph (Shader Graph).
//
// One Custom Function Node carries the whole surface response, rather than
// a wall of small math nodes, for a real reason: the script
// that builds this graph can add and connect nodes but has no way to create
// an exposed Blackboard property, only per-node constant values. Splitting
// this into twenty small nodes would still leave every number a fixed
// constant, so one well organised function with named inputs reads better
// in the graph and is exactly as tunable, each input is its own constant
// node the graph can drag a value on directly.
//
// The wave math itself is the same Gerstner sum reduced to one dimension as
// the hand written Water2D.shader this sits beside, researched from
// ameye.dev's stylized water breakdown: a height function and its own
// analytic slope, a pseudo normal built from that slope, lit with a real
// N dot L rather than thresholded noise.
void WaterSurface_float(float2 uv, float4 screenPosition, UnityTexture2D reflectionTex,
    float time, float3 deepColor, float3 shallowColor,
    float reflectionStrength, float reflectionExposure,
    float waveScale, float waveSpeed, float waveAmount, float distortStrength,
    float3 foamColor, float foamAlpha, float foamWidth,
    float3 causticColor, float causticAlpha, float causticScale, float causticSpeed, float causticStrength,
    float2 lightDir, float3 specularColor, float specularAlpha, float shininess,
    float sparkleScale, float sparkleThreshold,
    out float3 color, out float alpha)
{
    const float freqMul[4] = { 1.0, 1.9, 3.3, 5.7 };
    const float ampMul[4] = { 1.0, 0.5, 0.28, 0.14 };
    const float speedMul[4] = { 1.0, -1.4, 1.7, -2.3 };
    const float phase[4] = { 0.0, 2.1, 4.6, 1.3 };

    float height = 0;
    float slope = 0;

    for (int i = 0; i < 4; i++)
    {
        float k = waveScale * freqMul[i];
        float w = k * uv.x - time * waveSpeed * speedMul[i] + phase[i];
        float a = waveAmount * ampMul[i];
        height += a * sin(w);
        slope += a * k * cos(w);
    }

    float depth = saturate(uv.y - height);

    float2 screenUV = screenPosition.xy / screenPosition.w;
    screenUV += float2(-slope, 0) * distortStrength;

    float3 reflection = SAMPLE_TEXTURE2D(reflectionTex.tex, reflectionTex.samplerstate, screenUV).rgb;
    reflection = pow(reflection * reflectionExposure, 0.85);

    float3 baseColour = lerp(deepColor, shallowColor, depth);
    float fresnel = pow(depth, 2.2);
    color = lerp(baseColour, reflection, reflectionStrength * fresnel);
    alpha = lerp(0.85, 0.6, depth);

    float c1 = sin(uv.x * causticScale + time * causticSpeed
                 + frac(sin(dot(uv * 3 + time * 0.1, float2(12.9898, 78.233))) * 43758.5453) * 2);
    float c2 = sin(uv.x * causticScale * 1.3 - time * causticSpeed * 1.4
                 + uv.y * causticScale * 0.5 + 4.7);
    float caustic = saturate(c1 * c2);
    caustic = pow(caustic, 3) * causticStrength;
    caustic *= smoothstep(0, 0.15, depth) * smoothstep(1, 0.55, depth);
    color += causticColor * caustic * causticAlpha;

    float2 normal = normalize(float2(-slope, 1));
    float nDotL = saturate(dot(normal, normalize(lightDir)));
    float specular = pow(nDotL, shininess) * depth;
    color += specularColor * specular * specularAlpha;

    float sparkleNoise = frac(sin(dot(float2(uv.x * sparkleScale + time * 0.3,
                                              uv.y * sparkleScale * 0.4 - time * 0.2),
                                       float2(12.9898, 78.233))) * 43758.5453);
    float sparkle = smoothstep(sparkleThreshold, 1, sparkleNoise) * depth * 0.6;
    color += specularColor * sparkle;

    float foamMask = 1 - smoothstep(0, foamWidth, depth);
    color += foamColor * foamMask * foamAlpha;
}
