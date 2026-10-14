// Runs the shipped optical profile alone, so plasma knots cannot hide a ring.
#include <AnomalyCelestial.hlsli>
#include "../Assets/Celestial/Sun.hlsli"
float4 __pixel_shader(float4 pos : SV_Position, float2 uv : TEXCOORD0) : SV_Target
{
    float radius = CelestialUniform[0].z;
    // Match the production main-view reconstruction from SV_Position.
    float2 screenUv = pos.xy / CelestialView.xy;
    float3 ray = normalize(float3((screenUv * 2 - 1) * radius * 5, -1));
    float glow = SolarAureole(ray, float3(0,0,-1), radius, CelestialUniform[5].y);
    return float4(glow, glow, glow, 1);
}
