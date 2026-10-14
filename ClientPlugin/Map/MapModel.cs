using System;
using System.Collections.Generic;
using VRageMath;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("FinalFrontier.MapTests")]

namespace ClientPlugin.Map;

internal enum MapKind { Grid, Planet, Voxel, Asteroid }
internal enum MapRelation { Contact, Owned, Faction }

/// <summary>Canonical root ids deduplicate voxel chunks; removal of a child id cannot remove its root.</summary>
internal sealed class MapIndex<T> where T : class
{
    readonly Dictionary<long, T> entries = new();
    public readonly List<T> Values = new();
    public int Count => entries.Count;
    public bool ContainsKey(long id) => entries.ContainsKey(id);
    public bool TryGetValue(long id, out T value) => entries.TryGetValue(id, out value);
    public bool Add(long canonicalId, T value)
    {
        if (entries.ContainsKey(canonicalId)) return false;
        entries.Add(canonicalId, value); Values.Add(value); return true;
    }
    public bool Remove(long id)
    {
        if (!entries.TryGetValue(id, out var value)) return false;
        entries.Remove(id); Values.Remove(value); return true;
    }
    public void Clear() { entries.Clear(); Values.Clear(); }
}

/// <summary>Sanitized value snapshot. No game references, ownership ids or raw names reach the HUD.</summary>
internal readonly struct MapObject
{
    public readonly long Id;
    public readonly MapKind Kind;
    public readonly MapRelation Relation;
    public readonly string Label;
    public readonly MatrixD Transform;
    public readonly BoundingBoxD Bounds;
    public readonly double Radius, Distance;
    public MapObject(long id, MapKind kind, MapRelation relation, string name, MatrixD transform, BoundingBoxD bounds, double radius, double distance, bool admin)
    {
        Id = id; Kind = kind; Relation = relation; Transform = transform; Bounds = bounds; Radius = radius; Distance = distance;
        Label = kind == MapKind.Grid && relation == MapRelation.Contact && !admin ? "Unknown grid contact" :
            string.IsNullOrWhiteSpace(name) ? kind == MapKind.Planet ? "Planet / moon" : kind == MapKind.Asteroid ? "Asteroid" : kind == MapKind.Voxel ? "Voxel body" : "Grid" : name;
    }
    public Vector3D Center => Kind == MapKind.Planet ? Transform.Translation : Vector3D.Transform(Bounds.Center, Transform);
    public string Details => Label + "\n" + Kind + " · " + (Distance / 1000).ToString("0.00") + " km\n" +
        (Kind == MapKind.Planet ? "Radius: " + (Radius / 1000).ToString("0.0") + " km" : "Bounds: " + Bounds.Size.X.ToString("0") + " × " + Bounds.Size.Y.ToString("0") + " × " + Bounds.Size.Z.ToString("0") + " m");
}

internal static class MapPolicy
{
    public static MapRelation Classify(IReadOnlyList<long> owners, long identity, long faction, Func<long, long> ownerFaction)
    {
        bool shared = false;
        for (int i = 0; i < owners.Count; i++)
        {
            long owner = owners[i];
            if (owner != 0 && owner == identity) return MapRelation.Owned;
            if (owner != 0 && faction != 0 && ownerFaction(owner) == faction) shared = true;
        }
        return shared ? MapRelation.Faction : MapRelation.Contact;
    }
    public static bool Reveal(bool allowed, bool requested) => allowed && requested;
    // Planets are public landmarks whenever their entities are known to this client.
    public static bool IsVisible(MapKind kind, double distance, double range, bool admin) => kind == MapKind.Planet || admin || distance <= range;
    public static double SurfaceDistance(Vector3D player, MatrixD transform, BoundingBoxD bounds, double radius)
    {
        if (radius > 0) return Math.Max(0, Vector3D.Distance(player, transform.Translation) - radius);
        Vector3D local = Vector3D.Transform(player, MatrixD.Invert(transform));
        return Vector3D.Distance(local, Vector3D.Clamp(local, bounds.Min, bounds.Max));
    }
}

