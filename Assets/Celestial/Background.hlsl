#include <AnomalyCelestial.hlsli>
#include "Constellations.hlsli"
#include "Catalogue.hlsli"

float3 AnomalyEvaluateCelestial(AnomalyCelestialInput input)
{
    float3 result = CatalogueStars(input);
    result += ConstellationGuides(input);
    float footprint = input.angularPixel;
    result *= input.isProbe ? frame_.Light.envSkyboxBrightness : frame_.Light.skyboxBrightness;
    // Probes exclude the solar disc: directional specular already supplies the sun.
    if (!input.isProbe && CelestialUniform[0].w > 0.5)
    {
        float radius = max(CelestialUniform[0].z, 1e-6);
        float chordRadius = 2 * sin(radius * 0.5);
        float d = length(input.direction - input.sunDirection) / chordRadius;
        float mu = sqrt(saturate(1 - d * d));
        float limb = 0.4 + 0.6 * mu;
        result += input.sunRadiance * CelestialUniform[0].y * limb *
            AnomalyCelestialDisc(input.direction, input.sunDirection, radius, footprint);
    }
    return result;
}

#include <AnomalyCelestialEntry.hlsli>
