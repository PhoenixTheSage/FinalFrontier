using System;
using System.Reflection;
using ClientPlugin.Integration;
using VRageMath;

namespace ClientPlugin.Map;

/// <summary>No compile-time references to Anomaly or Rich HUD. Bind the v1 interface once, not per primitive.</summary>
internal sealed class MapBridge : IDisposable
{
    const string Id = "FinalFrontier.Map";
    readonly Type registry;
    readonly object window;
    readonly Type contract;
    public readonly Func<bool> Available, Visible;
    public readonly Action<bool> SetVisible;
    public readonly Action<int, bool, bool, bool> SetBinding;
    public readonly Action Refresh;
    public readonly Action<float[], int, float[], int, string[], float[]> SubmitGeometry;
    public readonly Action<long[], string[], string, string> SubmitRows;
    T Bind<T>(string method) where T : class => (T)(object)Delegate.CreateDelegate(typeof(T), window, contract.GetMethod(method));
    public static MapBridge TryCreate()
    {
        var registry = AnomalyBridge.Find("ClientPlugin.RichHud.InteractiveOverlayRegistry");
        if (registry == null || (int)registry.GetField("ApiVersion").GetRawConstantValue() != 1) return null;
        return new MapBridge(registry);
    }
    MapBridge(Type registry)
    {
        this.registry = registry;
        window = registry.GetMethod("RequestWindow").Invoke(null, new object[] { Id, "Final Frontier · 3D Map" });
        contract = window.GetType().GetInterface("ClientPlugin.RichHud.IInteractiveOverlayWindow");
        Available = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), window, contract.GetProperty("Available").GetGetMethod());
        Visible = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), window, contract.GetProperty("Visible").GetGetMethod());
        SetVisible = Bind<Action<bool>>("SetVisible"); SetBinding = Bind<Action<int, bool, bool, bool>>("SetBinding");
        Refresh = Bind<Action>("Refresh"); SubmitGeometry = Bind<Action<float[], int, float[], int, string[], float[]>>("SubmitGeometry");
        SubmitRows = Bind<Action<long[], string[], string, string>>("SubmitRows");
    }
    public void Handlers(Action<int, float, float, float> input, Action<float, float> resize, Action<long> select, Action<bool> visibility) =>
        contract.GetMethod("SetHandlers").Invoke(window, new object[] { input, resize, select, visibility });
    public void Button(string text, Action click) => contract.GetMethod("Button").Invoke(window, new object[] { text, click });
    public void Checkbox(string text, Func<bool> get, Action<bool> set, Func<bool> enabled) =>
        contract.GetMethod("Checkbox").Invoke(window, new object[] { text, get, set, enabled });
    public void Slider(string text, float min, float max, Func<float> get, Action<float> set) =>
        contract.GetMethod("Slider").Invoke(window, new object[] { text, min, max, get, set });
    public void Dispose() { SetVisible(false); registry.GetMethod("Unregister").Invoke(null, new object[] { Id }); }

    internal static void DisposeSettings()
    {
        if (page != null)
        {
            var terminal = AnomalyBridge.Find("ClientPlugin.RichHud.TerminalConfigRegistry");
            if (terminal != null) AnomalyBridge.Call(terminal, "UnregisterPage", "Final Frontier/Map");
        }
        page = null; nextPageRefresh = 0;
    }

    static object page;
    static int nextPageRefresh;
    static object Page(string method, params object[] args)
    {
        foreach (var candidate in page.GetType().GetInterface("ClientPlugin.RichHud.ITerminalConfigPage").GetMethods())
            if (candidate.Name == method && !candidate.IsGenericMethod && candidate.GetParameters().Length == args.Length) return candidate.Invoke(page, args);
        throw new MissingMethodException(method);
    }
    internal static void UpdateSettings()
    {
        int now = Environment.TickCount;
        if (nextPageRefresh != 0 && unchecked(now - nextPageRefresh) < 0) return;
        nextPageRefresh = unchecked(now + 1000);
        if (page == null)
        {
            var terminal = AnomalyBridge.Find("ClientPlugin.RichHud.TerminalConfigRegistry");
            if (terminal == null) return;
            page = AnomalyBridge.Call(terminal, "RequestFolderPage", "Final Frontier", "Map");
            if (page == null) return;
            var config = Config.Current;
            Page("Category", "3D world map", "Client-known physical objects. Admin reveal is an explicit session-only tool.");
            Page("Label", "Status", (Func<string>)(() => MapRuntime.Status));
            Page("Label", "Binding", (Func<string>)(() => config.MapToggle.ToString()));
            Page("Button", "Open map", (Action)MapRuntime.RequestOpen, "Open from gameplay after closing the terminal.");
            Page("Button", "Configure toggle key", (Action)MapRuntime.RequestSettings, "Use the Map toggle field in Pulsar settings; right-click to clear.");
            Page("Slider", "View distance fraction", .02f, 1f, (Func<float>)(() => config.MapRangeFraction), (Action<float>)(v => config.MapRangeFraction = v), "Range for grids and voxels. Client-known planets are always included.", .01f);
            Page("Checkbox", "Wireframe only", (Func<bool>)(() => config.MapWireframeOnly), (Action<bool>)(v => config.MapWireframeOnly = v), "Hide translucent faces.");
            Page("Category", "RGBA palette", "Face and line opacity are independent. Changes apply live.");
            Palette("Own grid lines", () => config.MapOwnedLine, v => config.MapOwnedLine = v);
            Palette("Own grid faces", () => config.MapOwnedFace, v => config.MapOwnedFace = v);
            Palette("Faction grid lines", () => config.MapFactionLine, v => config.MapFactionLine = v);
            Palette("Faction grid faces", () => config.MapFactionFace, v => config.MapFactionFace = v);
            Palette("Contact lines", () => config.MapContactLine, v => config.MapContactLine = v);
            Palette("Contact faces", () => config.MapContactFace, v => config.MapContactFace = v);
            Palette("Planet lines", () => config.MapPlanetLine, v => config.MapPlanetLine = v);
            Palette("Planet faces", () => config.MapPlanetFace, v => config.MapPlanetFace = v);
            Palette("Voxel lines", () => config.MapVoxelLine, v => config.MapVoxelLine = v);
            Palette("Voxel faces", () => config.MapVoxelFace, v => config.MapVoxelFace = v);
        }
        Page("Refresh");
    }
    static void Palette(string name, Func<Color> get, Action<Color> set)
    {
        Page("Color", name, get, (Action<Color>)(v => { v.A = get().A; set(v); }), "RGB tint; use the adjacent opacity slider for alpha.");
        Page("Slider", name + " opacity", 0f, 1f, (Func<float>)(() => get().A / 255f),
            (Action<float>)(v => { var color = get(); color.A = (byte)Math.Round(v * 255); set(color); }), "Independent alpha.", .01f);
    }
}
