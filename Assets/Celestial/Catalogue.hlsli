// Pixel-integrated power-shaped PSF. Flux is distributed between adjacent
// pixels; shrinking the angular field of view never resolves a source texture.
// power = 1 recovers the original triangular profile. Higher powers keep the
// same support and total flux while concentrating energy into a pin-prick core
// with a rapid soft falloff — linear HDR radiance, no SDR clamp.
float StarCDF(float x, float width, float power)
{
    x /= width;
    float e = power + 1;
    return x <= -1 ? 0 : x < 0 ? 0.5 * pow(max(1 + x, 0), e) : x < 1 ? 1 - 0.5 * pow(max(1 - x, 0), e) : 1;
}

// Atmospheric scintillation: multiplicative in linear light so HDR exposure and
// bloom see dancing radiance, not clipped SDR blinks. Phase varies per star;
// amount and speed are independent controls. Faint points dance a bit more than
// bright ones. Returns a factor centered near 1; never reaches zero.
float StarTwinkle(float3 dir, float relativeFlux, float amount, float speed, float time)
{
    if (amount <= 1e-4) return 1;
    float id = frac(sin(dot(dir, float3(12.9898, 78.233, 45.164))) * 43758.5453);
    float id2 = frac(sin(dot(dir.yzx, float3(39.346, 11.135, 83.157))) * 24634.634);
    float rate = max(speed, 0);
    float speedA = lerp(0.8, 3.2, id) * rate;
    float speedB = lerp(0.25, 1.4, id2) * rate;
    float wave = sin(time * speedA + id * 6.2831853) * 0.65
               + sin(time * speedB + id2 * 6.2831853) * 0.35;
    float damp = lerp(0.55, 1.0, saturate(1.0 - relativeFlux * 0.15));
    float amp = amount * damp;
    return max(1 + wave * amp, 0.05);
}

float3 CatalogueStars(AnomalyCelestialInput input)
{
    if (input.dataCount < 32769 || CelestialUniform[0].x <= 0) return 0;
    float3 ray = input.direction;
    float footprint = input.angularPixel;
    // Broader source support reduces temporal reconstruction's sensitivity to
    // subpixel phase. The normalized CDF keeps total flux unchanged.
    float width = clamp(CelestialUniform[1].z, 0.85, 2.5);
    // 0 means "unset" for older uniform packs / smoke defaults → triangular.
    float power = CelestialUniform[1].w > 0.5 ? clamp(CelestialUniform[1].w, 1, 8) : 1;
    float twinkleAmt = input.isProbe ? 0 : max(CelestialUniform[2].x, 0);
    float twinkleSpeed = max(CelestialUniform[2].y, 0);
    float fantasy = (!input.isProbe && CelestialUniform[2].w > 0.5) ? ConstellationSkyVisibility() : 0;
    float3 dx = input.directionDx / footprint, dy = input.directionDy / footprint;
    float xx = dot(dx,dx), xy = dot(dx,dy), yy = dot(dy,dy);
    float determinant = max(xx*yy-xy*xy, 1e-8);
    // Conservative support covers both pixel axes, including cell boundaries.
    // Fantasy members use a slightly wider glow; expand the search to match.
    float searchWidth = fantasy > 0 ? width * 1.55 : width;
    float support = (2 * (searchWidth + 0.5) + 0.3) * footprint;
    float searchRadius = support + 2e-6; // float32 cell-boundary rounding margin
    int3 lo = clamp((int3)floor((ray-searchRadius+1)*16), 0, 31);
    int3 hi = clamp((int3)floor((ray+searchRadius+1)*16), 0, 31);
    float3 result = 0;
    [loop] for (int z=lo.z; z<=hi.z; z++)
    [loop] for (int y=lo.y; y<=hi.y; y++)
    [loop] for (int x=lo.x; x<=hi.x; x++)
    {
        float2 range = AnomalyCelestialData[1+x+32*(y+32*z)].xy;
        uint start = (uint)range.x, count = (uint)range.y;
        [loop] for (uint i=0; i<count; i++)
        {
            uint index = start + 2*i;
            if (index+1 >= input.dataCount) break;
            float4 star = AnomalyCelestialData[index];
            float3 delta = star.xyz-ray;
            if (dot(delta,delta) > support*support) continue;
            float4 color = AnomalyCelestialData[index+1];
            float mag = color.w;
            float member = mag > 50 ? 1 : 0;
            if (member > 0) mag -= 100;
            if (mag > CelestialUniform[1].x) continue;
            float starWidth = lerp(width, width * 1.45, member * fantasy);
            float starPower = lerp(power, max(power * 0.55, 1.25), member * fantasy);
            delta /= footprint;
            float a = dot(delta,dx), b = dot(delta,dy);
            float2 q = float2(a*yy-b*xy, b*xx-a*xy)/determinant;
            float weight = (StarCDF(q.x+0.5, starWidth, starPower)-StarCDF(q.x-0.5, starWidth, starPower)) *
                           (StarCDF(q.y+0.5, starWidth, starPower)-StarCDF(q.y-0.5, starWidth, starPower));
            float scintillation = StarTwinkle(star.xyz, star.w, twinkleAmt, twinkleSpeed, frame_.frameTime);
            float3 tint = lerp(1, color.rgb, CelestialUniform[1].y);
            tint = lerp(tint, tint * float3(0.85, 0.95, 1.35) + 0.15, member * fantasy);
            float boost = lerp(1, 3.2, member * fantasy);
            result += tint * star.w * weight * scintillation * boost;
        }
    }
    return result * (8 * CelestialUniform[0].x);
}
