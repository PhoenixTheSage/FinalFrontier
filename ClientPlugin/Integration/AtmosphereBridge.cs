using System;
using System.IO;
using System.Reflection;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Planet;
using Sandbox.Game.World;
using VRageMath;

namespace ClientPlugin.Integration;

/// <summary>Final Frontier supplies a medium and UI; Anomaly owns all rendering.</summary>
internal static class AtmosphereBridge
{
    const string Id="FinalFrontier.Atmosphere";
    static Type host,terminal;
    static object page;
    static string shader;
    static bool registered;
    static int nextRefresh;
    internal static string Status {get;private set;}="Waiting for Anomaly shared volumes";
    internal static void SetAssets(string root) { shader=Path.Combine(root,"Atmosphere","Fog.hlsli"); }
    internal static void Changed()
    { if(registered) AnomalyBridge.Call(host,"InvalidateHistory",Id); }
    internal static void Update()
    {
        try
        {
            host ??= AnomalyBridge.Find("ClientPlugin.Shaders.VolumetricMediumRegistry");
            if(host==null || shader==null || !File.Exists(shader)) return;
            if(!registered) registered=(bool)AnomalyBridge.Call(host,"Register",Id,shader,Array.Empty<string>());
            if(!registered) { Status="Atmosphere provider registration failed"; return; }
            if(page==null) CreatePage();
            var config=Config.Current;
            MyPlanet planet=null;
            double best=double.MaxValue;
            if(MySession.Static!=null && MySector.MainCamera!=null && MyPlanets.Static!=null)
            foreach(var candidate in MyPlanets.GetPlanets())
            {
                if(candidate.Closed || !candidate.HasAtmosphere) continue;
                double distance=(candidate.PositionComp.GetPosition()-MySector.MainCamera.Position).LengthSquared();
                if(distance<best) {best=distance;planet=candidate;}
            }
            bool active=config.AtmosphereEnabled && planet!=null;
            float top=planet==null?0:planet.AverageRadius+Math.Max(planet.AtmosphereAltitude,200);
            active=active && Math.Sqrt(best)<=top+8000;
            AnomalyBridge.Call(host,"RequestRenderer",Id,active);
            AnomalyBridge.Call(host,"SetEnabled",Id,active);
            if(active)
            {
                var center=planet.PositionComp.GetPosition();
                AnomalyBridge.Call(host,"SetOrigin",Id,center.X,center.Y,center.Z);
                var values=new float[64];
                values[0]=planet.AverageRadius;values[1]=top;values[2]=config.FogHeight;values[3]=config.FogDensity;
                values[4]=config.FogRed;values[5]=config.FogGreen;values[6]=config.FogBlue;values[7]=config.FogForwardScattering;
                values[8]=config.FogNoiseScale;values[9]=config.FogVariation;
                values[11]=(float)MySession.Static.ElapsedGameTime.TotalSeconds;
                values[12]=config.FogWind*.8f;values[14]=config.FogWind*.6f;values[15]=config.FogShaftContrast;
                values[16]=config.FogBaseHeight;values[17]=config.FogDistance;
                AnomalyBridge.Call(host,"SetParameters",Id,values,new[]{center.X-top,center.Y-top,center.Z-top,center.X+top,center.Y+top,center.Z+top},new[]{values[12],0,values[14]});
                AnomalyBridge.Call(host,"Configure",(int)config.AtmosphereQuality,config.FogDistance,(int)config.AtmosphereDebugView);
            }
            Status=!config.AtmosphereEnabled?"Disabled (acceptance build)":!active?"Outside an eligible atmosphere":(string)host.GetProperty("StatusLine").GetValue(null);
            if(page!=null && (nextRefresh==0 || unchecked(Environment.TickCount-nextRefresh)>=0)) { Page("Refresh");nextRefresh=unchecked(Environment.TickCount+1000); }
        }
        catch(Exception e) { Status="Atmosphere: "+(e.InnerException??e).Message; }
    }
    static object Page(string method,params object[] args)
    {
        foreach(var candidate in page.GetType().GetInterface("ClientPlugin.RichHud.ITerminalConfigPage").GetMethods())
            if(candidate.Name==method && !candidate.IsGenericMethod && candidate.GetParameters().Length==args.Length) return candidate.Invoke(page,args);
        throw new MissingMethodException(method);
    }
    static void Slider(string label,float min,float max,Func<float> get,Action<float> set,string description)
        =>Page("Slider",label,min,max,get,set,description);
    static void CreatePage()
    {
        terminal ??= AnomalyBridge.Find("ClientPlugin.RichHud.TerminalConfigRegistry");
        if(terminal==null) return;
        page=AnomalyBridge.Call(terminal,"RequestFolderPage","Final Frontier","Atmosphere");
        if(page==null) return;
        var c=Config.Current;
        Page("Category","Atmospheric fog and godrays");
        Page("Checkbox","Enabled",(Func<bool>)(()=>c.AtmosphereEnabled),(Action<bool>)(v=>c.AtmosphereEnabled=v),"Opt-in acceptance build. Adds fog while retaining the world's atmosphere.");
        Page("Dropdown","Look",typeof(AtmosphereStyle),(Func<object>)(()=>c.AtmospherePreset),(Action<object>)(v=>c.ApplyAtmospherePreset((AtmosphereStyle)v)),"Clear, Dramatic, or Heavy Fantasy.");
        Page("Dropdown","Quality",typeof(AtmosphereQuality),(Func<object>)(()=>c.AtmosphereQuality),(Action<object>)(v=>c.AtmosphereQuality=(AtmosphereQuality)v),"Changes volume sampling; geometry shadow accuracy is retained.");
        Slider("Density",0,2,()=>c.FogDensity*1000,v=>c.FogDensity=v/1000,"Extinction per kilometre. Zero permits shared Clouds-only testing.");
        Slider("Height falloff (m)",20,3000,()=>c.FogHeight,v=>c.FogHeight=v,"Height over the planet reference surface.");
        Slider("Base height (m)",-3000,3000,()=>c.FogBaseHeight,v=>c.FogBaseHeight=v,"Raises or lowers the haze layer.");
        Slider("Variation",0,1,()=>c.FogVariation,v=>c.FogVariation=v,"Rolling world-anchored fog.");
        Slider("Noise scale (m)",50,3000,()=>c.FogNoiseScale,v=>c.FogNoiseScale=v,"Size of the rolling haze patches.");
        Slider("Forward scattering",0,.9f,()=>c.FogForwardScattering,v=>c.FogForwardScattering=v,"Concentrates scattering toward the sun.");
        Slider("Shaft contrast",.25f,1.5f,()=>c.FogShaftContrast,v=>c.FogShaftContrast=v,"Narrows the light-scattering lobe; blocked light stays blocked.");
        Slider("Wind (m/s)",0,100,()=>c.FogWind,v=>c.FogWind=v,"Fog drift speed.");
        Slider("Distance (m)",100,8000,()=>c.FogDistance,v=>c.FogDistance=v,"Shared near-volume distance; farther clouds retain their renderer.");
        Page("Color","Tint",(Func<Color>)(()=>new Color(c.FogRed,c.FogGreen,c.FogBlue)),(Action<Color>)(v=>{var rgb=v.ToVector3();c.FogRed=rgb.X;c.FogGreen=rgb.Y;c.FogBlue=rgb.Z;}),"Scattering colour.");
        Page("Category","Diagnostics");
        Page("Dropdown","Debug view",typeof(AtmosphereDebug),(Func<object>)(()=>c.AtmosphereDebugView),(Action<object>)(v=>c.AtmosphereDebugView=(AtmosphereDebug)v),"Inspect density, blockers, cloud light, rooms, cascade coverage and rejected history.");
        Page("Label","Status",(Func<string>)(()=>Status));
    }
    internal static void Dispose()
    {
        if(host!=null) { AnomalyBridge.Call(host,"RequestRenderer",Id,false);if(registered) AnomalyBridge.Call(host,"Unregister",Id); }
        if(terminal!=null) AnomalyBridge.Call(terminal,"UnregisterPage","Final Frontier/Atmosphere");
        registered=false;page=null;host=terminal=null;
    }
}
