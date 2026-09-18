using System;
using System.IO;

namespace ClientPlugin.Celestial;

/// <summary>Validates baked constellation stick-figure directions for GPU guides.</summary>
static class ConstellationGuide
{
    public const int HeaderRecords = 1;

    public struct Section
    {
        public float[] Records;
        public int EdgeCount;
        public int MemberCount;
        public int MemberStart; // float4 index within Records
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
        int edges = (int)data[0], members = (int)data[1];
        if (data[3] != 1 || edges < 0 || members < 0 || HeaderRecords + edges * 2 + members != records)
            throw new InvalidDataException("Invalid constellation header");
        int edgeStart = HeaderRecords;
        int memberStart = edgeStart + edges * 2;
        for (int i = 0; i < edges; i++)
        {
            RequireUnit(data, (edgeStart + i * 2) * 4);
            RequireUnit(data, (edgeStart + i * 2 + 1) * 4);
        }
        for (int i = 0; i < members; i++)
            RequireUnit(data, (memberStart + i) * 4);
        return new Section { Records = data, EdgeCount = edges, MemberCount = members, MemberStart = memberStart };
    }

    public static float[] Concatenate(float[] catalogue, Section guide)
    {
        var merged = new float[catalogue.Length + guide.Records.Length];
        Buffer.BlockCopy(catalogue, 0, merged, 0, catalogue.Length * 4);
        Buffer.BlockCopy(guide.Records, 0, merged, catalogue.Length * 4, guide.Records.Length * 4);
        // Stamp figure membership into catalogue magnitudes (+100) so the star
        // pass does not re-hunt directions every pixel. Rings still use members[].
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
