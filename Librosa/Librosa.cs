// Praat-ish LPC formants via Burg (librosa-like) — C# translation of your Python.
// Dependencies (NuGet):
//   - NAudio (for WAV loading)
//   - MathNet.Numerics (for eigen/roots + complex math)
//
// Install:
//   dotnet add package NAudio
//   dotnet add package MathNet.Numerics
//
// Notes vs Python:
// - librosa.load(..., mono=True) => we load WAV and downmix to mono.
// - librosa.resample => simple band-unaware linear resampler here (works, but not as good as librosa/Praat).
//   If you want higher quality, swap in a proper resampler (e.g., NWaves resampler or NAudio WdlResamplingSampleProvider).
// - Burg LPC is implemented for 1D frames (your usage).
// - Roots computed via companion matrix eigenvalues.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Factorization;
using NAudio.Wave;

namespace MTFVoiceTools.Librosa;


public static class FormantLpc
{
    public static double[]
        CalcualteFormantsWithLpc(
            double[] sample,
            int sampleRate,
            int maxFormants = 5,
            double lpcWindowLengthSeconds = 0.025,
            double? lpcWindowCenterSecond = null,
            double formantCeilingHz = 5500.0,
            double preemphFromHz = 50.0)
    {
        (double[] lpcWindow, sampleRate) = CreateAndFillLpcWindow(
            sample, 
            sampleRate, 
            lpcWindowLengthSeconds, 
            lpcWindowCenterSecond, 
            formantCeilingHz, 
            preemphFromHz);

        // 4) Gaussian-like window
        double[] w = GaussianWindow(lpcWindow.Length);
        for (int i = 0; i < lpcWindow.Length; i++)
        {
            lpcWindow[i] *= w[i];
        }

        // 5) LPC via Burg; Praat poles = 2*maxFormants => order = 2*maxFormants
        int order = 2 * maxFormants;
        double[] a = LpcBurg(lpcWindow, order);

        // 6) Roots -> formants + bandwidth
        Complex[] roots = PolynomialRoots(a);

        // Keep one from each conjugate pair: imag > 0
        Complex[] upper = roots.Where(r => r.Imaginary > 0).ToArray();

        // freqs = angle(root) * sr/(2*pi)
        // bws   = -0.5 * (sr/pi) * ln(|root|)
        List<(double f, double bw)> cand = new();
        
        foreach (Complex r in upper)
        {
            double ang = Math.Atan2(r.Imaginary, r.Real);
            double freq = ang * (sampleRate / (2.0 * Math.PI));
            double mag = r.Magnitude;
            if (mag <= 0)
            {
                continue;
            }

            double bw = -0.5 * (sampleRate / Math.PI) * Math.Log(mag);

            // plausibility filter (same as Python)
            if (freq > 50.0 && freq < formantCeilingHz && bw > 0.0 && bw < 400.0)
            {
                cand.Add((freq, bw));
            }
        }

        (double f, double bw)[] sorted = cand.OrderBy(t => t.f).ToArray();
        return sorted.Take(maxFormants).Select(t => t.f).ToArray();
    }

    private static (double[] lpcWindow, int sampleRate) CreateAndFillLpcWindow(
        double[] sample, 
        int sampleRate, 
        double lpcWindowLengthSeconds,
        double? lpcWindowCenterSecond,
        double formantCeilingHz,
        double preemphFromHz)
    {
        int targetSampleRate = (int)Math.Round(2.0 * formantCeilingHz);
        
        sample = ResampleLinear(sample, sampleRate, targetSampleRate);
        sampleRate = targetSampleRate;
        
        int lpcWindowLength = (int)Math.Round(lpcWindowLengthSeconds * sampleRate);
        if (lpcWindowLength < 16)
        {
            throw new ArgumentException("Window too small.");
        }

        int lpcWindowCenterIndex = lpcWindowCenterSecond.HasValue ? (int)Math.Round(lpcWindowCenterSecond.Value * sampleRate) : (sample.Length / 2);
        int lpcWindowStartIndex = Math.Max(0, lpcWindowCenterIndex - lpcWindowLength / 2);

        double[] lpcWindow = new double[lpcWindowLength];
        int availableLpcWindowLenthg = Math.Max(0, Math.Min(lpcWindowLength, sample.Length - lpcWindowStartIndex));
        if (availableLpcWindowLenthg > 0)
        {
            Array.Copy(sample, lpcWindowStartIndex, lpcWindow, 0, availableLpcWindowLenthg);
        }
        
        lpcWindow = PreemphasisPraat(lpcWindow, sampleRate, preemphFromHz);
        return (lpcWindow, sampleRate);
    }


