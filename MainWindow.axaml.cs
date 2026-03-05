using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Styling;
using MTFVoiceTools.KlattSynth;
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
        
        double[] samples = Klatt.GenerateSound(mainParams, frames);

        using (WaveFileWriter writer = new(Path.Combine(DOWNLOADS_PATH, "MTFvoiceTools_record.wav"),
                   WaveFormat.CreateIeeeFloatWaveFormat(mainParams.SampleRate, 1)))
        {
            writer.WriteSamples(samples.Select(s => (float)s).ToArray(), 0, samples.Length);
        }
        
        //Console.WriteLine(samples.Length);
        
        //using WaveFileReader reader = new(Path.Combine(DOWNLOADS_PATH, "My_record.wav"));
        //ISampleProvider sampleProvider = reader.ToSampleProvider();
        //float[] buffer = new float[reader.SampleCount];
        //sampleProvider.Read(buffer, 0, buffer.Length);
        //double[] samples = buffer.Select(x => (double)x).ToArray();
        
        //AvaPlot.Plot.Add.Signal(samples, period: 1 / mainParams.SampleRate);

        int fftSize = 1 << 15;
        double period = (double)mainParams.SampleRate / fftSize;
        //double power = 10;
        double minX = 0;
        double maxX = 6000;
        double minY = -100;
        double[] magnitudeDb = MagnitudeSpectrum(samples, fftSize);
        magnitudeDb = magnitudeDb.Skip((int)(minX / period)).Take((int)((maxX - minX) / period)).Select(x => Math.Max(x, minY)).ToArray();
        
        
        Signal signal = AvaPlot.Plot.Add.Signal(magnitudeDb, period);
        signal.Data.XOffset = minX;
        
        double f0 = EstimateF0(magnitudeDb, period, mainParams.SampleRate, new());
        
        double[] formants = FormantLpc.CalcualteFormantsWithLpc(
            samples,
            mainParams.SampleRate,
            formantCeilingHz: 5500.0,
            maxFormants: 5,
            lpcWindowLengthSeconds: 0.025,
            lpcWindowCenterSecond: null,
            preemphFromHz: 50.0
        );
        
        Console.WriteLine($"Pitch (Hz): {f0:F0}");
        Console.WriteLine("Formants (Hz): " + string.Join(", ", formants.Select(v => v.ToString("F0"))));
        
        AvaPlot.Plot.Add.VerticalLine(f0);
        foreach (double formant in formants)
        {
            AvaPlot.Plot.Add.VerticalLine(formant);
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
    public static double EstimateF0(
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
            return 0;
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

        return RefineLocal(bestF0, fmin, fmax, nyquistHz, p, SmoothedMagAt);
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
}