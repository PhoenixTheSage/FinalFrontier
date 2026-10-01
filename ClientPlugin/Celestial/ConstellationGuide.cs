using System;
using System.IO;

namespace ClientPlugin.Celestial;

/// <summary>Validates baked constellation stick figures (format 4: ranges + edge planes).</summary>
static class ConstellationGuide
{
    public const int HeaderRecords = 1;
    public const int FigureStride = 8;
    public const int EdgeStride = 3; // a, b, planeNormal(+cosArc)

    public struct Section
    {
        public float[] Records;
        public int EdgeCount;
        public int MemberCount;
        public int MemberStart;
        public int FigureCount;
        public int FigureStart;
        public int Format;
    }

    public static Section Load(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path));
        if (reader.ReadUInt32() != 0x47434646 || reader.ReadInt32() != 1)
            throw new InvalidDataException("Unsupported constellation guide");
        int records = reader.ReadInt32();
        if (records < 1 || records > 65536 || reader.BaseStream.Length != 12L + records * 16L)
            throw new InvalidDataException("Invalid constellation size");
        var data = new float[records * 4];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = reader.ReadSingle();
            if (float.IsNaN(data[i]) || float.IsInfinity(data[i]))
                throw new InvalidDataException("Nonfinite constellation data");
        }
        int edges = (int)data[0], members = (int)data[1], figures = (int)data[2], format = (int)data[3];
        if (edges < 0 || members < 0 || figures < 0)
            throw new InvalidDataException("Invalid constellation header");
        int edgeStart = HeaderRecords;
        int edgeRecords = format >= 4 ? edges * EdgeStride : edges * 2;
        int memberStart = edgeStart + edgeRecords;
        int figureStart = memberStart + members;
        if (format == 1)
        {
            if (HeaderRecords + edges * 2 + members != records)
                throw new InvalidDataException("Invalid constellation header");
            figures = 0;
            figureStart = records;
        }
        else if (format == 2)
        {
            int strokeStart = figureStart + figures * 7;
            if (strokeStart > records)
                throw new InvalidDataException("Constellation figures overrun buffer");
            int strokes = 0;
            for (int i = 0; i < figures; i++)
            {
                int o = (figureStart + i * 7) * 4;
                RequireUnit(data, o);
                RequireUnit(data, o + 4);
                RequireUnit(data, o + 8);
                if (data[o + 3] <= 0 || data[o + 7] < 0 || data[o + 11] < strokeStart)
                    throw new InvalidDataException("Invalid constellation figure frame");
                strokes += (int)data[o + 7];
            }
            if (strokeStart + strokes != records)
                throw new InvalidDataException("Constellation stroke count mismatch");
        }
        else if (format == 3)
        {
            if (figureStart + figures * 7 != records)
                throw new InvalidDataException("Invalid constellation format 3 size");
            for (int i = 0; i < figures; i++)
            {
                int o = (figureStart + i * 7) * 4;
                RequireUnit(data, o);
                RequireUnit(data, o + 4);
                RequireUnit(data, o + 8);
                if (data[o + 3] <= 0)
                    throw new InvalidDataException("Invalid constellation figure radius");
            }
        }
        else if (format == 4)
        {
            if (figureStart + figures * FigureStride != records)
                throw new InvalidDataException("Invalid constellation format 4 size");
            int coveredEdges = 0, coveredMembers = 0;
            for (int i = 0; i < figures; i++)
            {
                int o = (figureStart + i * FigureStride) * 4;
                RequireUnit(data, o);
                RequireUnit(data, o + 4);
                RequireUnit(data, o + 8);
                if (data[o + 3] <= 0)
                    throw new InvalidDataException("Invalid constellation figure radius");
                int es = (int)data[o + 7], ec = (int)data[o + 11];
                int ms = (int)data[o + 12], mc = (int)data[o + 13];
                if (es < 0 || ec < 0 || es + ec > edges || ms < 0 || mc < 0 || ms + mc > members)
                    throw new InvalidDataException("Invalid constellation figure ranges");
                coveredEdges += ec;
                coveredMembers += mc;
            }
            if (coveredEdges != edges || coveredMembers != members)
                throw new InvalidDataException("Constellation figure ranges do not cover buffer");
            for (int i = 0; i < edges; i++)
            {
                int o = (edgeStart + i * EdgeStride) * 4;
                RequireUnit(data, o);
                RequireUnit(data, o + 4);
                RequireUnit(data, o + 8);
            }
        }
        else
            throw new InvalidDataException("Unsupported constellation format");
        if (format < 4)
        {
            for (int i = 0; i < edges; i++)
            {
                RequireUnit(data, (edgeStart + i * 2) * 4);
                RequireUnit(data, (edgeStart + i * 2 + 1) * 4);
            }
        }
        for (int i = 0; i < members; i++)
            RequireUnit(data, (memberStart + i) * 4);
        return new Section
        {
            Records = data,
            EdgeCount = edges,
            MemberCount = members,
            MemberStart = memberStart,
            FigureCount = figures,
            FigureStart = figureStart,
            Format = format,
        };
    }

    public static float[] Concatenate(float[] catalogue, Section guide)
    {
        var merged = new float[catalogue.Length + guide.Records.Length];
        Buffer.BlockCopy(catalogue, 0, merged, 0, catalogue.Length * 4);
        Buffer.BlockCopy(guide.Records, 0, merged, catalogue.Length * 4, guide.Records.Length * 4);
        int stars = (int)catalogue[1];
        int starRecords = 32769;
        for (int m = 0; m < guide.MemberCount; m++)
        {
            int mo = (guide.MemberStart + m) * 4;
            float mx = guide.Records[mo], my = guide.Records[mo + 1], mz = guide.Records[mo + 2];
            for (int s = 0; s < stars; s++)
            {
                int o = (starRecords + s * 2) * 4;
                float dx = merged[o] - mx, dy = merged[o + 1] - my, dz = merged[o + 2] - mz;
                if (dx * dx + dy * dy + dz * dz < 1e-10)
                {
                    float mag = merged[o + 7];
                    if (mag < 50) merged[o + 7] = mag + 100;
                    break;
                }
            }
        }
        return merged;
    }

    static void RequireUnit(float[] data, int offset)
    {
        double length = data[offset] * data[offset] + data[offset + 1] * data[offset + 1] + data[offset + 2] * data[offset + 2];
        if (Math.Abs(length - 1) > 1e-4)
            throw new InvalidDataException("Constellation direction is not unit length");
    }
}