    // Praat-style pre-emphasis: y[n] = x[n] - a*x[n-1], a = exp(-2*pi*fc/sr)
    private static double[] PreemphasisPraat(double[] sample, int sampleRate, double preemphFromHz)
    {
        double a = Math.Exp(-2.0 * Math.PI * preemphFromHz / sampleRate);

        double[] y = new double[sample.Length];
        y[0] = sample[0];
        for (int i = 1; i < sample.Length; i++)
        {
            y[i] = sample[i] - a * sample[i - 1];
        }
        return y;
    }

    // Gaussian-like window similar in spirit to Praat's
    private static double[] GaussianWindow(int n)
    {
        if (n <= 0)
        {
            return Array.Empty<double>();
        }

        double[] w = new double[n];

        double sigma = 0.4 * (n - 1) / 2.0;
        double mid = (n - 1) / 2.0;
        double inv2sigma2 = 1.0 / (2.0 * sigma * sigma);

        for (int i = 0; i < n; i++)
        {
            double d = i - mid;
            w[i] = Math.Exp(-(d * d) * inv2sigma2);
        }
        return w;
    }

    private static void ValidateAudio(double[] y)
    {
        if (y == null)
        {
            throw new ArgumentNullException("Audio data must not be null.");
        }

        if (y.Length == 0)
        {
            throw new ArgumentException("Audio data must not be empty.");
        }

        for (int i = 0; i < y.Length; i++)
        {
            if (double.IsNaN(y[i]) || double.IsInfinity(y[i]))
            {
                throw new ArgumentException("Audio buffer is not finite everywhere.");
            }
        }
    }

    // --- Burg LPC (1D) ---------------------------------------------------
    // Returns AR denominator polynomial a[0..order], with a[0]=1 (like librosa)
    private static double[] LpcBurg(double[] y, int order)
    {
        if (order <= 0)
        {
            throw new ArgumentException($"order={order} must be an integer > 0");
        }

        ValidateAudio(y);

        int n = y.Length;
        if (n < order + 1)
        {
            throw new ArgumentException($"Input is too short for order={order}. Need at least {order + 1} samples, got {n}.");
        }

        // Coeff arrays
        double[] ar = new double[order + 1];
        double[] arPrev = new double[order + 1];
        ar[0] = 1.0;
        arPrev[0] = 1.0;

        // Forward/backward errors
        // fwd = y[1:], bwd = y[:-1]
        double[] fwd = new double[n - 1];
        double[] bwd = new double[n - 1];
        Array.Copy(y, 1, fwd, 0, n - 1);
        Array.Copy(y, 0, bwd, 0, n - 1);

        // den = sum(fwd^2 + bwd^2)
        double den = 0.0;
        for (int i = 0; i < fwd.Length; i++)
        {
            den += fwd[i] * fwd[i] + bwd[i] * bwd[i];
        }

        for (int i = 0; i < order; i++)
        {
            // reflect = -2 * sum(bwd * fwd) / (den + eps)
            double num = 0.0;
            for (int k = 0; k < fwd.Length; k++)
            {
                num += bwd[k] * fwd[k];
            }

            double reflect = (-2.0 * num) / (den + double.Epsilon);

            // Levinson-Durbin recursion update
            // swap ar/arPrev buffers
            (arPrev, ar) = (ar, arPrev);

            ar[0] = 1.0;
            for (int j = 1; j <= i + 1; j++)
            {
                ar[j] = arPrev[j] + reflect * arPrev[i - j + 1];
            }
            for (int j = i + 2; j <= order; j++)
            {
                // keep remaining coefficients from previous iteration (they are unused until filled)
                ar[j] = arPrev[j];
            }

            // Update prediction errors:
            // fwd = fwd + reflect * bwd
            // bwd = bwd + reflect * old_fwd
            double[] oldFwd = fwd;
            double[] newFwd = new double[oldFwd.Length];
            double[] newBwd = new double[bwd.Length];

            for (int k = 0; k < oldFwd.Length; k++)
            {
                newFwd[k] = oldFwd[k] + reflect * bwd[k];
                newBwd[k] = bwd[k] + reflect * oldFwd[k];
            }

            fwd = newFwd;
            bwd = newBwd;

            // DEN recursion: den = (1 - reflect^2)*den - bwd[-1]^2 - fwd[0]^2
            double q = 1.0 - reflect * reflect;
            den = q * den - (bwd[bwd.Length - 1] * bwd[bwd.Length - 1]) - (fwd[0] * fwd[0]);

            if (double.IsNaN(den) || double.IsInfinity(den))
            {
                throw new Exception("numerical error in Burg recursion; input ill-conditioned?");
            }

            // Shift errors for next order
            // fwd = fwd[1:], bwd = bwd[:-1]
            if (fwd.Length <= 1)
            {
                break;
            }

            double[] fwdShift = new double[fwd.Length - 1];
            double[] bwdShift = new double[bwd.Length - 1];
            Array.Copy(fwd, 1, fwdShift, 0, fwdShift.Length);
            Array.Copy(bwd, 0, bwdShift, 0, bwdShift.Length);
            fwd = fwdShift;
            bwd = bwdShift;
        }

        return ar;
    }

