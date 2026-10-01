// Constellation stick figures: highlight rings, connecting lines, optional look-at
// name headers and Texture2DArray fantasy art. Main view only — probes stay catalogue-clean.
// Guide records live after the Hipparcos catalogue in AnomalyCelestialData.
// Format 4: per-figure edge/member ranges + precomputed edge plane normals.
// Figure membership for fantasy glow is stamped into catalogue magnitudes (+100).

#define CONSTELLATION_FIGURE_STRIDE 8
#define CONSTELLATION_EDGE_STRIDE 3

// CelestialUniform[5].x: 0 (default) = full visibility; 1 = daylight under an atmosphere.
float ConstellationSkyVisibility()
{
    return 1.0 - saturate(CelestialUniform[5].x);
}

// Delayed Pattern alphas uploaded from CPU (figure 0..8).
float PatternAlpha(uint figure)
{
    if (figure < 4) return CelestialUniform[6][figure];
    if (figure < 8) return CelestialUniform[7][figure - 4];
    return CelestialUniform[8].x;
}

// Cheap stick segment: baked plane normal, no per-pixel Slerp/acos chain.
float GuideSegmentFast(float3 ray, float3 a, float3 b, float3 n, float cosAb,
    float radiusPx, float widthPx, float footprint)
{
    float arc2 = max(0, 2.0 - 2.0 * cosAb); // chord^2 proxy for short-arc reject
    float inset = radiusPx * footprint;
    if (arc2 <= (inset * 2.2) * (inset * 2.2)) return 0;

    float plane = abs(dot(ray, n));
    float halfW = max(widthPx * footprint * 0.5, footprint * 0.2);
    float stroke = 1.0 - smoothstep(halfW * 0.35, halfW * 1.4, plane);
    if (stroke <= 0) return 0;

    float3 projected = normalize(ray - n * dot(ray, n));
    // Same-winding test keeps the sample on the short arc between a and b.
    if (dot(cross(a, projected), n) < 0 || dot(cross(projected, b), n) < 0) return 0;
    // Keep stroke out of the endpoint highlight rings (small-angle cos inset).
    float cosInset = 1.0 - inset * inset * 0.5;
    if (dot(projected, a) > cosInset || dot(projected, b) > cosInset) return 0;
    return stroke;
}

float HighlightRing(float3 ray, float3 center, float radiusPx, float widthPx, float footprint)
{
    float chord = length(ray - center);
    float r = radiusPx * footprint;
    float w = max(widthPx * footprint, footprint * 0.35);
    float inner = smoothstep(r - w * 1.6, r - w * 0.35, chord);
    float outer = 1 - smoothstep(r + w * 0.15, r + w * 1.1, chord);
    return saturate(inner * outer);
}

float GuidePulse(float3 seed, float rate, float depth)
{
    float phase = dot(seed, float3(2.17, 1.61, 3.07)) * 4.0;
    float wave = sin(frame_.frameTime * rate + phase) * 0.5 + 0.5;
    wave = wave * wave * (3.0 - 2.0 * wave);
    return 1.0 - depth + depth * wave;
}

float3 CelestialLookDir()
{
    return normalize(mul(float3(0, 0, -1), (float3x3)frame_.Environment.inv_view_matrix));
}

float3 CelestialCameraUp()
{
    return normalize(mul(float3(0, 1, 0), (float3x3)frame_.Environment.inv_view_matrix));
}

float3 CelestialCameraRight()
{
    return normalize(mul(float3(1, 0, 0), (float3x3)frame_.Environment.inv_view_matrix));
}

float FocusFade(float3 look, float3 center, float radius)
{
    float d = acos(clamp(dot(look, center), -1, 1));
    // Tighter than the old 0.45..1.35×radius window to cut multi-label overlap.
    return 1.0 - smoothstep(radius * 0.30, radius * 0.85, d);
}

