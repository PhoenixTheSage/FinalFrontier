using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using Sandbox.Game.Entities;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using VRage.Game.Entity;
using VRageMath;

namespace ClientPlugin.Map;

/// <summary>Game-thread entity registry and snapshot producer. HUD delegates enqueue value commands only.</summary>
internal static class MapRuntime
{
    sealed class Tracked
    {
        public MyEntity Entity;
        public MatrixD Transform;
        public BoundingBoxD Bounds;
        public double Radius;
        public int NextSample;
        public bool Sampled;
    }
    readonly struct Command
    {
        public readonly int Kind;
        public readonly float X, Y, Z;
        public readonly long Id;
        public Command(int kind, float x = 0, float y = 0, float z = 0, long id = 0) { Kind = kind; X = x; Y = y; Z = z; Id = id; }
    }
    static readonly ConcurrentQueue<Command> Commands = new();
    static readonly ConcurrentQueue<MyEntity> Added = new(), Removed = new();
    static readonly ConcurrentQueue<Action> Settings = new();
    static readonly MapIndex<Tracked> Index = new();
    static readonly List<Tracked> TrackedObjects = Index.Values;
    static readonly List<MapObject> Objects = new();
    static readonly MapCamera Camera = new();
    static readonly MapRenderer Renderer = new();
    static MapBridge bridge;
    static MySession session;
    static int sampleCursor, nextSnapshot, nextBridge;
    static bool open, adminReveal, dirty = true, rowsDirty = true;
    static volatile bool adminAllowed, privacyDirty;
    static long selected, identity;
    static double range;
    static Vector3D player;
    static string error;
    static readonly Stopwatch SamplingClock = new(), FrameClock = new();
    internal static string Status { get; private set; } = "Map: waiting for a world and Rich HUD Master";
    internal static void RequestOpen() => Commands.Enqueue(new Command(10));
    internal static void RequestSettings() => Settings.Enqueue(() => { bridge?.SetVisible(false); Plugin.Instance?.OpenConfigDialog(); });
    internal static void Changed() { dirty = privacyDirty = true; }
    static void Add(MyEntity entity) => Added.Enqueue(entity);
    static void Remove(MyEntity entity) => Removed.Enqueue(entity);
    static void Ownership(MyCubeGrid grid) { privacyDirty = true; }
    static void FactionChanged(VRage.Game.ModAPI.MyFactionStateChange action, long from, long to, long id, long sender) { privacyDirty = true; }
    static long OwnerFaction(long owner) => session.Factions.TryGetPlayerFaction(owner)?.FactionId ?? 0;

