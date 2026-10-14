// Blender-authored solar plasma: advected granulation and evolving flux bundles.
// Uniforms 34..40: surface detail, animation speed, corona strength,
// loop height / radius, granulation frequency, golden palette strength.
// Slot 40 selects the HDR display palette; unset/older hosts retain SDR.
// Slots 41..46: glow enabled, strength, half-strength radius, falloff,
// atmospheric width expansion, gold tint. One optical glow, independent of plasma.
// All layers stay in Anomaly's supplied sunRadiance units. No dark spot mask.

void SunTangentBasis(float3 sunDir, out float3 tangent, out float3 bitangent)
{
    float3 up = abs(sunDir.y) < 0.995 ? float3(0, 1, 0) : float3(1, 0, 0);
    tangent = normalize(cross(up, sunDir));
    bitangent = cross(sunDir, tangent);
}

// The only smooth glow: filled through the centre and additive over the sphere.
// No limb cutout, compact rim gate, second skirt or detail-dependent width.
float SolarAureole(float3 ray, float3 sunDir, float radius, float air)
{
    if (CelestialUniform[10].y <= 0.5) return 0;
    float strength = max(CelestialUniform[10].z, 0);
    if (strength <= 0) return 0;
    // atan2 cancels small normalization-length errors. Chord subtraction can
    // invent a nonzero centre distance at extreme zoom or on software devices.
    float ang = atan2(length(cross(ray, sunDir)), dot(ray, sunDir));
    float width = max(radius, 1e-6) * clamp(CelestialUniform[10].w, 0.5, 24);
    width *= 1.0 + saturate(air) * clamp(CelestialUniform[11].y, 0, 4);
    float falloff = clamp(CelestialUniform[11].x, 1, 4);
    return strength * exp2(-pow(max(ang / width, 0), falloff));
}

float SolarHash(float3 p)
{
    p = frac(p * float3(0.1031, 0.1030, 0.0973));
    p += dot(p, p.yxz + 33.33);
    return frac((p.x + p.y) * p.z);
}

float SolarNoise(float3 p)
{
    float3 cell = floor(p), f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(lerp(SolarHash(cell), SolarHash(cell + float3(1,0,0)), f.x),
                     lerp(SolarHash(cell + float3(0,1,0)), SolarHash(cell + float3(1,1,0)), f.x), f.y),
                lerp(lerp(SolarHash(cell + float3(0,0,1)), SolarHash(cell + float3(1,0,1)), f.x),
                     lerp(SolarHash(cell + float3(0,1,1)), SolarHash(cell + float3(1,1,1)), f.x), f.y), f.z);
}

float SolarFbm(float3 p)
{
    return SolarNoise(p) * 0.57 + SolarNoise(p * 2.03 + 11.7) * 0.29
         + SolarNoise(p * 4.11 + 29.3) * 0.14;
}

float3 SolarBodyToView(float3 p, float time)
{
    float s, c;
    sincos(time * 0.018, s, c);
    p.xz = float2(c * p.x + s * p.z, -s * p.x + c * p.z);
    p.xy = float2(0.9781476 * p.x - 0.2079117 * p.y, 0.2079117 * p.x + 0.9781476 * p.y);
    return p;
}

float3 SolarViewToBody(float3 p, float time)
{
    p.xy = float2(0.9781476 * p.x + 0.2079117 * p.y, -0.2079117 * p.x + 0.9781476 * p.y);
    float s, c;
    sincos(time * 0.018, s, c);
    p.xz = float2(c * p.x - s * p.z, s * p.x + c * p.z);
    return p;
}

float3 SolarPalette(float heat, float golden, float3 illuminant)
{
    float hot = saturate(heat * heat);
    float3 gold = lerp(float3(1.0, 0.40, 0.012), float3(1.0, 0.91, 0.27), hot);
    if (CelestialUniform[10].x > 0.5)
    {
        // Keen's SDR shoulder compresses channels separately, pushing bright
        // orange energy towards yellow. HDR's luminance shoulder preserves that
        // orange ratio. Author golden chromaticity for HDR, retaining the SDR
        // palette's luminance so this changes hue rather than exposure.
        float3 hdrGold = lerp(float3(1.0, 0.72, 0.035), float3(1.0, 0.94, 0.27), hot);
        float3 weights = float3(0.2126, 0.7152, 0.0722) * max(illuminant, 0);
        float3 original = lerp(float3(1,1,1), gold, golden);
        float3 corrected = lerp(float3(1,1,1), hdrGold, golden);
        // Preserve the lit palette's energy too, including warm world sunlight.
        float originalLuma = dot(original, weights);
        float correctedLuma = dot(corrected, weights);
        return corrected * (correctedLuma > 1e-8 ? originalLuma / correctedLuma : 1.0);
    }
    return lerp(float3(1,1,1), gold, golden);
}

