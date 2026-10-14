using System;
using System.Globalization;
using ClientPlugin.StarDust;
using VRageMath;

namespace ClientPlugin.Integration;

/// <summary>The terminal is optional; movement dust works without Anomaly or Rich HUD.</summary>
internal static class StarDustBridge
{
    static Type terminal;
    static object page;
    static int nextRefresh;
    static string pageError;

    internal static void Update()
    {
        if (nextRefresh != 0 && unchecked(Environment.TickCount - nextRefresh) < 0) return;
        nextRefresh = unchecked(Environment.TickCount + 1000);
        try
        {
            if (page == null) CreatePage();
            if (page != null) Page("Refresh");
        }
        catch (Exception e)
        {
            string error = (e.InnerException ?? e).Message;
            if (pageError != error) VRage.Utils.MyLog.Default?.WriteLine("Final Frontier star dust page: " + error);
            pageError = error;
        }
    }

    static object Page(string method, params object[] args)
    {
        foreach (var candidate in page.GetType().GetInterface("ClientPlugin.RichHud.ITerminalConfigPage").GetMethods())
            if (candidate.Name == method && !candidate.IsGenericMethod && candidate.GetParameters().Length == args.Length)
                return candidate.Invoke(page, args);
        throw new MissingMethodException(method);
    }

    static void Slider(string label, float min, float max, float step, Func<float> get, Action<float> set, string description) =>
        Page("Slider", label, min, max, get, set, description, step);

