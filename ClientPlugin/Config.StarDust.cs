using System;
using ClientPlugin.Settings.Elements;

namespace ClientPlugin;

public enum StarDustMode { Vanilla, Enhanced, Off }
public enum StarDustStyle { Subtle, Cinematic, HighSpeed }

public partial class Config
{
    StarDustMode starDustMode;
    StarDustStyle starDustPreset;
    bool starDustNearPlanets, starDustHideInsideGrids = true;
    int starDustParticleLimit = 128;
    float starDustDensity = .65f, starDustSpawnRadius = 10, starDustLifetime = 1.5f;
    float starDustMinimumSpeed = 10, starDustSpeedFade = 15, starDustReferenceSpeed = 50;
    float starDustLength = .65f, starDustWidth = .018f, starDustStretch = 1, starDustMaxLength = 2;
    float starDustBrightness = 1, starDustOpacity = .75f, starDustVariation = .25f, starDustFade = .15f;
    float starDustRed = 1, starDustGreen = 1, starDustBlue = 1;

    [Dropdown(description: "Vanilla keeps Keen's effect. Enhanced uses Final Frontier's live controls. Off hides only movement dust.")]
    public StarDustMode StarDustMode { get => starDustMode; set => Set(ref starDustMode, Enum.IsDefined(typeof(StarDustMode), value) ? value : StarDustMode.Vanilla); }
    public StarDustStyle StarDustPreset { get => starDustPreset; set => Set(ref starDustPreset, Enum.IsDefined(typeof(StarDustStyle), value) ? value : StarDustStyle.Subtle); }
    public bool StarDustNearPlanets { get => starDustNearPlanets; set => Set(ref starDustNearPlanets, value); }
    public bool StarDustHideInsideGrids { get => starDustHideInsideGrids; set => Set(ref starDustHideInsideGrids, value); }
    public int StarDustParticleLimit { get => starDustParticleLimit; set => Set(ref starDustParticleLimit, Math.Max(8, Math.Min(128, value))); }
    public float StarDustDensity { get => starDustDensity; set => Set(ref starDustDensity, Clamp(value, 0, 5, .65f)); }
    public float StarDustSpawnRadius { get => starDustSpawnRadius; set => Set(ref starDustSpawnRadius, Clamp(value, 3, 40, 10)); }
    public float StarDustLifetime { get => starDustLifetime; set => Set(ref starDustLifetime, Clamp(value, .25f, 5, 1.5f)); }
    public float StarDustMinimumSpeed { get => starDustMinimumSpeed; set => Set(ref starDustMinimumSpeed, Clamp(value, 0, 100, 10)); }
    public float StarDustSpeedFade { get => starDustSpeedFade; set => Set(ref starDustSpeedFade, Clamp(value, 1, 100, 15)); }
    public float StarDustReferenceSpeed { get => starDustReferenceSpeed; set => Set(ref starDustReferenceSpeed, Clamp(value, 1, 200, 50)); }
    public float StarDustLength { get => starDustLength; set => Set(ref starDustLength, Clamp(value, .05f, 5, .65f)); }
    public float StarDustWidth { get => starDustWidth; set => Set(ref starDustWidth, Clamp(value, .005f, .1f, .018f)); }
    public float StarDustStretch { get => starDustStretch; set => Set(ref starDustStretch, Clamp(value, 0, 2, 1)); }
    public float StarDustMaxLength { get => starDustMaxLength; set => Set(ref starDustMaxLength, Clamp(value, .05f, 8, 2)); }
    public float StarDustBrightness { get => starDustBrightness; set => Set(ref starDustBrightness, Clamp(value, 0, 3, 1)); }
    public float StarDustOpacity { get => starDustOpacity; set => Set(ref starDustOpacity, Clamp(value, 0, 1, .75f)); }
    public float StarDustVariation { get => starDustVariation; set => Set(ref starDustVariation, Clamp(value, 0, 1, .25f)); }
    public float StarDustFade { get => starDustFade; set => Set(ref starDustFade, Clamp(value, 0, .5f, .15f)); }
    public float StarDustRed { get => starDustRed; set => Set(ref starDustRed, Clamp(value, 0, 1, 1)); }
    public float StarDustGreen { get => starDustGreen; set => Set(ref starDustGreen, Clamp(value, 0, 1, 1)); }
    public float StarDustBlue { get => starDustBlue; set => Set(ref starDustBlue, Clamp(value, 0, 1, 1)); }

    public void ApplyStarDustPreset(StarDustStyle preset)
    {
        StarDustPreset = preset;
        StarDustDensity = preset == StarDustStyle.Subtle ? .65f : preset == StarDustStyle.Cinematic ? 1.75f : 1;
        StarDustParticleLimit = 128;
        StarDustSpawnRadius = preset == StarDustStyle.HighSpeed ? 20 : 10;
        StarDustLifetime = preset == StarDustStyle.Cinematic ? 2.5f : 1.5f;
        StarDustMinimumSpeed = preset == StarDustStyle.HighSpeed ? 35 : 10;
        StarDustSpeedFade = preset == StarDustStyle.HighSpeed ? 30 : 15;
        StarDustReferenceSpeed = preset == StarDustStyle.HighSpeed ? 100 : 50;
        StarDustLength = preset == StarDustStyle.Subtle ? .65f : preset == StarDustStyle.Cinematic ? 1.5f : 2;
        StarDustMaxLength = preset == StarDustStyle.Subtle ? 2 : preset == StarDustStyle.Cinematic ? 4 : 6;
        StarDustWidth = preset == StarDustStyle.Subtle ? .018f : .025f;
        StarDustStretch = 1;
        StarDustBrightness = preset == StarDustStyle.Cinematic ? 1.25f : 1;
        StarDustOpacity = preset == StarDustStyle.Subtle ? .75f : 1;
        StarDustVariation = preset == StarDustStyle.Cinematic ? .5f : .25f;
        StarDustFade = .15f;
        StarDustRed = StarDustGreen = StarDustBlue = 1;
        // Presets retain the mode and location gates; changing a look never enables it.
    }
}
