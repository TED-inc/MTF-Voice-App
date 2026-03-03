using System;

namespace MTFVoiceTools.KlattSynth.Utils;

internal static class SignalUtil
{
    public static void AdjustSignalGain(double[] buf, double targetRms)
    {
        int n = buf.Length;
        if (n == 0)
        {
            return;
        }

        double rms = ComputeRms(buf);
        if (rms == 0)
        {
            return;
        }

        double r = targetRms / rms;

        double maxAbs = FindMaxAbsValue(buf);
        if (maxAbs == 0)
        {
            return;
        }

        if ((r * maxAbs) >= 1)
        {
            // Prevent clipping
            r = 0.99 / maxAbs;
        }

        for (int i = 0; i < n; i++)
        {
            buf[i] *= r;
        }
    }

    public static double ComputeRms(double[] buf)
    {
        int n = buf.Length;
        if (n == 0)
        {
            return 0;
        }

        double acc = 0;
        for (int i = 0; i < n; i++)
        {
            acc += buf[i] * buf[i];
        }

        return Math.Sqrt(acc / n);
    }

    public static double FindMaxAbsValue(double[] buf)
    {
        int n = buf.Length;
        double maxAbs = 0;
        for (int i = 0; i < n; i++)
        {
            double v = Math.Abs(buf[i]);
            if (v > maxAbs)
            {
                maxAbs = v;
            }
        }
        return maxAbs;
    }
}