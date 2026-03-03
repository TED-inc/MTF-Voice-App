using System;
using MTFVoiceTools.KlattSynth.Filters;
using MTFVoiceTools.KlattSynth.Utils;

namespace MTFVoiceTools.KlattSynth.Sources;

internal sealed class LpNoiseSource
{
    private readonly LpFilter1 _lpFilter;
    private readonly Random _rng;

    public LpNoiseSource(double sampleRate, Random rng)
    {
        _rng = rng;

        // Original logic: first-order LP with b=0.75 at sample rate 10 kHz.
        const double oldB = 0.75;
        const double oldSampleRate = 10000;

        // Gain at 1000 Hz, DC gain = 1:
        const double f = 1000;
        double g = (1 - oldB) /
                   Math.Sqrt(1 - 2 * oldB * Math.Cos(2 * Math.PI * f / oldSampleRate) + (oldB * oldB));

        // Compensate amplitude for output range -1..+1:
        double extraGain = 2.5 * Math.Pow(sampleRate / 10000.0, 0.33);

        _lpFilter = new LpFilter1(sampleRate);
        _lpFilter.Set(f, g, extraGain);
    }

    public double GetNext()
    {
        double x = MathUtil.WhiteNoise(_rng);
        return _lpFilter.Step(x);
    }
}