using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Serialization;
using ClientPlugin.StarDust;
using HarmonyLib;

// Executes the shipped plugin's config, RHUD page and prefixes against the local
// game assemblies. No game window, device, real user config or particle clock.
static class StarDustSmokeTests
{
    const BindingFlags All = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static Assembly plugin;
    static Type configType, runtime, logic, particleType, stateType, vector;
    static object config;
    static int checks;
    static object Call(Type type, object target, string method, params object[] args) => type.GetMethod(method, All).Invoke(target, args);
    static object Read(object target, string property) => target.GetType().GetProperty(property).GetValue(target);
    static void Set(string property, object value) => configType.GetProperty(property).SetValue(config, value);
    static void Mode(string name) => Set("StarDustMode", Enum.Parse(configType.GetProperty("StarDustMode").PropertyType, name));
    static void Check(string name, bool condition)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        checks++;
    }

    static int Main(string[] args)
    {
        string scratch = Path.Combine(Path.GetTempPath(), "FinalFrontier-dust-" + Guid.NewGuid().ToString("N"));
        try
        {
            string gameBin = Path.GetFullPath(args[0]);
            AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
            {
                string path = Path.Combine(gameBin, new AssemblyName(e.Name).Name + ".dll");
                return File.Exists(path) ? Assembly.LoadFrom(path) : null;
            };
            Directory.CreateDirectory(scratch);
            var library = Assembly.LoadFrom(Path.Combine(gameBin, "VRage.Library.dll"));
            Call(library.GetType("VRage.FileSystem.MyFileSystem", true), null, "Init", scratch, scratch, "Mods", null, null);
            // Ensure TypeByName can see the assembly containing Keen's space handler.
            var game = Assembly.LoadFrom(Path.Combine(gameBin, "SpaceEngineers.Game.dll"));
            plugin = Assembly.LoadFrom(args[1]);
            configType = plugin.GetType("ClientPlugin.Config", true);
            config = configType.GetField("Current").GetValue(null);
            MathChecks();
            ConfigChecks();
            TerminalChecks();
            SolarDisplayChecks();
            RuntimeChecks(game);
            Console.WriteLine("PASS: " + checks + " star dust checks (math, config, RHUD contract, installed-game Harmony hooks, pool cleanup and precision).");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        finally
        {
            if (runtime != null) Call(runtime, null, "Dispose");
            // Scratch is a unique absolute directory created by this process.
            if (Directory.Exists(scratch)) Directory.Delete(scratch, true);
        }
    }

    static void MathChecks()
    {
        Check("below threshold is invisible", StarDustMath.SpeedFade(5, 10, 15) == 0);
        Check("at threshold is invisible", StarDustMath.SpeedFade(10, 10, 15) == 0);
        Check("full speed fade", StarDustMath.SpeedFade(25, 10, 15) == 1);
        Check("smooth ramp midpoint", Math.Abs(StarDustMath.SpeedFade(17.5f, 10, 15) - .5f) < 1e-6);
        Check("birth fades from zero", StarDustMath.LifeFade(0, 1.5f, .15f) == 0);
        Check("expired particles invisible", StarDustMath.LifeFade(1.5f, 1.5f, .15f) == 0);
        Check("lifetime middle is opaque", StarDustMath.LifeFade(.75f, 1.5f, .15f) == 1);
        Check("fade may be disabled", StarDustMath.LifeFade(0, 1.5f, 0) == 1);
        Check("length capped at extreme mod speeds", StarDustMath.TrailLength(1000000, 50, 1, 2, 4) == 4);
        Check("fixed stretch ignores speed", StarDustMath.TrailLength(500, 50, 1, 0, 4) == 1);
        Check("reference speed gives base length", StarDustMath.TrailLength(50, 50, 1.5f, 1, 4) == 1.5f);
        Check("zero density emits nothing", StarDustMath.SpawnCount(50, 10, 0, 1, 1) == 0);
        Check("low minimum speed can emit below Keen's threshold", StarDustMath.SpawnCount(5, 10, 1, 1, 1, 1) > 0);
        Check("world density remains authoritative", StarDustMath.SpawnCount(50, 10, 1, 1, 1, 10, 0) == 0);
        float oneSecond = StarDustMath.SpawnCount(50, 10, 1, 1, 1);
        Check("spawn integration agrees at 30 and 60 Hz", Math.Abs(oneSecond - StarDustMath.SpawnCount(50, 10, 1, 1, 1f / 30) * 30) < .001f
            && Math.Abs(oneSecond - StarDustMath.SpawnCount(50, 10, 1, 1, 1f / 60) * 60) < .001f);
    }

    static void ConfigChecks()
    {
        Check("old configs default to Vanilla", Read(config, "StarDustMode").ToString() == "Vanilla");
        Set("StarDustWidth", float.NaN);
        Check("NaN width falls back", (float)Read(config, "StarDustWidth") == .018f);
        Set("StarDustMaxLength", float.PositiveInfinity);
        Check("infinite length falls back", (float)Read(config, "StarDustMaxLength") == 2);
        Set("StarDustDensity", 99f);
        Check("density is bounded", (float)Read(config, "StarDustDensity") == 5);
        Set("StarDustParticleLimit", 999999);
        Check("particle budget is bounded", (int)Read(config, "StarDustParticleLimit") == 128);
        var modeType = configType.GetProperty("StarDustMode").PropertyType;
        Set("StarDustMode", Enum.ToObject(modeType, 999));
        Check("invalid mode falls back", Read(config, "StarDustMode").ToString() == "Vanilla");
        Mode("Off");
        Set("StarDustNearPlanets", true);
        Set("StarDustHideInsideGrids", false);
        var styleType = configType.GetProperty("StarDustPreset").PropertyType;
        foreach (object style in Enum.GetValues(styleType))
        {
            Call(configType, config, "ApplyStarDustPreset", style);
            Check(style + " preset retains Off and gates", Read(config, "StarDustMode").ToString() == "Off"
                && (bool)Read(config, "StarDustNearPlanets") && !(bool)Read(config, "StarDustHideInsideGrids"));
        }
        Set("StarDustRed", .25f);
        var serializer = new XmlSerializer(configType);
        using var writer = new StringWriter();
        serializer.Serialize(writer, config);
        object restored = serializer.Deserialize(new StringReader(writer.ToString()));
        Check("XML round trip preserves dust", Read(restored, "StarDustMode").ToString() == "Off" && (float)Read(restored, "StarDustRed") == .25f
            && (float)Read(restored, "StarDustSpawnRadius") == 20);
        object old = serializer.Deserialize(new StringReader("<Config><StarBrightness>2</StarBrightness></Config>"));
        Check("old XML remains compatible", Read(old, "StarDustMode").ToString() == "Vanilla" && (float)Read(old, "StarBrightness") == 2);
        Check("old XML receives animated-sun defaults", (float)Read(old, "SunSurfaceDetail") == 1
            && (float)Read(old, "SunAnimationSpeed") == 1 && (float)Read(old, "SunGranulationScale") == 48);
        Set("SunFlareHeight", 100f);
        Check("solar flare height is bounded", (float)Read(config, "SunFlareHeight") == .35f);
        Set("SunAnimationSpeed", -100f);
        Check("negative solar speed freezes animation", (float)Read(config, "SunAnimationSpeed") == 0);
        Set("SunGranulationScale", float.NaN);
        Check("NaN solar frequency falls back", (float)Read(config, "SunGranulationScale") == 48);
        Set("SunCoronaStrength", float.PositiveInfinity);
        Check("infinite corona strength falls back", (float)Read(config, "SunCoronaStrength") == .8f);
        Check("old XML gains unified glow defaults", (bool)Read(old, "SunGlowEnabled") && (float)Read(old, "SunGlowRadius") == 3);
        object legacy = serializer.Deserialize(new StringReader("<Config><NativeSunGlare>true</NativeSunGlare></Config>"));
        Check("legacy native flag deserializes without disabling unified glow", (bool)Read(legacy, "NativeSunGlare") && (bool)Read(legacy, "SunGlowEnabled"));
        Set("SunGlowRadius", float.NaN);
        Check("NaN glow radius falls back", (float)Read(config, "SunGlowRadius") == 3);
        Set("SunGlowFalloff", 999f);
        Check("glow falloff bounded", (float)Read(config, "SunGlowFalloff") == 4);
        Set("SunGlowStrength", -2f);
        Check("negative glow strength clamps to zero", (float)Read(config, "SunGlowStrength") == 0);
    }

    static void TerminalChecks()
    {
        var bridge = plugin.GetType("ClientPlugin.Integration.StarDustBridge", true);
        Call(bridge, null, "Update");
        var page = ClientPlugin.RichHud.TerminalConfigRegistry.Page;
        Check("page registered under Final Frontier", page.Path == "Final Frontier/Star Dust");
        Check("page construction completed", page.Buttons.ContainsKey("Show Status") && page.Sliders.Count == 14);
        Check("width has millimetre step", page.Sliders["Width (m)"] == .001f);
        Check("lifetime fade has fractional step", page.Sliders["Lifetime fade fraction"] == .01f);
        page.Buttons["Reset dust settings"]();
        Check("reset restores Vanilla and location gates", Read(config, "StarDustMode").ToString() == "Vanilla"
            && !(bool)Read(config, "StarDustNearPlanets") && (bool)Read(config, "StarDustHideInsideGrids"));
        Check("reset refreshes page", page.Refreshes >= 2);
        Call(bridge, null, "Dispose");
        Check("page removed on dispose", ClientPlugin.RichHud.TerminalConfigRegistry.Removed == "Final Frontier/Star Dust");
        var celestial = plugin.GetType("ClientPlugin.Integration.AnomalyBridge", true);
        Call(celestial, null, "CreatePage");
        page = ClientPlugin.RichHud.TerminalConfigRegistry.Page;
        Check("solar controls remain under Final Frontier Settings", page.Path == "Final Frontier/Settings");
        Check("solar animation sliders use fractional steps", page.Sliders["Surface detail"] == .05f
            && page.Sliders["Solar animation speed"] == .05f && page.Sliders["Corona and flare strength"] == .05f
            && page.Sliders["Flare height (solar radii)"] == .01f && page.Sliders["Golden colour strength"] == .05f);
        Check("unified glow has live precision controls", page.Sliders["Glow strength"] == .01f
            && page.Sliders["Glow radius (solar radii)"] == .25f && page.Sliders["Glow falloff"] == .1f
            && page.Sliders["Glow atmosphere expansion"] == .1f && page.Sliders["Glow golden tint"] == .05f);
        Check("RHUD no longer exposes overlapping native solar glare", !page.Checkboxes.Contains("Native sun glare") && page.Checkboxes.Contains("Glow enabled"));
        page.Buttons["Reset sun glow"]();
        Check("glow reset restores defaults without changing surface settings", (float)Read(config, "SunGlowStrength") == .18f
            && (float)Read(config, "SunGlowFalloff") == 2 && (float)Read(config, "SunAnimationSpeed") == 0);
    }

    static void SolarDisplayChecks()
    {
        var bridge = plugin.GetType("ClientPlugin.Integration.AnomalyBridge", true);
        foreach (string status in new[] { "pending", "compile-failed: shader", "off (pack) AfterUpscale/Replace", "off (conflict) AfterUpscale/Replace" })
        {
            ClientPlugin.Shaders.FullscreenPassRegistry.ProgramStatus = status;
            Check("solar HDR detection rejects " + status, !(bool)Call(bridge, null, "ReadHdrDisplay"));
        }
        ClientPlugin.Shaders.FullscreenPassRegistry.Known = false;
        Check("unknown HDR display preserves SDR", !(bool)Call(bridge, null, "ReadHdrDisplay"));
        ClientPlugin.Shaders.FullscreenPassRegistry.Known = true;
        ClientPlugin.Shaders.FullscreenPassRegistry.ProgramStatus = "live AfterUpscale/Replace";
        Check("live HDR display selects HDR gold", (bool)Call(bridge, null, "ReadHdrDisplay"));
        bridge.GetField("host", All).SetValue(null, typeof(ClientPlugin.Shaders.CelestialBackgroundRegistry));
        bridge.GetField("registered", All).SetValue(null, true);
        Set("ConstellationDelayedPattern", false);
        Set("NativeSunGlare", true); // Existing user configs must still have one owner.
        Call(bridge, null, "Update");
        Check("HDR palette stays in its slot with glow controls appended", ClientPlugin.Shaders.CelestialBackgroundRegistry.Uniforms.Length == 47
            && ClientPlugin.Shaders.CelestialBackgroundRegistry.Uniforms[40] == 1);
        Check("unified glow claims native sun even with legacy flag true", !ClientPlugin.Shaders.CelestialBackgroundRegistry.NativeGlare);
        int uploads = ClientPlugin.Shaders.CelestialBackgroundRegistry.Uploads;
        Call(bridge, null, "Update");
        Check("unchanged display does not force uniform uploads", ClientPlugin.Shaders.CelestialBackgroundRegistry.Uploads == uploads);
        ClientPlugin.Shaders.FullscreenPassRegistry.ProgramStatus = "off (pack) AfterUpscale/Replace";
        Call(bridge, null, "Update");
        Check("HDR off updates palette without a settings change", ClientPlugin.Shaders.CelestialBackgroundRegistry.Uniforms[40] == 0
            && ClientPlugin.Shaders.CelestialBackgroundRegistry.Uploads == uploads + 1);
        ClientPlugin.Shaders.FullscreenPassRegistry.ProgramStatus = "live AfterUpscale/Replace";
        Call(bridge, null, "Update");
        Check("HDR on updates palette without a settings change", ClientPlugin.Shaders.CelestialBackgroundRegistry.Uniforms[40] == 1
            && ClientPlugin.Shaders.CelestialBackgroundRegistry.Uploads == uploads + 2);
        // Plugin.OnConfigChanged forwards dirty state in-game; this harness
        // does not initialize the whole game plugin, so exercise that signal.
        Set("SunGlowEnabled", false); Call(bridge, null, "MarkDirty"); Call(bridge, null, "Update");
        Check("glow off uploads off without restoring native glare", ClientPlugin.Shaders.CelestialBackgroundRegistry.Uniforms[41] == 0
            && !ClientPlugin.Shaders.CelestialBackgroundRegistry.NativeGlare);
        Set("SunGlowRadius", 7f); Call(bridge, null, "MarkDirty"); Call(bridge, null, "Update");
        Check("glow radius updates shader uniforms live", ClientPlugin.Shaders.CelestialBackgroundRegistry.Uniforms[43] == 7);
        Set("SunGlowEnabled", true); Call(configType, config, "ResetSunGlow");
        Call(bridge, null, "Dispose");
        Check("dispose clears solar display state", !(bool)bridge.GetProperty("HdrSolarPalette", All).GetValue(null));
    }

    static void RuntimeChecks(Assembly game)
    {
        runtime = plugin.GetType("ClientPlugin.StarDust.StarDustRuntime", true);
        Call(runtime, null, "Initialize");
        Check("Harmony hooks installed", (bool)runtime.GetField("ready", All).GetValue(null));
        var handler = game.GetType("SpaceEngineers.Game.World.Environment.MyEnvironmentalParticleLogicSpace", true);
        foreach (string name in new[] { "UpdateBeforeSimulation", "UpdateAfterSimulation", "Draw" })
        {
            var info = Harmony.GetPatchInfo(handler.GetMethod(name));
            Check("isolated " + name + " hooks", info != null && info.Owners.Contains("FinalFrontier.StarDust")
                && (name != "Draw" || info.Postfixes.Any(p => p.owner == "FinalFrontier.StarDust")));
        }
        logic = handler.BaseType;
        particleType = logic.GetNestedType("MyEnvironmentalParticle");
        stateType = runtime.GetNestedType("State", BindingFlags.NonPublic);
        vector = Assembly.Load("VRage.Math").GetType("VRageMath.Vector3D", true);
        object instance = Activator.CreateInstance(logic);
        var active = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(particleType));
        var idle = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(particleType));
        object state = Activator.CreateInstance(stateType, true);
        object camera = Activator.CreateInstance(vector, 1000000000d, 0d, 0d);
        Seed(state, active, 1000000000.25, 10);
        Seed(state, active, 1000000010d, 10);
        Seed(state, active, 1000000001d, 1);
        Call(runtime, null, "Prune", state, active, idle, 10.5d, camera, config);
        Check("sub-metre positions survive far from origin", active.Count == 2 && idle.Count == 1);
        Call(runtime, null, "Prune", state, active, idle, 12d, camera, config);
        Check("expiry returns pool without allocation", active.Count == 0 && idle.Count == 3);
        for (int i = 0; i < 12; i++) Seed(state, active, 1000000001d, 10);
        Set("StarDustParticleLimit", 8);
        Call(runtime, null, "Prune", state, active, idle, 10.5d, camera, config);
        Check("lowering live budget immediately prunes excess", active.Count == 8 && idle.Count == 7);
        Call(runtime, null, "Clear", state, active, idle);
        Set("StarDustParticleLimit", 128);
        Seed(state, active, 1000000001d, 10);
        Seed(state, active, 1000000001d, 10);
        Set("StarDustSpawnRadius", 3f);
        Call(runtime, null, "Prune", state, active, idle, 10.5d, Activator.CreateInstance(vector, 1000000100d, 0d, 0d), config);
        Check("teleports cull distant pool", active.Count == 0 && idle.Count == 17);
        var tracked = Call(runtime, null, "GetState", instance, active, idle);
        Seed(tracked, active, 1000000001d, 10);
        Mode("Off");
        Call(runtime, null, "GetState", instance, active, idle);
        Check("mode transition clears pool", active.Count == 0 && ((IDictionary)stateType.GetField("Samples", All).GetValue(tracked)).Count == 0);
        foreach (string method in new[] { "UpdateBeforeSimulationPrefix", "UpdateAfterSimulationPrefix", "DrawPrefix" })
            Check("Off suppresses " + method, !(bool)Prefix(method, instance, active, idle));
        Mode("Vanilla");
        foreach (string method in new[] { "UpdateBeforeSimulationPrefix", "UpdateAfterSimulationPrefix", "DrawPrefix" })
            Check("Vanilla delegates " + method, (bool)Prefix(method, instance, active, idle));
        Call(runtime, null, "DrawPostfix", active, idle);
        Check("draw diagnostics distinguish active pool from submission without a renderer",
            (int)runtime.GetProperty("DrawCalls", All).GetValue(null) == 2
            && (int)runtime.GetProperty("SubmittedCount", All).GetValue(null) == 0
            && (int)runtime.GetProperty("PoolCapacity", All).GetValue(null) == active.Count + idle.Count);
        Mode("Enhanced");
        Call(runtime, null, "GetState", instance, active, idle);
        Seed(tracked, active, 1000000001d, 10);
        Check("Enhanced handles missing world without running vanilla", !(bool)Prefix("UpdateBeforeSimulationPrefix", instance, active, idle) && active.Count == 0);
        runtime.GetField("faulted", All).SetValue(null, true);
        Check("faulted hook delegates to Keen", (bool)Prefix("UpdateBeforeSimulationPrefix", instance, active, idle));
        runtime.GetField("faulted", All).SetValue(null, false);
        Seed(tracked, active, 1000000001d, 10);
        Call(runtime, null, "Dispose");
        Check("dispose returns enhanced particles to world pool", active.Count == 0);
        foreach (string name in new[] { "UpdateBeforeSimulation", "UpdateAfterSimulation", "Draw" })
            Check("dispose removes " + name, !Harmony.GetPatchInfo(handler.GetMethod(name)).Owners.Contains("FinalFrontier.StarDust"));
    }

    static object Prefix(string method, object instance, IList active, IList idle) => method == "UpdateBeforeSimulationPrefix"
        ? Call(runtime, null, method, instance, active, idle, .0000235f)
        : Call(runtime, null, method, instance, active, idle);

    static void Seed(object state, IList active, double x, double born)
    {
        // Avoid the real game's clock-dependent Activate constructor in a headless host.
        object particle = RuntimeHelpers.GetUninitializedObject(particleType);
        particleType.GetField("m_active", All).SetValue(particle, true);
        var sampleType = runtime.GetNestedType("Sample", BindingFlags.NonPublic);
        object sample = Activator.CreateInstance(sampleType);
        sampleType.GetField("Position", All).SetValue(sample, Activator.CreateInstance(vector, x, 0d, 0d));
        sampleType.GetField("Born", All).SetValue(sample, born);
        sampleType.GetField("Scale", All).SetValue(sample, 1f);
        ((IDictionary)stateType.GetField("Samples", All).GetValue(state)).Add(particle, sample);
        active.Add(particle);
    }
}

