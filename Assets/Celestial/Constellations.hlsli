// Constellation stick figures: highlight rings around member stars and great-circle
// segments that meet those rings. Main view only — probes stay catalogue-clean.
// Guide records live after the Hipparcos catalogue in AnomalyCelestialData.
// Figure membership for fantasy glow is stamped into catalogue magnitudes (+100).

float3 SlerpDir(float3 a, float3 b, float t)
{
    float cosOm = clamp(dot(a, b), -1, 1);
    float omega = acos(cosOm);
    if (omega < 1e-5) return normalize(lerp(a, b, t));
    float s = sin(omega);
    return (a * sin((1 - t) * omega) + b * sin(t * omega)) / s;
}

// Soft annulus in angular pixels around a member direction.
float HighlightRing(float3 ray, float3 center, float radiusPx, float widthPx, float footprint)
{
    float chord = length(ray - center);
    float r = radiusPx * footprint;
    float w = max(widthPx * footprint, footprint * 0.35);
    float inner = smoothstep(r - w * 1.6, r - w * 0.35, chord);
    float outer = 1 - smoothstep(r + w * 0.15, r + w * 1.1, chord);
    return saturate(inner * outer);
}

// Great-circle segment inset so lines begin/end on the highlight rings.
float GuideSegment(float3 ray, float3 a, float3 b, float radiusPx, float widthPx, float footprint)
{
    float cosAb = clamp(dot(a, b), -1, 1);
    float arc = acos(cosAb);
    float inset = radiusPx * footprint;
    if (arc <= inset * 2.2) return 0;
    float t0 = inset / arc;
    float t1 = 1 - t0;
    float3 a2 = SlerpDir(a, b, t0);
    float3 b2 = SlerpDir(a, b, t1);
    float3 n = cross(a2, b2);
    float nLen = length(n);
    if (nLen < 1e-8) return 0;
    n /= nLen;
    float plane = abs(dot(ray, n));
    float halfW = max(widthPx * footprint * 0.5, footprint * 0.2);
    float stroke = 1 - smoothstep(halfW * 0.35, halfW * 1.4, plane);
    if (stroke <= 0) return 0;
    float3 projected = normalize(ray - n * dot(ray, n));
    if (dot(projected, a2) < 0 && dot(projected, b2) < 0) return 0;
    float cosAp = clamp(dot(a2, projected), -1, 1);
    float cosPb = clamp(dot(projected, b2), -1, 1);
    float cosSpan = clamp(dot(a2, b2), -1, 1);
    float along = abs(acos(cosAp) + acos(cosPb) - acos(cosSpan));
    float onSeg = 1 - smoothstep(0.002, 0.012, along);
    return stroke * onSeg;
}

float3 ConstellationGuides(AnomalyCelestialInput input)
{
    if (input.isProbe || CelestialUniform[2].z < 0.5) return 0;
    uint guideStart = (uint)CelestialUniform[3].x;
    uint edgeCount = (uint)CelestialUniform[3].y;
    uint memberCount = (uint)CelestialUniform[3].z;
    uint memberStart = (uint)CelestialUniform[3].w;
    if (guideStart == 0 || edgeCount == 0 || memberCount == 0) return 0;
    if (memberStart + memberCount > input.dataCount) return 0;
    float footprint = input.angularPixel;
    float radiusPx = 5.5;
    float ringWidthPx = 1.35;
    float lineWidthPx = 1.15;
    float3 ink = float3(0.45, 0.72, 1.35);
    float3 result = 0;
    [loop] for (uint i = 0; i < memberCount; i++)
    {
        float3 center = AnomalyCelestialData[memberStart + i].xyz;
        float ring = HighlightRing(input.direction, center, radiusPx, ringWidthPx, footprint);
        result += ink * (0.55 * ring);
    }
    uint edgeStart = guideStart + 1;
    [loop] for (uint e = 0; e < edgeCount; e++)
    {
        float3 a = AnomalyCelestialData[edgeStart + e * 2].xyz;
        float3 b = AnomalyCelestialData[edgeStart + e * 2 + 1].xyz;
        float seg = GuideSegment(input.direction, a, b, radiusPx, lineWidthPx, footprint);
        result += ink * (0.4 * seg);
    }
    return result;
}
