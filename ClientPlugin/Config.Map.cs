using ClientPlugin.Settings.Elements;
using ClientPlugin.Settings.Tools;
using VRage.Input;
using VRageMath;

namespace ClientPlugin;

public partial class Config
{
    Binding mapToggle = new(MyKeys.F2, ctrl: true, shift: true);
    float mapRangeFraction = 1;
    bool mapWireframeOnly, mapGrids = true, mapPlanets = true, mapVoxels = true;
    Color mapOwnedLine = new(85, 220, 255, 220), mapOwnedFace = new(85, 220, 255, 28);
    Color mapFactionLine = new(105, 235, 150, 220), mapFactionFace = new(105, 235, 150, 28);
    Color mapContactLine = new(245, 185, 90, 210), mapContactFace = new(245, 185, 90, 24);
    Color mapPlanetLine = new(120, 155, 255, 220), mapPlanetFace = new(120, 155, 255, 24);
    Color mapVoxelLine = new(175, 175, 185, 200), mapVoxelFace = new(175, 175, 185, 24);

    [Keybind(label: "Map toggle", description: "Toggle the RHUD 3D map. Default Ctrl+Shift+F2. Right-click to unbind.")]
    public Binding MapToggle { get => mapToggle; set => Set(ref mapToggle, value); }
    [Slider(.02f, 1, .01f, SliderAttribute.SliderType.Float, label: "Map range fraction", description: "Fraction of the world's view distance for grids and voxels. Client-known planets are always included.")]
    public float MapRangeFraction { get => mapRangeFraction; set => Set(ref mapRangeFraction, Clamp(value, .02f, 1, 1)); }
    [Checkbox(label: "Map wireframe only")]
    public bool MapWireframeOnly { get => mapWireframeOnly; set => Set(ref mapWireframeOnly, value); }
    [Checkbox(label: "Map grids")]
    public bool MapGrids { get => mapGrids; set => Set(ref mapGrids, value); }
    [Checkbox(label: "Map planets / moons")]
    public bool MapPlanets { get => mapPlanets; set => Set(ref mapPlanets, value); }
    [Checkbox(label: "Map asteroids / voxels")]
    public bool MapVoxels { get => mapVoxels; set => Set(ref mapVoxels, value); }
    [ColorAttribute(true, label: "Map own grid lines")]
    public Color MapOwnedLine { get => mapOwnedLine; set => Set(ref mapOwnedLine, value); }
    [ColorAttribute(true, label: "Map own grid faces")]
    public Color MapOwnedFace { get => mapOwnedFace; set => Set(ref mapOwnedFace, value); }
    [ColorAttribute(true, label: "Map faction grid lines")]
    public Color MapFactionLine { get => mapFactionLine; set => Set(ref mapFactionLine, value); }
    [ColorAttribute(true, label: "Map faction grid faces")]
    public Color MapFactionFace { get => mapFactionFace; set => Set(ref mapFactionFace, value); }
    [ColorAttribute(true, label: "Map contact lines")]
    public Color MapContactLine { get => mapContactLine; set => Set(ref mapContactLine, value); }
    [ColorAttribute(true, label: "Map contact faces")]
    public Color MapContactFace { get => mapContactFace; set => Set(ref mapContactFace, value); }
    [ColorAttribute(true, label: "Map planet lines")]
    public Color MapPlanetLine { get => mapPlanetLine; set => Set(ref mapPlanetLine, value); }
    [ColorAttribute(true, label: "Map planet faces")]
    public Color MapPlanetFace { get => mapPlanetFace; set => Set(ref mapPlanetFace, value); }
    [ColorAttribute(true, label: "Map voxel lines")]
    public Color MapVoxelLine { get => mapVoxelLine; set => Set(ref mapVoxelLine, value); }
    [ColorAttribute(true, label: "Map voxel faces")]
    public Color MapVoxelFace { get => mapVoxelFace; set => Set(ref mapVoxelFace, value); }
    [Button(label: "Open 3D map", description: "Requires Anomaly's interactive overlay API and Rich HUD Master in the world.")]
    public static void OpenMap() => Map.MapRuntime.RequestOpen();
    [Button(label: "Map status")]
    public static void ShowMapStatus() => Sandbox.ModAPI.MyAPIGateway.Utilities?.ShowNotification(Map.MapRuntime.Status, 6000);
}
