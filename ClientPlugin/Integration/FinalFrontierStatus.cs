using System;
using System.Globalization;
using System.Reflection;
using System.Text;
using ClientPlugin.Gui;
using ClientPlugin.StarDust;
using Sandbox.Graphics.GUI;

namespace ClientPlugin.Integration;

/// <summary>
/// Live celestial + shared-volume diagnostics. Prefer this over a single Status
/// label when Atmosphere enables but nothing draws — StatusLine / shadow Status
/// name the fail-closed gate.
/// </summary>
internal static class FinalFrontierStatus
{
    public static string CurrentText
    {
        get
        {
            var sb = new StringBuilder();
            sb.AppendLine(PluginStatusPage.RendererLine);
            AppendCelestial(sb);
            sb.AppendLine();
            AppendAtmosphere(sb);
            sb.AppendLine();
            AppendStarDust(sb);
            sb.AppendLine();
            sb.AppendLine(Map.MapRuntime.Status);
            sb.AppendLine();
            AppendSharedVolume(sb);
            sb.AppendLine();
            AppendNotes(sb);
            return sb.ToString();
        }
    }

    public static void Show() =>
        MyGuiSandbox.AddScreen(new StatusScreen(CurrentText));

    static void AppendCelestial(StringBuilder sb)
    {
        var c = Config.Current;
        sb.AppendLine("Celestial");
        sb.Append("     enabled ").Append(Yes(c.Enabled));
        sb.Append(" · sun ").Append(Yes(c.SunEnabled));
        sb.Append(" · radius ").Append(F(c.SunRadiusDegrees)).AppendLine(" deg");
        sb.Append("     solar detail ").Append(F(c.SunSurfaceDetail));
        sb.Append(" · animation ").Append(F(c.SunAnimationSpeed));
        sb.Append(" · corona ").Append(F(c.SunCoronaStrength));
        sb.Append(" · flare height ").Append(F(c.SunFlareHeight)).AppendLine(" radii");
        sb.Append("     solar display palette ").AppendLine(AnomalyBridge.HdrSolarPalette ? "HDR gold" : "SDR gold");
        sb.Append("     unified sun glow ").Append(Yes(c.SunGlowEnabled));
        sb.Append(" · strength ").Append(F(c.SunGlowStrength));
        sb.Append(" · radius ").Append(F(c.SunGlowRadius)).Append(" radii");
        sb.Append(" · falloff ").Append(F(c.SunGlowFalloff)).AppendLine();
        sb.Append("     bridge ").AppendLine(Empty(AnomalyBridge.Status));
    }

    static void AppendAtmosphere(StringBuilder sb)
    {
        var c = Config.Current;
        sb.AppendLine("Atmosphere (Final Frontier fog provider)");
        sb.Append("     enabled ").Append(Yes(c.AtmosphereEnabled));
        sb.Append(" · preset ").Append(c.AtmospherePreset);
        sb.Append(" · quality ").AppendLine(c.AtmosphereQuality.ToString());
        sb.Append("     debug view ").Append(c.AtmosphereDebugView);
        sb.Append(" · density ").Append(F(c.FogDensity * 1000f)).AppendLine(" /km (UI)");
        sb.Append("     height ").Append(F(c.FogHeight)).Append(" m");
        sb.Append(" · distance ").Append(F(c.FogDistance)).AppendLine(" m");
        sb.Append("     bridge ").AppendLine(Empty(AtmosphereBridge.Status));
    }

    static void AppendSharedVolume(StringBuilder sb)
    {
        sb.AppendLine("Anomaly shared volumes");
        sb.Append("     StatusLine ").AppendLine(Empty(ReadStaticProp("ClientPlugin.Shaders.VolumetricMediumRegistry", "StatusLine")));
        sb.Append("     Requested ").AppendLine(Empty(ReadStaticProp("ClientPlugin.Shaders.VolumetricMediumRegistry", "Requested")));
        sb.Append("     Shadows ").AppendLine(Empty(ReadStaticProp("ClientPlugin.ShaderFramework.DirectionalVolumeShadows", "Status")));
        sb.Append("     MSAA ").AppendLine(Empty(ReadMsaa()));
    }

    static void AppendStarDust(StringBuilder sb)
    {
        sb.AppendLine("Star Dust (Keen movement-dust producer)");
        sb.Append("     mode ").Append(Config.Current.StarDustMode).Append(" · preset ").AppendLine(Config.Current.StarDustPreset.ToString());
        sb.Append("     hook ").AppendLine(StarDustRuntime.Status);
        sb.Append("     active ").Append(StarDustRuntime.ActiveCount).Append(" / world capacity ").Append(StarDustRuntime.PoolCapacity);
        sb.Append(" · last enhanced speed ").Append(F(StarDustRuntime.Speed)).AppendLine(" m/s");
        sb.Append("     billboards submitted ").Append(StarDustRuntime.SubmittedCount);
        sb.Append(" last draw · draw calls ").Append(StarDustRuntime.DrawCalls).AppendLine();
    }

    static void AppendNotes(StringBuilder sb)
    {
        sb.AppendLine("Notes");
        sb.AppendLine("     Debug views only draw after Prepare commits (StatusLine = Shared volume active…).");
        sb.AppendLine("     Fallback / IndexOutOfRange / compile errors mean no fog and no debug overlay.");
        sb.AppendLine("     Prefer Density / GeometryVisibility first; None restores the composite.");
    }

    static string ReadStaticProp(string typeName, string prop)
    {
        try
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(typeName, false);
                if (type == null) continue;
                var p = type.GetProperty(prop, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (p != null) return p.GetValue(null)?.ToString() ?? "(null)";
                var f = type.GetField(prop, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (f != null) return f.GetValue(null)?.ToString() ?? "(null)";
            }
            return "(Anomaly type not loaded)";
        }
        catch (Exception e) { return e.GetType().Name + ": " + e.Message; }
    }

    static string ReadMsaa()
    {
        try
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType("VRageRender.MyRender11", false);
                var p = type?.GetProperty("MultisamplingEnabled", BindingFlags.Public | BindingFlags.Static);
                if (p != null) return (bool)p.GetValue(null) ? "ON (volumes unsupported)" : "off";
            }
        }
        catch { /* ignore */ }
        return "(unknown)";
    }

    static string Yes(bool v) => v ? "yes" : "no";
    static string Empty(string s) => string.IsNullOrWhiteSpace(s) ? "(empty)" : s;
    static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}
