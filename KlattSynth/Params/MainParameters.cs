using System;
using MTFVoiceTools.KlattSynth.Utils;

namespace MTFVoiceTools.KlattSynth.Params;

public sealed class MainParameters
{
    public MainParameters(double sampleRate, GlottalSourceType glottalSourceType)
    {
        if (MathUtil.IsFinite(sampleRate) == false || sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate), "Sample rate must be finite and > 0.");
        }

        SampleRate = sampleRate;
        GlottalSourceType = glottalSourceType;
    }
    
    public double SampleRate { get; }

    public GlottalSourceType GlottalSourceType { get; }
}