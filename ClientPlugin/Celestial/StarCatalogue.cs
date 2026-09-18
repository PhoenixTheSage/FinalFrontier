using System;
using System.IO;

namespace ClientPlugin.Celestial;

/// <summary>Validates the offline spatial index before exposing it to GPU traversal.</summary>
static class StarCatalogue
{
    public static float[] Load(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path));
        if (reader.ReadUInt32() != 0x43534646 || reader.ReadInt32() != 1)
            throw new InvalidDataException("Unsupported star catalogue");
        int records = reader.ReadInt32();
        if (records < 32769 || records > 1048576 || reader.BaseStream.Length != 12L + records * 16L)
            throw new InvalidDataException("Invalid catalogue size");
        var data = new float[records * 4];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = reader.ReadSingle();
            if (float.IsNaN(data[i]) || float.IsInfinity(data[i])) throw new InvalidDataException("Nonfinite star data");
        }
        if (data[0] != 32 || data[2] != 2 || data[3] != 1 || data[1] != (records - 32769) / 2f)
            throw new InvalidDataException("Invalid catalogue header");
        int next = 32769;
        for (int cell = 0; cell < 32768; cell++)
        {
            int h = (cell + 1) * 4;
            float start = data[h], count = data[h + 1];
            if (count < 0 || count != Math.Floor(count) || count > data[1] ||
                (count == 0 ? start != 0 : start != next) || next + count * 2 > records)
                throw new InvalidDataException("Invalid catalogue index");
            for (int j = 0; j < (int)count; j++)
            {
                int p = (next + j * 2) * 4;
                double length = data[p]*data[p] + data[p+1]*data[p+1] + data[p+2]*data[p+2];
                int x = Cell(data[p]), y = Cell(data[p+1]), z = Cell(data[p+2]);
                if (Math.Abs(length - 1) > 1e-5 || x + 32*(y+32*z) != cell || data[p+3] <= 0 ||
                    data[p+4] < 0 || data[p+5] < 0 || data[p+6] < 0 || data[p+7] > 8)
                    throw new InvalidDataException("Invalid catalogue star");
            }
            next += (int)count * 2;
        }
        if (next != records) throw new InvalidDataException("Unindexed catalogue records");
        return data;
    }
    static int Cell(float value) => Math.Max(0, Math.Min(31, (int)((value + 1.0) * 16)));
}
