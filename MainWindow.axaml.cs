using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Styling;
using MTFVoiceTools.KlattSynth.Params;
using MTFVoiceTools.Librosa;
using MTFVoiceTools.Samples;
using NAudio.Wave;
using NWaves.Transforms;
using ScottPlot.Plottables;

namespace MTFVoiceTools;

public partial class MainWindow : Window
{
    private static readonly string DOWNLOADS_PATH = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    
    public MainWindow()
    {
        
        Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        InitializeComponent();
        RunKlattSynth();
    }
    
    public void ClickHandler(object sender, RoutedEventArgs args)
    {
        _ = ProgramSample.Main();
        Message.Text = "Button clicked!";
    }

    private void RunKlattSynth()
    {
        MainParameters mainParams = new (sampleRate: 44100, GlottalSourceType.Impulsive);
        IReadOnlyList<FrameParameters> frames =
        [
            //FrameParametersSamples.MaleI,
            FrameParametersSamples.FemaleI,
            //FrameParametersSamples.MaleE,
            //FrameParametersSamples.FemaleE,
            //FrameParametersSamples.MaleA,
            //FrameParametersSamples.FemaleA,
            //FrameParametersSamples.MaleO,
            //FrameParametersSamples.FemaleO,
            //FrameParametersSamples.MaleU,
            //FrameParametersSamples.FemaleU,
        ];
        
        //double[] samples = Klatt.GenerateSound(mainParams, frames);
        //
        //using WaveFileWriter writer = new(Path.Combine(DOWNLOADS_PATH, "MTFvoiceTools_record.wav"), WaveFormat.CreateIeeeFloatWaveFormat(44100, 1));
        //writer.WriteSamples(samples.Select(s => (float)s).ToArray(), 0, samples.Length);
        //Console.WriteLine(samples.Length);
        
        using WaveFileReader reader = new(Path.Combine(DOWNLOADS_PATH, "My_record.wav"));
        ISampleProvider sampleProvider = reader.ToSampleProvider();
        float[] buffer = new float[reader.SampleCount];
        sampleProvider.Read(buffer, 0, buffer.Length);
        double[] samples = buffer.Select(x => (double)x).ToArray();
        
        //AvaPlot.Plot.Add.Signal(samples, period: 1 / mainParams.SampleRate);

        int fftSize = 1 << 15;
        double period = mainParams.SampleRate / fftSize;
        //double power = 10;
        double minX = 0;
        double maxX = 6000;
        double minY = -100;
        double[] magnitudeDb = MagnitudeSpectrum(samples, fftSize);
        magnitudeDb = magnitudeDb.Skip((int)(minX / period)).Take((int)((maxX - minX) / period)).Select(x => Math.Max(x, minY)).ToArray();
        //double[] logXs = Enumerable.Range(0, magnitudeYs.Length).Select(i => Math.Log(i * period, power)).ToArray();
        
        
        Signal signal = AvaPlot.Plot.Add.Signal(magnitudeDb, period);
        signal.Data.XOffset = minX;
        
        //ScottPlot.TickGenerators.NumericAutomatic tickGen = new()
        //{
        //    MinorTickGenerator = new ScottPlot.TickGenerators.LogMinorTickGenerator(),
        //    LabelFormatter = y => $"{Math.Pow(power, y):N0}"
        //};
        //AvaPlot.Plot.Axes.Bottom.TickGenerator = tickGen;

        double[] smooth = GaussianOctaveSmoothDb(magnitudeDb, period);
        signal = AvaPlot.Plot.Add.Signal(smooth, period);
        signal.Data.XOffset = minX;
        signal.Data.YOffset = 20;

        smooth = GaussianSmoothDb(magnitudeDb, 10, 500);
        signal = AvaPlot.Plot.Add.Signal(smooth, period);
        signal.Data.XOffset = minX;
        signal.Data.YOffset = 15;

        //double[] frame = samples.Skip(samples.Length / 2).Take((int)(mainParams.SampleRate * 0.25)).ToArray();
        (double f0, double score) = EstimateF0(magnitudeDb, period, mainParams.SampleRate, new());
        //(double f1, double f2, double f3) = EstimateFormants(frame, mainParams.SampleRate);
        //Console.WriteLine($"{f0:F0} {f1:F0} {f2:F0} {f3:F0}");

        
        //smooth = GaussianOctaveSmoothDb(magnitudeDb, period, fraction: 3, radiusSigmas: 3);
        //signal = AvaPlot.Plot.Add.Signal(smooth, period);
        //signal.Data.XOffset = minX;
        //signal.Data.YOffset = 20;
        //
        //smooth = GaussianOctaveSmoothDb(magnitudeDb, period, fraction: 1.5, radiusSigmas: 3);
        //signal = AvaPlot.Plot.Add.Signal(smooth, period);
        //signal.Data.XOffset = minX;
        //signal.Data.YOffset = 20;
        
        //smooth = GaussianOctaveSmoothDb(magnitudeDb, period, fraction: 6, radiusSigmas: 6);
        //signal = AvaPlot.Plot.Add.Signal(smooth, period);
        //signal.Data.XOffset = minX;
        

        
        
        var (F, BW, srUsed, a) = FormantLpc.LpcFormantsBurgLikePraat(
            Path.Combine(DOWNLOADS_PATH, "My_record.wav"),
            formantCeilingHz: 5500.0,
            maxFormants: 5,
            windowLengthS: 0.025,
            timeS: null,
            preemphFromHz: 50.0
        );
        
        Console.WriteLine($"Pitch (Hz): {f0:F0}");
        Console.WriteLine("Formants (Hz): " + string.Join(", ", F.Select(v => v.ToString("F0"))));
        Console.WriteLine("Bandwidths (Hz): " + string.Join(", ", BW.Select(v => v.ToString("F0"))));
        
        AvaPlot.Plot.Add.VerticalLine(f0);
        foreach (var f in F)
        {
            AvaPlot.Plot.Add.VerticalLine(f);
        }
        
        AvaPlot.Plot.Axes.AutoScale();
        AvaPlot.Refresh();
    }
    
