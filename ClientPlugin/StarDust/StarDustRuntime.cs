using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Sandbox.Game.Entities;
using Sandbox.Game.GameSystems;
using Sandbox.Game.World;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Utils;
using VRageMath;
using VRageRender;
using Particle = Sandbox.Game.World.MyEnvironmentalParticleLogic.MyEnvironmentalParticle;

namespace ClientPlugin.StarDust;

/// <summary>
/// Replaces only Keen's space-dust producer. Reuses its material and bounded
/// particle pool, and submits to the original depth-tested LDR billboard pass.
/// World positions stay double precision until Keen builds camera-relative vertices.
/// </summary>
internal static class StarDustRuntime
{
    const string PatchId = "FinalFrontier.StarDust";
    static Harmony harmony;
    static bool ready, faulted, attempted;
    static string detail = "Not initialized";
    static ConditionalWeakTable<MyEnvironmentalParticleLogic, State> states = new();
    static readonly List<WeakReference<State>> trackedStates = new();
    internal static int ActiveCount { get; private set; }
    internal static int PoolCapacity { get; private set; }
    internal static float Speed { get; private set; }
    internal static int SubmittedCount { get; private set; }
    internal static int DrawCalls { get; private set; }
    [ThreadStatic] static int drawStart;
    internal static string Status => !ready || faulted ? detail : Config.Current.StarDustMode == StarDustMode.Vanilla
        ? "Vanilla (Keen controls movement dust)" : Config.Current.StarDustMode == StarDustMode.Off
        ? "Off" : detail;

    struct Sample
    {
        internal Vector3D Position;
        internal double Born;
        internal float Scale;
    }

    sealed class State
    {
        internal readonly Dictionary<Particle, Sample> Samples = new(128);
        internal readonly Random Random = new(256);
        internal MySession Session;
        internal StarDustMode Mode;
        internal double LastTime = -1;
        internal float Debt, Speed;
        internal Vector3 Velocity;
        internal bool Planetary, Visible;
        internal List<Particle> Active, Idle;
    }

    internal static void Initialize()
    {
        if (ready || attempted) return;
        attempted = true;
        try
        {
            var type = AccessTools.TypeByName("SpaceEngineers.Game.World.Environment.MyEnvironmentalParticleLogicSpace");
            if (type == null || !typeof(MyEnvironmentalParticleLogic).IsAssignableFrom(type))
                throw new TypeLoadException("Keen's space dust logic was not found");
            foreach (string field in new[] { "m_activeParticles", "m_nonActiveParticles" })
                if (AccessTools.Field(type, field)?.FieldType != typeof(List<Particle>))
                    throw new MissingFieldException(type.FullName, field);
            if (AccessTools.Field(type, "m_particleDensity")?.FieldType != typeof(float))
                throw new MissingFieldException(type.FullName, "m_particleDensity");
            string[] methods = { "UpdateBeforeSimulation", "UpdateAfterSimulation", "Draw" };
            foreach (string name in methods)
            {
                var target = AccessTools.DeclaredMethod(type, name, Type.EmptyTypes);
                if (target == null || target.ReturnType != typeof(void)) throw new MissingMethodException(type.FullName, name);
            }
            harmony = new Harmony(PatchId);
            foreach (string name in methods)
                harmony.Patch(AccessTools.DeclaredMethod(type, name, Type.EmptyTypes),
                    prefix: new HarmonyMethod(typeof(StarDustRuntime), name + "Prefix"),
                    postfix: name == "Draw" ? new HarmonyMethod(typeof(StarDustRuntime), nameof(DrawPostfix)) : null);
            ready = true;
            detail = "Enhanced ready; waiting for Keen's movement-dust handler";
        }
        catch (Exception e)
        {
            harmony?.UnpatchAll(PatchId);
            detail = "Vanilla fallback: " + e.Message;
            MyLog.Default?.WriteLine("Final Frontier star dust: " + detail);
        }
    }

