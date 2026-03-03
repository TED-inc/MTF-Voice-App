using System;

namespace MTFVoiceTools.KlattSynth.Polynomial;

internal static class PolyOps
{
    public static double[] Convolve(double[] a, double[] b)
    {
        int na = a.Length;
        int nb = b.Length;
        double[] y = new double[na + nb - 1];

        for (int i = 0; i < na; i++)
        {
            double ai = a[i];
            for (int j = 0; j < nb; j++)
            {
                y[i + j] += ai * b[j];
            }
        }

        return y;
    }

    public static double[] AddPolys(double[] a, double[] b)
    {
        int n = Math.Max(a.Length, b.Length);
        double[] y = new double[n];

        for (int i = 0; i < n; i++)
        {
            double av = (i < a.Length) ? a[i] : 0;
            double bv = (i < b.Length) ? b[i] : 0;
            y[i] = av + bv;
        }

        return y;
    }

    public static double[] Trim(double[] p, double eps)
    {
        // Zero very small coefficients, then trim trailing near-zeros.
        double[] q = (double[])p.Clone();

        for (int i = 0; i < q.Length; i++)
        {
            if (Math.Abs(q[i]) < eps)
            {
                q[i] = 0;
            }
        }

        int last = q.Length - 1;
        while (last > 0 && Math.Abs(q[last]) <= eps)
        {
            last--;
        }

        if (last == q.Length - 1)
        {
            return q;
        }

        double[] r = new double[last + 1];
        Array.Copy(q, 0, r, 0, last + 1);
        return r;
    }
}