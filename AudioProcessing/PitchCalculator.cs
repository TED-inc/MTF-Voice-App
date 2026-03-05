using System;

namespace MTFVoiceTools.AudioProcessing;

public static class PitchCalculator
{
    public sealed record Params
    {
        public readonly double Fmin = 50;
        public readonly double Fmax = 500;

        // Candidate resolution (log-spaced): candidates per octave (higher = more precise, slower)
        public readonly int CandidatesPerOctave = 48;

        // Harmonics to consider (limited further by Nyquist in code)
        public readonly int MaxHarmonics = 30;

        // Neighborhood half-width around each harmonic for smoothing (in cents).
        // 25–50 cents often works well.
        public readonly double NeighborhoodCents = 35;

        // Harmonic weighting exponent: weight = 1 / h^HarmonicWeightExp
        // 0.5 is common-ish (1/sqrt(h)).
        public readonly double HarmonicWeightExp = 0.5;

        // Subharmonic penalty weight. 0.2–0.6 typical.
        public readonly double SubharmonicPenalty = 0.35;

        // Optional: if you want to ignore DC/very low bins
        public readonly double MinFreqToUseHz = 20;
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
        double[] magnitudeLinear,
        double periodHz,
        double sampleRate,
        Params? param = null)
    {
        param ??= new ();
        
        double nyquistHz = sampleRate * 0.5;

        // Build log-spaced candidate list
        double fmin = param.Fmin;
        double fmax = Math.Min(param.Fmax, nyquistHz * 0.95);
        if (fmax <= fmin)
        {
            return 0;
        }

        // number of octaves in range
        double octaves = Math.Log(fmax / fmin, 2.0);
        int numCandidates = Math.Max(8, (int)Math.Ceiling(octaves * param.CandidatesPerOctave) + 1);

        double bestF0 = 0.0;
        double bestScore = double.NegativeInfinity;

        for (int c = 0; c < numCandidates; c++)
        {
            double frac = (numCandidates == 1) ? 0.0 : (double)c / (numCandidates - 1);
            double f0 = fmin * Math.Pow(2.0, frac * octaves);

            // Determine harmonics within Nyquist
            int hMax = Math.Min(param.MaxHarmonics, (int)Math.Floor(nyquistHz / f0));
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
                double w = 1.0 / Math.Pow(h, param.HarmonicWeightExp);

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
            score -= param.SubharmonicPenalty * ((wpos > 0.0) ? (neg / wpos) : 0.0);

            if (score > bestScore)
            {
                bestScore = score;
                bestF0 = f0;
            }
        }

        return RefineLocal(bestF0, fmin, fmax, nyquistHz, param, SmoothedMagAt);
        
        double SmoothedMagAt(double fCenter) => SmoothedMagnituteAt(fCenter, param, nyquistHz, periodHz, magnitudeLinear);
    }
    
    // Helper: get magnitude at arbitrary frequency with linear interpolation.
    // Assumes monotonic increasing frequency bins and roughly uniform spacing.

    // Neighborhood smoothing around a target frequency using a cosine window on log-frequency scale.
    // This is the SWIPE-ish part: a kernel in cents around each harmonic.
    private static double SmoothedMagnituteAt(double fCenter, Params param, double nyquistHz, double periodHz, double[] magnitudeLinear)
    {
        if (fCenter <= 0)
        {
            return 0.0;
        }

        double halfWidthCents = param.NeighborhoodCents;
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

            double m = MagnituteLinearAt(f, param, nyquistHz, periodHz, magnitudeLinear);
            sum += w * m;
            wsum += w;
        }

        return wsum > 0.0 ? sum / wsum : 0.0;
    }

    private static double MagnituteLinearAt(double frequencyHz, Params param, double nyquistHz, double periodHz, double[] magnitudeLinear)
    {
        if (frequencyHz <= 0
            || frequencyHz < param.MinFreqToUseHz
            || frequencyHz >= nyquistHz)
        {
            return 0;
        }
        
        double position = frequencyHz / periodHz;
        int index = (int)Math.Floor(position);
        double lerpT = position - index;

        if (index < 0
            || index + 1 >= magnitudeLinear.Length)
        {
            return 0;
        }

        return magnitudeLinear[index] * (1 - lerpT) + magnitudeLinear[index + 1] * lerpT;
    }

    private static double RefineLocal(
        double f0,
        double fmin,
        double fmax,
        double nyquistHz,
        Params param,
        Func<double, double> smoothedMagAt)
    {
        // Small multiplicative steps (in cents)
        double stepCents = 10.0;
        double r = Math.Pow(2.0, stepCents / 1200.0);

        double fL = Math.Max(fmin, f0 / r);
        double fC = f0;
        double fR = Math.Min(fmax, f0 * r);

        double sL = ScoreAt(fL, nyquistHz, param, smoothedMagAt);
        double sC = ScoreAt(fC, nyquistHz, param, smoothedMagAt);
        double sR = ScoreAt(fR, nyquistHz, param, smoothedMagAt);

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

    private static double ScoreAt(double f0, double nyquistHz, Params param, Func<double, double> smoothedMagAt)
    {
        int hMax = Math.Min(param.MaxHarmonics, (int)Math.Floor(nyquistHz / f0));
        if (hMax < 1)
        {
            return double.NegativeInfinity;
        }

        double pos = 0.0, neg = 0.0, wpos = 0.0;

        for (int h = 1; h <= hMax; h++)
        {
            double fh = h * f0;
            double w = 1.0 / Math.Pow(h, param.HarmonicWeightExp);

            double mh = smoothedMagAt(fh);

            double penalty = 0.0;
            double fSub1 = (h - 0.5) * f0;
            double fSub2 = (h - 1.0) * f0;

            if (fSub1 > 0)
            {
                penalty += smoothedMagAt(fSub1);
            }

            if (fSub2 > 0)
            {
                penalty += smoothedMagAt(fSub2);
            }

            pos += w * mh;
            neg += w * penalty;
            wpos += w;
        }

        double score = (wpos > 0.0) ? (pos / wpos) : 0.0;
        score -= param.SubharmonicPenalty * ((wpos > 0.0) ? (neg / wpos) : 0.0);
        return score;
    }
}