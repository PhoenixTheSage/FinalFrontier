// Actual Keen SDR filmic shoulder, with neutral exposure and grading.
#include <Postprocess/Tonemapping/Filters.hlsli>
Texture2D<float4> SolarRadiance : register(t0);
float4 __pixel_shader(float4 pos : SV_Position, float2 uv : TEXCOORD0) : SV_Target
{
    return float4(saturate(ToneMapFilmic_Hable(SolarRadiance.Load(int3(uint2(pos.xy),0)).rgb,
                                             frame_.Post.WhitePoint)), 1);
}
