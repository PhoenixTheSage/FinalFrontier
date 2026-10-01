using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ClientPlugin.Settings;
using ClientPlugin.Settings.Elements;

namespace ClientPlugin;

public enum AtmosphereStyle { Clear, Dramatic, HeavyFantasy }
public enum AtmosphereQuality { Performance, High, Ultra }
public enum AtmosphereDebug { None, Density, GeometryVisibility, CloudTransmittance, InteriorMasks, CascadeCoverage, TemporalRejection }

public class Config : INotifyPropertyChanged
{
    public readonly string Title = "Final Frontier";
    public static readonly Config Default = new Config();
    public static readonly Config Current = ConfigStorage.Load();
    bool enabled = true, sunEnabled = true;
    float starBrightness = 1, sunBrightness = 1, sunRadius = 0.2666f;
    float limitingMagnitude = 6.5f, starColorStrength = 0.65f;
    float starProfileWidth = 1.5f;
    float starCoreSharpness = 3f, starTwinkleStrength = 0.6f, starTwinkleSpeed = 1f;
    bool nativeSunGlare = true;
    bool constellationLines = true, constellationFantasy = true;
    bool constellationNames = true, constellationArt = true;
    bool constellationDelayedPattern = true;
    float constellationPatternDelay = 1f;


    [Checkbox(description: "Replace the celestial background. Disabling restores the world sky and sun.")]
    public bool Enabled { get => enabled; set => Set(ref enabled, value); }
    [Slider(0, 10, 0.1f, SliderAttribute.SliderType.Float, description: "Brightness of the catalogue star field.")]
    public float StarBrightness { get => starBrightness; set => Set(ref starBrightness, Clamp(value, 0, 10, 1)); }
    [Slider(0, 8, 0.1f, SliderAttribute.SliderType.Float, description: "Faintest visible catalogue magnitude.")]
    public float LimitingMagnitude { get => limitingMagnitude; set => Set(ref limitingMagnitude, Clamp(value, 0, 8, 6.5f)); }
    [Slider(0, 1, 0.05f, SliderAttribute.SliderType.Float, description: "Strength of approximate B-V star colours.")]
    public float StarColorStrength { get => starColorStrength; set => Set(ref starColorStrength, Clamp(value, 0, 1, 0.65f)); }
    [Slider(0.85f, 2.5f, 0.05f, SliderAttribute.SliderType.Float, description: "Star profile radius in render pixels. Wider profiles reduce temporal sensitivity while preserving total light.")]
    public float StarProfileWidth { get => starProfileWidth; set => Set(ref starProfileWidth, Clamp(value, 0.85f, 2.5f, 1.5f)); }
    [Slider(1, 8, 0.25f, SliderAttribute.SliderType.Float, description: "Pin-prick core vs soft falloff. 1 is the original triangle; higher keeps total flux and tightens the core.")]
    public float StarCoreSharpness { get => starCoreSharpness; set => Set(ref starCoreSharpness, Clamp(value, 1, 8, 3)); }
    [Slider(0, 2, 0.05f, SliderAttribute.SliderType.Float, description: "Twinkle depth in linear HDR light. Independent of speed. Off for probes.")]
    public float StarTwinkleStrength { get => starTwinkleStrength; set => Set(ref starTwinkleStrength, Clamp(value, 0, 2, 0.6f)); }
    [Slider(0, 3, 0.05f, SliderAttribute.SliderType.Float, description: "Twinkle rate multiplier. 1 is the base scintillation tempo; does not change depth.")]
    public float StarTwinkleSpeed { get => starTwinkleSpeed; set => Set(ref starTwinkleSpeed, Clamp(value, 0, 3, 1)); }
    [Checkbox(description: "Draw constellation highlight rings and connecting stick-figure lines. Off in reflection probes.")]
    public bool ConstellationLines { get => constellationLines; set => Set(ref constellationLines, value); }
    [Checkbox(description: "Brighten constellation stars and give them a softer fantasy glow. Off in reflection probes.")]
    public bool ConstellationFantasy { get => constellationFantasy; set => Set(ref constellationFantasy, value); }
    [Checkbox(description: "Show constellation name headers when looking at a figure. Fades in and out with view. Off in probes.")]
    public bool ConstellationNames { get => constellationNames; set => Set(ref constellationNames, value); }
    [Checkbox(description: "Overlay colored fantasy constellation artwork when looking at a figure. Fades with view. Off in probes.")]
    public bool ConstellationArt { get => constellationArt; set => Set(ref constellationArt, value); }
    [Checkbox(description: "When on, stick lines and rings stay hidden until you look at a figure (same zone as names), then fade in after a delay. When off, connections stay visible.")]
    public bool ConstellationDelayedPattern { get => constellationDelayedPattern; set => Set(ref constellationDelayedPattern, value); }
    [Slider(0, 5, 0.05f, SliderAttribute.SliderType.Float, description: "Seconds after look-at before stick lines and rings begin fading in. Names still appear immediately.")]
    public float ConstellationPatternDelay { get => constellationPatternDelay; set => Set(ref constellationPatternDelay, Clamp(value, 0, 5, 1)); }
    [Checkbox(description: "Retain Keen's sun glare. Turn off to isolate the orange ring; other lights are unaffected.")]
    public bool NativeSunGlare { get => nativeSunGlare; set => Set(ref nativeSunGlare, value); }
    [Checkbox(description: "Draw the analytic sun disc. Gameplay sunlight is unchanged.")]
    public bool SunEnabled { get => sunEnabled; set => Set(ref sunEnabled, value); }
    [Slider(0, 5, 0.05f, SliderAttribute.SliderType.Float, description: "Visual disc brightness; does not change world lighting.")]
    public float SunBrightness { get => sunBrightness; set => Set(ref sunBrightness, Clamp(value, 0, 5, 1)); }
    [Slider(0.02f, 2, 0.005f, SliderAttribute.SliderType.Float, description: "Sun angular radius in degrees. Real sun ≈ 0.27°.")]
    public float SunRadiusDegrees { get => sunRadius; set => Set(ref sunRadius, Clamp(value, 0.02f, 2, 0.2666f)); }

