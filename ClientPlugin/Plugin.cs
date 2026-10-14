using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using ClientPlugin.Integration;
using ClientPlugin.Settings;
using ClientPlugin.Settings.Layouts;
using ClientPlugin.StarDust;
using Sandbox.Graphics.GUI;
using VRage.Plugins;

#if !LOCAL_BUILD
[assembly: AssemblyVersion("1.2.1.0")]
[assembly: AssemblyFileVersion("1.2.1.0")]
#endif
namespace ClientPlugin;

public sealed class Plugin : IPlugin
{
    public const string Name = "FinalFrontier";
    public static Plugin Instance { get; private set; }
    SettingsGenerator settings;
    public void LoadAssets(IReadOnlyDictionary<string, string> assets)
    {
        if (assets.TryGetValue("Celestial", out var root)) { AnomalyBridge.SetAssets(root); AtmosphereBridge.SetAssets(root); }
    }
    public void Init(object gameInstance)
    {
        Instance = this;
        settings = new SettingsGenerator();
        Config.Current.PropertyChanged += Changed;
        if (!AnomalyRenderer.LinuxRequiresGuest)
            StarDustRuntime.Initialize();
        StarDustBridge.Update();
        AnomalyBridge.Update();
        AtmosphereBridge.Update();
    }
    static void Changed(object sender, PropertyChangedEventArgs args)
    {
        ConfigStorage.Save(Config.Current);
        if (!args.PropertyName.StartsWith("StarDust") && !args.PropertyName.StartsWith("Map")) AnomalyBridge.MarkDirty();
        if (args.PropertyName.StartsWith("Map")) Map.MapRuntime.Changed();
        if(args.PropertyName.StartsWith("Fog") || args.PropertyName.StartsWith("Atmosphere")) AtmosphereBridge.Changed();
    }
    public void Update()
    {
        PluginStatusPage.TryInstall("Final Frontier", Config.ShowStatus);
        AnomalyRenderer.Observe();
        if (AnomalyRenderer.LinuxRequiresGuest)
        {
            ConfigStorage.FlushPending();
            return;
        }
        StarDustRuntime.Initialize();
        ConfigStorage.FlushPending();
        AnomalyBridge.Update();
        AtmosphereBridge.Update();
        StarDustBridge.Update();
        Map.MapRuntime.Update();
    }
    public void Dispose()
    {
        Config.Current.PropertyChanged -= Changed;
        ConfigStorage.FlushPending(true);
        StarDustRuntime.Dispose();
        Map.MapRuntime.Dispose();
        StarDustBridge.Dispose();
        AtmosphereBridge.Dispose();
        AnomalyBridge.Dispose();
        Instance = null;
    }
    public void OpenConfigDialog()
    {
        if (settings == null) return;
        settings.SetLayout<Simple>();
        settings.Dialog.RecreateControls(true);
        MyGuiSandbox.AddScreen(settings.Dialog);
    }
    internal void CloseConfigDialog() => settings?.Dialog.CloseScreen();
}