    private static double[] MagnitudeSpectrum(double[] signal, int fftSize)
    {
        if ((fftSize & (fftSize - 1)) != 0)
        {
            throw new ArgumentException("fftSize must be a power of 2.", nameof(fftSize));
        }

        // 1) copy + zero-pad (or truncate) into length-N buffer
        double[] reIn = new double[fftSize];
        int copyLen = Math.Min(signal.Length, fftSize);
        Array.Copy(signal, reIn, copyLen);

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
            double decibels = 10 * Math.Log10(amplitude);
            magnitudes[k] = decibels;
        }

        return magnitudes;
    }
    
    /// <summary>
    /// 1/6-octave Gaussian smoothing of a dB spectrum.
    /// Smoothing is performed in linear power and uses Gaussian weights in log2(frequency).
    /// </summary>
    /// <param name="magDb">Input spectrum in dB (power dB is assumed: 10*log10(P)).</param>
    /// <param name="df">Frequency step between bins in Hz (e.g., sampleRate/fftSize).</param>
    /// <param name="fraction">Octave fraction, e.g. 6 for 1/6 octave.</param>
    /// <param name="radiusSigmas">Kernel half-width in sigmas (3 is typical).</param>
    public static double[] GaussianOctaveSmoothDb(
        double[] magDb,
        double df,
        double fraction = 6.0,
        double radiusSigmas = 3.0)
    {
        int n = magDb.Length;
        var outDb = new double[n];

        // Convert dB -> linear power
        var p = new double[n];
        for (int k = 0; k < n; k++)
        {
            p[k] = Math.Pow(10.0, magDb[k] / 10.0);
        }

        // Gaussian sigma in "octaves" on log2 axis.
        // FWHM = 1/fraction octave.
        double fwhm = 1.0 / fraction;
        double sigma = fwhm / (2.0 * Math.Sqrt(2.0 * Math.Log(2.0)));

        // Precompute log2(f) for bins (bin 0 invalid)
        var log2f = new double[n];
        log2f[0] = double.NegativeInfinity;
        for (int k = 1; k < n; k++)
        {
            double f = k * df;
            log2f[k] = Math.Log(f, 2.0);
        }

        // Smoothing
        outDb[0] = magDb[0]; // keep DC as-is (or set to NaN / copy)
        for (int k = 1; k < n; k++)
        {
            double fk = k * df;
            if (fk <= 0)
            {
                outDb[k] = magDb[k];
                continue;
            }

            // Limit neighbors to a log-frequency window: +/- radiusSigmas*sigma octaves
            double r = radiusSigmas * sigma;

            // Convert log2 window back to frequency bounds:
            // log2(f) +/- r  =>  f * 2^(+/-r)
            double fLow = fk * Math.Pow(2.0, -r);
            double fHigh = fk * Math.Pow(2.0, +r);

            int i0 = (int)Math.Floor(fLow / df);
            int i1 = (int)Math.Ceiling(fHigh / df);

            if (i0 < 1)
            {
                i0 = 1;
            }

            if (i1 > n - 1)
            {
                i1 = n - 1;
            }

            double center = log2f[k];
            double denom2 = 2.0 * sigma * sigma;

            double wSum = 0.0;
            double pSum = 0.0;

            for (int i = i0; i <= i1; i++)
            {
                double d = log2f[i] - center;
                double w = Math.Exp(-(d * d) / denom2);

                wSum += w;
                pSum += w * p[i];
            }

            double pSmoothed = (wSum > 0.0) ? (pSum / wSum) : p[k];

            // Convert back to dB (power dB)
            outDb[k] = 10.0 * Math.Log10(pSmoothed + 1e-300); // tiny floor to avoid log(0)
        }

        return outDb;
    }
    
    public static double[] GaussianOctaveSmoothDb_ClampedBins(
        double[] magDb,
        double df,
        double fraction = 6.0,
        double radiusSigmas = 3.0,
        int minHalfWidthBins = 12,
        int maxHalfWidthBins = 250)
    {
        int n = magDb.Length;
        var outDb = new double[n];

        // dB -> linear power
        var p = new double[n];
        for (int k = 0; k < n; k++)
        {
            p[k] = Math.Pow(10.0, magDb[k] / 10.0);
        }

        double fwhm = 1.0 / fraction;
        double sigma = fwhm / (2.0 * Math.Sqrt(2.0 * Math.Log(2.0)));

        var log2f = new double[n];
        log2f[0] = double.NegativeInfinity;
        for (int k = 1; k < n; k++)
        {
            log2f[k] = Math.Log(k * df, 2.0);
        }

        outDb[0] = magDb[0];

        for (int k = 1; k < n; k++)
        {
            double fk = k * df;
            double r = radiusSigmas * sigma;

            // Log-frequency bounds
            double fLow = fk * Math.Pow(2.0, -r);
            double fHigh = fk * Math.Pow(2.0, +r);

            int i0 = (int)Math.Floor(fLow / df);
            int i1 = (int)Math.Ceiling(fHigh / df);

            if (i0 < 1)
            {
                i0 = 1;
            }

            if (i1 > n - 1)
            {
                i1 = n - 1;
            }

            // Clamp by bins (this is the fix)
            int half = Math.Max(k - i0, i1 - k);
            half = Math.Max(half, minHalfWidthBins);
            half = Math.Min(half, maxHalfWidthBins);

            i0 = Math.Max(1, k - half);
            i1 = Math.Min(n - 1, k + half);

            double center = log2f[k];
            double denom2 = 2.0 * sigma * sigma;

            double wSum = 0.0, pSum = 0.0;
            for (int i = i0; i <= i1; i++)
            {
                double d = log2f[i] - center;
                double w = Math.Exp(-(d * d) / denom2);
                wSum += w;
                pSum += w * p[i];
            }

            double pSmoothed = (wSum > 0.0) ? (pSum / wSum) : p[k];
            outDb[k] = 10.0 * Math.Log10(pSmoothed + 1e-300);
        }

        return outDb;
    }
    
    /// <summary>
    /// Gaussian smooth a dB magnitude spectrum with uniform frequency spacing.
    /// </summary>
    /// <param name="db">Input spectrum in dB (length N).</param>
    /// <param name="dfHz">Frequency spacing per bin (Hz).</param>
    /// <param name="sigmaHz">Gaussian sigma in Hz (controls smoothing strength).</param>
    /// <returns>Smoothed spectrum in dB (length N).</returns>
    public static double[] GaussianSmoothDb(double[] db, double dfHz, double sigmaHz)
    {
        if (db == null)
        {
            throw new ArgumentNullException(nameof(db));
        }

        if (dfHz <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dfHz));
        }

        if (sigmaHz <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sigmaHz));
        }

        int n = db.Length;
        double sigmaBins = sigmaHz / dfHz;
        if (sigmaBins < 1e-9)
        {
            return (double[])db.Clone();
        }

        int radius = (int)Math.Ceiling(3.0 * sigmaBins); // 3-sigma
        double[] kernel = BuildGaussianKernel(radius, sigmaBins);

        // Convolution with edge handling via "renormalize near borders"
        var output = new double[n];

        for (int i = 0; i < n; i++)
        {
            double acc = 0.0;
            double wsum = 0.0;

            int kMin = Math.Max(-radius, -i);
            int kMax = Math.Min(radius, n - 1 - i);

            for (int k = kMin; k <= kMax; k++)
            {
                double w = kernel[k + radius];
                acc += w * db[i + k];
                wsum += w;
            }

            output[i] = acc / wsum; // renormalize near edges
        }

        return output;
    }

    /// <summary>
    /// Builds a normalized Gaussian kernel of length 2*radius+1.
    /// </summary>
    private static double[] BuildGaussianKernel(int radius, double sigmaBins)
    {
        int len = 2 * radius + 1;
        double[] kernel = new double[len];

        double twoSigma2 = 2.0 * sigmaBins * sigmaBins;
        double sum = 0.0;

        for (int i = -radius; i <= radius; i++)
        {
            double v = Math.Exp(-(i * i) / twoSigma2);
            kernel[i + radius] = v;
            sum += v;
        }

        // Normalize so sum(kernel) = 1
        for (int i = 0; i < len; i++)
        {
            kernel[i] /= sum;
        }

        return kernel;
    }
    
    public sealed class Params
    {
        // Pitch search range
        public double Fmin = 50.0;
        public double Fmax = 500.0;

        // Candidate resolution (log-spaced): candidates per octave (higher = more precise, slower)
        public int CandidatesPerOctave = 48;

        // Harmonics to consider (limited further by Nyquist in code)
        public int MaxHarmonics = 30;

        // Neighborhood half-width around each harmonic for smoothing (in cents).
        // 25–50 cents often works well.
        public double NeighborhoodCents = 35.0;

        // Harmonic weighting exponent: weight = 1 / h^HarmonicWeightExp
        // 0.5 is common-ish (1/sqrt(h)).
        public double HarmonicWeightExp = 0.5;

        // Subharmonic penalty weight. 0.2–0.6 typical.
        public double SubharmonicPenalty = 0.35;

        // Minimum score to accept voiced. Tune for your signal.
        public double VoicingThreshold = 0.10;

        // Optional: if you want to ignore DC/very low bins
        public double MinFreqToUse = 20.0;
    }

    /// <summary>
    /// Estimate F0 from one magnitude spectrum frame using a SWIPE-like harmonic scoring.
    /// Provide either (magnitudesLinear != null) OR (magnitudesDb != null).
    ///
    /// frequenciesHz: can be null if you have uniform bins and provide deltaFHz and f0StartHz=0.
    /// If provided, it must align with magnitudes.
    ///
    /// deltaFHz: bin spacing in Hz (e.g. fs/Nfft). Used for interpolation and bounds.
    /// nyquistHz: usually fs/2.
    /// </summary>
    public static (double f0Hz, double score) EstimateF0(
        double[] magnitudesDb,
        double periodHz,
        double sampleRate,
        Params p)
    {
        int n = magnitudesDb.Length;
        double nyquistHz = sampleRate * 0.5;

        // Convert to linear amplitude (not power) for scoring
        double[] mag = new double[n];
        for (int i = 0; i < n; i++)
        {
            mag[i] = Math.Pow(10.0, magnitudesDb[i] / 20.0);
        }

        // Helper: get magnitude at arbitrary frequency with linear interpolation.
        // Assumes monotonic increasing frequency bins and roughly uniform spacing.
        double MagAt(double fHz)
        {
            if (fHz <= 0)
            {
                return 0.0;
            }

            if (fHz < p.MinFreqToUse)
            {
                return 0.0;
            }

            if (fHz >= nyquistHz)
            {
                return 0.0;
            }

            // If we have explicit frequency array, do a simple uniform-index approximation
            // (fast). If your frequencies[] is NOT uniform, replace this with a binary search.
            double idx = fHz / periodHz;
            int i0 = (int)Math.Floor(idx);
            double t = idx - i0;

            if (i0 < 0)
            {
                return 0.0;
            }

            if (i0 >= n - 1)
            {
                return 0.0;
            }

            return mag[i0] * (1.0 - t) + mag[i0 + 1] * t;
        }

        // Neighborhood smoothing around a target frequency using a cosine window on log-frequency scale.
        // This is the SWIPE-ish part: a kernel in cents around each harmonic.
        double SmoothedMagAt(double fCenter)
        {
            if (fCenter <= 0)
            {
                return 0.0;
            }

            double halfWidthCents = p.NeighborhoodCents;
            // convert cents to multiplicative ratio: r = 2^(cents/1200)
            double r = Math.Pow(2.0, halfWidthCents / 1200.0);
            double fLo = fCenter / r;
            double fHi = fCenter * r;

            // sample a few points (odd count) for a cheap approximation
            // Increase samples if needed
            const int samples = 7;
            double sum = 0.0;
            double wsum = 0.0;

            for (int s = 0; s < samples; s++)
            {
                double u = (double)s / (samples - 1); // 0..1
                double f = fLo + u * (fHi - fLo);

                // cosine window weight (0 at edges, 1 at center)
                double x = (u - 0.5) * 2.0; // -1..1
                double w = 0.5 * (1.0 + Math.Cos(Math.PI * x)); // Hann on [-1,1]

                double m = MagAt(f);
                sum += w * m;
                wsum += w;
            }

            return (wsum > 0.0) ? (sum / wsum) : 0.0;
        }

        // Build log-spaced candidate list
        double fmin = p.Fmin;
        double fmax = Math.Min(p.Fmax, nyquistHz * 0.95);
        if (fmax <= fmin)
        {
            return (0.0, 0.0);
        }

        // number of octaves in range
        double octaves = Math.Log(fmax / fmin, 2.0);
        int numCandidates = Math.Max(8, (int)Math.Ceiling(octaves * p.CandidatesPerOctave) + 1);

        double bestF0 = 0.0;
        double bestScore = double.NegativeInfinity;

        for (int c = 0; c < numCandidates; c++)
        {
            double frac = (numCandidates == 1) ? 0.0 : (double)c / (numCandidates - 1);
            double f0 = fmin * Math.Pow(2.0, frac * octaves);

            // Determine harmonics within Nyquist
            int hMax = Math.Min(p.MaxHarmonics, (int)Math.Floor(nyquistHz / f0));
            if (hMax < 1)
            {
                continue;
            }

            double pos = 0.0;
            double neg = 0.0;
            double wpos = 0.0;

            for (int h = 1; h <= hMax; h++)
            {
                double fh = h * f0;
                double w = 1.0 / Math.Pow(h, p.HarmonicWeightExp);

                // harmonic support (smoothed)
                double mh = SmoothedMagAt(fh);

                // subharmonic suppression: penalize energy at (h - 0.5)*f0 and (h - 1)*f0
                // This discourages picking 2x, 3x, etc.
                double penalty = 0.0;
                double fSub1 = (h - 0.5) * f0;
                double fSub2 = (h - 1.0) * f0;

                if (fSub1 > 0)
                {
                    penalty += SmoothedMagAt(fSub1);
                }

                if (fSub2 > 0)
                {
                    penalty += SmoothedMagAt(fSub2);
                }

                pos += w * mh;
                neg += w * penalty;
                wpos += w;
            }

            // normalize and apply subharmonic penalty
            double score = (wpos > 0.0) ? (pos / wpos) : 0.0;
            score -= p.SubharmonicPenalty * ((wpos > 0.0) ? (neg / wpos) : 0.0);

            if (score > bestScore)
            {
                bestScore = score;
                bestF0 = f0;
            }
        }

        // Voicing decision
        //if (bestScore < p.VoicingThreshold)
        //    return (0.0, bestScore);

        // Optional: refine bestF0 by local search (parabolic-ish) in log space
        // Cheap 3-point refinement around best candidate:
        double refined = RefineLocal(bestF0, fmin, fmax, nyquistHz, p, SmoothedMagAt);
        return (refined, bestScore);
    }

    private static double RefineLocal(
        double f0,
        double fmin,
        double fmax,
        double nyquistHz,
        Params p,
        Func<double, double> smoothedMagAt)
    {
        // Small multiplicative steps (in cents)
        double stepCents = 10.0;
        double r = Math.Pow(2.0, stepCents / 1200.0);

        double fL = Math.Max(fmin, f0 / r);
        double fC = f0;
        double fR = Math.Min(fmax, f0 * r);

        double sL = ScoreAt(fL, nyquistHz, p, smoothedMagAt);
        double sC = ScoreAt(fC, nyquistHz, p, smoothedMagAt);
        double sR = ScoreAt(fR, nyquistHz, p, smoothedMagAt);

        // Parabolic fit over log-frequency axis
        double xL = Math.Log(fL);
        double xC = Math.Log(fC);
        double xR = Math.Log(fR);

        // Fit parabola through (xL,sL),(xC,sC),(xR,sR) and find vertex
        // Using standard 3-point formula:
        double denom = (xL - xC) * (xL - xR) * (xC - xR);
        if (Math.Abs(denom) < 1e-12)
        {
            return f0;
        }

        double a = (xR * (sC - sL) + xC * (sL - sR) + xL * (sR - sC)) / denom;
        double b = (xR * xR * (sL - sC) + xC * xC * (sR - sL) + xL * xL * (sC - sR)) / denom;

        if (Math.Abs(a) < 1e-12)
        {
            return f0;
        }

        double xV = -b / (2.0 * a);
        double fV = Math.Exp(xV);

        if (double.IsNaN(fV) || fV < fmin || fV > fmax)
        {
            return f0;
        }

        return fV;
    }

    private static double ScoreAt(double f0, double nyquistHz, Params p, Func<double, double> SmoothedMagAt)
    {
        int hMax = Math.Min(p.MaxHarmonics, (int)Math.Floor(nyquistHz / f0));
        if (hMax < 1)
        {
            return double.NegativeInfinity;
        }

        double pos = 0.0, neg = 0.0, wpos = 0.0;

        for (int h = 1; h <= hMax; h++)
        {
            double fh = h * f0;
            double w = 1.0 / Math.Pow(h, p.HarmonicWeightExp);

            double mh = SmoothedMagAt(fh);

            double penalty = 0.0;
            double fSub1 = (h - 0.5) * f0;
            double fSub2 = (h - 1.0) * f0;

            if (fSub1 > 0)
            {
                penalty += SmoothedMagAt(fSub1);
            }

            if (fSub2 > 0)
            {
                penalty += SmoothedMagAt(fSub2);
            }

            pos += w * mh;
            neg += w * penalty;
            wpos += w;
        }

        double score = (wpos > 0.0) ? (pos / wpos) : 0.0;
        score -= p.SubharmonicPenalty * ((wpos > 0.0) ? (neg / wpos) : 0.0);
        return score;
    }
    
    public static (double F1, double F2, double F3) EstimateFormants(
        double[] frame,
        double sampleRate,
        int lpcOrder = 16)
    {
        // 1. Pre-emphasis
        double pre = 0.97f;
        double[] x = new double[frame.Length];
        x[0] = frame[0];
        for (int i = 1; i < frame.Length; i++)
        {
            x[i] = frame[i] - pre * frame[i - 1];
        }

        // 2. Hann window
        for (int i = 0; i < x.Length; i++)
        {
            x[i] *= (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (x.Length - 1)));
        }

        // 3. LPC via Burg
        double[] lpc = Burg(x, lpcOrder);

        // 4. Find roots of LPC polynomial
        Complex[] roots = FindPolynomialRoots(lpc);

        // 5. Convert roots → formants
        List<double> formants = new List<double>();

        foreach (var r in roots)
        {
            if (r.Imaginary > 0) // one per conjugate pair
            {
                double angle = Math.Atan2(r.Imaginary, r.Real);
                double freq = angle * sampleRate / (2 * Math.PI);

                double bandwidth = -0.5 * sampleRate *
                    Math.Log(r.Magnitude) / Math.PI;

                // Reject unrealistic formants
                if (freq > 90 && freq < 5000 && bandwidth < 400)
                {
                    formants.Add(freq);
                }
            }
        }

        formants.Sort();

        return (
            formants.Count > 0 ? formants[0] : 0,
            formants.Count > 1 ? formants[1] : 0,
            formants.Count > 2 ? formants[2] : 0
        );
    }
    
    private static double[] Burg(double[] x, int order)
    {
        int N = x.Length;
        double[] ef = new double[N];
        double[] eb = new double[N];
        double[] a = new double[order + 1];
        double[] aPrev = new double[order + 1];

        for (int i = 0; i < N; i++)
        {
            ef[i] = eb[i] = x[i];
        }

        a[0] = 1.0;
        double E = 0.0;
        for (int i = 0; i < N; i++)
        {
            E += x[i] * x[i];
        }

        for (int m = 1; m <= order; m++)
        {
            double num = 0.0, den = 0.0;

            for (int n = m; n < N; n++)
            {
                num += ef[n] * eb[n - 1];
                den += ef[n] * ef[n] + eb[n - 1] * eb[n - 1];
            }

            double k = -2.0 * num / den;

            aPrev = (double[])a.Clone();
            a[m] = k;

            for (int i = 1; i < m; i++)
            {
                a[i] = aPrev[i] + k * aPrev[m - i];
            }

            for (int n = N - 1; n >= m; n--)
            {
                double tmp = ef[n];
                ef[n] += k * eb[n - 1];
                eb[n - 1] += k * tmp;
            }

            E *= (1.0 - k * k);
        }

        return a;
    }
    
    private static Complex[] FindPolynomialRoots(double[] a)
    {
        int n = a.Length - 1;
        Complex[] roots = new Complex[n];
        Complex[] prev = new Complex[n];

        double radius = 0.5;

        for (int i = 0; i < n; i++)
        {
            roots[i] = Complex.FromPolarCoordinates(
                radius,
                2 * Math.PI * i / n);
        }

        for (int iter = 0; iter < 100; iter++)
        {
            for (int i = 0; i < n; i++)
            {
                Complex prod = Complex.One;
                for (int j = 0; j < n; j++)
                {
                    if (i != j)
                    {
                        prod *= (roots[i] - roots[j]);
                    }
                }

                Complex f = EvaluatePolynomial(a, roots[i]);
                prev[i] = roots[i];
                roots[i] -= f / prod;
            }
        }

        return roots;
    }

    private static Complex EvaluatePolynomial(double[] a, Complex z)
    {
        Complex result = Complex.Zero;
        for (int i = 0; i < a.Length; i++)
        {
            result += a[i] * Complex.Pow(z, a.Length - i - 1);
        }

        return result;
    }
}