using System;
using System.IO;
using System.Reflection;
using System.Threading;
using ClientPlugin.Celestial;
using VRage.Utils;

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
                MarkDirty();
            }
            if (Interlocked.Exchange(ref dirty, 0) != 0)
            {
                var config = Config.Current;
                Call(host, "SetUniforms", Id, new[] { config.StarBrightness, config.SunBrightness,
                    config.SunRadiusDegrees * (float)Math.PI / 180, config.SunEnabled ? 1f : 0f,
                    config.LimitingMagnitude, config.StarColorStrength, config.StarProfileWidth,
                    config.StarCoreSharpness, config.StarTwinkleStrength, config.StarTwinkleSpeed,
                    config.ConstellationLines ? 1f : 0f, config.ConstellationFantasy ? 1f : 0f,
                    guideStart, guides.EdgeCount, guides.MemberCount, guideStart + guides.MemberStart });
                Call(host, "SetNativeSunGlare", Id, config.NativeSunGlare);
                Call(host, "SetEnabled", Id, config.Enabled);
                if (page != null) Page("Refresh");
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
        Page("Label", "Hipparcos star catalogue  -  Credit: ESA. Stick figures are curated for recognition.");
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
            (Action<bool>)(v => Config.Current.ConstellationLines = v), "Highlight rings around figure stars with connecting stick lines. Hidden from probes.");
        Page("Checkbox", "Constellation fantasy", (Func<bool>)(() => Config.Current.ConstellationFantasy),
            (Action<bool>)(v => Config.Current.ConstellationFantasy = v), "Brighten figure stars with a softer fantasy glow. Hidden from probes.");
        Page("Checkbox", "Native sun glare", (Func<bool>)(() => Config.Current.NativeSunGlare),
            (Action<bool>)(v => Config.Current.NativeSunGlare = v), "Turn off to isolate the orange ring. Other light flares and gameplay sunlight are unchanged.");
        Page("Slider", "Sun brightness", 0f, 5f, (Func<float>)(() => Config.Current.SunBrightness),
            (Action<float>)(v => Config.Current.SunBrightness = v), "Disc radiance multiplier.");
        Page("Slider", "Sun radius (degrees)", 0.02f, 2f, (Func<float>)(() => Config.Current.SunRadiusDegrees),
            (Action<float>)(v => Config.Current.SunRadiusDegrees = v), "Angular radius, not diameter.");
        Page("Label", "Status", (Func<string>)(() => Status));
        Page("Button", "Retry shaders", (Action)(() => Call(host, "Retry", Id)), "Recompile both views after fixing an asset.");
    }
    public static void Dispose()
    {
        try
        {
            if (registered) Call(host, "Unregister", Id);
            if (terminal != null) Call(terminal, "UnregisterPage", "Final Frontier/Settings");
        }
        catch (Exception e) { MyLog.Default.WriteLine("Final Frontier cleanup: " + e.Message); }
        registered = false; catalogue = null; guides = default; guideStart = 0; page = null; host = null; terminal = null;
    }
}