// Continuous fourth noise dimension: neighbouring seeded 3D slices interpolate
// with a quintic kernel. No texture, frame atlas, or fixed pixel resolution.
float SolarNoise4(float3 p, float w)
{
    float cell = floor(w), f = frac(w);
    f = f * f * f * (f * (f * 6 - 15) + 10);
    return lerp(SolarNoise(p + cell * float3(37.7, 71.3, 19.1)),
                SolarNoise(p + (cell + 1) * float3(37.7, 71.3, 19.1)), f);
}

float SolarFilteredFbm(float3 p, float w, float footprint)
{
    float sum = 0, weight = 0, amplitude = 1;
    [unroll] for (int octave = 0; octave < 6; octave++)
    {
        float filter = 1.0 - smoothstep(0.35, 0.95, footprint);
        // Beyond Nyquist the mean replaces the octave, preserving its energy.
        if (filter > 0.001) sum += lerp(0.5, SolarNoise4(p, w), filter) * amplitude;
        else sum += 0.5 * amplitude;
        weight += amplitude;
        p = p * 2.03 + 13.7; footprint *= 2.03; amplitude *= 0.62;
    }
    return sum / weight;
}

float3 SolarPhotosphere(float3 normal, float time, float footprint, float frequency, float golden, float3 illuminant)
{
    float3 p = SolarViewToBody(normal, time);
    float w = time * 0.055;
    float3 warp = float3(SolarNoise4(p * 4.3, w), SolarNoise4(p * 4.3 + 17.4, w),
                        SolarNoise4(p * 4.3 + 39.8, w)) - 0.5;
    float3 flowing = p + warp * 0.24;
    float sampleWidth = footprint / max(normal.z, 0.18);
    float broad = SolarFilteredFbm(flowing * 8, time * 0.045, sampleWidth * 8);
    float meso = SolarFilteredFbm(flowing * 38, time * 0.085, sampleWidth * 38);
    float ridge = pow(saturate(1.0 - abs(meso * 2.0 - 1.0)), 12.0);
    float ridgeFilter = 1.0 - smoothstep(0.35, 0.95, sampleWidth * 38);
    // Preserve the existing XML/RHUD scale while resolving six times finer
    // structure. Octaves extend detail for large renders and close inspection.
    float granulation = frequency * (320.0 / 48.0);
    float grains = SolarFilteredFbm(flowing * granulation, time * 0.11, sampleWidth * granulation);
    float grainFilter = 1.0 - smoothstep(0.35, 0.95, sampleWidth * granulation);
    grains = saturate((grains - 0.30) * 2.4);
    float fiberNoise = SolarNoise4(flowing * 95 + 9.3, time * 0.09);
    float fibrils = pow(saturate(1.0 - abs(fiberNoise * 2.0 - 1.0)), 18.0);
    float fiberFilter = 1.0 - smoothstep(0.30, 0.85, sampleWidth * 95);
    float active = pow(saturate((broad - 0.30) * 2.4), 3.0);
    float veins = ridge * ridgeFilter * active;
    // The radiance floor excludes sunspot masks; texture contrast comes from
    // additive heat at multiple scales, rather than opaque black patches.
    float convectionHeat = pow(max(broad * 1.7, 0), 4.0);
    float grainHeat = lerp(0.65, pow(grains * 1.7, 4.0), grainFilter);
    float energy = 0.62 + convectionHeat * 0.70 + grainHeat * 0.50
                 + veins * 0.32 + fibrils * fiberFilter * 0.16;
    return SolarPalette(saturate(veins * 0.38 + grainHeat * 0.08), golden, illuminant) * energy;
}