    static State GetState(MyEnvironmentalParticleLogic logic, List<Particle> active, List<Particle> idle)
    {
        var state = states.GetValue(logic, _ =>
        {
            var created = new State();
            // Weak references permit teardown without retaining handlers across worlds.
            trackedStates.RemoveAll(weak => !weak.TryGetTarget(out var unused));
            trackedStates.Add(new WeakReference<State>(created));
            return created;
        });
        state.Active = active;
        state.Idle = idle;
        var mode = Config.Current.StarDustMode;
        if (state.Mode != mode || state.Session != MySession.Static)
        {
            Clear(state, active, idle);
            state.Mode = mode;
            state.Session = MySession.Static;
            state.LastTime = -1;
        }
        return state;
    }

    static void Clear(State state, List<Particle> active, List<Particle> idle)
    {
        foreach (var particle in active) { particle.Deactivate(); idle.Add(particle); }
        active.Clear();
        state.Samples.Clear();
        state.Debt = 0;
        state.Visible = false;
        ActiveCount = 0;
        SubmittedCount = 0;
    }

    static void Fail(Exception error, MyEnvironmentalParticleLogic logic, List<Particle> active, List<Particle> idle)
    {
        if (states.TryGetValue(logic, out var state)) Clear(state, active, idle);
        faulted = true;
        detail = "Vanilla fallback: " + error.GetType().Name + ": " + error.Message;
        MyLog.Default?.WriteLine("Final Frontier star dust: " + detail);
    }

    static bool UpdateBeforeSimulationPrefix(MyEnvironmentalParticleLogic __instance,
        List<Particle> ___m_activeParticles, List<Particle> ___m_nonActiveParticles, float ___m_particleDensity)
    {
        if (!ready || faulted) return true;
        try
        {
            var active = ___m_activeParticles;
            var idle = ___m_nonActiveParticles;
            var state = GetState(__instance, active, idle);
            var c = Config.Current;
            if (c.StarDustMode == StarDustMode.Vanilla) return true;
            PoolCapacity = active.Count + idle.Count;
            if (c.StarDustMode == StarDustMode.Off) { Clear(state, active, idle); return false; }
            var session = MySession.Static;
            var camera = MySector.MainCamera;
            if (session == null || camera == null) { Clear(state, active, idle); detail = "Waiting for a world camera"; return false; }
            double now = session.ElapsedGameTime.TotalSeconds;
            float seconds = state.LastTime < 0 ? 1f / 60 : (float)Math.Max(0, Math.Min(.1, now - state.LastTime));
            if (now < state.LastTime) Clear(state, active, idle);
            state.LastTime = now;
            var entity = session.ControlledEntity as MyEntity;
            if (entity == null || session.IsCameraUserControlledSpectator())
            { Clear(state, active, idle); Speed = 0; detail = "No controlled body (spectator dust suppressed)"; return false; }
            if (entity is MyCockpit || entity is MyRemoteControl) entity = entity.GetTopMostParent();
            state.Velocity = entity.Physics?.LinearVelocity ?? Vector3.Zero;
            Speed = state.Speed = state.Velocity.Length();
            if (float.IsNaN(state.Speed) || float.IsInfinity(state.Speed))
            { Clear(state, active, idle); Speed = 0; detail = "Invalid controlled velocity"; return false; }
            if (state.Speed <= Math.Max(.001f, c.StarDustMinimumSpeed))
            { Clear(state, active, idle); detail = "Below minimum speed"; return false; }
            if (c.StarDustHideInsideGrids && InsideLargeGrid(camera.Position))
            { Clear(state, active, idle); detail = "Inside a large grid (location gate)"; return false; }
            state.Planetary = !Vector3.IsZero(MyGravityProviderSystem.CalculateNaturalGravityInPoint(entity.PositionComp.GetPosition()));
            if (state.Planetary && !c.StarDustNearPlanets)
            { Clear(state, active, idle); detail = "Natural gravity (near-planet dust disabled)"; return false; }

            Prune(state, active, idle, now, camera.Position, c);
            float fade = StarDustMath.SpeedFade(state.Speed, c.StarDustMinimumSpeed, c.StarDustSpeedFade);
            float nativeDensity = float.IsNaN(___m_particleDensity) || float.IsInfinity(___m_particleDensity) ? 0 : ___m_particleDensity;
            state.Debt += StarDustMath.SpawnCount(state.Speed, c.StarDustSpawnRadius, c.StarDustDensity, fade, seconds, c.StarDustMinimumSpeed, nativeDensity);
            int capacity = Math.Max(0, Math.Min(c.StarDustParticleLimit - active.Count, idle.Count));
            int count = (int)Math.Min(capacity, Math.Floor(state.Debt));
            state.Debt = capacity == 0 || state.Debt >= capacity ? 0 : state.Debt - count;
            var forward = Vector3.Normalize(state.Velocity);
            var right = Vector3.Cross(forward, camera.UpVector);
            if (right.LengthSquared() < .0001f) right = Vector3.CalculatePerpendicularVector(forward);
            else right.Normalize();
            var up = Vector3.Cross(right, forward);
            for (int i = 0; i < count; i++)
            {
                // Uniform solid-angle front hemisphere, excluding the central 6 degrees.
                double z = state.Random.NextDouble() * Math.Cos(Math.PI / 30);
                double angle = state.Random.NextDouble() * Math.PI * 2;
                double radial = Math.Sqrt(1 - z * z);
                var offset = (Vector3D)forward * z + (Vector3D)right * (radial * Math.Cos(angle)) + (Vector3D)up * (radial * Math.Sin(angle));
                Vector3D position = camera.Position + offset * c.StarDustSpawnRadius;
                var particle = idle[idle.Count - 1];
                idle.RemoveAt(idle.Count - 1);
                particle.Activate((Vector3)position);
                active.Add(particle);
                state.Samples[particle] = new Sample { Position = position, Born = now,
                    Scale = 1 + ((float)state.Random.NextDouble() * 2 - 1) * c.StarDustVariation * .75f };
            }
            state.Visible = true;
            ActiveCount = active.Count;
            detail = state.Planetary ? "Enhanced active (near planet)" : "Enhanced active (space)";
            return false;
        }
        catch (Exception e) { Fail(e, __instance, ___m_activeParticles, ___m_nonActiveParticles); return true; }
    }

