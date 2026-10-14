using System;
using System.IO;
using System.Reflection;
using System.Threading;
using ClientPlugin.Celestial;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Planet;
using Sandbox.Game.World;
using VRage.Utils;
using VRageMath;

namespace ClientPlugin.Integration;

/// <summary>No compile-time Anomaly or Rich HUD dependency; the host owns GPU work.</summary>
static class AnomalyBridge
{
    const string Id = "FinalFrontier";
    static Type host, terminal;
    static string shaderFile, lastStatus;
    static object page;
    static float[] catalogue;
    static ConstellationGuide.Section guides;
    static int guideStart;
    static bool registered;
    static int dirty = 1, nextAttempt;
    static float lastDayOcclusion = -1;
    static float lastAirImmersion = -1;
    static MethodInfo displayProgramStatus;
    internal static bool HdrSolarPalette { get; private set; }
    static readonly float[] patternAlpha = new float[16];
    static readonly float[] patternFocusStart = new float[16];
    static readonly bool[] patternFocused = new bool[16];
    public static string Status { get; private set; } = "Waiting for Anomaly and celestial assets";

    public static void SetAssets(string root) { shaderFile = Path.Combine(root, "Background.hlsl"); MarkDirty(); }
    public static void MarkDirty() => Interlocked.Exchange(ref dirty, 1);
    internal static Type Find(string name)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var type = assembly.GetType(name, false);
            if (type != null) return type;
        }
        return null;
    }
    public static void Update()
    {
        if (AnomalyRenderer.LinuxRequiresGuest || !AnomalyRenderer.Decided)
        {
            if (AnomalyRenderer.LinuxRequiresGuest && !registered)
                Status = "Linux stays idle until Anomaly's Vulkan guest bus is open";
            return;
        }
        if (!AnomalyRenderer.AllowDx11Device && !AnomalyRenderer.AllowGuestWork)
        {
            if (!registered)
                Status = "Celestial draws closed (" + AnomalyRenderer.ActiveName + ")";
            return;
        }
        try
        {
            if (!registered)
            {
                if (nextAttempt != 0 && unchecked(Environment.TickCount - nextAttempt) < 0) return;
                nextAttempt = unchecked(Environment.TickCount + 1000);
                host ??= Find("ClientPlugin.Shaders.CelestialBackgroundRegistry");
                if (host == null || shaderFile == null || !File.Exists(shaderFile)) return;
                var root = Path.GetDirectoryName(shaderFile);
                catalogue ??= StarCatalogue.Load(Path.Combine(root, "Hipparcos.bin"));
                guides = ConstellationGuide.Load(Path.Combine(root, "Constellations.bin"));
                guideStart = catalogue.Length / 4;
                registered = (bool)Call(host, "Register", Id, shaderFile);
                if (!registered) { Status = "Celestial registration rejected"; return; }
                Call(host, "SetData", Id, ConstellationGuide.Concatenate(catalogue, guides));
                try
                {
                    var figures = new string[guides.FigureCount];
                    for (int i = 0; i < figures.Length; i++)
                    {
                        int o = (guides.FigureStart + i * ConstellationGuide.FigureStride) * 4;
                        figures[i] = DecodeName(guides.Records, o + 16);
                    }
                    if (ConstellationArt.TryLoad(Path.Combine(root, "Art"), figures))
                        Call(host, "SetArt", Id, ConstellationArt.Width, ConstellationArt.Height,
                            ConstellationArt.SliceCount, ConstellationArt.Rgba);
                }
                catch (Exception artError)
                {
                    MyLog.Default.WriteLine("Final Frontier constellation art: " + artError.Message);
                }
                MarkDirty();
            }
            var config = Config.Current;
            ComputeSkyFactors(out float dayOcclusion, out float airImmersion);
            UpdatePatternAlphas(config);
            float anyPatternLive = 0f;
            for (int i = 0; i < guides.FigureCount && i < patternAlpha.Length; i++)
                if (patternAlpha[i] > 0.01f) { anyPatternLive = 1f; break; }
            bool configDirty = Interlocked.Exchange(ref dirty, 0) != 0;
            bool hdrPalette = ReadHdrDisplay();
            bool displayChanged = hdrPalette != HdrSolarPalette;
            // Live altitude/sun/pattern fade must track every frame; config still uses dirty.
            bool liveFade = config.ConstellationDelayedPattern
                || Math.Abs(dayOcclusion - lastDayOcclusion) > 0.004f
                || Math.Abs(airImmersion - lastAirImmersion) > 0.004f;
            if (configDirty || liveFade || displayChanged)
            {
                lastDayOcclusion = dayOcclusion;
                lastAirImmersion = airImmersion;
                var uniforms = new float[] {
                    config.StarBrightness, config.SunBrightness,
                    config.SunRadiusDegrees * (float)Math.PI / 180, config.SunEnabled ? 1f : 0f,
                    config.LimitingMagnitude, config.StarColorStrength, config.StarProfileWidth,
                    config.StarCoreSharpness, config.StarTwinkleStrength, config.StarTwinkleSpeed,
                    config.ConstellationLines ? 1f : 0f, config.ConstellationFantasy ? 1f : 0f,
                    guideStart, guides.EdgeCount, guides.MemberCount, guideStart + guides.MemberStart,
                    guideStart + guides.FigureStart, guides.FigureCount,
                    config.ConstellationNames ? 1f : 0f, config.ConstellationArt ? 1f : 0f,
                    dayOcclusion, airImmersion,
                    config.ConstellationDelayedPattern ? 1f : 0f, config.ConstellationPatternDelay,
                    patternAlpha[0], patternAlpha[1], patternAlpha[2], patternAlpha[3],
                    patternAlpha[4], patternAlpha[5], patternAlpha[6], patternAlpha[7],
                    patternAlpha[8], anyPatternLive,
                    config.SunSurfaceDetail, config.SunAnimationSpeed,
                    config.SunCoronaStrength, config.SunFlareHeight,
                    config.SunGranulationScale, config.SunGoldStrength,
                    hdrPalette ? 1f : 0f,
                    config.SunGlowEnabled ? 1f : 0f, config.SunGlowStrength,
                    config.SunGlowRadius, config.SunGlowFalloff,
                    config.SunGlowAtmosphereExpansion, config.SunGlowGoldStrength
                };
                Call(host, "SetUniforms", Id, uniforms);
                HdrSolarPalette = hdrPalette;
                if (configDirty)
                {
                    // One owner: old XML NativeSunGlare=true must not reintroduce
                    // the overlapping native halo. Anomaly restores it on fallback.
                    Call(host, "SetNativeSunGlare", Id, false);
                    Call(host, "SetEnabled", Id, config.Enabled);
                    if (page != null) Page("Refresh");
                }
            }
            Status = (string)host.GetProperty("StatusLine").GetValue(null);
            if (page == null) CreatePage();
            if (page != null && lastStatus != Status) { Page("Refresh"); lastStatus = Status; }
        }
        catch (Exception e)
        {
            var error = "Anomaly integration: " + (e.InnerException ?? e).Message;
            if (Status != error) MyLog.Default.WriteLine("Final Frontier: " + error);
            Status = error;
        }
    }
    internal static object Call(Type type, string method, params object[] args)
    {
        foreach (var candidate in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            if (candidate.Name == method && candidate.GetParameters().Length == args.Length)
                return candidate.Invoke(null, args);
        throw new MissingMethodException(type.FullName, method);
    }
    // GAP: Slice AI — Anomaly has no structured display colour-mode contract.
    // Use the public status of the known HDR display program, never the presence
    // of an HDR assembly or an arbitrary Display tenant. Missing/off/failed = SDR.
    internal static bool ReadHdrDisplay()
    {
        try
        {
            displayProgramStatus ??= Find("ClientPlugin.Shaders.FullscreenPassRegistry")?.GetMethod(
                "TryGetProgramStatus", BindingFlags.Public | BindingFlags.Static, null,
                new[] { typeof(string), typeof(string).MakeByRefType() }, null);
            if (displayProgramStatus == null) return false;
            var args = new object[] { "hdr.tonemap", null };
            return (bool)displayProgramStatus.Invoke(null, args)
                && args[1] is string status && status.StartsWith("live ", StringComparison.Ordinal);
        }
        catch { return false; }
    }
    static object Page(string method, params object[] args)
    {
        var contract = page.GetType().GetInterface("ClientPlugin.RichHud.ITerminalConfigPage");
        foreach (var candidate in contract.GetMethods())
            if (candidate.Name == method && !candidate.IsGenericMethod && candidate.GetParameters().Length == args.Length)
                return candidate.Invoke(page, args);
        throw new MissingMethodException(contract.FullName, method);
    }
    static void CreatePage()
    {
        terminal ??= Find("ClientPlugin.RichHud.TerminalConfigRegistry");
        if (terminal == null) return;
        page = Call(terminal, "RequestFolderPage", "Final Frontier", "Settings");
        if (page == null) return;
        Page("Category", "Celestial environment");
        Page("Label", "Hipparcos catalogue  -  Credit: ESA. Stick figures follow IAU / Alan MacRobert patterns.");
        Page("Checkbox", "Enabled", (Func<bool>)(() => Config.Current.Enabled),
            (Action<bool>)(v => Config.Current.Enabled = v), "Restore the world sky when disabled.");
        Page("Slider", "Star brightness", 0f, 10f, (Func<float>)(() => Config.Current.StarBrightness),
            (Action<float>)(v => Config.Current.StarBrightness = v), "Catalogue star intensity.");
        Page("Slider", "Limiting magnitude", 0f, 8f, (Func<float>)(() => Config.Current.LimitingMagnitude),
            (Action<float>)(v => Config.Current.LimitingMagnitude = v), "Higher values reveal fainter stars; default 6.5.");
        Page("Slider", "Star colour strength", 0f, 1f, (Func<float>)(() => Config.Current.StarColorStrength),
            (Action<float>)(v => Config.Current.StarColorStrength = v), "Approximate colours from catalogue B-V measurements.");
        Page("Checkbox", "Sun disc", (Func<bool>)(() => Config.Current.SunEnabled),
            (Action<bool>)(v => Config.Current.SunEnabled = v), "Visual disc only; gameplay lighting is unchanged.");
        Page("Slider", "Star profile width", 0.85f, 2.5f, (Func<float>)(() => Config.Current.StarProfileWidth),
            (Action<float>)(v => Config.Current.StarProfileWidth = v), "Render-pixel radius; wider profiles preserve total light and reduce temporal sensitivity. Original width: 0.85.");
        Page("Slider", "Star core sharpness", 1f, 8f, (Func<float>)(() => Config.Current.StarCoreSharpness),
            (Action<float>)(v => Config.Current.StarCoreSharpness = v), "1 = original triangle. Higher keeps catalogue flux and tightens a pin-prick core with soft falloff.");
        Page("Slider", "Star twinkle strength", 0f, 2f, (Func<float>)(() => Config.Current.StarTwinkleStrength),
            (Action<float>)(v => Config.Current.StarTwinkleStrength = v), "Depth of linear-HDR scintillation. Independent of speed. Off in reflection probes.");
        Page("Slider", "Star twinkle speed", 0f, 3f, (Func<float>)(() => Config.Current.StarTwinkleSpeed),
            (Action<float>)(v => Config.Current.StarTwinkleSpeed = v), "Rate multiplier for scintillation. Does not change how deep stars wink.");
        Page("Checkbox", "Constellation lines", (Func<bool>)(() => Config.Current.ConstellationLines),
            (Action<bool>)(v => Config.Current.ConstellationLines = v), "Highlight rings and stick lines. Hidden in daylight under an atmosphere; fade back in toward space. Hidden from probes.");
        Page("Checkbox", "Constellation fantasy", (Func<bool>)(() => Config.Current.ConstellationFantasy),
            (Action<bool>)(v => Config.Current.ConstellationFantasy = v), "Brighten figure stars with a softer fantasy glow. Same daylight/atmosphere fade as lines. Hidden from probes.");
        Page("Checkbox", "Constellation names", (Func<bool>)(() => Config.Current.ConstellationNames),
            (Action<bool>)(v => Config.Current.ConstellationNames = v), "Name header when looking at a figure. Fades with view and with daylight under an atmosphere. Hidden from probes.");
        Page("Checkbox", "Constellation art", (Func<bool>)(() => Config.Current.ConstellationArt),
            (Action<bool>)(v => Config.Current.ConstellationArt = v), "Fantasy art over the figure. Fades with view and with daylight under an atmosphere. Hidden from probes.");
        Page("Checkbox", "Delayed Pattern", (Func<bool>)(() => Config.Current.ConstellationDelayedPattern),
            (Action<bool>)(v => Config.Current.ConstellationDelayedPattern = v), "Hide stick lines and rings until you look at a figure (same zone as names), then fade them in after a delay. Off = always show connections.");
        Page("Slider", "Pattern delay (s)", 0f, 5f, (Func<float>)(() => Config.Current.ConstellationPatternDelay),
            (Action<float>)(v => Config.Current.ConstellationPatternDelay = v), "Seconds after look-at before lines and rings begin fading in. Names still appear immediately.");
        Page("Slider", "Sun brightness", 0f, 5f, (Func<float>)(() => Config.Current.SunBrightness),
            (Action<float>)(v => Config.Current.SunBrightness = v), "Disc radiance multiplier.");
        // Step must be passed — Anomaly's no-step Slider overload defaults to 1°.
        Page("Slider", "Sun radius (degrees)", 0.02f, 2f, (Func<float>)(() => Config.Current.SunRadiusDegrees),
            (Action<float>)(v => Config.Current.SunRadiusDegrees = v), "Angular radius, not diameter. Real sun ≈ 0.27°.", 0.005f);
        Page("Category", "Solar surface and flare bands");
        Page("Slider", "Surface detail", 0f, 1f, (Func<float>)(() => Config.Current.SunSurfaceDetail),
            (Action<float>)(v => Config.Current.SunSurfaceDetail = v), "Luminous granulation and flowing plasma veins on a sphere; no dark sunspots.", .05f);
        Page("Slider", "Solar animation speed", 0f, 3f, (Func<float>)(() => Config.Current.SunAnimationSpeed),
            (Action<float>)(v => Config.Current.SunAnimationSpeed = v), "Rotation, evolving surface plasma and travelling flare bands. Zero freezes every solar animation.", .05f);
        Page("Slider", "Corona and flare strength", 0f, 2f, (Func<float>)(() => Config.Current.SunCoronaStrength),
            (Action<float>)(v => Config.Current.SunCoronaStrength = v), "Animated plasma arches and flare strands. Glow is controlled separately below.", .05f);
        Page("Slider", "Flare height (solar radii)", .02f, .35f, (Func<float>)(() => Config.Current.SunFlareHeight),
            (Action<float>)(v => Config.Current.SunFlareHeight = v), "Maximum loop height above the sphere, relative to the sun radius.", .01f);
        Page("Slider", "Granulation scale", 8f, 60f, (Func<float>)(() => Config.Current.SunGranulationScale),
            (Action<float>)(v => Config.Current.SunGranulationScale = v), "Surface detail frequency. Detail is filtered as the sun shrinks on screen.", 1f);
        Page("Slider", "Golden colour strength", 0f, 1f, (Func<float>)(() => Config.Current.SunGoldStrength),
            (Action<float>)(v => Config.Current.SunGoldStrength = v), "Golden plasma with hotter pale-yellow highlights, adapted automatically for SDR or HDR output. Zero retains the world's sun colour.", .05f);
        Page("Category", "Sun glow");
        Page("Label", "One filled glow replaces Keen's solar lens flare and the former rim halos. Scene bloom may still spread its light.");
        Page("Checkbox", "Glow enabled", (Func<bool>)(() => Config.Current.SunGlowEnabled),
            (Action<bool>)(v => Config.Current.SunGlowEnabled = v), "Off removes this glow. It does not restore the overlapping native solar flare or hide plasma strands.");
        Page("Slider", "Glow strength", 0f, 2f, (Func<float>)(() => Config.Current.SunGlowStrength),
            (Action<float>)(v => Config.Current.SunGlowStrength = v), "Additive radiance relative to the sun. Zero removes the glow.", .01f);
        Page("Slider", "Glow radius (solar radii)", .5f, 24f, (Func<float>)(() => Config.Current.SunGlowRadius),
            (Action<float>)(v => Config.Current.SunGlowRadius = v), "Half-strength radius measured from the centre. Scales with the sun and zoom.", .25f);
        Page("Slider", "Glow falloff", 1f, 4f, (Func<float>)(() => Config.Current.SunGlowFalloff),
            (Action<float>)(v => Config.Current.SunGlowFalloff = v), "1 = long soft tail; 2 = Gaussian; 4 = flatter centre and steeper fade. No hollow centre.", .1f);
        Page("Slider", "Glow atmosphere expansion", 0f, 4f, (Func<float>)(() => Config.Current.SunGlowAtmosphereExpansion),
            (Action<float>)(v => Config.Current.SunGlowAtmosphereExpansion = v), "Extra width beneath an atmosphere. 0 keeps space width; 1 doubles it at full immersion.", .1f);
        Page("Slider", "Glow golden tint", 0f, 1f, (Func<float>)(() => Config.Current.SunGlowGoldStrength),
            (Action<float>)(v => Config.Current.SunGlowGoldStrength = v), "From world sunlight colour to the sun's golden palette; adapts automatically for HDR.", .05f);
        Page("Button", "Reset sun glow", (Action)(() => { Config.Current.ResetSunGlow(); Page("Refresh"); }), "Restore glow defaults without changing the surface, animation or flare strands.");
        Page("Button", "Retry shaders", (Action)(() => Call(host, "Retry", Id)), "Recompile both views after fixing an asset.");
    }
    public static void Dispose()
    {
        try
        {
            if (registered)
            {
                try { Call(host, "SetArt", Id, null); } catch { /* host may be gone */ }
                Call(host, "Unregister", Id);
            }
            if (terminal != null) Call(terminal, "UnregisterPage", "Final Frontier/Settings");
        }
        catch (Exception e) { MyLog.Default.WriteLine("Final Frontier cleanup: " + e.Message); }
        ConstellationArt.Release();
        registered = false; catalogue = null; guides = default; guideStart = 0; page = null; host = null; terminal = null;
        lastDayOcclusion = -1;
        lastAirImmersion = -1;
        displayProgramStatus = null;
        HdrSolarPalette = false;
        Array.Clear(patternAlpha, 0, patternAlpha.Length);
        Array.Clear(patternFocusStart, 0, patternFocusStart.Length);
        Array.Clear(patternFocused, 0, patternFocused.Length);
    }

    /// <summary>
    /// Per-figure line/ring opacity when Delayed Pattern is on. Names use the
    /// shader focus fade immediately; patterns wait for the configured delay,
    /// then ease in over ~0.9 s while look stays in the name zone.
    /// </summary>
    static void UpdatePatternAlphas(Config config)
    {
        int count = guides.FigureCount;
        if (!config.ConstellationDelayedPattern || !config.ConstellationLines || count <= 0
            || MySession.Static == null || MySector.MainCamera == null)
        {
            for (int i = 0; i < patternAlpha.Length; i++)
            {
                patternAlpha[i] = 0;
                patternFocused[i] = false;
            }
            return;
        }

        Vector3 look = MySector.MainCamera.ForwardVector;
        float lookLen = look.Length();
        if (lookLen < 1e-6f) return;
        look /= lookLen;
        float now = (float)MySession.Static.ElapsedGameTime.TotalSeconds;
        float delay = MathHelper.Clamp(config.ConstellationPatternDelay, 0f, 5f);
        const float fadeIn = 0.9f;

        for (int f = 0; f < patternAlpha.Length; f++)
        {
            if (f >= count)
            {
                patternAlpha[f] = 0;
                patternFocused[f] = false;
                continue;
            }
            int o = (guides.FigureStart + f * ConstellationGuide.FigureStride) * 4;
            var center = new Vector3(guides.Records[o], guides.Records[o + 1], guides.Records[o + 2]);
            float radius = Math.Max(guides.Records[o + 3], 0.02f);
            float focus = FocusFadeCpu(look, center, radius);
            if (focus > 0.02f)
            {
                if (!patternFocused[f])
                {
                    patternFocusStart[f] = now;
                    patternFocused[f] = true;
                }
                float elapsed = Math.Max(0f, now - patternFocusStart[f]);
                float delayed = Smoothstep(delay, delay + fadeIn, elapsed);
                patternAlpha[f] = focus * delayed;
            }
            else
            {
                patternFocused[f] = false;
                patternAlpha[f] = 0;
            }
        }
    }

    // Mirrors Constellations.hlsli FocusFade.
    static float FocusFadeCpu(Vector3 look, Vector3 center, float radius)
    {
        float d = (float)Math.Acos(MathHelper.Clamp(Vector3.Dot(look, center), -1f, 1f));
        return 1f - Smoothstep(radius * 0.30f, radius * 0.85f, d);
    }

    /// <summary>
    /// dayOcclusion: 1 hides constellation overlays (daylight inside an atmosphere).
    /// airImmersion: 1 deep under an air column (widens sun glare); 0 in space.
    /// Both default to 0 so smoke / unset uniforms keep prior behavior.
    /// </summary>
    static void ComputeSkyFactors(out float dayOcclusion, out float airImmersion)
    {
        dayOcclusion = 0f;
        airImmersion = 0f;
        try
        {
            if (MySession.Static == null || MySector.MainCamera == null || MyPlanets.Static == null)
                return;
            MyPlanet planet = null;
            double best = double.MaxValue;
            var cam = MySector.MainCamera.Position;
            foreach (var candidate in MyPlanets.GetPlanets())
            {
                if (candidate.Closed || !candidate.HasAtmosphere) continue;
                double distance = (candidate.PositionComp.GetPosition() - cam).LengthSquared();
                if (distance < best) { best = distance; planet = candidate; }
            }
            if (planet == null) return;

            var center = planet.PositionComp.GetPosition();
            var toCam = cam - center;
            double radius = toCam.Length();
            if (radius < 1.0) return;

            double airTop = planet.AverageRadius + Math.Max(planet.AtmosphereAltitude, 200.0);
            if (radius > airTop + 15000.0) return;

            Vector3D up = toCam / radius;
            float sunElev = (float)Vector3D.Dot(up, MySector.DirectionToSunNormalized);
            float daylight = Smoothstep(-0.06f, 0.14f, sunElev);
            // 1 deep under the air column, 0 once the camera has left it.
            airImmersion = 1f - Smoothstep((float)(airTop - 12000.0), (float)(airTop + 3000.0), (float)radius);
            airImmersion = MathHelper.Clamp(airImmersion, 0f, 1f);
            dayOcclusion = MathHelper.Clamp(daylight * airImmersion, 0f, 1f);
        }
        catch
        {
            dayOcclusion = 0f;
            airImmersion = 0f;
        }
    }

    static float Smoothstep(float edge0, float edge1, float x)
    {
        float t = MathHelper.Clamp((x - edge0) / Math.Max(edge1 - edge0, 1e-5f), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    static string DecodeName(float[] data, int offset)
    {
        var chars = new char[16];
        int n = 0;
        for (int i = 0; i < 16; i++)
        {
            float code = data[offset + i];
            if (code < 0.5f) break;
            if (code > 26.5f && code < 27.5f) chars[n++] = ' ';
            else if (code >= 0.5f && code < 26.5f) chars[n++] = (char)('A' + (int)code - 1);
        }
        return n == 0 ? "Figure" + offset : new string(chars, 0, n).Trim();
    }
}