    static void CreatePage()
    {
        terminal ??= AnomalyBridge.Find("ClientPlugin.RichHud.TerminalConfigRegistry");
        if (terminal == null) return;
        page = AnomalyBridge.Call(terminal, "RequestFolderPage", "Final Frontier", "Star Dust");
        if (page == null) return;
        var c = Config.Current;
        Page("Category", "Movement dust", "A local visual speed cue. Independent of the celestial background toggle.");
        Page("Dropdown", "Mode", typeof(StarDustMode), (Func<object>)(() => c.StarDustMode),
            (Action<object>)(v => c.StarDustMode = (StarDustMode)v), "Vanilla uses Keen's values. Enhanced enables the controls below. Off hides movement dust only.");
        Page("Dropdown", "Preset", typeof(StarDustStyle), (Func<object>)(() => c.StarDustPreset),
            (Action<object>)(v => c.ApplyStarDustPreset((StarDustStyle)v)), "Subtle, Cinematic, or High Speed. Applies appearance and motion values; retains Mode and location gates.");
        Page("Button", "Reapply preset", (Action)(() => { c.ApplyStarDustPreset(c.StarDustPreset); Page("Refresh"); }), "Restore the selected preset after manual tuning.");
        Page("Button", "Reset dust settings", (Action)(() =>
        {
            c.ApplyStarDustPreset(StarDustStyle.Subtle);
            c.StarDustNearPlanets = false;
            c.StarDustHideInsideGrids = true;
            c.StarDustMode = StarDustMode.Vanilla;
            Page("Refresh");
        }), "Select Vanilla, reset the enhanced look to Subtle, and restore location gates.");

        Page("Category", "Density and range", "Enhanced only. Uses the world's existing Stardust material and particle pool.");
        Slider("Density multiplier", 0, 5, .05f, () => c.StarDustDensity, v => c.StarDustDensity = v, "Scales Keen's average spawn rate. Zero stops new particles; existing particles fade out.");
        Page("IntSlider", "Particle budget", 8, 128, (Func<int>)(() => c.StarDustParticleLimit),
            (Action<int>)(v => c.StarDustParticleLimit = v), "Maximum active dust particles, also limited by the world's pool capacity.");
        Slider("Spawn radius (m)", 3, 40, .5f, () => c.StarDustSpawnRadius, v => c.StarDustSpawnRadius = v, "Spawn ahead of the camera. Despawn range is 1.5 times this radius.");
        Slider("Lifetime (s)", .25f, 5, .05f, () => c.StarDustLifetime, v => c.StarDustLifetime = v, "Maximum lifetime. Expired and distant particles return to Keen's pool.");

        Page("Category", "Speed response", "Uses the controlled character, cockpit, or remote-control grid's velocity; spectator cameras stay clear.");
        Slider("Minimum speed (m/s)", 0, 100, 1, () => c.StarDustMinimumSpeed, v => c.StarDustMinimumSpeed = v, "Dust starts above this speed. A stationary body never produces streaks.");
        Slider("Speed fade range (m/s)", 1, 100, 1, () => c.StarDustSpeedFade, v => c.StarDustSpeedFade = v, "Smoothly ramps density and visibility above minimum speed.");
        Slider("Reference speed (m/s)", 1, 200, 1, () => c.StarDustReferenceSpeed, v => c.StarDustReferenceSpeed = v, "Speed at which streaks reach their base length.");
        Slider("Speed stretch", 0, 2, .05f, () => c.StarDustStretch, v => c.StarDustStretch = v, "0 fixes streak length; 1 scales linearly with speed; 2 emphasizes high speeds.");

        Page("Category", "Streak appearance");
        Slider("Base length (m)", .05f, 5, .05f, () => c.StarDustLength, v => c.StarDustLength = v, "Streak length at reference speed, before size variation.");
        Slider("Maximum length (m)", .05f, 8, .05f, () => c.StarDustMaxLength, v => c.StarDustMaxLength = v, "Hard cap after speed stretch and variation, including very fast modded ships.");
        Slider("Width (m)", .005f, .1f, .001f, () => c.StarDustWidth, v => c.StarDustWidth = v, "Base thickness of the Stardust billboard.");
        Slider("Size variation", 0, 1, .05f, () => c.StarDustVariation, v => c.StarDustVariation = v, "Stable per-particle size variation. New particles use the new value.");
        Slider("Brightness", 0, 3, .05f, () => c.StarDustBrightness, v => c.StarDustBrightness = v, "Scales material RGB in Keen's LDR pass. Does not illuminate the world.");
        Slider("Opacity", 0, 1, .05f, () => c.StarDustOpacity, v => c.StarDustOpacity = v, "Scales RGB and alpha together to retain the native blending behavior.");
        Slider("Lifetime fade fraction", 0, .5f, .01f, () => c.StarDustFade, v => c.StarDustFade = v, "Fraction of lifetime spent fading in and out. Zero disables the lifetime fade.");
        Page("Color", "Tint", (Func<Color>)(() => new Color(c.StarDustRed, c.StarDustGreen, c.StarDustBlue)),
            (Action<Color>)(v => { var rgb = v.ToVector3(); c.StarDustRed = rgb.X; c.StarDustGreen = rgb.Y; c.StarDustBlue = rgb.Z; }), "Multiplies the world's dust colour. White retains it.");

        Page("Category", "Location gates");
        Page("Checkbox", "Allow near planets", (Func<bool>)(() => c.StarDustNearPlanets),
            (Action<bool>)(v => c.StarDustNearPlanets = v), "Allow enhanced dust in natural gravity; use the world's planetary material and colour. Does not change Keen's global planet flag.");
        Page("Checkbox", "Hide inside large grids", (Func<bool>)(() => c.StarDustHideInsideGrids),
            (Action<bool>)(v => c.StarDustHideInsideGrids = v), "Retains Keen's camera-in-large-grid bounds test. Turning it off can show dust inside cockpits and rooms.");
        Page("Category", "Live diagnostics");
        Page("Label", "Status", (Func<string>)(() => StarDustRuntime.Status));
        Page("Label", "Particles", (Func<string>)(() => StarDustRuntime.ActiveCount + " active / " + StarDustRuntime.PoolCapacity + " world capacity"));
        Page("Label", "Submission", (Func<string>)(() => StarDustRuntime.SubmittedCount + " billboards submitted last draw / " + StarDustRuntime.DrawCalls + " draw calls"));
        Page("Label", "Speed", (Func<string>)(() => StarDustRuntime.Speed.ToString("0.0", CultureInfo.InvariantCulture) + " m/s (enhanced)"));
        Page("Label", "Particle counts prove spawning; submitted counts prove queuing, not GPU visibility. Both modes use Keen's LDR billboard renderer.");
        Page("Button", "Show Status", (Action)FinalFrontierStatus.Show, "Full Final Frontier diagnostics, including the star dust hook.");
    }

    internal static void Dispose()
    {
        if (terminal != null) AnomalyBridge.Call(terminal, "UnregisterPage", "Final Frontier/Star Dust");
        page = null;
        terminal = null;
        nextRefresh = 0;
        pageError = null;
    }
}