    static bool UpdateAfterSimulationPrefix(MyEnvironmentalParticleLogic __instance,
        List<Particle> ___m_activeParticles, List<Particle> ___m_nonActiveParticles)
    {
        if (!ready || faulted) return true;
        try
        {
            var state = GetState(__instance, ___m_activeParticles, ___m_nonActiveParticles);
            if (state.Mode == StarDustMode.Vanilla) return true;
            if (state.Mode == StarDustMode.Off || MySession.Static == null || MySector.MainCamera == null)
                Clear(state, ___m_activeParticles, ___m_nonActiveParticles);
            else Prune(state, ___m_activeParticles, ___m_nonActiveParticles,
                MySession.Static.ElapsedGameTime.TotalSeconds, MySector.MainCamera.Position, Config.Current);
            ActiveCount = ___m_activeParticles.Count;
            return false;
        }
        catch (Exception e) { Fail(e, __instance, ___m_activeParticles, ___m_nonActiveParticles); return true; }
    }

    static void Prune(State state, List<Particle> active, List<Particle> idle, double now, Vector3D camera, Config c)
    {
        double distance = c.StarDustSpawnRadius * 1.5;
        for (int i = active.Count - 1; i >= 0; i--)
        {
            var particle = active[i];
            if (i < c.StarDustParticleLimit && particle.Active && state.Samples.TryGetValue(particle, out var sample)
                && now >= sample.Born && now - sample.Born < c.StarDustLifetime
                && Vector3D.DistanceSquared(sample.Position, camera) < distance * distance) continue;
            state.Samples.Remove(particle);
            particle.Deactivate();
            idle.Add(particle);
            active.RemoveAt(i);
        }
    }

