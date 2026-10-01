using System;
using System.IO;

namespace ClientPlugin.Celestial;

/// <summary>Loads constellation fantasy art RGBA slices for Anomaly SetArt.</summary>
static class ConstellationArt
{
    public const int Resolution = 512;
    static byte[] rgba;
    static int slices;

    public static byte[] Rgba => rgba;
    public static int SliceCount => slices;
    public static int Width => Resolution;
    public static int Height => Resolution;

    public static bool TryLoad(string artDirectory, string[] figureNames)
    {
        Release();
        if (string.IsNullOrEmpty(artDirectory) || figureNames == null || figureNames.Length == 0)
            return false;
        if (!Directory.Exists(artDirectory))
            return false;

        slices = figureNames.Length;
        rgba = new byte[Resolution * Resolution * 4 * slices];
        for (int i = 0; i < slices; i++)
        {
            string path = Path.Combine(artDirectory, Sanitize(figureNames[i]) + ".rgba");
            if (!File.Exists(path))
                throw new FileNotFoundException("Missing constellation art", path);
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length != Resolution * Resolution * 4)
                throw new InvalidDataException("Art slice size mismatch: " + path);
            Buffer.BlockCopy(bytes, 0, rgba, i * bytes.Length, bytes.Length);
        }
        return true;
    }

    public static void Release()
    {
        rgba = null;
        slices = 0;
    }

    static string Sanitize(string name) => name.Replace(' ', '_');
}
