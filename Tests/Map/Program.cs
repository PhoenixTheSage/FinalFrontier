using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Serialization;
using ClientPlugin;
using ClientPlugin.Map;
using VRageMath;

static class Program
{
    static int checks;
    static void Check(string name, bool passed)
    { if (!passed) throw new Exception("FAIL: " + name); checks++; }
    static int Main(string[] args)
    {
        string scratch = Path.Combine(Path.GetTempPath(), "FinalFrontier-map-" + Guid.NewGuid().ToString("N"));
        try
        {
            string game = Path.GetFullPath(args[0]);
            AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
            {
                string path = Path.Combine(game, new AssemblyName(e.Name).Name + ".dll");
                return File.Exists(path) ? Assembly.LoadFrom(path) : null;
            };
            Directory.CreateDirectory(scratch);
            InitFiles(scratch);
            Run(args[1], scratch);
            Console.WriteLine("PASS: " + checks + " map checks (policy, precision, clipping, picking, budgets, XML, lifecycle, reflection contract).");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        finally { if (Directory.Exists(scratch)) Directory.Delete(scratch, true); }
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static void InitFiles(string scratch) => VRage.FileSystem.MyFileSystem.Init(scratch, scratch, "Mods", null, null);
    [MethodImpl(MethodImplOptions.NoInlining)]
    static void Run(string anomalyPath, string scratch)
    {
        Policy(); CameraChecks(); IndexChecks();
        var config = new Config();
        ConfigChecks(config, scratch);
        Rendering(config);
        Contract(anomalyPath);
        Benchmark(config);
    }
    static void Policy()
    {
        var owners = new List<long> { 0, 11, 12 };
        Check("own major owner takes precedence", MapPolicy.Classify(owners, 12, 4, _ => 4) == MapRelation.Owned);
        Check("same faction major owner", MapPolicy.Classify(owners, 20, 4, id => id == 11 ? 4 : 5) == MapRelation.Faction);
        Check("unowned is anonymous", MapPolicy.Classify(new List<long>(), 20, 0, _ => 0) == MapRelation.Contact);
        Check("other factions anonymous", MapPolicy.Classify(owners, 20, 6, _ => 4) == MapRelation.Contact);
        foreach (bool allowed in new[] { false, true }) foreach (bool requested in new[] { false, true })
            Check("admin requires both permission and explicit request", MapPolicy.Reveal(allowed, requested) == (allowed && requested));
        Check("boundary inclusive", MapPolicy.IsVisible(MapKind.Grid, 100, 100, false));
        foreach (var kind in new[] { MapKind.Grid, MapKind.Asteroid, MapKind.Voxel })
        {
            Check(kind + " outside excluded", !MapPolicy.IsVisible(kind, 100.01, 100, false));
            Check(kind + " admin bypasses range", MapPolicy.IsVisible(kind, 1e12, 100, true));
        }
        Check("distant planet visible without admin reveal", MapPolicy.IsVisible(MapKind.Planet, 1e12, 100, false));
        var bounds = new BoundingBoxD(new Vector3D(-10), new Vector3D(10));
        var matrix = MatrixD.CreateRotationY(.5); matrix.Translation = new Vector3D(1e12, 2e12, -3e12);
        var surface = Vector3D.Transform(new Vector3D(13, 0, 0), matrix);
        Check("rotated bounds surface distance", Math.Abs(MapPolicy.SurfaceDistance(surface, matrix, bounds, 0) - 3) < .002);
        Check("inside bounds distance zero", MapPolicy.SurfaceDistance(matrix.Translation, matrix, bounds, 0) < .001);
        Check("planet nearest surface instead of center", MapPolicy.SurfaceDistance(new Vector3D(61000, 0, 0), MatrixD.Identity, bounds, 60000) == 1000);
        var contact = new MapObject(123, MapKind.Grid, MapRelation.Contact, "SecretGrid - SecretFaction - SecretOwner", MatrixD.Identity, bounds, 0, 20, false);
        Check("redaction before labels and details", contact.Label == "Unknown grid contact" && !contact.Details.Contains("Secret"));
        Check("admin name permitted", new MapObject(123, MapKind.Grid, MapRelation.Contact, "SecretGrid", MatrixD.Identity, bounds, 0, 20, true).Label == "SecretGrid");
        Check("faction name permitted", new MapObject(123, MapKind.Grid, MapRelation.Faction, "FactionGrid", MatrixD.Identity, bounds, 0, 20, false).Label == "FactionGrid");
        Check("planet name public without admin", new MapObject(124, MapKind.Planet, MapRelation.Contact, "Earthlike", MatrixD.Identity, bounds, 60000, 1e9, false).Label == "Earthlike");
        Check("unnamed asteroid classified in details", new MapObject(125, MapKind.Asteroid, MapRelation.Contact, null, MatrixD.Identity, bounds, 0, 20, false).Details.StartsWith("Asteroid\nAsteroid"));
    }
    static void CameraChecks()
    {
        var camera = new MapCamera { Width = 800, Height = 600 };
        camera.Reset(new Vector3D(1e12, 2e12, -3e12), 10000);
        Check("large coordinate target projects to center", camera.ProjectPoint(camera.Target, out var center) && center.LengthSquared() < 1e-8);
        var oneMeter = camera.View(camera.Target + camera.Right);
        Check("large coordinate one-meter precision", Math.Abs(oneMeter.X - 1) < .001);
        var a = new Vector3D(-1, 0, -1); var b = new Vector3D(1, 0, 10);
        Check("near crossing line clipped", camera.ClipLine(ref a, ref b, out var x, out var y) && Math.Abs(x.X) <= 400 && Math.Abs(y.X) <= 400);
        a = new Vector3D(0, 0, -10); b = new Vector3D(1, 0, -5);
        Check("behind camera line rejected", !camera.ClipLine(ref a, ref b, out _, out _));
        var p = new Vector2(-1000, 0); var q = new Vector2(1000, 0);
        Check("viewport clipping", MapCamera.Clip2D(ref p, ref q, 400, 300) && p.X == -400 && q.X == 400);
        p = new Vector2(-1000, 500); q = new Vector2(1000, 500);
        Check("outside parallel line rejected", !MapCamera.Clip2D(ref p, ref q, 400, 300));
        for (int i = 0; i < 1000; i++) camera.Zoom(20);
        Check("zoom lower limit", camera.Distance == 1);
        for (int i = 0; i < 1000; i++) camera.Zoom(-20);
        Check("zoom upper limit", camera.Distance == 1e10);
        camera.Orbit(10000, 10000); Check("orbit pitch bounded", camera.Pitch == 1.5);
        var target = camera.Target; camera.Pan(10, 0); Check("pan changes camera only", camera.Target != target);
    }
    static void IndexChecks()
    {
        var index = new MapIndex<object>(); var root = new object();
        Check("root inserted", index.Add(100, root));
        Check("root / voxel chunks deduplicated by canonical id", !index.Add(100, new object()) && index.Count == 1);
        Check("child removal retains root", !index.Remove(101) && index.Count == 1);
        Check("root removal clears index and iteration", index.Remove(100) && index.Count == 0 && index.Values.Count == 0);
        index.Add(200, new object()); index.Clear(); Check("world unload clears references", index.Count == 0 && index.Values.Count == 0);
    }
    static void ConfigChecks(Config config, string scratch)
    {
        Check("default chord", config.MapToggle.Key == VRage.Input.MyKeys.F2 && config.MapToggle.Ctrl && config.MapToggle.Shift && !config.MapToggle.Alt);
        config.MapRangeFraction = float.NaN; Check("invalid range defaults", config.MapRangeFraction == 1);
        config.MapRangeFraction = 2; Check("range capped", config.MapRangeFraction == 1);
        config.MapRangeFraction = -1; Check("range positive", config.MapRangeFraction == .02f);
        config.MapRangeFraction = .5f;
        config.MapOwnedFace = new Color(1, 2, 3, 4);
        config.MapToggle = new ClientPlugin.Settings.Tools.Binding(VRage.Input.MyKeys.None);
        var serializer = new XmlSerializer(typeof(Config)); string path = Path.Combine(scratch, "MapRoundTrip.xml");
        using (var file = File.Create(path)) serializer.Serialize(file, config);
        Config restored; using (var file = File.OpenRead(path)) restored = (Config)serializer.Deserialize(file);
        Check("RGBA survives XML", restored.MapOwnedFace == config.MapOwnedFace);
        Check("unbind survives XML", restored.MapToggle.Key == VRage.Input.MyKeys.None);
        Check("range survives XML", restored.MapRangeFraction == .5f);
        config.MapOwnedFace = new Color(85, 220, 255, 28);
    }
    static MapObject Object(long id, Vector3D center, double size = 100, MapKind kind = MapKind.Grid)
    {
        var transform = MatrixD.CreateTranslation(center);
        return new MapObject(id, kind, MapRelation.Contact, "SensitiveName", transform, new BoundingBoxD(new Vector3D(-size), new Vector3D(size)), kind == MapKind.Planet ? size : 0, center.Length(), false);
    }
    static void Rendering(Config config)
    {
        var camera = new MapCamera(); camera.Reset(Vector3D.Zero, 1000);
        var renderer = new MapRenderer(); var objects = new List<MapObject> { Object(1, Vector3D.Zero) };
        renderer.Build(objects, camera, config, 1);
        Check("box rendered", renderer.LineCount == 12 && renderer.TriangleCount == 12);
        Check("selection picking", renderer.PickObject(0, 0) == 1);
        Check("selected anonymous label redacted", renderer.Labels.Length == 1 && !renderer.Labels[0].Contains("Sensitive"));
        config.MapWireframeOnly = true; renderer.Build(objects, camera, config, 1);
        Check("wireframe switch", renderer.TriangleCount == 0); config.MapWireframeOnly = false;
        objects[0] = Object(2, Vector3D.Zero, 100, MapKind.Planet); renderer.Build(objects, camera, config, 2);
        Check("cached low poly sphere", renderer.LineCount == 30 && renderer.TriangleCount == 20);
        objects[0] = Object(5, Vector3D.Zero, 100, MapKind.Asteroid); renderer.Build(objects, camera, config, 0);
        Check("asteroid diamond topology", renderer.LineCount == 12 && renderer.TriangleCount == 8);
        Check("diamond picking", renderer.PickObject(0, 0) == 5);
        Check("asteroid uses voxel line palette", Math.Abs(renderer.Lines[4] - config.MapVoxelLine.R / 255f) < 1e-6);
        var orientation = MatrixD.CreateRotationY(.7);
        var offsetBounds = new BoundingBoxD(new Vector3D(100, 200, 300), new Vector3D(400, 500, 600));
        orientation.Translation = -Vector3D.TransformNormal(offsetBounds.Center, orientation);
        objects[0] = new MapObject(6, MapKind.Asteroid, MapRelation.Contact, null, orientation, offsetBounds, 0, 0, false);
        renderer.Build(objects, camera, config, 6);
        Check("oriented offset diamond centered and finite", renderer.PickObject(0, 0) == 6 && Finite(renderer.Lines, renderer.LineCount * 8));
        config.MapWireframeOnly = true; renderer.Build(objects, camera, config, 6);
        Check("diamond wireframe-only switch", renderer.LineCount == 12 && renderer.TriangleCount == 0);
        config.MapWireframeOnly = false;
        objects[0] = Object(7, Vector3D.Zero, 100, MapKind.Voxel); renderer.Build(objects, camera, config, 7);
        Check("generic voxel retains box topology", renderer.LineCount == 12 && renderer.TriangleCount == 12);
        objects[0] = Object(3, camera.Target + camera.Forward * -4000); renderer.Build(objects, camera, config, 0);
        Check("behind camera not rendered", renderer.LineCount == 0 && renderer.TriangleCount == 0);
        objects[0] = Object(4, Vector3D.Zero, 5000); renderer.Build(objects, camera, config, 4);
        Check("near-plane intersecting large proxy stays finite", Finite(renderer.Triangles, renderer.TriangleCount * 10) && renderer.TriangleCount > 0);
        var crowded = Crowd(); renderer.Build(crowded, camera, config, 4999);
        Check("primitive caps", renderer.LineCount <= MapRenderer.MaxLines && renderer.TriangleCount <= MapRenderer.MaxTriangles);
        Check("marker aggregation", renderer.ClusteredObjects > 0);
        Check("render LOD preserves input list", crowded.Count == 5000);
        Check("all geometry finite", Finite(renderer.Lines, renderer.LineCount * 8) && Finite(renderer.Triangles, renderer.TriangleCount * 10));
        int lineCount = renderer.LineCount, triCount = renderer.TriangleCount;
        var before = (float[])renderer.Lines.Clone(); renderer.Build(crowded, camera, config, 4999);
        bool same = renderer.LineCount == lineCount && renderer.TriangleCount == triCount;
        for (int i = 0; i < lineCount * 8; i++) same &= before[i] == renderer.Lines[i];
        Check("deterministic budget degradation", same);
    }
    static bool Finite(float[] values, int count)
    { for (int i = 0; i < count; i++) if (float.IsNaN(values[i]) || float.IsInfinity(values[i])) return false; return true; }
    static List<MapObject> Crowd()
    {
        var random = new Random(42); var objects = new List<MapObject>(5000);
        for (int i = 1; i <= 5000; i++) objects.Add(Object(i, new Vector3D(random.NextDouble() * 16000 - 8000, random.NextDouble() * 8000 - 4000, random.NextDouble() * 16000 - 8000), 5 + random.NextDouble() * 150));
        objects.Sort((a, b) => a.Distance.CompareTo(b.Distance)); return objects;
    }
    static void Contract(string path)
    {
        var assembly = Assembly.LoadFrom(path);
        var registry = assembly.GetType("ClientPlugin.RichHud.InteractiveOverlayRegistry", true);
        var inputPolicy = assembly.GetType("ClientPlugin.RichHud.InteractiveOverlayInputPolicy", true).GetMethod("CanUse", BindingFlags.Static | BindingFlags.NonPublic);
        bool CanUse(bool ready, bool gameplay, bool ownGuard, bool chat, bool terminal, bool full, bool visible) =>
            (bool)inputPolicy.Invoke(null, new object[] { ready, gameplay, ownGuard, chat, terminal, full, visible });
        Check("chord allowed in gameplay", CanUse(true, true, false, false, false, false, false));
        Check("own full capture stays open", CanUse(true, false, true, false, false, true, true));
        Check("chat blocks map", !CanUse(true, true, false, true, false, true, false));
        Check("terminal blocks map", !CanUse(true, true, false, false, true, true, false));
        Check("another modal blocks map", !CanUse(true, false, false, false, false, false, true));
        Check("loading blocks map", !CanUse(false, true, false, false, false, false, false));
        Check("other text entry blocks opening", !CanUse(true, true, false, false, false, true, false));
        Check("API v1", (int)registry.GetField("ApiVersion").GetRawConstantValue() == 1);
        var bridge = MapBridge.TryCreate(); Check("reflection bridge bound", bridge != null);
        Check("absent Master fails closed", !bridge.Available() && !bridge.Visible());
        bridge.SetBinding((int)VRage.Input.MyKeys.None, false, false, false);
        bridge.Handlers((_, _, _, _) => { }, (_, _) => { }, _ => { }, _ => { });
        bridge.Button("Test button", () => { }); bridge.Checkbox("Test checkbox", () => false, _ => { }, () => true);
        bridge.Slider("Test slider", 0, 1, () => .5f, _ => { }); bridge.Refresh();
        float[] line = { 0,0, 10,10, 1,1,1,1 }; float[] triangle = { 0,0, 10,0, 0,10, 1,1,1,.2f };
        bridge.SubmitGeometry(line, 1, triangle, 1, new[] { "safe" }, new float[] { 0, 0 });
        bridge.SubmitRows(new long[] { 1 }, new[] { "Unknown grid contact" }, "redacted", "test");
        var window = registry.GetMethod("RequestWindow").Invoke(null, new object[] { "FinalFrontier.Map", "same id" });
        var type = window.GetType();
        Check("geometry buffers copied", ((float[])type.GetField("Lines", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window))[2] == 10);
        line[2] = 999;
        Check("caller reuse cannot corrupt snapshot", ((float[])type.GetField("Lines", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window))[2] == 10);
        bool rejected = false;
        try { bridge.SubmitGeometry(line, 4097, triangle, 1, Array.Empty<string>(), Array.Empty<float>()); }
        catch (ArgumentException) { rejected = true; }
        Check("host enforces budgets", rejected);
        rejected = false; line[0] = float.NaN;
        try { bridge.SubmitGeometry(line, 1, triangle, 1, Array.Empty<string>(), Array.Empty<float>()); }
        catch (ArgumentException) { rejected = true; }
        Check("nonfinite geometry rejected", rejected);
#if NET10_0_OR_GREATER
        IsolatedContract(path);
#endif
        bridge.Dispose();
        Check("unregister clears rows", ((string[])type.GetField("Rows", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window)).Length == 0);
        MapRuntime.Update(); Check("no world diagnostic", MapRuntime.Status.Contains("waiting for a world"));
        MapRuntime.Dispose(); MapRuntime.Update(); MapRuntime.Dispose();
        Check("repeated lifecycle without world safe", !MapRuntime.Status.Contains("Map unavailable:"));
    }
#if NET10_0_OR_GREATER
    static void IsolatedContract(string path)
    {
        var context = new System.Runtime.Loader.AssemblyLoadContext("MapContractProbe", isCollectible: true);
        context.Resolving += (_, name) => Assembly.Load(name);
        var assembly = context.LoadFromAssemblyPath(Path.GetFullPath(path));
        var registry = assembly.GetType("ClientPlugin.RichHud.InteractiveOverlayRegistry", true);
        var window = registry.GetMethod("RequestWindow").Invoke(null, new object[] { "IsolatedMapTest", "Isolated" });
        var contract = window.GetType().GetInterface("ClientPlugin.RichHud.IInteractiveOverlayWindow");
        var submit = (Action<float[], int, float[], int, string[], float[]>)Delegate.CreateDelegate(typeof(Action<float[], int, float[], int, string[], float[]>), window, contract.GetMethod("SubmitGeometry"));
        submit(Array.Empty<float>(), 0, Array.Empty<float>(), 0, Array.Empty<string>(), Array.Empty<float>());
        var handlers = (Action<Action<int, float, float, float>, Action<float, float>, Action<long>, Action<bool>>)Delegate.CreateDelegate(
            typeof(Action<Action<int, float, float, float>, Action<float, float>, Action<long>, Action<bool>>), window, contract.GetMethod("SetHandlers"));
        handlers((_, _, _, _) => { }, (_, _) => { }, _ => { }, _ => { });
        Check("isolated loader arrays and callbacks accepted", System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(assembly) == context);
        registry.GetMethod("Unregister").Invoke(null, new object[] { "IsolatedMapTest" });
        context.Unload();
    }
#endif
    static void Benchmark(Config config)
    {
        var camera = new MapCamera { Width = 1200, Height = 800 }; camera.Reset(Vector3D.Zero, 10000);
        var renderer = new MapRenderer(); var objects = Crowd();
        // Give .NET 10's tiered JIT time to optimize the same continuously used path as gameplay.
        var warmup = Stopwatch.StartNew();
        while (warmup.Elapsed.TotalSeconds < 2) { camera.Orbit(1, 0); renderer.Build(objects, camera, config, 1); }
        var times = new double[200]; var watch = new Stopwatch();
        for (int i = 0; i < times.Length; i++)
        {
            camera.Orbit(1, 0); watch.Restart(); renderer.Build(objects, camera, config, 1); watch.Stop(); times[i] = watch.Elapsed.TotalMilliseconds;
        }
        Array.Sort(times);
        Console.WriteLine("BENCHMARK: 5000 snapshots / 200 rebuilds / 1200x800 viewport; p50=" + times[100].ToString("0.000") + " ms; p95=" + times[190].ToString("0.000") + " ms; p99=" + times[198].ToString("0.000") + " ms. CPU projection only; GPU and in-world timings pending.");
    }
}
