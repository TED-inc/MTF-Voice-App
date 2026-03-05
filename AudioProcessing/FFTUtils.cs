using System;
using NWaves.Transforms;

namespace MTFVoiceTools.AudioProcessing;

public static class FFTUtils
{
    public static double[] FFTMagnitudeLinearSpectrumFromSample(double[] sample, int fftSize)
    {
        if ((fftSize & (fftSize - 1)) != 0)
        {
            throw new ArgumentException("fftSize must be a power of 2.", nameof(fftSize));
        }

        // 1) copy + zero-pad (or truncate) into length-N buffer
        double[] reIn = new double[fftSize];
        int copyLen = Math.Min(sample.Length, fftSize);
        Array.Copy(sample, reIn, copyLen);

        // 2) outputs (full complex spectrum, length N)
        double[] re = new double[fftSize];
        double[] im = new double[fftSize];

        // 3) FFT: real -> (re, im)
        RealFft64 rfft = new (fftSize);
        rfft.Direct(reIn, re, im); // RealFft direct: real -> complex :contentReference[oaicite:1]{index=1}

        // 4) one-sided magnitude: bins 0..N/2 (inclusive)
        int fftHalfSize = fftSize / 2 + 1;
        double scale = 2.0 / fftSize;
        double[] magnitudes = new double[fftHalfSize];
        for (int k = 0; k < fftHalfSize; k++)
        {
            double amplitude = Math.Sqrt(re[k] * re[k] + im[k] * im[k]) * scale;
            magnitudes[k] = Math.Sqrt(amplitude);
        }

        return magnitudes;
    }

    public static void FromDbToLinear(in double[] magnitudes)
    {
        for (int i = 0; i < magnitudes.Length; i++)
        {
            magnitudes[i] = Math.Pow(10, magnitudes[i] / 20);
        }
    }
    
    public static void FromLinearToDb(in double[] magnitudes)
    {
        for (int i = 0; i < magnitudes.Length; i++)
        {
            magnitudes[i] = 20 * Math.Log10(magnitudes[i]);
        }
    }
    
    public static int ClosestLowerPowerOfTwo(int n)
    {
        if (n < 1)
        {
            return 0;
        }

        int power = 1;
        while (power <= n)
        {
            power <<= 1;
        }

        return power >> 1;
    }
}