internal sealed class MapCamera
{
    public Vector3D Target;
    public double Distance = 20000, Yaw, Pitch = .5;
    public float Width = 800, Height = 600;
    public double Near => Math.Max(.01, Distance * .00001);
    public double Focal => Height * .8660254037844386;
    public Vector3D Right { get; private set; } = Vector3D.Right;
    public Vector3D Up { get; private set; } = Vector3D.Up;
    public Vector3D Forward { get; private set; } = Vector3D.Forward;
    public void Prepare()
    {
        double cy = Math.Cos(Yaw), sy = Math.Sin(Yaw), cp = Math.Cos(Pitch), sp = Math.Sin(Pitch);
        Right = new Vector3D(cy, 0, -sy); Up = new Vector3D(-sy * sp, cp, -cy * sp); Forward = new Vector3D(-sy * cp, -sp, -cy * cp);
    }
    public void Reset(Vector3D target, double range) { Target = target; Distance = Math.Max(10, range * 2); Yaw = 0; Pitch = .5; Prepare(); }
    public void Zoom(float steps) { Distance = Math.Max(1, Math.Min(1e10, Distance * Math.Pow(.85, Math.Max(-20, Math.Min(20, steps))))); }
    public void Orbit(float x, float y) { Yaw = (Yaw - x * .006) % (Math.PI * 2); Pitch = Math.Max(-1.5, Math.Min(1.5, Pitch + y * .006)); Prepare(); }
    public void Pan(float x, float y) { Target -= (Right * x + Up * y) * (Distance / Focal); }
    public Vector3D View(Vector3D point)
    {
        var local = point - Target;
        return new Vector3D(Vector3D.Dot(local, Right), Vector3D.Dot(local, Up), Vector3D.Dot(local, Forward) + Distance);
    }
    public Vector2 Project(Vector3D view) => new((float)(view.X / view.Z * Focal), (float)(view.Y / view.Z * Focal));
    public bool ProjectPoint(Vector3D point, out Vector2 projected)
    {
        var view = View(point); projected = Vector2.Zero;
        if (view.Z < Near) return false;
        projected = Project(view);
        return Math.Abs(projected.X) <= Width * .5f && Math.Abs(projected.Y) <= Height * .5f;
    }
    public bool ClipLine(ref Vector3D a, ref Vector3D b, out Vector2 start, out Vector2 end)
    {
        start = end = Vector2.Zero;
        if (a.Z < Near && b.Z < Near) return false;
        if (a.Z < Near) a = Vector3D.Lerp(a, b, (Near - a.Z) / (b.Z - a.Z));
        if (b.Z < Near) b = Vector3D.Lerp(b, a, (Near - b.Z) / (a.Z - b.Z));
        start = Project(a); end = Project(b);
        return Clip2D(ref start, ref end, Width * .5f, Height * .5f);
    }
    public static bool Clip2D(ref Vector2 a, ref Vector2 b, float halfWidth, float halfHeight)
    {
        double t0 = 0, t1 = 1; var delta = b - a;
        if (!Slab(-delta.X, a.X + halfWidth, ref t0, ref t1) || !Slab(delta.X, halfWidth - a.X, ref t0, ref t1) ||
            !Slab(-delta.Y, a.Y + halfHeight, ref t0, ref t1) || !Slab(delta.Y, halfHeight - a.Y, ref t0, ref t1)) return false;
        b = a + delta * (float)t1; a += delta * (float)t0; return true;
    }
    static bool Slab(double p, double q, ref double min, ref double max)
    {
        if (Math.Abs(p) < 1e-12) return q >= 0;
        double t = q / p;
        if (p < 0) { if (t > max) return false; min = Math.Max(min, t); }
        else { if (t < min) return false; max = Math.Min(max, t); }
        return true;
    }
}
