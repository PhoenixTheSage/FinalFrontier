#include <AnomalyCelestial.hlsli>
#include "Sun.hlsli"
#include "Constellations.hlsli"
#include "Catalogue.hlsli"

float3 AnomalyEvaluateCelestial(AnomalyCelestialInput input)
{
    float3 result = CatalogueStars(input);
    result += ConstellationGuides(input);
    result *= input.isProbe ? frame_.Light.envSkyboxBrightness : frame_.Light.skyboxBrightness;
    // Disc/aureole stay in sunRadiance units (Keen already folded intensity in).
    result += EvaluateSun(input);
    return result;
}

#include <AnomalyCelestialEntry.hlsli>
