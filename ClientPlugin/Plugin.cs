using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using ClientPlugin.Integration;
using ClientPlugin.Settings;
using ClientPlugin.Settings.Layouts;
using Sandbox.Graphics.GUI;
using VRage.Plugins;

#if !LOCAL_BUILD
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]
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
        AnomalyBridge.Update();
        AtmosphereBridge.Update();
    }
    static void Changed(object sender, PropertyChangedEventArgs args)
    {
        ConfigStorage.Save(Config.Current);
        AnomalyBridge.MarkDirty();
        if(args.PropertyName.StartsWith("Fog") || args.PropertyName.StartsWith("Atmosphere")) AtmosphereBridge.Changed();
    }
    public void Update()
    {
        ConfigStorage.FlushPending();
        AnomalyBridge.Update();
        AtmosphereBridge.Update();
    }
    public void Dispose()
    {
        Config.Current.PropertyChanged -= Changed;
        ConfigStorage.FlushPending(true);
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
}
