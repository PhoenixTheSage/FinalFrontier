using System;
using System.Collections.Generic;
using VRageMath;

namespace ClientPlugin.Map;

/// <summary>CPU projection of sanitized snapshots. Buffers are reused; the host copies them on SubmitGeometry.</summary>
internal sealed class MapRenderer
{
    public const int MaxLines = 4096, MaxTriangles = 1024;
    public readonly float[] Lines = new float[MaxLines * 8], Triangles = new float[MaxTriangles * 10];
    public int LineCount, TriangleCount, ClusteredObjects;
    public string[] Labels = Array.Empty<string>();
    public float[] LabelPositions = Array.Empty<float>();
    readonly Vector3D[] vertices = new Vector3D[12], nearPolygon = new Vector3D[6];
    readonly Vector2[] clipA = new Vector2[16], clipB = new Vector2[16];
    readonly List<Face> faces = new(MaxTriangles);
    readonly List<Pick> picks = new(512);
    readonly Dictionary<long, Cluster> clusters = new();
    readonly List<string> labels = new(128);
    readonly List<float> labelPositions = new(256);
    static readonly int[] BoxEdges = { 0,1, 1,3, 3,2, 2,0, 4,5, 5,7, 7,6, 6,4, 0,4, 1,5, 2,6, 3,7 };
    static readonly int[] BoxFaces = { 0,2,3, 0,3,1, 4,5,7, 4,7,6, 0,1,5, 0,5,4, 2,6,7, 2,7,3, 0,4,6, 0,6,2, 1,3,7, 1,7,5 };
    // An octahedron places its six tips at the oriented bounds' face centers.
    static readonly Vector3D[] Diamond = { Vector3D.Right, Vector3D.Left, Vector3D.Up, Vector3D.Down, Vector3D.Forward, Vector3D.Backward };
    static readonly int[] DiamondFaces = { 0,2,4, 4,2,1, 1,2,5, 5,2,0, 4,3,0, 1,3,4, 5,3,1, 0,3,5 };
    static readonly int[] DiamondEdges = MakeEdges(DiamondFaces);
    static readonly Vector3D[] Sphere = MakeSphere();
    static readonly int[] SphereFaces = { 0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8, 3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1 };
    static readonly int[] SphereEdges = MakeEdges(SphereFaces);
    readonly struct Face
    {
        public readonly Vector2 A, B, C;
        public readonly double Depth;
        public readonly Color Color;
        public Face(Vector2 a, Vector2 b, Vector2 c, double depth, Color color) { A = a; B = b; C = c; Depth = depth; Color = color; }
    }
    readonly struct Pick
    {
        public readonly long Id;
        public readonly BoundingBox2 Bounds;
        public readonly double Depth;
        public Pick(long id, BoundingBox2 bounds, double depth) { Id = id; Bounds = bounds; Depth = depth; }
    }
    sealed class Cluster
    {
        public Vector2 Point;
        public Color Color;
        public long Id;
        public double Depth;
        public int Count;
    }
    static Vector3D[] MakeSphere()
    {
        double t = (1 + Math.Sqrt(5)) / 2;
        var result = new[] { new Vector3D(-1,t,0), new Vector3D(1,t,0), new Vector3D(-1,-t,0), new Vector3D(1,-t,0), new Vector3D(0,-1,t), new Vector3D(0,1,t), new Vector3D(0,-1,-t), new Vector3D(0,1,-t), new Vector3D(t,0,-1), new Vector3D(t,0,1), new Vector3D(-t,0,-1), new Vector3D(-t,0,1) };
        for (int i = 0; i < result.Length; i++) result[i].Normalize();
        return result;
    }
    static int[] MakeEdges(int[] triangles)
    {
        var unique = new SortedSet<int>();
        for (int i = 0; i < triangles.Length; i += 3) for (int j = 0; j < 3; j++)
        {
            int a = triangles[i + j], b = triangles[i + (j + 1) % 3];
            unique.Add(Math.Min(a, b) * 16 + Math.Max(a, b));
        }
        var result = new int[unique.Count * 2]; int n = 0;
        foreach (int edge in unique) { result[n++] = edge / 16; result[n++] = edge % 16; }
        return result;
    }
    static void Palette(MapObject obj, Config config, out Color line, out Color face)
    {
        if (obj.Kind == MapKind.Planet) { line = config.MapPlanetLine; face = config.MapPlanetFace; }
        else if (obj.Kind == MapKind.Voxel || obj.Kind == MapKind.Asteroid) { line = config.MapVoxelLine; face = config.MapVoxelFace; }
        else if (obj.Relation == MapRelation.Owned) { line = config.MapOwnedLine; face = config.MapOwnedFace; }
        else if (obj.Relation == MapRelation.Faction) { line = config.MapFactionLine; face = config.MapFactionFace; }
        else { line = config.MapContactLine; face = config.MapContactFace; }
    }
    public void Build(IReadOnlyList<MapObject> objects, MapCamera camera, Config config, long selected)
    {
        camera.Prepare();
        LineCount = TriangleCount = ClusteredObjects = 0;
        faces.Clear(); picks.Clear(); clusters.Clear(); labels.Clear(); labelPositions.Clear();
        // The list is sorted by distance and id. Selection gets first access to the detail budgets.
        for (int i = 0; i < objects.Count; i++) if (objects[i].Id == selected) { DrawObject(objects[i], camera, config, true); break; }
        for (int i = 0; i < objects.Count; i++) if (objects[i].Id != selected) DrawObject(objects[i], camera, config, false);
        foreach (var cluster in clusters.Values)
        {
            if (LineCount + 2 > MaxLines) break;
            AddLine(cluster.Point - new Vector2(3, 0), cluster.Point + new Vector2(3, 0), cluster.Color);
            AddLine(cluster.Point - new Vector2(0, 3), cluster.Point + new Vector2(0, 3), cluster.Color);
            picks.Add(new Pick(cluster.Id, new BoundingBox2(cluster.Point - new Vector2(7), cluster.Point + new Vector2(7)), cluster.Depth));
            if (cluster.Count > 1) { ClusteredObjects += cluster.Count; AddLabel(cluster.Count.ToString(), cluster.Point + new Vector2(7, 7)); }
        }
        faces.Sort((a, b) => b.Depth.CompareTo(a.Depth));
        for (int i = 0; i < faces.Count; i++)
        {
            var face = faces[i]; int p = TriangleCount++ * 10;
            Triangles[p] = face.A.X; Triangles[p + 1] = face.A.Y; Triangles[p + 2] = face.B.X; Triangles[p + 3] = face.B.Y;
            Triangles[p + 4] = face.C.X; Triangles[p + 5] = face.C.Y; WriteColor(Triangles, p + 6, face.Color);
        }
        Labels = labels.ToArray(); LabelPositions = labelPositions.ToArray();
    }
    void DrawObject(MapObject obj, MapCamera camera, Config config, bool selected)
    {
        Palette(obj, config, out var line, out var face);
        var center = camera.View(obj.Center);
        double radius = obj.Kind == MapKind.Planet ? obj.Radius : obj.Bounds.HalfExtents.Length();
        if (center.Z + radius < camera.Near) return;
        double projectedSize = radius / Math.Max(camera.Near, center.Z) * camera.Focal * 2;
        var edges = obj.Kind == MapKind.Planet ? SphereEdges : obj.Kind == MapKind.Asteroid ? DiamondEdges : BoxEdges;
        if (!selected && (projectedSize < 8 || LineCount + edges.Length / 2 > MaxLines - 512))
        {
            if (center.Z < camera.Near) return;
            var point = camera.Project(center);
            if (Math.Abs(point.X) > camera.Width * .5f || Math.Abs(point.Y) > camera.Height * .5f) return;
            long key = ((long)(int)Math.Floor((point.X + camera.Width * .5f) / 32) << 32) | (uint)(int)Math.Floor((point.Y + camera.Height * .5f) / 32);
            if (clusters.TryGetValue(key, out var cell))
            {
                cell.Count++;
                if (center.Z < cell.Depth) { cell.Point = point; cell.Id = obj.Id; cell.Depth = center.Z; cell.Color = line; }
            }
            else if (clusters.Count < 126) clusters.Add(key, new Cluster { Point = point, Count = 1, Color = line, Id = obj.Id, Depth = center.Z });
            else
            {
                // Overflow is a visible aggregate, rather than silently omitting known objects.
                long overflow = long.MinValue;
                if (!clusters.TryGetValue(overflow, out cell)) clusters.Add(overflow, cell = new Cluster { Point = new Vector2(-camera.Width * .5f + 16, -camera.Height * .5f + 16), Color = line, Id = obj.Id, Depth = center.Z });
                cell.Count++;
            }
            return;
        }
        int count = obj.Kind == MapKind.Planet ? 12 : obj.Kind == MapKind.Asteroid ? 6 : 8;
        var min = new Vector2(float.PositiveInfinity); var max = new Vector2(float.NegativeInfinity);
        for (int i = 0; i < count; i++)
        {
            Vector3D world = obj.Kind == MapKind.Planet ? obj.Center + Sphere[i] * obj.Radius : obj.Kind == MapKind.Asteroid ?
                Vector3D.Transform(obj.Bounds.Center + Diamond[i] * obj.Bounds.HalfExtents, obj.Transform) :
                Vector3D.Transform(new Vector3D((i & 1) == 0 ? obj.Bounds.Min.X : obj.Bounds.Max.X,
                    (i & 2) == 0 ? obj.Bounds.Min.Y : obj.Bounds.Max.Y, (i & 4) == 0 ? obj.Bounds.Min.Z : obj.Bounds.Max.Z), obj.Transform);
            vertices[i] = camera.View(world);
            if (vertices[i].Z >= camera.Near) { var p = camera.Project(vertices[i]); min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
        }
        if (min.X <= max.X)
        {
            min = Vector2.Max(min, new Vector2(-camera.Width * .5f, -camera.Height * .5f));
            max = Vector2.Min(max, new Vector2(camera.Width * .5f, camera.Height * .5f));
            if (min.X <= max.X && min.Y <= max.Y) picks.Add(new Pick(obj.Id, new BoundingBox2(min, max), center.Z));
        }
        for (int i = 0; i < edges.Length && LineCount < MaxLines; i += 2)
        {
            var a = vertices[edges[i]]; var b = vertices[edges[i + 1]];
            if (camera.ClipLine(ref a, ref b, out var start, out var end)) AddLine(start, end, selected ? Color.White : line);
        }
        if (!config.MapWireframeOnly && face.A > 0)
        {
            var triangles = obj.Kind == MapKind.Planet ? SphereFaces : obj.Kind == MapKind.Asteroid ? DiamondFaces : BoxFaces;
            for (int i = 0; i < triangles.Length && faces.Count < MaxTriangles; i += 3)
                ClipFace(vertices[triangles[i]], vertices[triangles[i + 1]], vertices[triangles[i + 2]], camera, face);
        }
        if (selected && camera.ProjectPoint(obj.Center, out var labelPoint)) AddLabel(obj.Label, labelPoint + new Vector2(0, 12));
    }
    void AddLabel(string text, Vector2 point)
    {
        if (labels.Count >= 128) return;
        labels.Add(text); labelPositions.Add(point.X); labelPositions.Add(point.Y);
    }
    void AddLine(Vector2 a, Vector2 b, Color color)
    {
        if (LineCount >= MaxLines) return;
        int p = LineCount++ * 8; Lines[p] = a.X; Lines[p + 1] = a.Y; Lines[p + 2] = b.X; Lines[p + 3] = b.Y; WriteColor(Lines, p + 4, color);
    }
    static void WriteColor(float[] target, int p, Color color)
    { target[p] = color.R / 255f; target[p + 1] = color.G / 255f; target[p + 2] = color.B / 255f; target[p + 3] = color.A / 255f; }
    void ClipFace(Vector3D a, Vector3D b, Vector3D c, MapCamera camera, Color color)
    {
        int count = 0;
        ClipNear(c, a, camera.Near, ref count); ClipNear(a, b, camera.Near, ref count); ClipNear(b, c, camera.Near, ref count);
        if (count < 3) return;
        for (int i = 0; i < count; i++) clipA[i] = camera.Project(nearPolygon[i]);
        Vector2[] input = clipA, output = clipB;
        for (int edge = 0; edge < 4; edge++)
        {
            int nextCount = 0;
            for (int i = 0; i < count; i++)
            {
                var from = input[(i + count - 1) % count]; var to = input[i];
                float df = EdgeDistance(from, edge, camera), dt = EdgeDistance(to, edge, camera);
                if ((df >= 0) != (dt >= 0)) output[nextCount++] = Vector2.Lerp(from, to, df / (df - dt));
                if (dt >= 0) output[nextCount++] = to;
            }
            count = nextCount; var swap = input; input = output; output = swap;
            if (count < 3) return;
        }
        double depth = (a.Z + b.Z + c.Z) / 3;
        for (int i = 1; i + 1 < count && faces.Count < MaxTriangles; i++) faces.Add(new Face(input[0], input[i], input[i + 1], depth, color));
    }
    void ClipNear(Vector3D from, Vector3D to, double near, ref int count)
    {
        if ((from.Z >= near) != (to.Z >= near)) nearPolygon[count++] = Vector3D.Lerp(from, to, (near - from.Z) / (to.Z - from.Z));
        if (to.Z >= near) nearPolygon[count++] = to;
    }
    static float EdgeDistance(Vector2 p, int edge, MapCamera camera) => edge == 0 ? p.X + camera.Width * .5f : edge == 1 ? camera.Width * .5f - p.X : edge == 2 ? p.Y + camera.Height * .5f : camera.Height * .5f - p.Y;
    public long PickObject(float x, float y)
    {
        long result = 0; double nearest = double.PositiveInfinity;
        for (int i = 0; i < picks.Count; i++)
        {
            var pick = picks[i];
            if (x >= pick.Bounds.Min.X && x <= pick.Bounds.Max.X && y >= pick.Bounds.Min.Y && y <= pick.Bounds.Max.Y && pick.Depth < nearest)
            { result = pick.Id; nearest = pick.Depth; }
        }
        return result;
    }
}
