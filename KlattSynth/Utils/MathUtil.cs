using System;

namespace MTFVoiceTools.KlattSynth.Utils;

internal static class MathUtil
{
    public static bool IsFinite(double x) => !(double.IsNaN(x) || double.IsInfinity(x));

    public static bool IsUsableFreqBw(double f, double bw)
    {
        // Mirrors JS truthiness checks (NaN and 0 are treated as "off").
        return IsFinite(f) && IsFinite(bw) && f != 0 && bw != 0;
    }

    /// <summary>White noise in range [-1, 1).</summary>
    public static double WhiteNoise(Random rng) => (rng.NextDouble() * 2.0) - 1.0;

    /// <summary>F0 flutter modulation.</summary>
    public static double PerformFrequencyModulation(double f0, double flutterLevel, double timeSeconds)
    {
        if (flutterLevel <= 0)
        {
            return f0;
        }

        double w = 2 * Math.PI * timeSeconds;
        double a = Math.Sin(12.7 * w) + Math.Sin(7.1 * w) + Math.Sin(4.7 * w);
        return f0 * (1 + a * flutterLevel / 50.0);
    }

    /// <summary>Convert dB to linear. dB &lt;= -99 or NaN => 0.</summary>
    public static double DbToLin(double db)
    {
        if (db <= -99 || double.IsNaN(db))
        {
            return 0;
        }

        return Math.Pow(10, db / 20.0);
    }
}