// Upright label axes in the tangent plane at `center`. Prefers camera-up so glyphs
// stay readable; soft-falls back near the view pole to avoid a spin singularity.
void NameAxes(float3 center, out float3 nameRight, out float3 nameUp)
{
    float3 camUp = CelestialCameraUp();
    float3 camRight = CelestialCameraRight();
    float3 upProj = camUp - center * dot(camUp, center);
    float3 rightProj = camRight - center * dot(camRight, center);
    float upLen2 = dot(upProj, upProj);
    float rightLen2 = dot(rightProj, rightProj);

    float3 upFallback = float3(0, 1, 0);
    if (rightLen2 > 1e-8)
        upFallback = normalize(cross(center, rightProj));

    // Blend out the primary up when it collapses (looking nearly along camera up/down).
    float keep = smoothstep(0.0025, 0.02, upLen2);
    float3 upPrimary = upLen2 > 1e-10 ? upProj * rsqrt(upLen2) : upFallback;
    // Keep fallback on the same hemisphere as primary when both are valid to reduce flips.
    if (dot(upFallback, upPrimary) < 0) upFallback = -upFallback;
    nameUp = normalize(lerp(upFallback, upPrimary, keep));

    // Right-handed with outward center ≈ view direction: R = N × U.
    nameRight = cross(center, nameUp);
    float rLen2 = dot(nameRight, nameRight);
    if (rLen2 < 1e-8)
    {
        nameRight = rightLen2 > 1e-8 ? normalize(rightProj) : float3(1, 0, 0);
        nameUp = normalize(cross(nameRight, center));
        nameRight = cross(center, nameUp);
    }
    else
        nameRight *= rsqrt(rLen2);

    // Prefer camera-right so reading order matches the view (avoid mirrored labels).
    if (rightLen2 > 1e-8 && dot(nameRight, rightProj) < 0)
    {
        nameRight = -nameRight;
        nameUp = -nameUp;
    }
}

