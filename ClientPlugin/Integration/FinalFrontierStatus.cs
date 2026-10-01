using System;
using System.Globalization;
using System.Reflection;
using System.Text;
using ClientPlugin.Gui;
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
            AppendCelestial(sb);
            sb.AppendLine();
            AppendAtmosphere(sb);
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