float3 SolarFluxPoint(float3 n, float3 t, float3 side, float u, float width,
                     float height, float id, float strand, float time)
{
    float spreadFactor = (strand - 1.5) / 1.5;
    width *= 1.0 + spreadFactor * 0.22;
    height *= 1.0 + spreadFactor * 0.38;
    float arc = pow(max(sin(u * 3.14159265), 0), 0.65 + frac(id * 0.731) * 0.70);
    float skew = (frac(id * 0.319) - 0.5) * 1.1;
    float along = width * (2.0 * u - 1.0 + skew * arc);
    along += width * 0.035 * arc * sin(u * 23.0 + time * 0.9 + strand * 2.3 + id);
    float spread = (strand - 1.5) * width * 0.065;
    float crossField = spread * arc * cos(u * 8.0 + time * 0.45 + id)
                     + width * 0.13 * arc * sin(u * 8.0 + time * 0.5 + id * 3.1);
    float rise = height * arc * (1.0 + 0.15 * sin(u * 9.0 + time * 0.7 + id));
    return normalize(n + t * along + side * crossField) * (0.9995 + rise);
}

// 32 regions, four thin evolving strands each; tight bounds skip distant pixels.
// The geometry model follows the Blender flux study, with a real-time budget.
float3 SolarFlareBands(float2 q, float mu, float time, float footprint, float extent, float golden, float3 illuminant)
{
    float3 result = 0;
    float radiusSquared = dot(q, q);
    float3 surface = float3(q, mu);
    float aa = max(footprint * 0.55, 0.00015);
    [loop] for (int i = 0; i < 32; i++)
    {
        float id = (float)i;
        float y = 1.0 - 2.0 * (id + 0.5) / 32.0;
        float azimuth = id * 2.3999632;
        float horizontal = sqrt(saturate(1.0 - y * y));
        float3 anchor = float3(cos(azimuth) * horizontal, y, sin(azimuth) * horizontal);
        float3 tangent, bitangent;
        float3 pole = abs(anchor.z) < 0.97 ? float3(0,0,1) : float3(0,1,0);
        tangent = normalize(cross(pole, anchor));
        bitangent = cross(anchor, tangent);
        float spin = sin(id * 0.71) * 1.5;
        tangent = tangent * cos(spin) + bitangent * sin(spin);
        bitangent = cross(anchor, tangent);
        float width = 0.025 + frac(id * 0.371 + 0.19) * 0.085;
        float life = 0.5 + 0.5 * sin(time * (0.34 + frac(id * 0.177) * 0.26) + id * 2.13);
        float growth = 0.10 + 0.90 * pow(life, 1.6);
        float height = extent * (0.12 + frac(id * 0.613 + 0.27) * 0.88) * growth;
        float3 n = SolarBodyToView(anchor, time);
        float3 t = SolarBodyToView(tangent, time);
        float3 side = SolarBodyToView(bitangent, time);
        if (length(q - n.xy) > width * 1.9 + height * 1.6 + 0.025 + aa * 2.0) continue;
        float pulse = smoothstep(0.02, 0.45, life) * (0.3 + life * 0.7);
        float3 footA = normalize(n + t * width), footB = normalize(n - t * width);
        float activeRegion = exp(-min(dot(surface - footA, surface - footA),
                                      dot(surface - footB, surface - footB)) / (width * width * 0.22));
        if (radiusSquared < 1.0)
            result += SolarPalette(0.78, golden, illuminant) * activeRegion * pulse * 0.80;
        [loop] for (int strand = 0; strand < 4; strand++)
        {
          float3 a = SolarFluxPoint(n,t,side,0,width,height,id,strand,time);
          float archIntensity = 0, archHeat = 0;
          [loop] for (int segment = 0; segment < 24; segment++)
          {
            float u1 = (segment + 1.0) / 24.0;
            float3 b = SolarFluxPoint(n,t,side,u1,width,height,id,strand,time);
            float2 ab = b.xy - a.xy;
            float along = saturate(dot(q - a.xy, ab) / max(dot(ab, ab), 1e-8));
            float3 nearest = lerp(a, b, along);
            float distance = length(q - nearest.xy);
            // Hide back-facing arc pieces inside the projected photosphere.
            float visible = radiusSquared >= 1.0 ? 1.0 : smoothstep(mu - 0.0015, mu + 0.002, nearest.z);
            float bandWidth = 0.00065 + frac(id * 0.417 + strand * 0.23) * 0.00085;
            float filtered = bandWidth + aa;
            float band = exp(-distance * distance / (filtered * filtered)) * bandWidth / filtered;
            float haloWidth = 0.0035 + bandWidth * 2.0;
            float halo = exp(-distance * distance / (haloWidth * haloWidth + aa * aa)) * 0.075;
            float path = (segment + along) / 24.0;
            float packets = saturate(0.5 + 0.20 * sin(path * 32.0 - time * 2.1 + id * 2.1 + strand)
                                   + 0.18 * sin(path * 57.0 + time * 2.4 + id * 3.7)
                                   + 0.16 * sin(path * 13.0 - time * 0.6 + strand * 1.4));
            float travelling = 0.025 + 0.975 * pow(packets, 3.0);
            float intensity = (band * 2.1 * travelling + halo) * visible * pulse;
            // Nearest/brighter segment owns the arch, avoiding doubled joints.
            if (intensity > archIntensity) { archIntensity = intensity; archHeat = 0.72 + travelling * 0.28; }
            a = b;
          }
          result += SolarPalette(archHeat, golden, illuminant) * archIntensity;
        }
    }
    return result;
}