// Stand-ins execute the production bridge against Anomaly's published contract.
namespace ClientPlugin.Shaders
{
    public static class FullscreenPassRegistry
    {
        public static bool Known = true;
        public static string ProgramStatus;
        public static bool TryGetProgramStatus(string id, out string status)
        {
            status = ProgramStatus;
            return Known && id == "hdr.tonemap";
        }
    }
    public static class CelestialBackgroundRegistry
    {
        public static float[] Uniforms;
        public static int Uploads;
        public static bool NativeGlare = true;
        public static string StatusLine => "Test celestial provider";
        public static void SetUniforms(string id, float[] uniforms) { Uniforms = uniforms; Uploads++; }
        public static void SetEnabled(string id, bool enabled) { }
        public static void SetNativeSunGlare(string id, bool enabled) { NativeGlare = enabled; }
        public static void SetArt(string id, object art) { }
        public static void Unregister(string id) { }
    }
}

// The actual page is built by reflection against this stand-in for Anomaly's
// published interface; an arity mismatch or missing overload fails construction.
namespace ClientPlugin.RichHud
{
    public interface ITerminalConfigPage
    {
        void Category(string header);
        void Category(string header, string subtitle);
        void Dropdown(string label, Type type, Func<object> get, Action<object> set, string description);
        void Button(string label, Action click, string description);
        void Slider(string label, float min, float max, Func<float> get, Action<float> set, string description, float step);
        void Slider(string label, float min, float max, Func<float> get, Action<float> set, string description);
        void IntSlider(string label, int min, int max, Func<int> get, Action<int> set, string description);
        void Checkbox(string label, Func<bool> get, Action<bool> set, string description);
        void Color(string label, Delegate get, Delegate set, string description);
        void Label(string text);
        void Label(string label, Func<string> get);
        void Refresh();
    }
    public class FakePage : ITerminalConfigPage
    {
        public string Path;
        public int Refreshes;
        public Dictionary<string, Action> Buttons = new();
        public Dictionary<string, float> Sliders = new();
        public HashSet<string> Checkboxes = new();
        public void Category(string header) { }
        public void Category(string header, string subtitle) { }
        public void Dropdown(string label, Type type, Func<object> get, Action<object> set, string description) { _ = get(); }
        public void Button(string label, Action click, string description) { Buttons[label] = click; }
        public void Slider(string label, float min, float max, Func<float> get, Action<float> set, string description, float step) { Sliders[label] = step; _ = get(); }
        public void Slider(string label, float min, float max, Func<float> get, Action<float> set, string description) { Sliders[label] = 1; _ = get(); }
        public void IntSlider(string label, int min, int max, Func<int> get, Action<int> set, string description) { _ = get(); }
        public void Checkbox(string label, Func<bool> get, Action<bool> set, string description) { Checkboxes.Add(label); _ = get(); }
        public void Color(string label, Delegate get, Delegate set, string description) { _ = get.DynamicInvoke(); }
        public void Label(string text) { }
        public void Label(string label, Func<string> get) { _ = get(); }
        public void Refresh() { Refreshes++; }
    }
    public static class TerminalConfigRegistry
    {
        public static FakePage Page = new();
        public static string Removed;
        public static object RequestFolderPage(string folder, string title) { Page = new FakePage(); Page.Path = folder + "/" + title; return Page; }
        public static void UnregisterPage(string path) { Removed = path; }
    }
}
