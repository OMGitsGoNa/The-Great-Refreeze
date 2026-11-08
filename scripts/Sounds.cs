using System;
using Godot;

namespace GreatRefreeze;

/// <summary>
/// Sonidos generados en código (sin archivos de audio). Son cortos y sencillos a propósito.
/// </summary>
public static class Sounds
{
    private const int Rate = 22050;
    private static readonly Random Rng = new(7);

    /// <summary>Crujido corto de hielo al colocar un cubo.</summary>
    public static AudioStream Crack() => Make(0.15f, t =>
        (float)(Rng.NextDouble() * 2 - 1) * Math.Exp(-t * 55) * 0.6f
        + Math.Sin(2 * Math.PI * 1100 * t) * Math.Exp(-t * 45) * 0.25f);

    /// <summary>Acorde ascendente al completar un sector.</summary>
    public static AudioStream Chime() => Make(0.6f, t =>
        (float)((Math.Sin(2 * Math.PI * 523 * t) + Math.Sin(2 * Math.PI * 659 * t) + Math.Sin(2 * Math.PI * 784 * t)) / 3
        * Math.Exp(-t * 4)) * 0.6f);

    /// <summary>Gota suave cuando se derrite un hielo del montón.</summary>
    public static AudioStream Drip() => Make(0.25f, t =>
        (float)Math.Sin(2 * Math.PI * (1300 - 900 * t) * t) * (float)Math.Exp(-t * 25) * 0.35f);

    private static AudioStreamWav Make(float seconds, Func<double, double> sample)
    {
        int count = (int)(seconds * Rate);
        var data = new byte[count * 2];
        for (int i = 0; i < count; i++)
        {
            double t = i / (double)Rate;
            float v = (float)Math.Clamp(sample(t), -1.0, 1.0);
            short s = (short)(v * 32767);
            data[i * 2] = (byte)(s & 0xFF);
            data[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
        }

        return new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = Rate,
            Stereo = false,
            Data = data,
        };
    }
}