    // --- Polynomial roots (np.roots) via companion matrix ----------------
    // For polynomial a[0]*x^n + a[1]*x^(n-1) + ... + a[n]
    // We assume a[0] != 0. (Here a[0]=1 from LPC.)
    private static Complex[] PolynomialRoots(double[] a)
    {
        if (a == null || a.Length < 2)
        {
            return Array.Empty<Complex>();
        }

        int n = a.Length - 1;
        double a0 = a[0];
        if (a0 == 0.0)
        {
            throw new ArgumentException("Leading coefficient is zero; cannot compute roots.");
        }

        // Build companion matrix (n x n)
        // [ -a1/a0  -a2/a0 ... -an/a0 ]
        // [  1        0   ...   0    ]
        // [  0        1   ...   0    ]
        // ...
        Matrix<double>? M = Matrix<double>.Build.Dense(n, n, 0.0);

        // first row
        for (int j = 0; j < n; j++)
        {
            M[0, j] = -a[j + 1] / a0;
        }

        // subdiagonal ones
        for (int i = 1; i < n; i++)
        {
            M[i, i - 1] = 1.0;
        }

        // Eigenvalues are roots
        Evd<double>? evd = M.Evd();
        return evd.EigenValues.Select(c => new Complex(c.Real, c.Imaginary)).ToArray();
    }

    // --- Audio loading/resampling ---------------------------------------
    // Load WAV -> mono double[] in [-1,1] and sample rate.
    private static (double[] x, int sr) LoadWavMono(string wavPath)
    {
        if (!File.Exists(wavPath))
        {
            throw new FileNotFoundException("WAV file not found.", wavPath);
        }

        using AudioFileReader reader = new(wavPath); // outputs float samples, auto converts
        int sr = reader.WaveFormat.SampleRate;
        int ch = reader.WaveFormat.Channels;

        float[] buffer = new float[4096 * ch];
        List<double> samples = new(sr * 10);

        int read;
        while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            int frames = read / ch;
            for (int i = 0; i < frames; i++)
            {
                double sum = 0.0;
                for (int c = 0; c < ch; c++)
                {
                    sum += buffer[i * ch + c];
                }

                samples.Add(sum / ch);
            }
        }

        return (samples.ToArray(), sr);
    }
    
    private static double[] ResampleLinear(double[] sample, int originalSampleRate, int targetSampleRate)
    {
        if (originalSampleRate == targetSampleRate || sample.Length == 0)
        {
            return sample;
        }

        double ratio = (double)targetSampleRate / originalSampleRate;
        int resampleLength = (int)Math.Round(sample.Length * ratio);
        resampleLength = Math.Max(1, resampleLength);

        double[] resampled = new double[resampleLength];
        double step = (double)(sample.Length - 1) / (resampleLength - 1);

        for (int resampleIndex = 0; resampleIndex < resampleLength; resampleIndex++)
        {
            double samplePosition = resampleIndex * step;
            int floorSampleIndex = (int)Math.Floor(samplePosition);
            int ceilSampleIndex = Math.Min(floorSampleIndex + 1, sample.Length - 1);
            double fracrion = samplePosition - floorSampleIndex;
            resampled[resampleIndex] = sample[floorSampleIndex] * (1 - fracrion) + sample[ceilSampleIndex] * fracrion;
        }
        
        return resampled;
    }
}