// Compact 5x7 capitals. Each row is a 5-bit mask (MSB = left).
uint GlyphRow(int code, int row)
{
    if (code <= 0 || code == 27 || row < 0 || row > 6) return 0;
    uint r0, r1, r2, r3, r4, r5, r6;
    r0 = r1 = r2 = r3 = r4 = r5 = r6 = 0;
    switch (code)
    {
        case 1: r0=0x0E; r1=0x11; r2=0x11; r3=0x1F; r4=0x11; r5=0x11; r6=0x11; break; // A
        case 2: r0=0x1E; r1=0x11; r2=0x11; r3=0x1E; r4=0x11; r5=0x11; r6=0x1E; break; // B
        case 3: r0=0x0E; r1=0x11; r2=0x10; r3=0x10; r4=0x10; r5=0x11; r6=0x0E; break; // C
        case 4: r0=0x1E; r1=0x11; r2=0x11; r3=0x11; r4=0x11; r5=0x11; r6=0x1E; break; // D
        case 5: r0=0x1F; r1=0x10; r2=0x10; r3=0x1E; r4=0x10; r5=0x10; r6=0x1F; break; // E
        case 6: r0=0x1F; r1=0x10; r2=0x10; r3=0x1E; r4=0x10; r5=0x10; r6=0x10; break; // F
        case 7: r0=0x0E; r1=0x11; r2=0x10; r3=0x13; r4=0x11; r5=0x11; r6=0x0F; break; // G
        case 8: r0=0x11; r1=0x11; r2=0x11; r3=0x1F; r4=0x11; r5=0x11; r6=0x11; break; // H
        case 9: r0=0x1F; r1=0x04; r2=0x04; r3=0x04; r4=0x04; r5=0x04; r6=0x1F; break; // I
        case 10: r0=0x01; r1=0x01; r2=0x01; r3=0x01; r4=0x11; r5=0x11; r6=0x0E; break; // J
        case 11: r0=0x11; r1=0x12; r2=0x14; r3=0x18; r4=0x14; r5=0x12; r6=0x11; break; // K
        case 12: r0=0x10; r1=0x10; r2=0x10; r3=0x10; r4=0x10; r5=0x10; r6=0x1F; break; // L
        case 13: r0=0x11; r1=0x1B; r2=0x15; r3=0x15; r4=0x11; r5=0x11; r6=0x11; break; // M
        case 14: r0=0x11; r1=0x19; r2=0x15; r3=0x13; r4=0x11; r5=0x11; r6=0x11; break; // N
        case 15: r0=0x0E; r1=0x11; r2=0x11; r3=0x11; r4=0x11; r5=0x11; r6=0x0E; break; // O
        case 16: r0=0x1E; r1=0x11; r2=0x11; r3=0x1E; r4=0x10; r5=0x10; r6=0x10; break; // P
        case 17: r0=0x0E; r1=0x11; r2=0x11; r3=0x11; r4=0x15; r5=0x12; r6=0x0D; break; // Q
        case 18: r0=0x1E; r1=0x11; r2=0x11; r3=0x1E; r4=0x14; r5=0x12; r6=0x11; break; // R
        case 19: r0=0x0F; r1=0x10; r2=0x10; r3=0x0E; r4=0x01; r5=0x01; r6=0x1E; break; // S
        case 20: r0=0x1F; r1=0x04; r2=0x04; r3=0x04; r4=0x04; r5=0x04; r6=0x04; break; // T
        case 21: r0=0x11; r1=0x11; r2=0x11; r3=0x11; r4=0x11; r5=0x11; r6=0x0E; break; // U
        case 22: r0=0x11; r1=0x11; r2=0x11; r3=0x11; r4=0x11; r5=0x0A; r6=0x04; break; // V
        case 23: r0=0x11; r1=0x11; r2=0x11; r3=0x15; r4=0x15; r5=0x1B; r6=0x11; break; // W
        case 24: r0=0x11; r1=0x11; r2=0x0A; r3=0x04; r4=0x0A; r5=0x11; r6=0x11; break; // X
        case 25: r0=0x11; r1=0x11; r2=0x0A; r3=0x04; r4=0x04; r5=0x04; r6=0x04; break; // Y
        case 26: r0=0x1F; r1=0x01; r2=0x02; r3=0x04; r4=0x08; r5=0x10; r6=0x1F; break; // Z
        default: return 0;
    }
    if (row == 0) return r0;
    if (row == 1) return r1;
    if (row == 2) return r2;
    if (row == 3) return r3;
    if (row == 4) return r4;
    if (row == 5) return r5;
    return r6;
}

float SampleGlyph(int code, float2 uv)
{
    if (code <= 0 || uv.x < 0 || uv.y < 0 || uv.x > 1 || uv.y > 1) return 0;
    int gx = (int)floor(uv.x * 5.0);
    int gy = (int)floor((1.0 - uv.y) * 7.0);
    gx = clamp(gx, 0, 4);
    gy = clamp(gy, 0, 6);
    uint row = GlyphRow(code, gy);
    float on = (row >> (4 - gx)) & 1u;
    float2 f = abs(frac(float2(uv.x * 5.0, (1.0 - uv.y) * 7.0)) - 0.5);
    float soft = 1.0 - smoothstep(0.32, 0.5, max(f.x, f.y));
    return on * soft;
}

