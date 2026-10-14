using ClientPlugin.Settings.Elements;

namespace ClientPlugin;

public partial class Config
{
    float sunSurfaceDetail = 1, sunAnimationSpeed = 1, sunCoronaStrength = .8f;
    float sunFlareHeight = .18f, sunGranulationScale = 48, sunGoldStrength = 1;
    bool sunGlowEnabled = true;
    float sunGlowStrength = .18f, sunGlowRadius = 3, sunGlowFalloff = 2;
    float sunGlowAtmosphereExpansion = 1, sunGlowGoldStrength = .55f;

    [Checkbox(description: "One filled solar glow. Off removes it without bringing back Keen's overlapping solar lens flare.")]
    public bool SunGlowEnabled { get => sunGlowEnabled; set => Set(ref sunGlowEnabled, value); }
    [Slider(0, 2, .01f, SliderAttribute.SliderType.Float, description: "Glow radiance relative to the sun. Zero removes all custom optical glow; plasma strands remain separate.")]
    public float SunGlowStrength { get => sunGlowStrength; set => Set(ref sunGlowStrength, Clamp(value, 0, 2, .18f)); }
    [Slider(.5f, 24, .25f, SliderAttribute.SliderType.Float, description: "Distance from the sun centre where glow falls to half strength, in solar radii. Tracks the sun's angular size and zoom.")]
    public float SunGlowRadius { get => sunGlowRadius; set => Set(ref sunGlowRadius, Clamp(value, .5f, 24, 3)); }
    [Slider(1, 4, .1f, SliderAttribute.SliderType.Float, description: "Outward falloff shape. 1 = long soft tail, 2 = Gaussian, 4 = flatter centre with a steeper fade. Always brightest at the centre.")]
    public float SunGlowFalloff { get => sunGlowFalloff; set => Set(ref sunGlowFalloff, Clamp(value, 1, 4, 2)); }
    [Slider(0, 4, .1f, SliderAttribute.SliderType.Float, description: "Extra glow width under an atmosphere. 0 keeps space width; 1 doubles the half-strength radius at full immersion.")]
    public float SunGlowAtmosphereExpansion { get => sunGlowAtmosphereExpansion; set => Set(ref sunGlowAtmosphereExpansion, Clamp(value, 0, 4, 1)); }
    [Slider(0, 1, .05f, SliderAttribute.SliderType.Float, description: "Glow tint between world sunlight and the sun's golden palette. Adapts to HDR together with the photosphere.")]
    public float SunGlowGoldStrength { get => sunGlowGoldStrength; set => Set(ref sunGlowGoldStrength, Clamp(value, 0, 1, .55f)); }

    public void ResetSunGlow()
    {
        SunGlowEnabled = true; SunGlowStrength = .18f; SunGlowRadius = 3;
        SunGlowFalloff = 2; SunGlowAtmosphereExpansion = 1; SunGlowGoldStrength = .55f;
    }

    [Slider(0, 1, .05f, SliderAttribute.SliderType.Float, description: "Luminous granulation and flowing plasma veins on a rotating sphere. No dark sunspots.")]
    public float SunSurfaceDetail { get => sunSurfaceDetail; set => Set(ref sunSurfaceDetail, Clamp(value, 0, 1, 1)); }
    [Slider(0, 3, .05f, SliderAttribute.SliderType.Float, description: "Surface rotation and plasma-loop animation speed. Zero freezes the entire solar animation.")]
    public float SunAnimationSpeed { get => sunAnimationSpeed; set => Set(ref sunAnimationSpeed, Clamp(value, 0, 3, 1)); }
    [Slider(0, 2, .05f, SliderAttribute.SliderType.Float, description: "Strength of luminous surface arches and animated flare strands. Solar glow has its own controls.")]
    public float SunCoronaStrength { get => sunCoronaStrength; set => Set(ref sunCoronaStrength, Clamp(value, 0, 2, .8f)); }
    [Slider(.02f, .35f, .01f, SliderAttribute.SliderType.Float, description: "Maximum plasma-loop height as a fraction of the sun radius.")]
    public float SunFlareHeight { get => sunFlareHeight; set => Set(ref sunFlareHeight, Clamp(value, .02f, .35f, .18f)); }
    [Slider(8, 60, 1, SliderAttribute.SliderType.Float, description: "Granulation frequency across the solar sphere. Fine detail fades out below the pixel footprint.")]
    public float SunGranulationScale { get => sunGranulationScale; set => Set(ref sunGranulationScale, Clamp(value, 8, 60, 48)); }
    [Slider(0, 1, .05f, SliderAttribute.SliderType.Float, description: "Golden reference-image palette. Zero retains the world's sun colour; hot veins remain brighter than the surface.")]
    public float SunGoldStrength { get => sunGoldStrength; set => Set(ref sunGoldStrength, Clamp(value, 0, 1, 1)); }
}
