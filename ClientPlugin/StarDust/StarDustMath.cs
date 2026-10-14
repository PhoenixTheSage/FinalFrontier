using System;

namespace ClientPlugin.StarDust;

internal static class StarDustMath
{
    internal static float Smooth(float value)
    {
        value = Math.Max(0, Math.Min(1, value));
        return value * value * (3 - 2 * value);
    }

    internal static float SpeedFade(float speed, float minimum, float range) =>
        Smooth((speed - minimum) / Math.Max(1, range));

    internal static float LifeFade(float age, float lifetime, float fraction)
    {
        if (age < 0 || age >= lifetime) return 0;
        if (fraction <= 0) return 1;
        float edge = Math.Max(.001f, lifetime * fraction);
        return Smooth(age / edge) * Smooth((lifetime - age) / edge);
    }

    internal static float TrailLength(float speed, float reference, float length, float stretch, float maximum)
    {
        float response = (float)Math.Pow(Math.Max(.001f, speed / Math.Max(1, reference)), stretch);
        return Math.Max(.001f, Math.Min(maximum, length * response));
    }

    // Keen's average random factor is .875 and its per-tick rate is scaled by 16.
    // Integrate in seconds at the nominal 60 Hz rate; saturation discards debt.
    internal static float SpawnCount(float speed, float radius, float density, float speedFade, float seconds,
        float minimum = 10, float nativeDensity = .0000235f) =>
        .875f * Math.Max(0, speed - minimum * .85f) * 4 * radius * radius * Math.Max(0, nativeDensity) * 16 * 60 * seconds * density * speedFade;
}
