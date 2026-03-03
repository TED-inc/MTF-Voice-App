using System;
using System.Collections.Generic;

namespace MTFVoiceTools.KlattSynth.Params;

public sealed class FrameParameters
{
    public double Duration { get; init; }

    /// <summary>Fundamental frequency in Hz.</summary>
    public double F0 { get; init; }

    /// <summary>F0 flutter level 0..1 (typical 0.25).</summary>
    public double FlutterLevel { get; init; }

    /// <summary>Relative length of the open glottis phase 0..1 (typical 0.7).</summary>
    public double OpenPhaseRatio { get; init; }

    /// <summary>Breathiness in voicing (turbulence) in dB.</summary>
    public double BreathinessDb { get; init; }

    /// <summary>Spectral tilt (attenuation at 3 kHz) in dB. 0 = no tilt.</summary>
    public double TiltDb { get; init; }

    /// <summary>Overall output gain in dB. Use NaN to enable automatic gain control (AGC).</summary>
    public double GainDb { get; init; } = 0;

    /// <summary>Target RMS for AGC (only used if GainDb is NaN).</summary>
    public double AgcRmsLevel { get; init; } = 0.1;

    public double NasalFormantFreq { get; init; } = double.NaN;
    public double NasalFormantBw { get; init; } = double.NaN;

    public IReadOnlyList<double> OralFormantFreq { get; init; } = Array.Empty<double>();
    public IReadOnlyList<double> OralFormantBw { get; init; } = Array.Empty<double>();

    // Cascade branch:
    public bool CascadeEnabled { get; init; } = true;
    public double CascadeVoicingDb { get; init; } = 0;
    public double CascadeAspirationDb { get; init; } = -99;
    public double CascadeAspirationMod { get; init; } = 0;
    public double NasalAntiformantFreq { get; init; } = double.NaN;
    public double NasalAntiformantBw { get; init; } = double.NaN;

    // Parallel branch:
    public bool ParallelEnabled { get; init; } = false;
    public double ParallelVoicingDb { get; init; } = -99;
    public double ParallelAspirationDb { get; init; } = -99;
    public double ParallelAspirationMod { get; init; } = 0;
    public double FricationDb { get; init; } = -99;
    public double FricationMod { get; init; } = 0;
    public double ParallelBypassDb { get; init; } = -99;
    public double NasalFormantDb { get; init; } = -99;
    public IReadOnlyList<double> OralFormantDb { get; init; } = Array.Empty<double>();
}