    static bool DrawPrefix(MyEnvironmentalParticleLogic __instance,
        List<Particle> ___m_activeParticles, List<Particle> ___m_nonActiveParticles)
    {
        drawStart = BillboardCount();
        SubmittedCount = 0;
        DrawCalls++;
        if (!ready || faulted) return true;
        try
        {
            var state = GetState(__instance, ___m_activeParticles, ___m_nonActiveParticles);
            if (state.Mode == StarDustMode.Vanilla) return true;
            if (state.Mode == StarDustMode.Off || !state.Visible || MySession.Static == null || MySector.MainCamera == null) return false;
            var c = Config.Current;
            double now = MySession.Static.ElapsedGameTime.TotalSeconds;
            float speedFade = StarDustMath.SpeedFade(state.Speed, c.StarDustMinimumSpeed, c.StarDustSpeedFade);
            float length = StarDustMath.TrailLength(state.Speed, c.StarDustReferenceSpeed, c.StarDustLength, c.StarDustStretch, c.StarDustMaxLength);
            var direction = -Vector3.Normalize(state.Velocity);
            var tint = new Vector3(c.StarDustRed, c.StarDustGreen, c.StarDustBlue) * c.StarDustBrightness;
            foreach (var particle in ___m_activeParticles)
            {
                if (!particle.Active || !state.Samples.TryGetValue(particle, out var sample)) continue;
                float distance = (float)Vector3D.Distance(sample.Position, MySector.MainCamera.Position);
                float distanceFade = StarDustMath.Smooth((c.StarDustSpawnRadius * 1.5f - distance) / Math.Max(1, c.StarDustSpawnRadius * .25f));
                float fade = speedFade * distanceFade * c.StarDustOpacity * StarDustMath.LifeFade((float)(now - sample.Born), c.StarDustLifetime, c.StarDustFade);
                if (fade <= .001f) continue;
                var color = state.Planetary ? particle.ColorPlanet : particle.Color;
                color.X *= tint.X; color.Y *= tint.Y; color.Z *= tint.Z;
                color *= fade; // Scale premultiplied RGB and alpha together.
                MyTransparentGeometry.AddLineBillboard(state.Planetary ? particle.MaterialPlanet : particle.Material,
                    color, sample.Position, direction, Math.Min(c.StarDustMaxLength, length * sample.Scale),
                    c.StarDustWidth * sample.Scale, MyBillboard.BlendTypeEnum.LDR);
            }
            return false;
        }
        catch (Exception e) { Fail(e, __instance, ___m_activeParticles, ___m_nonActiveParticles); return true; }
    }

    // Observe both native and enhanced submission, not just pooled particles.
    // A queued billboard still does not establish successful GPU visibility.
    static void DrawPostfix(List<Particle> ___m_activeParticles, List<Particle> ___m_nonActiveParticles)
    {
        SubmittedCount = Math.Max(0, BillboardCount() - drawStart);
        ActiveCount = ___m_activeParticles.Count;
        PoolCapacity = ActiveCount + ___m_nonActiveParticles.Count;
    }

    static int BillboardCount() => MySession.Static == null || !MyTransparentGeometry.HasCamera
        ? 0 : MyRenderProxy.BillboardsWrite?.Count ?? 0;

    static bool InsideLargeGrid(Vector3D position)
    {
        var sphere = new BoundingSphereD(position, .1);
        var entities = MyEntities.GetEntitiesInSphere(ref sphere);
        try
        {
            foreach (var entity in entities)
                if (entity is MyCubeGrid grid && grid.GridSizeEnum != MyCubeSize.Small) return true;
            return false;
        }
        finally { entities?.Clear(); }
    }

    internal static void Dispose()
    {
        foreach (var weak in trackedStates)
            if (weak.TryGetTarget(out var state) && state.Mode != StarDustMode.Vanilla)
                Clear(state, state.Active, state.Idle);
        trackedStates.Clear();
        harmony?.UnpatchAll(PatchId);
        harmony = null;
        ready = faulted = false;
        states = new ConditionalWeakTable<MyEnvironmentalParticleLogic, State>();
        ActiveCount = PoolCapacity = 0;
        SubmittedCount = DrawCalls = 0;
        Speed = 0;
        detail = "Not initialized";
    }
}
