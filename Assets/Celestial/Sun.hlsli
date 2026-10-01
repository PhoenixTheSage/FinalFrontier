// Analytic photosphere + continuous limb glare. The disc owns the interior;
// glare fills the AA fringe and falls off outside so there is no dark ring
// between photosphere and bloom. Atmosphere immersion widens / softens the wing.

void SunTangentBasis(float3 sunDir, out float3 tangent, out float3 bitangent)
{
    float3 up = abs(sunDir.y) < 0.995 ? float3(0, 1, 0) : float3(1, 0, 0);
    tangent = normalize(cross(up, sunDir));
    bitangent = cross(sunDir, tangent);
}

// Returns glare radiance relative to sunRadiance (same units as limb*disc).
// Peak near the limb ≈ 1 so the core matches disc nits; (1-disc) in EvaluateSun
// composites without a hole.
float SolarAureole(float3 ray, float3 sunDir, float radius, float pixelWidth, float air)
{
    float edge = max(2.0 * sin(radius * 0.5), 1e-6);
    float ang = acos(clamp(dot(ray, sunDir), -1, 1));

    // Space: compact diffraction skirt. Air: wider Mie-like scatter.
    float support = lerp(
        clamp(radius * 10.0, 0.018, 0.08),
        clamp(radius * 28.0, 0.10, 0.32),
        air);
    if (ang > support) return 0;

    float3 t, b;
    SunTangentBasis(sunDir, t, b);
    float2 q = float2(dot(ray - sunDir, t), dot(ray - sunDir, b)) / edge;
    // Slight ellipse in space; nearly round under air (scatter washes anisotropy).
    float ovalX = lerp(1.06, 1.02, air);
    float ovalY = lerp(0.94, 0.98, air);
    float shaped = length(float2(q.x * ovalX, q.y * ovalY));

    // Begin the skirt inside the photosphere so AA pixels never go dark.
    float join = 1.0 - saturate(pixelWidth / edge);
    float skirt = smoothstep(join - 0.22, join + 0.02, shaped);
    if (skirt <= 0) return 0;

    float u = saturate(ang / support);
    // Distance past the near-limb in solar radii (0 at limb join).
    float x = max(shaped - join, 0);

    // Limb-matched peak: air a bit hotter so the bloom core reads as bright as the disc.
    float peak = lerp(0.85, 1.25, air);
    // Lorentzian core hugging the limb, then a slower power wing (air = softer exponent).
    float coreScale = lerp(1.7, 0.55, air);
    float core = rcp(1.0 + (x * coreScale) * (x * coreScale));
    float wingExp = lerp(2.4, 1.35, air);
    float wing = pow(saturate(1.0 - u), wingExp);
    // Diffraction spikes mainly in vacuum; kill them in atmosphere.
    float theta = atan2(q.y, q.x);
    float spike = 1.0 + lerp(0.11, 0.0, air) * pow(saturate(abs(cos(2.0 * theta))), 12.0);
    float gate = 1.0 - smoothstep(0.72, 1.0, u);

    return peak * core * wing * spike * skirt * gate;
}

float3 EvaluateSun(AnomalyCelestialInput input)
{
    if (input.isProbe || CelestialUniform[0].w <= 0.5) return 0;

    float radius = max(CelestialUniform[0].z, 1e-6);
    float footprint = input.angularPixel;
    float air = saturate(CelestialUniform[5].y);
    float chordRadius = 2.0 * sin(radius * 0.5);
    float d = length(input.direction - input.sunDirection) / max(chordRadius, 1e-6);
    float mu = sqrt(saturate(1.0 - d * d));
    // Soften limb darkening slightly so the rim stays bright into the glare join.
    float limb = lerp(0.55, 1.0, mu);
    float disc = AnomalyCelestialDisc(input.direction, input.sunDirection, radius, footprint);
    float glare = SolarAureole(input.direction, input.sunDirection, radius, footprint, air);
    float3 radiance = input.sunRadiance * CelestialUniform[0].y;
    // Disc owns the interior; glare fills (1-disc) so the AA fringe never drops.
    return radiance * (limb * disc + glare * (1.0 - disc));
}