float3 EvaluateSun(AnomalyCelestialInput input)
{
    if (input.isProbe || CelestialUniform[0].w <= 0.5) return 0;

    float radius = max(CelestialUniform[0].z, 1e-6);
    float footprint = input.angularPixel;
    float air = saturate(CelestialUniform[5].y);
    float chordRadius = max(2.0 * sin(radius * 0.5), 1e-6);
    float d = length(input.direction - input.sunDirection) / max(chordRadius, 1e-6);
    // Keep the broad glare cheap. Plasma is evaluated only near the solar body.
    float pixelsAcrossRadius = chordRadius / max(footprint, 1e-8);
    float resolved = smoothstep(4.0, 18.0, pixelsAcrossRadius);
    float detail = saturate(CelestialUniform[8].z) * resolved;
    float speed = max(CelestialUniform[8].w, 0);
    float corona = max(CelestialUniform[9].x, 0) * resolved;
    float extent = clamp(CelestialUniform[9].y, 0.02, 0.35);
    float frequency = clamp(CelestialUniform[9].z, 8, 60);
    float golden = saturate(CelestialUniform[9].w);
    float time = frame_.frameTime * speed;
    float mu = sqrt(saturate(1.0 - d * d));
    // Soften limb darkening slightly so the rim stays bright into the glare join.
    float limb = lerp(0.55, 1.0, mu);
    float disc = AnomalyCelestialDisc(input.direction, input.sunDirection, radius, footprint);
    float glare = SolarAureole(input.direction, input.sunDirection, radius, air);
    float3 radiance = input.sunRadiance * CelestialUniform[0].y;
    float3 photosphere = SolarPalette(0, golden, radiance) * limb;
    float3 bands = 0;
    if (d < 1.0 + extent * 1.6 + 0.06 + footprint / chordRadius * 2.0 && (detail > 0 || corona > 0))
    {
        float3 t, b;
        SunTangentBasis(input.sunDirection, t, b);
        float2 q = float2(dot(input.direction, t), dot(input.direction, b)) / max(sin(radius), 1e-6);
        float surfaceMu = sqrt(saturate(1.0 - dot(q, q)));
        float solarPixel = footprint / chordRadius;
        if (disc > 0 && detail > 0)
        {
            float3 surface = SolarPhotosphere(float3(q, surfaceMu), time, solarPixel, frequency, golden, radiance);
            float sphereLimb = lerp(0.72, 1.0, surfaceMu);
            photosphere = lerp(photosphere, surface * sphereLimb, detail);
        }
        if (corona > 0)
        {
            bands = SolarFlareBands(q, surfaceMu, time, solarPixel, extent, golden, radiance);
            bands *= corona;
        }
    }
    // Optical glow is one additive filled profile, not an annulus composited
    // outside the disc. Its slider cannot affect plasma geometry or animation.
    float glowGold = golden * saturate(CelestialUniform[11].z);
    return radiance * (photosphere * disc + SolarPalette(0.85, glowGold, radiance) * glare + bands);
}