float DrawFigureName(float3 ray, float3 center, float radius, float4 name0, float4 name1, float4 name2, float4 name3)
{
    float chars[16] = {
        name0.x, name0.y, name0.z, name0.w,
        name1.x, name1.y, name1.z, name1.w,
        name2.x, name2.y, name2.z, name2.w,
        name3.x, name3.y, name3.z, name3.w
    };
    int count = 0;
    [unroll] for (int i = 0; i < 16; i++) if (chars[i] > 0.5) count = i + 1;
    if (count == 0) return 0;
    float toward = dot(ray, center);
    if (toward < 0.15) return 0;

    float3 nameRight, nameUp;
    NameAxes(center, nameRight, nameUp);
    // Sit the header above the cluster in screen space (along camera-up).
    float2 local = float2(dot(ray, nameRight), dot(ray, nameUp));
    // Floor/ceiling angular size: tiny figures stay readable, huge ones don't dominate.
    float nameScale = clamp(radius, 0.12, 0.24);
    float glyphW = nameScale * 0.20;
    float glyphH = nameScale * 0.28;
    float gap = glyphW * 0.16;
    float total = count * glyphW + max(count - 1, 0) * gap;
    // Closer to the figure pivot than the old ~0.95×radius header offset.
    float baseV = min(radius * 0.28, 0.10);
    float left = -0.5 * total;
    float ink = 0;
    [loop] for (int c = 0; c < count; c++)
    {
        float x0 = left + c * (glyphW + gap);
        float2 uv = float2((local.x - x0) / glyphW, (local.y - baseV) / glyphH);
        ink = max(ink, SampleGlyph((int)chars[c], uv));
    }
    float ang = acos(clamp(toward, -1, 1));
    float front = 1.0 - smoothstep(nameScale * 0.35, nameScale * 1.85, ang);
    return ink * front;
}

float3 DrawFigureArt(float3 ray, float3 center, float3 drawRight, float3 up, float radius, uint slice)
{
    float toward = dot(ray, center);
    if (toward < 0.15) return 0;
    float2 local = float2(dot(ray, drawRight), dot(ray, up));
    float2 uv = local / max(radius, 1e-4) * 0.5 + 0.5;
    float edge = 1.0 - smoothstep(0.92, 1.02, max(abs(local.x), abs(local.y)) / max(radius, 1e-4));
    if (edge <= 0.01 || any(uv < 0) || any(uv > 1)) return 0;
    float4 tex = AnomalyCelestialArt.SampleLevel(AnomalyCelestialArtSampler, float3(uv, slice), 0);
    float ang = acos(clamp(toward, -1, 1));
    float front = 1.0 - smoothstep(radius * 0.35, radius * 1.55, ang);
    return tex.rgb * tex.a * edge * front;
}