    internal static void Update()
    {
        try { UpdateCore(); }
        catch (Exception e)
        {
            string message = (e.InnerException ?? e).Message;
            if (message != error) VRage.Utils.MyLog.Default?.WriteLine("Final Frontier map: " + e);
            error = message; Status = "Map unavailable: " + message; open = adminReveal = false;
            try { bridge?.SetVisible(false); } catch { }
        }
    }
    static void UpdateCore()
    {
        int now = Environment.TickCount;
        if (!ReferenceEquals(session, MySession.Static))
        {
            ResetSession(); session = MySession.Static;
            if (session != null)
            {
                MyEntities.OnEntityAdd += Add; MyEntities.OnEntityRemove += Remove;
                session.Factions.FactionStateChanged += FactionChanged;
                foreach (var entity in MyEntities.GetEntities()) Added.Enqueue(entity);
            }
        }
        MapBridge.UpdateSettings();
        if (bridge == null && (nextBridge == 0 || unchecked(now - nextBridge) >= 0))
        {
            nextBridge = unchecked(now + 2000);
            bridge = MapBridge.TryCreate();
            if (bridge != null) ConfigureBridge();
        }
        var config = Config.Current;
        var binding = config.MapToggle;
        bridge?.SetBinding((int)binding.Key, binding.Ctrl, binding.Alt, binding.Shift);
        while (Settings.TryDequeue(out var setting)) setting();
        while (Commands.TryDequeue(out var command)) Handle(command);
        if (session?.LocalHumanPlayer == null || bridge == null || !bridge.Available())
        {
            if (open) { open = adminReveal = false; bridge?.SetVisible(false); }
            Status = session == null ? "Map: waiting for a world" : bridge == null ? "Map unavailable: update Anomaly (interactive overlay API v1)" : "Map unavailable: Rich HUD Master is not registered";
            return;
        }
        bool allowed = session.IsUserAdmin(session.LocalHumanPlayer.Id.SteamId);
        if (allowed != adminAllowed) { adminAllowed = allowed; privacyDirty = true; bridge.Refresh(); }
        if (!allowed && adminReveal) { adminReveal = false; privacyDirty = true; }
        long currentIdentity = session.LocalPlayerId;
        if (identity != currentIdentity) { identity = currentIdentity; privacyDirty = true; }
        player = session.LocalHumanPlayer.GetPosition();
        range = Math.Max(1, session.Settings.ViewDistance * config.MapRangeFraction);
        DrainEntities();
        if (!open) { Status = "Map ready · " + binding; return; }
        Sample(now);
        if (privacyDirty || unchecked(now - nextSnapshot) >= 0)
        {
            privacyDirty = false; nextSnapshot = unchecked(now + 200);
            Snapshot(); dirty = true;
        }
        if (dirty)
        {
            FrameClock.Restart();
            Renderer.Build(Objects, Camera, config, selected);
            bridge.SubmitGeometry(Renderer.Lines, Renderer.LineCount, Renderer.Triangles, Renderer.TriangleCount, Renderer.Labels, Renderer.LabelPositions);
            FrameClock.Stop();
            Status = (adminReveal ? "All client-known objects" : "Range " + (range / 1000).ToString("0.0") + " km") + " · " + Objects.Count + " objects" +
                (adminReveal ? "" : " · planets: all client-known") + "\n" +
                Renderer.LineCount + " lines / " + Renderer.TriangleCount + " faces · CPU " + FrameClock.Elapsed.TotalMilliseconds.ToString("0.00") + " ms\n" +
                "RMB orbit · MMB pan · wheel zoom";
            if (rowsDirty) { PublishRows(); rowsDirty = false; }
            dirty = false;
        }
    }
    static void ConfigureBridge()
    {
        bridge.Handlers((kind, x, y, z) => Commands.Enqueue(new Command(kind, x, y, z)),
            (w, h) => Commands.Enqueue(new Command(4, w, h)), id => Commands.Enqueue(new Command(5, id: id)),
            visible => Commands.Enqueue(new Command(6, visible ? 1 : 0)));
        bridge.Button("Center on player", () => Commands.Enqueue(new Command(7)));
        bridge.Button("Focus selection", () => Commands.Enqueue(new Command(8)));
        bridge.Button("Reset view", () => Commands.Enqueue(new Command(9)));
        bridge.Checkbox("Admin reveal", () => adminReveal, value => Settings.Enqueue(() => { adminReveal = MapPolicy.Reveal(adminAllowed, value); privacyDirty = true; }), () => adminAllowed);
        var c = Config.Current;
        bridge.Checkbox("Grids", () => c.MapGrids, value => Settings.Enqueue(() => c.MapGrids = value), () => true);
        bridge.Checkbox("Planets / moons", () => c.MapPlanets, value => Settings.Enqueue(() => c.MapPlanets = value), () => true);
        bridge.Checkbox("Asteroids / voxels", () => c.MapVoxels, value => Settings.Enqueue(() => c.MapVoxels = value), () => true);
        bridge.Checkbox("Wireframe only", () => c.MapWireframeOnly, value => Settings.Enqueue(() => c.MapWireframeOnly = value), () => true);
        bridge.Slider("Range fraction", .02f, 1, () => c.MapRangeFraction, value => Settings.Enqueue(() => c.MapRangeFraction = value));
        bridge.Button("Palette / keybind settings", RequestSettings);
    }
    static void Handle(Command command)
    {
        switch (command.Kind)
        {
            case 0: selected = Renderer.PickObject(command.X, command.Y); break;
            case 1: Camera.Orbit(command.X, command.Y); break;
            case 2: Camera.Pan(command.X, command.Y); break;
            case 3: Camera.Zoom(command.Z); break;
            case 4: Camera.Width = Math.Max(1, command.X); Camera.Height = Math.Max(1, command.Y); break;
            case 5: selected = command.Id; break;
            case 6:
                open = command.X != 0;
                if (open) { Camera.Reset(player, range); nextSnapshot = 0; privacyDirty = true; }
                else { adminReveal = false; selected = 0; bridge?.Refresh(); }
                break;
            case 7: Camera.Target = player; break;
            case 8:
                for (int i = 0; i < Objects.Count; i++) if (Objects[i].Id == selected)
                { var obj = Objects[i]; Camera.Target = obj.Center; Camera.Distance = Math.Max(10, (obj.Kind == MapKind.Planet ? obj.Radius : obj.Bounds.HalfExtents.Length()) * 4); break; }
                break;
            case 9: Camera.Reset(player, range); break;
            case 10:
                if (bridge != null && bridge.Available()) { Plugin.Instance?.CloseConfigDialog(); bridge.SetVisible(true); }
                else MyAPIGateway.Utilities?.ShowNotification(Status, 6000);
                break;
        }
        if (command.Kind == 0 || command.Kind == 5 || command.Kind == 6) rowsDirty = true;
        dirty = true;
    }
    static void DrainEntities()
    {
        while (Removed.TryDequeue(out var removed))
            if (Index.TryGetValue(removed.EntityId, out var tracked))
            {
                if (tracked.Entity is MyCubeGrid grid) grid.OnBlockOwnershipChanged -= Ownership;
                Index.Remove(removed.EntityId); privacyDirty = true;
            }
        while (Added.TryDequeue(out var entity))
        {
            if (entity is MyVoxelBase voxel) entity = voxel.RootVoxel;
            if (entity == null || entity.Closed || entity.MarkedForClose || entity.IsPreview || !(entity is MyCubeGrid || entity is MyVoxelBase) || Index.ContainsKey(entity.EntityId)) continue;
            var tracked = new Tracked { Entity = entity };
            Index.Add(entity.EntityId, tracked);
            if (entity is MyCubeGrid grid) grid.OnBlockOwnershipChanged += Ownership;
            privacyDirty = true;
        }
    }
    static void Sample(int now)
    {
        SamplingClock.Restart();
        int count = Math.Min(1024, TrackedObjects.Count);
        for (int i = 0; i < count && SamplingClock.Elapsed.TotalMilliseconds < .75; i++)
        {
            if (sampleCursor >= TrackedObjects.Count) sampleCursor = 0;
            var tracked = TrackedObjects[sampleCursor++]; var entity = tracked.Entity;
            if (entity.Closed || entity.MarkedForClose) continue;
            if (tracked.Sampled && unchecked(now - tracked.NextSample) < 0) continue;
            tracked.Transform = entity.WorldMatrix;
            var bounds = entity.PositionComp.LocalAABB;
            tracked.Bounds = new BoundingBoxD(bounds.Min, bounds.Max);
            tracked.Radius = entity is MyPlanet planet ? planet.MaximumRadius : 0;
            tracked.Sampled = true;
            tracked.NextSample = unchecked(now + (entity is MyCubeGrid grid && !grid.IsStatic ? 200 : 1000));
            dirty = true;
        }
    }
    static void Snapshot()
    {
        rowsDirty = true;
        Objects.Clear();
        long faction = OwnerFaction(identity); var c = Config.Current;
        bool reveal = MapPolicy.Reveal(adminAllowed, adminReveal);
        for (int i = 0; i < TrackedObjects.Count; i++)
        {
            var tracked = TrackedObjects[i]; var entity = tracked.Entity;
            if (!tracked.Sampled || entity.Closed || entity.MarkedForClose || entity.IsPreview) continue;
            MapKind kind = entity is MyPlanet ? MapKind.Planet : entity is MyCubeGrid ? MapKind.Grid :
                entity is MyVoxelMap voxel && !voxel.BoulderInfo.HasValue ? MapKind.Asteroid : MapKind.Voxel;
            if (kind == MapKind.Grid && !c.MapGrids || kind == MapKind.Planet && !c.MapPlanets ||
                (kind == MapKind.Voxel || kind == MapKind.Asteroid) && !c.MapVoxels) continue;
            double distance = MapPolicy.SurfaceDistance(player, tracked.Transform, tracked.Bounds, tracked.Radius);
            if (!MapPolicy.IsVisible(kind, distance, range, reveal)) continue;
            var relation = entity is MyCubeGrid grid ? MapPolicy.Classify(grid.BigOwners, identity, faction, OwnerFaction) : MapRelation.Contact;
            Objects.Add(new MapObject(entity.EntityId, kind, relation, kind == MapKind.Grid && relation == MapRelation.Contact && !reveal ? null : entity.DisplayName,
                tracked.Transform, tracked.Bounds, tracked.Radius, distance, reveal));
        }
        Objects.Sort((a, b) => { int compare = a.Distance.CompareTo(b.Distance); return compare == 0 ? a.Id.CompareTo(b.Id) : compare; });
        if (!Objects.Exists(obj => obj.Id == selected)) selected = 0;
        bridge.Refresh();
    }
    static void PublishRows()
    {
        var ids = new long[Objects.Count]; var rows = new string[Objects.Count];
        string details = "Select an object\nNames of unknown grids are hidden.";
        for (int i = 0; i < Objects.Count; i++)
        {
            var obj = Objects[i]; ids[i] = obj.Id; rows[i] = obj.Label + " · " + (obj.Distance / 1000).ToString("0.0") + " km";
            if (obj.Id == selected) details = obj.Details;
        }
        bridge.SubmitRows(ids, rows, details, Status);
    }
    static void ResetSession()
    {
        bridge?.SetVisible(false);
        if (session != null) { MyEntities.OnEntityAdd -= Add; MyEntities.OnEntityRemove -= Remove; session.Factions.FactionStateChanged -= FactionChanged; }
        foreach (var tracked in TrackedObjects) if (tracked.Entity is MyCubeGrid grid) grid.OnBlockOwnershipChanged -= Ownership;
        Index.Clear(); TrackedObjects.Clear(); Objects.Clear();
        while (Added.TryDequeue(out _)) { } while (Removed.TryDequeue(out _)) { }
        while (Commands.TryDequeue(out _)) { } while (Settings.TryDequeue(out _)) { }
        open = adminReveal = adminAllowed = false; selected = identity = 0; sampleCursor = 0;
        nextSnapshot = 0; dirty = privacyDirty = rowsDirty = true;
        bridge?.SubmitGeometry(Array.Empty<float>(), 0, Array.Empty<float>(), 0, Array.Empty<string>(), Array.Empty<float>());
        bridge?.SubmitRows(Array.Empty<long>(), Array.Empty<string>(), "", "");
    }
    internal static void Dispose() { ResetSession(); session = null; bridge?.Dispose(); bridge = null; MapBridge.DisposeSettings(); nextBridge = 0; }
}