    [Button(label: "Show Status", description: "Celestial + Atmosphere + shared-volume StatusLine / shadow gates. Prefer this when fog or debug views do nothing.")]
    public static void ShowStatus() => Integration.FinalFrontierStatus.Show();

    bool atmosphereEnabled;
    AtmosphereStyle atmospherePreset=AtmosphereStyle.Dramatic;
    AtmosphereQuality atmosphereQuality=AtmosphereQuality.High;
    AtmosphereDebug atmosphereDebug;
    float fogDensity=.00012f,fogHeight=350,fogVariation=.55f,fogForward=.72f,fogContrast=1,fogWind=8,fogDistance=8000,fogNoiseScale=350,fogBaseHeight;
    float fogRed=.92f,fogGreen=.95f,fogBlue=1;
    // Opt-in until the required in-game acceptance scenes have passed.
    public bool AtmosphereEnabled { get=>atmosphereEnabled;set=>Set(ref atmosphereEnabled,value); }
    public AtmosphereStyle AtmospherePreset { get=>atmospherePreset;set=>Set(ref atmospherePreset,value); }
    public AtmosphereQuality AtmosphereQuality { get=>atmosphereQuality;set=>Set(ref atmosphereQuality,value); }
    public AtmosphereDebug AtmosphereDebugView { get=>atmosphereDebug;set=>Set(ref atmosphereDebug,value); }
    public float FogDensity { get=>fogDensity;set=>Set(ref fogDensity,Clamp(value,0,.002f,.00012f)); }
    public float FogHeight { get=>fogHeight;set=>Set(ref fogHeight,Clamp(value,20,3000,350)); }
    public float FogVariation { get=>fogVariation;set=>Set(ref fogVariation,Clamp(value,0,1,.55f)); }
    public float FogForwardScattering { get=>fogForward;set=>Set(ref fogForward,Clamp(value,0,.9f,.72f)); }
    public float FogShaftContrast { get=>fogContrast;set=>Set(ref fogContrast,Clamp(value,.25f,1.5f,1)); }
    public float FogWind { get=>fogWind;set=>Set(ref fogWind,Clamp(value,0,100,8)); }
    public float FogDistance { get=>fogDistance;set=>Set(ref fogDistance,Clamp(value,100,8000,8000)); }
    public float FogNoiseScale { get=>fogNoiseScale;set=>Set(ref fogNoiseScale,Clamp(value,50,3000,350)); }
    public float FogBaseHeight { get=>fogBaseHeight;set=>Set(ref fogBaseHeight,Clamp(value,-3000,3000,0)); }
    public float FogRed { get=>fogRed;set=>Set(ref fogRed,Clamp(value,0,1,.92f)); }
    public float FogGreen { get=>fogGreen;set=>Set(ref fogGreen,Clamp(value,0,1,.95f)); }
    public float FogBlue { get=>fogBlue;set=>Set(ref fogBlue,Clamp(value,0,1,1)); }
    public void ApplyAtmospherePreset(AtmosphereStyle preset)
    {
        AtmospherePreset=preset;
        FogDensity=preset==AtmosphereStyle.Clear?.00001f:preset==AtmosphereStyle.Dramatic?.00012f:.00045f;
        FogHeight=preset==AtmosphereStyle.Clear?180:preset==AtmosphereStyle.Dramatic?350:700;
        FogVariation=preset==AtmosphereStyle.Clear?.2f:preset==AtmosphereStyle.Dramatic?.55f:.75f;
        FogForwardScattering=preset==AtmosphereStyle.Clear?.35f:preset==AtmosphereStyle.Dramatic?.72f:.82f;
        FogShaftContrast=preset==AtmosphereStyle.HeavyFantasy?1.08f:1;
    }

    public event PropertyChangedEventHandler PropertyChanged;
    void Set<T>(ref T field, T value, [CallerMemberName] string name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
    static float Clamp(float value, float min, float max, float fallback) =>
        float.IsNaN(value) || float.IsInfinity(value) ? fallback : Math.Max(min, Math.Min(max, value));
}