float3 ConstellationGuides(AnomalyCelestialInput input)
{
    if (input.isProbe) return 0;
    float skyVis = ConstellationSkyVisibility();
    if (skyVis <= 0.01) return 0;
    bool linesOn = CelestialUniform[2].z > 0.5;
    bool namesOn = CelestialUniform[4].z > 0.5;
    bool artOn = CelestialUniform[4].w > 0.5;
    if (!linesOn && !namesOn && !artOn) return 0;

    uint guideStart = (uint)CelestialUniform[3].x;
    uint edgeCount = (uint)CelestialUniform[3].y;
    uint memberCount = (uint)CelestialUniform[3].z;
    uint memberStart = (uint)CelestialUniform[3].w;
    uint figureStart = (uint)CelestialUniform[4].x;
    uint figureCount = (uint)CelestialUniform[4].y;
    if (guideStart == 0) return 0;

    float footprint = input.angularPixel;
    float3 look = CelestialLookDir();
    float3 inkLine = float3(0.45, 0.72, 1.35);
    float3 inkName = float3(0.75, 0.88, 1.15);
    float3 result = 0;

    bool delayPattern = CelestialUniform[5].z > 0.5;
    // CelestialUniform[8].y = anyPatternLive (CPU). Skip all stick work when delayed and idle.
    bool anyPatternLive = CelestialUniform[8].y > 0.5;
    if (linesOn && edgeCount > 0 && memberCount > 0 && figureCount > 0
        && memberStart + memberCount <= input.dataCount
        && (!delayPattern || anyPatternLive))
    {
        float radiusPx = 5.5;
        float ringWidthPx = 1.35;
        float lineWidthPx = 1.15;
        uint edgeRecordBase = guideStart + 1;

        // Extra angular pad so rings/strokes past the baked figure radius still draw.
        float figureConeMargin = max(0.03, footprint * (radiusPx + 4.0));

        [loop] for (uint f = 0; f < figureCount; f++)
        {
            float weight = delayPattern ? PatternAlpha(f) : 1;
            if (weight <= 0.01) continue;

            uint base = figureStart + f * CONSTELLATION_FIGURE_STRIDE;
            float4 c0 = AnomalyCelestialData[base];
            float4 c1 = AnomalyCelestialData[base + 1];
            float4 c2 = AnomalyCelestialData[base + 2];
            float4 c3 = AnomalyCelestialData[base + 3];
            float3 figCenter = c0.xyz;
            float figRadius = max(c0.w, 0.02);
            // Skip this figure's rings/edges unless the pixel ray is inside its cone.
            float cone = min(figRadius + figureConeMargin, 1.2);
            if (dot(input.direction, figCenter) < cos(cone)) continue;

            uint figEdgeStart = (uint)c1.w;
            uint figEdgeCount = (uint)c2.w;
            uint figMemberStart = memberStart + (uint)c3.x;
            uint figMemberCount = (uint)c3.y;
            if (figEdgeStart + figEdgeCount > edgeCount) continue;
            if (figMemberStart + figMemberCount > memberStart + memberCount) continue;

            [loop] for (uint i = 0; i < figMemberCount; i++)
            {
                float3 center = AnomalyCelestialData[figMemberStart + i].xyz;
                float ring = HighlightRing(input.direction, center, radiusPx, ringWidthPx, footprint);
                if (ring <= 0) continue;
                float pulse = GuidePulse(center, 0.85, 0.22);
                result += inkLine * (0.55 * ring * pulse * weight);
            }
            [loop] for (uint e = 0; e < figEdgeCount; e++)
            {
                uint er = edgeRecordBase + (figEdgeStart + e) * CONSTELLATION_EDGE_STRIDE;
                float3 a = AnomalyCelestialData[er].xyz;
                float3 b = AnomalyCelestialData[er + 1].xyz;
                float4 plane = AnomalyCelestialData[er + 2];
                float seg = GuideSegmentFast(input.direction, a, b, plane.xyz, plane.w,
                    radiusPx, lineWidthPx, footprint);
                if (seg <= 0) continue;
                float pulse = GuidePulse(normalize(a + b), 1.05, 0.38);
                result += inkLine * (0.42 * seg * pulse * weight);
            }
        }
    }

    if ((namesOn || artOn) && figureCount > 0 && figureStart + figureCount * CONSTELLATION_FIGURE_STRIDE <= input.dataCount)
    {
        [loop] for (uint f = 0; f < figureCount; f++)
        {
            uint base = figureStart + f * CONSTELLATION_FIGURE_STRIDE;
            float4 c0 = AnomalyCelestialData[base];
            float4 c1 = AnomalyCelestialData[base + 1];
            float4 c2 = AnomalyCelestialData[base + 2];
            float3 center = c0.xyz;
            float radius = max(c0.w, 0.02);
            float focus = FocusFade(look, center, radius);
            if (focus <= 0.01) continue;
            // Art stays in the constellation frame; names billboard to camera-up.
            float3 drawRight = -c1.xyz;
            float3 up = c2.xyz;
            if (artOn)
                result += DrawFigureArt(input.direction, center, drawRight, up, radius, f) * (0.55 * focus);
            if (namesOn)
            {
                float4 n0 = AnomalyCelestialData[base + 4];
                float4 n1 = AnomalyCelestialData[base + 5];
                float4 n2 = AnomalyCelestialData[base + 6];
                float4 n3 = AnomalyCelestialData[base + 7];
                float name = DrawFigureName(input.direction, center, radius, n0, n1, n2, n3);
                // Square the focus window so labels drop faster than art near neighbors.
                float nameFocus = focus * focus;
                result += inkName * (0.28 * name * nameFocus);
            }
        }
    }
    return result * skyVis;
}
