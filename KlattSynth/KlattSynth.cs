// Port of Klatt.ts (TypeScript) to C#
// Notes:
// - This is a direct, dependency-free port of the core Klatt synthesizer and transfer-function logic. Done using ChatGPT 5.2

#nullable enable

using System;
using System.Collections.Generic;

namespace MTFVoiceTools.KlattSynth;

/// <summary>Glottal source selection.</summary>
public enum GlottalSourceType
{
    Impulsive = 0,
    Natural = 1,
    Noise = 2
}

public static class Constants
{
    public static readonly string[] GlottalSourceTypeEnumNames = { "impulsive", "natural", "noise" };
    public const int MaxOralFormants = 6;
}

/// <summary>Parameters for the whole sound.</summary>
public sealed class MainParameters
{
    public MainParameters(double sampleRate, GlottalSourceType glottalSourceType)
    {
        if (!MathUtil.IsFinite(sampleRate) || sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate), "Sample rate must be finite and > 0.");

        SampleRate = sampleRate;
        GlottalSourceType = glottalSourceType;
    }

    /// <summary>Sample rate in Hz.</summary>
    public double SampleRate { get; }

    public GlottalSourceType GlottalSourceType { get; }
}

/// <summary>Parameters for a sound frame.</summary>
public sealed class FrameParameters
{
    /// <summary>Frame duration in seconds.</summary>
    public double Duration { get; set; }

    /// <summary>Fundamental frequency in Hz.</summary>
    public double F0 { get; set; }

    /// <summary>F0 flutter level 0..1 (typical 0.25).</summary>
    public double FlutterLevel { get; set; }

    /// <summary>Relative length of the open glottis phase 0..1 (typical 0.7).</summary>
    public double OpenPhaseRatio { get; set; }

    /// <summary>Breathiness in voicing (turbulence) in dB.</summary>
    public double BreathinessDb { get; set; }

    /// <summary>Spectral tilt (attenuation at 3 kHz) in dB. 0 = no tilt.</summary>
    public double TiltDb { get; set; }

    /// <summary>Overall output gain in dB. Use NaN to enable automatic gain control (AGC).</summary>
    public double GainDb { get; set; } = 0;

    /// <summary>Target RMS for AGC (only used if GainDb is NaN).</summary>
    public double AgcRmsLevel { get; set; } = 0.1;

    public double NasalFormantFreq { get; set; } = double.NaN;
    public double NasalFormantBw { get; set; } = double.NaN;

    public double[] OralFormantFreq { get; set; } = Array.Empty<double>();
    public double[] OralFormantBw { get; set; } = Array.Empty<double>();

    // Cascade branch:
    public bool CascadeEnabled { get; set; } = true;
    public double CascadeVoicingDb { get; set; } = 0;
    public double CascadeAspirationDb { get; set; } = -99;
    public double CascadeAspirationMod { get; set; } = 0;
    public double NasalAntiformantFreq { get; set; } = double.NaN;
    public double NasalAntiformantBw { get; set; } = double.NaN;

    // Parallel branch:
    public bool ParallelEnabled { get; set; } = false;
    public double ParallelVoicingDb { get; set; } = -99;
    public double ParallelAspirationDb { get; set; } = -99;
    public double ParallelAspirationMod { get; set; } = 0;
    public double FricationDb { get; set; } = -99;
    public double FricationMod { get; set; } = 0;
    public double ParallelBypassDb { get; set; } = -99;
    public double NasalFormantDb { get; set; } = -99;
    public double[] OralFormantDb { get; set; } = Array.Empty<double>();
}

// -------------------------------------------------------------------------
// Filters
// -------------------------------------------------------------------------

/// <summary>
/// A first-order IIR LP filter:
/// y[n] = a * x[n] + b * y[n-1]
/// </summary>
internal sealed class LpFilter1
{
    private readonly double _sampleRate;
    private double _a;
    private double _b;
    private double _y1;
    private bool _passthrough;
    private bool _muted;

    public LpFilter1(double sampleRate)
    {
        _sampleRate = sampleRate;
        _y1 = 0;
        _passthrough = true;
        _muted = false;
    }

    /// <summary>
    /// Adjusts the filter parameters without resetting the inner state.
    /// </summary>
    /// <param name="f">Frequency at which the gain is specified.</param>
    /// <param name="g">Gain at frequency f. Must be 0..1 (LP behavior, matching TS port).</param>
    /// <param name="extraGain">Extra gain factor; resulting DC gain.</param>
    public void Set(double f, double g, double extraGain = 1)
    {
        if (!(f > 0) || !(f < _sampleRate / 2) ||
            !(g > 0) || !(g < 1) ||
            !MathUtil.IsFinite(f) || !MathUtil.IsFinite(g) || !MathUtil.IsFinite(extraGain))
        {
            throw new ArgumentException("Invalid filter parameters.");
        }

        double w = 2 * Math.PI * f / _sampleRate;
        double q = (1 - (g * g) * Math.Cos(w)) / (1 - (g * g));
        _b = q - Math.Sqrt((q * q) - 1);
        _a = (1 - _b) * extraGain;

        _passthrough = false;
        _muted = false;
    }

    public void SetPassthrough()
    {
        _passthrough = true;
        _muted = false;
        _y1 = 0;
    }

    public void SetMute()
    {
        _passthrough = false;
        _muted = true;
        _y1 = 0;
    }

    public RationalPoly GetTransferFunction()
    {
        if (_passthrough) return RationalPoly.PassThrough;
        if (_muted) return RationalPoly.Mute;
        return new RationalPoly(new[] { _a }, new[] { 1.0, -_b });
    }

    public double Step(double x)
    {
        if (_passthrough) return x;
        if (_muted) return 0;

        double y = _a * x + _b * _y1;
        _y1 = y;
        return y;
    }
}

/// <summary>
/// A Klatt resonator (2nd-order IIR). With f=0 it can be used as an LP filter.
/// y[n] = a*x[n] + b*y[n-1] + c*y[n-2]
/// </summary>
internal sealed class Resonator
{
    private readonly double _sampleRate;
    private double _a;
    private double _b;
    private double _c;
    private double _y1;
    private double _y2;
    private double _r;
    private bool _passthrough;
    private bool _muted;

    public Resonator(double sampleRate)
    {
        _sampleRate = sampleRate;
        _y1 = 0;
        _y2 = 0;
        _passthrough = true;
        _muted = false;
    }

    /// <summary>Adjusts parameters without resetting internal state.</summary>
    public void Set(double f, double bw, double dcGain = 1)
    {
        if (f < 0 || f >= _sampleRate / 2 ||
            bw <= 0 || dcGain <= 0 ||
            !MathUtil.IsFinite(f) || !MathUtil.IsFinite(bw) || !MathUtil.IsFinite(dcGain))
        {
            throw new ArgumentException("Invalid resonator parameters.");
        }

        _r = Math.Exp(-Math.PI * bw / _sampleRate);
        double w = 2 * Math.PI * f / _sampleRate;
        _c = -(_r * _r);
        _b = 2 * _r * Math.Cos(w);
        _a = (1 - _b - _c) * dcGain;

        _passthrough = false;
        _muted = false;
    }

    public void SetPassthrough()
    {
        _passthrough = true;
        _muted = false;
        _y1 = 0;
        _y2 = 0;
    }

    public void SetMute()
    {
        _passthrough = false;
        _muted = true;
        _y1 = 0;
        _y2 = 0;
    }

    public void AdjustImpulseGain(double newA) => _a = newA;

    public void AdjustPeakGain(double peakGain)
    {
        if (peakGain <= 0 || !MathUtil.IsFinite(peakGain))
            throw new ArgumentException("Invalid resonator peak gain.");

        _a = peakGain * (1 - _r);
    }

    public RationalPoly GetTransferFunction()
    {
        if (_passthrough) return RationalPoly.PassThrough;
        if (_muted) return RationalPoly.Mute;
        return new RationalPoly(new[] { _a }, new[] { 1.0, -_b, -_c });
    }

    public double Step(double x)
    {
        if (_passthrough) return x;
        if (_muted) return 0;

        double y = _a * x + _b * _y1 + _c * _y2;
        _y2 = _y1;
        _y1 = y;
        return y;
    }
}

/// <summary>
/// A Klatt anti-resonator (2nd-order FIR):
/// y[n] = a*x[n] + b*x[n-1] + c*x[n-2]
/// </summary>
internal sealed class AntiResonator
{
    private readonly double _sampleRate;
    private double _a;
    private double _b;
    private double _c;
    private double _x1;
    private double _x2;
    private bool _passthrough;
    private bool _muted;

    public AntiResonator(double sampleRate)
    {
        _sampleRate = sampleRate;
        _x1 = 0;
        _x2 = 0;
        _passthrough = true;
        _muted = false;
    }

    public void Set(double f, double bw)
    {
        if (!(f > 0) || !(f < _sampleRate / 2) || !(bw > 0) ||
            !MathUtil.IsFinite(f) || !MathUtil.IsFinite(bw))
        {
            throw new ArgumentException("Invalid anti-resonator parameters.");
        }

        double r = Math.Exp(-Math.PI * bw / _sampleRate);
        double w = 2 * Math.PI * f / _sampleRate;
        double c0 = -(r * r);
        double b0 = 2 * r * Math.Cos(w);
        double a0 = 1 - b0 - c0;

        // For bw > 0, a0 is strictly > 0, so this branch is practically unreachable.
        if (a0 == 0)
        {
            _a = 0;
            _b = 0;
            _c = 0;
            return;
        }

        _a = 1 / a0;
        _b = -b0 / a0;
        _c = -c0 / a0;

        _passthrough = false;
        _muted = false;
    }

    public void SetPassthrough()
    {
        _passthrough = true;
        _muted = false;
        _x1 = 0;
        _x2 = 0;
    }

    public void SetMute()
    {
        _passthrough = false;
        _muted = true;
        _x1 = 0;
        _x2 = 0;
    }

    public RationalPoly GetTransferFunction()
    {
        if (_passthrough) return RationalPoly.PassThrough;
        if (_muted) return RationalPoly.Mute;
        return new RationalPoly(new[] { _a, _b, _c }, new[] { 1.0 });
    }

    public double Step(double x)
    {
        if (_passthrough) return x;
        if (_muted) return 0;

        double y = _a * x + _b * _x1 + _c * _x2;
        _x2 = _x1;
        _x1 = x;
        return y;
    }
}

/// <summary>First-order FIR differencing filter: y[n] = x[n] - x[n-1].</summary>
internal sealed class DifferencingFilter
{
    private double _x1;

    public DifferencingFilter() => _x1 = 0;

    public RationalPoly GetTransferFunction() => new RationalPoly(new[] { 1.0, -1.0 }, new[] { 1.0 });

    public double Step(double x)
    {
        double y = x - _x1;
        _x1 = x;
        return y;
    }
}

// -------------------------------------------------------------------------
// Noise sources
// -------------------------------------------------------------------------

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

// -------------------------------------------------------------------------
// Glottal sources
// -------------------------------------------------------------------------

/// <summary>Generates a glottal source signal by LP filtering a pulse train.</summary>
internal sealed class ImpulsiveGlottalSource
{
    private readonly double _sampleRate;
    private Resonator? _resonator; // used as LP filter
    private int _positionInPeriod;

    public ImpulsiveGlottalSource(double sampleRate)
    {
        _sampleRate = sampleRate;
        _resonator = null;
    }

    public void StartPeriod(int openPhaseLength)
    {
        if (openPhaseLength <= 0)
        {
            _resonator = null;
            return;
        }

        _resonator ??= new Resonator(_sampleRate);

        double bw = _sampleRate / openPhaseLength;
        _resonator.Set(0, bw);
        _resonator.AdjustImpulseGain(1);
        _positionInPeriod = 0;
    }

    public double GetNext()
    {
        if (_resonator == null) return 0;

        double pulse = (_positionInPeriod == 1) ? 1 :
            (_positionInPeriod == 2) ? -1 : 0;

        _positionInPeriod++;
        return _resonator.Step(pulse);
    }
}

/// <summary>
/// "Natural" glottal source (KLGLOTT88) based on derivative of t^2 - t^3:
/// derivative is 2t - 3t^2.
/// </summary>
internal sealed class NaturalGlottalSource
{
    private double _x;
    private double _a;
    private double _b;
    private int _openPhaseLength;
    private int _positionInPeriod;

    public NaturalGlottalSource() => StartPeriod(0);

    public void StartPeriod(int openPhaseLength)
    {
        _openPhaseLength = openPhaseLength;
        _x = 0;
        _positionInPeriod = 0;

        if (_openPhaseLength <= 0)
        {
            _a = 0;
            _b = 0;
            return;
        }

        const double amplification = 5;
        _b = -amplification / (_openPhaseLength * (double)_openPhaseLength);
        _a = -_b * _openPhaseLength / 3.0;
    }

    public double GetNext()
    {
        if (_positionInPeriod++ >= _openPhaseLength)
        {
            _x = 0;
            return 0;
        }

        _a += _b;
        _x += _a;
        return _x;
    }
}

// -------------------------------------------------------------------------
// Main generator (controller)
// -------------------------------------------------------------------------

internal sealed class FrameState
{
    public double BreathinessLin;
    public double GainLin;

    public double CascadeVoicingLin;
    public double CascadeAspirationLin;

    public double ParallelVoicingLin;
    public double ParallelAspirationLin;
    public double FricationLin;
    public double ParallelBypassLin;
}

internal sealed class PeriodState
{
    public double F0;
    public int PeriodLength;
    public int OpenPhaseLength;

    public int PositionInPeriod;
}

/// <summary>Sound generator controller.</summary>
public sealed class Generator
{
    private readonly MainParameters _mParms;
    private FrameParameters? _fParms;              // currently active frame parameters
    private FrameParameters? _newFParms;           // new frame parameters for start of next F0 period
    private readonly FrameState _fState;
    private PeriodState? _pState;
    private long _absPosition;
    private readonly LpFilter1 _tiltFilter;
    private readonly Resonator _outputLpFilter;
    private readonly double _flutterTimeOffset;

    // Glottal sources
    private ImpulsiveGlottalSource? _impulsiveGSource;
    private NaturalGlottalSource? _naturalGSource;
    private Func<double> _glottalSource = () => 0;

    // Random + noise sources
    private readonly Random _rng;
    private readonly LpNoiseSource _aspirationSourceCasc;
    private readonly LpNoiseSource _aspirationSourcePar;
    private readonly LpNoiseSource _fricationSourcePar;

    // Cascade branch
    private readonly Resonator _nasalFormantCasc;
    private readonly AntiResonator _nasalAntiformantCasc;
    private readonly Resonator[] _oralFormantCasc;

    // Parallel branch
    private readonly Resonator _nasalFormantPar;
    private readonly Resonator[] _oralFormantPar;
    private readonly DifferencingFilter _differencingFilterPar;

    public Generator(MainParameters mParms, int? randomSeed = null)
    {
        _mParms = mParms ?? throw new ArgumentNullException(nameof(mParms));
        _rng = randomSeed.HasValue ? new Random(randomSeed.Value) : new Random();

        _fState = new FrameState();
        _absPosition = 0;

        _tiltFilter = new LpFilter1(_mParms.SampleRate);
        _flutterTimeOffset = _rng.NextDouble() * 1000.0;

        _outputLpFilter = new Resonator(_mParms.SampleRate);
        _outputLpFilter.Set(0, _mParms.SampleRate / 2);

        InitGlottalSource();

        // Noise sources (independent streams would require independent RNGs;
        // this port keeps a single RNG like the TS version's Math.random()).
        _aspirationSourceCasc = new LpNoiseSource(_mParms.SampleRate, _rng);
        _aspirationSourcePar = new LpNoiseSource(_mParms.SampleRate, _rng);
        _fricationSourcePar = new LpNoiseSource(_mParms.SampleRate, _rng);

        // Cascade branch filters:
        _nasalFormantCasc = new Resonator(_mParms.SampleRate);
        _nasalAntiformantCasc = new AntiResonator(_mParms.SampleRate);
        _oralFormantCasc = new Resonator[Constants.MaxOralFormants];
        for (int i = 0; i < Constants.MaxOralFormants; i++)
            _oralFormantCasc[i] = new Resonator(_mParms.SampleRate);

        // Parallel branch filters:
        _nasalFormantPar = new Resonator(_mParms.SampleRate);
        _oralFormantPar = new Resonator[Constants.MaxOralFormants];
        for (int i = 0; i < Constants.MaxOralFormants; i++)
            _oralFormantPar[i] = new Resonator(_mParms.SampleRate);

        _differencingFilterPar = new DifferencingFilter();
    }

    /// <summary>
    /// Generates a frame. The length is determined by <paramref name="outBuf"/>; FrameParameters.Duration is ignored.
    /// </summary>
    public void GenerateFrame(FrameParameters fParms, double[] outBuf)
    {
        if (fParms == null) throw new ArgumentNullException(nameof(fParms));
        if (outBuf == null) throw new ArgumentNullException(nameof(outBuf));

        if (_fParms != null && ReferenceEquals(fParms, _fParms))
            throw new ArgumentException("FrameParameters structure must not be re-used (matches TS behavior).", nameof(fParms));

        _newFParms = fParms;

        for (int outPos = 0; outPos < outBuf.Length; outPos++)
        {
            if (_pState == null || _pState.PositionInPeriod >= _pState.PeriodLength)
                StartNewPeriod();

            outBuf[outPos] = ComputeNextOutputSignalSample();
            _pState!.PositionInPeriod++;
            _absPosition++;
        }

        if (double.IsNaN(fParms.GainDb))
        {
            // Automatic gain control (AGC)
            SignalUtil.AdjustSignalGain(outBuf, fParms.AgcRmsLevel);
        }
    }

    private double ComputeNextOutputSignalSample()
    {
        var fParms = _fParms!;
        var fState = _fState;
        var pState = _pState!;

        double voice = _glottalSource();
        voice = _tiltFilter.Step(voice); // spectral tilt

        if (pState.PositionInPeriod < pState.OpenPhaseLength)
        {
            voice += MathUtil.WhiteNoise(_rng) * fState.BreathinessLin; // turbulence
        }

        double cascadeOut = fParms.CascadeEnabled ? ComputeCascadeBranch(voice) : 0;
        double parallelOut = fParms.ParallelEnabled ? ComputeParallelBranch(voice) : 0;

        double outSample = cascadeOut + parallelOut;
        outSample = _outputLpFilter.Step(outSample);
        outSample *= fState.GainLin;

        return outSample;
    }

    private double ComputeCascadeBranch(double voice)
    {
        var fParms = _fParms!;
        var fState = _fState;
        var pState = _pState!;

        double cascadeVoice = voice * fState.CascadeVoicingLin;

        double currentAspirationMod = (pState.PositionInPeriod >= pState.PeriodLength / 2.0)
            ? fParms.CascadeAspirationMod
            : 0;

        double aspiration = _aspirationSourceCasc.GetNext() * fState.CascadeAspirationLin * (1 - currentAspirationMod);

        double v = cascadeVoice + aspiration;
        v = _nasalAntiformantCasc.Step(v);
        v = _nasalFormantCasc.Step(v);

        for (int i = 0; i < Constants.MaxOralFormants; i++)
            v = _oralFormantCasc[i].Step(v);

        return v;
    }

    private double ComputeParallelBranch(double voice)
    {
        var fParms = _fParms!;
        var fState = _fState;
        var pState = _pState!;

        double parallelVoice = voice * fState.ParallelVoicingLin;

        double currentAspirationMod = (pState.PositionInPeriod >= pState.PeriodLength / 2.0)
            ? fParms.ParallelAspirationMod
            : 0;

        double aspiration = _aspirationSourcePar.GetNext() * fState.ParallelAspirationLin * (1 - currentAspirationMod);
        double source = parallelVoice + aspiration;

        double sourceDifference = _differencingFilterPar.Step(source);

        double currentFricationMod = (pState.PositionInPeriod >= pState.PeriodLength / 2.0)
            ? fParms.FricationMod
            : 0;

        double fricationNoise = _fricationSourcePar.GetNext() * fState.FricationLin * (1 - currentFricationMod);
        double source2 = sourceDifference + fricationNoise;

        double v = 0;

        // nasal formant and F1 applied to source
        v += _nasalFormantPar.Step(source);
        v += _oralFormantPar[0].Step(source);

        // F2..F6 applied to differenced source + frication with alternating sign
        for (int i = 1; i < Constants.MaxOralFormants; i++)
        {
            double alternatingSign = (i % 2 == 0) ? 1 : -1; // refer to Klatt (1980) Fig. 13
            v += alternatingSign * _oralFormantPar[i].Step(source2);
        }

        // bypass applied to source2
        v += fState.ParallelBypassLin * source2;

        return v;
    }

    private void StartNewPeriod()
    {
        if (_newFParms != null)
        {
            // Activate new frame params only at period boundaries (reduces glitches).
            _fParms = _newFParms;
            _newFParms = null;
            StartUsingNewFrameParameters();
        }

        _pState ??= new PeriodState();

        var pState = _pState;
        var mParms = _mParms;
        var fParms = _fParms!;

        double flutterTime = (_absPosition / mParms.SampleRate) + _flutterTimeOffset;
        pState.F0 = MathUtil.PerformFrequencyModulation(fParms.F0, fParms.FlutterLevel, flutterTime);

        pState.PeriodLength = (pState.F0 > 0)
            ? (int)Math.Round(mParms.SampleRate / pState.F0, MidpointRounding.AwayFromZero)
            : 1;

        pState.OpenPhaseLength = (pState.PeriodLength > 1)
            ? (int)Math.Round(pState.PeriodLength * fParms.OpenPhaseRatio, MidpointRounding.AwayFromZero)
            : 0;

        pState.PositionInPeriod = 0;

        StartGlottalSourcePeriod();
    }

    private void StartUsingNewFrameParameters()
    {
        var mParms = _mParms;
        var fParms = _fParms!;
        var fState = _fState;

        fState.BreathinessLin = MathUtil.DbToLin(fParms.BreathinessDb);

        // TS uses: dbToLin(fParms.gainDb || 0). (NaN is falsy in JS, so NaN => 0)
        double gainDb = double.IsNaN(fParms.GainDb) ? 0 : fParms.GainDb;
        fState.GainLin = MathUtil.DbToLin(gainDb);

        KlattHelpers.SetTiltFilter(_tiltFilter, fParms.TiltDb);

        // Cascade branch:
        fState.CascadeVoicingLin = MathUtil.DbToLin(fParms.CascadeVoicingDb);
        fState.CascadeAspirationLin = MathUtil.DbToLin(fParms.CascadeAspirationDb);

        KlattHelpers.SetNasalFormantCasc(_nasalFormantCasc, fParms);
        KlattHelpers.SetNasalAntiformantCasc(_nasalAntiformantCasc, fParms);

        for (int i = 0; i < Constants.MaxOralFormants; i++)
            KlattHelpers.SetOralFormantCasc(_oralFormantCasc[i], fParms, i);

        // Parallel branch:
        fState.ParallelVoicingLin = MathUtil.DbToLin(fParms.ParallelVoicingDb);
        fState.ParallelAspirationLin = MathUtil.DbToLin(fParms.ParallelAspirationDb);
        fState.FricationLin = MathUtil.DbToLin(fParms.FricationDb);
        fState.ParallelBypassLin = MathUtil.DbToLin(fParms.ParallelBypassDb);

        KlattHelpers.SetNasalFormantPar(_nasalFormantPar, fParms);

        for (int i = 0; i < Constants.MaxOralFormants; i++)
            KlattHelpers.SetOralFormantPar(_oralFormantPar[i], mParms, fParms, i);
    }

    private void InitGlottalSource()
    {
        switch (_mParms.GlottalSourceType)
        {
            case GlottalSourceType.Impulsive:
                _impulsiveGSource = new ImpulsiveGlottalSource(_mParms.SampleRate);
                _glottalSource = () => _impulsiveGSource.GetNext();
                break;

            case GlottalSourceType.Natural:
                _naturalGSource = new NaturalGlottalSource();
                _glottalSource = () => _naturalGSource.GetNext();
                break;

            case GlottalSourceType.Noise:
                _glottalSource = () => MathUtil.WhiteNoise(_rng);
                break;

            default:
                throw new InvalidOperationException("Undefined glottal source type.");
        }
    }

    private void StartGlottalSourcePeriod()
    {
        int openPhaseLength = _pState!.OpenPhaseLength;

        switch (_mParms.GlottalSourceType)
        {
            case GlottalSourceType.Impulsive:
                _impulsiveGSource!.StartPeriod(openPhaseLength);
                break;

            case GlottalSourceType.Natural:
                _naturalGSource!.StartPeriod(openPhaseLength);
                break;

            default:
                // noise source: no per-period init
                break;
        }
    }
}

// -------------------------------------------------------------------------
// Helpers (parameter application, utility math, etc.)
// -------------------------------------------------------------------------

internal static class KlattHelpers
{
    public static void SetTiltFilter(LpFilter1 tiltFilter, double tiltDb)
    {
        if (tiltDb == 0 || double.IsNaN(tiltDb))
        {
            tiltFilter.SetPassthrough();
        }
        else
        {
            // Attenuation at 3 kHz: set LP gain at 3000 Hz
            tiltFilter.Set(3000, MathUtil.DbToLin(-tiltDb));
        }
    }

    public static void SetNasalFormantCasc(Resonator nasalFormantCasc, FrameParameters fParms)
    {
        if (MathUtil.IsUsableFreqBw(fParms.NasalFormantFreq, fParms.NasalFormantBw))
            nasalFormantCasc.Set(fParms.NasalFormantFreq, fParms.NasalFormantBw);
        else
            nasalFormantCasc.SetPassthrough();
    }

    public static void SetNasalAntiformantCasc(AntiResonator nasalAntiformantCasc, FrameParameters fParms)
    {
        if (MathUtil.IsUsableFreqBw(fParms.NasalAntiformantFreq, fParms.NasalAntiformantBw))
            nasalAntiformantCasc.Set(fParms.NasalAntiformantFreq, fParms.NasalAntiformantBw);
        else
            nasalAntiformantCasc.SetPassthrough();
    }

    public static void SetOralFormantCasc(Resonator oralFormantCasc, FrameParameters fParms, int i)
    {
        double f = (fParms.OralFormantFreq != null && i < fParms.OralFormantFreq.Length) ? fParms.OralFormantFreq[i] : double.NaN;
        double bw = (fParms.OralFormantBw != null && i < fParms.OralFormantBw.Length) ? fParms.OralFormantBw[i] : double.NaN;

        if (MathUtil.IsUsableFreqBw(f, bw))
            oralFormantCasc.Set(f, bw);
        else
            oralFormantCasc.SetPassthrough();
    }

    public static void SetNasalFormantPar(Resonator nasalFormantPar, FrameParameters fParms)
    {
        double peakGain = MathUtil.DbToLin(fParms.NasalFormantDb);

        if (MathUtil.IsUsableFreqBw(fParms.NasalFormantFreq, fParms.NasalFormantBw) && peakGain > 0)
        {
            nasalFormantPar.Set(fParms.NasalFormantFreq, fParms.NasalFormantBw);
            nasalFormantPar.AdjustPeakGain(peakGain);
        }
        else
        {
            nasalFormantPar.SetMute();
        }
    }

    public static void SetOralFormantPar(Resonator oralFormantPar, MainParameters mParms, FrameParameters fParms, int i)
    {
        int formant = i + 1;

        double f = (fParms.OralFormantFreq != null && i < fParms.OralFormantFreq.Length) ? fParms.OralFormantFreq[i] : double.NaN;
        double bw = (fParms.OralFormantBw != null && i < fParms.OralFormantBw.Length) ? fParms.OralFormantBw[i] : double.NaN;
        double db = (fParms.OralFormantDb != null && i < fParms.OralFormantDb.Length) ? fParms.OralFormantDb[i] : double.NaN;

        double peakGain = MathUtil.DbToLin(db);

        if (MathUtil.IsUsableFreqBw(f, bw) && peakGain > 0)
        {
            oralFormantPar.Set(f, bw);

            double w = 2 * Math.PI * f / mParms.SampleRate;
            double diffGain = Math.Sqrt(2 - 2 * Math.Cos(w)); // differencing filter gain

            double filterGain = (formant >= 2) ? (peakGain / diffGain) : peakGain;
            oralFormantPar.AdjustPeakGain(filterGain);
        }
        else
        {
            oralFormantPar.SetMute();
        }
    }
}

internal static class MathUtil
{
    public static bool IsFinite(double x) => !(double.IsNaN(x) || double.IsInfinity(x));

    public static bool IsUsableFreqBw(double f, double bw)
    {
        // Mirrors JS truthiness checks (NaN and 0 are treated as "off").
        return IsFinite(f) && IsFinite(bw) && f != 0 && bw != 0;
    }

    /// <summary>White noise in range [-1, 1).</summary>
    public static double WhiteNoise(Random rng) => (rng.NextDouble() * 2.0) - 1.0;

    /// <summary>F0 flutter modulation.</summary>
    public static double PerformFrequencyModulation(double f0, double flutterLevel, double timeSeconds)
    {
        if (flutterLevel <= 0) return f0;

        double w = 2 * Math.PI * timeSeconds;
        double a = Math.Sin(12.7 * w) + Math.Sin(7.1 * w) + Math.Sin(4.7 * w);
        return f0 * (1 + a * flutterLevel / 50.0);
    }

    /// <summary>Convert dB to linear. dB &lt;= -99 or NaN => 0.</summary>
    public static double DbToLin(double db)
    {
        if (db <= -99 || double.IsNaN(db)) return 0;
        return Math.Pow(10, db / 20.0);
    }
}

internal static class SignalUtil
{
    public static void AdjustSignalGain(double[] buf, double targetRms)
    {
        int n = buf.Length;
        if (n == 0) return;

        double rms = ComputeRms(buf);
        if (rms == 0) return;

        double r = targetRms / rms;

        double maxAbs = FindMaxAbsValue(buf);
        if (maxAbs == 0) return;

        if ((r * maxAbs) >= 1)
        {
            // Prevent clipping
            r = 0.99 / maxAbs;
        }

        for (int i = 0; i < n; i++)
            buf[i] *= r;
    }

    public static double ComputeRms(double[] buf)
    {
        int n = buf.Length;
        if (n == 0) return 0;

        double acc = 0;
        for (int i = 0; i < n; i++)
            acc += buf[i] * buf[i];

        return Math.Sqrt(acc / n);
    }

    public static double FindMaxAbsValue(double[] buf)
    {
        int n = buf.Length;
        double maxAbs = 0;
        for (int i = 0; i < n; i++)
        {
            double v = Math.Abs(buf[i]);
            if (v > maxAbs) maxAbs = v;
        }
        return maxAbs;
    }
}

// -------------------------------------------------------------------------
// Public convenience functions (matching TS exports)
// -------------------------------------------------------------------------

public static class Klatt
{
    /// <summary>Generates a sound consisting of multiple frames.</summary>
    public static double[] GenerateSound(MainParameters mParms, IReadOnlyList<FrameParameters> frames)
    {
        if (mParms == null) throw new ArgumentNullException(nameof(mParms));
        if (frames == null) throw new ArgumentNullException(nameof(frames));

        var generator = new Generator(mParms);

        int outBufLen = 0;
        for (int i = 0; i < frames.Count; i++)
            outBufLen += (int)Math.Round(frames[i].Duration * mParms.SampleRate, MidpointRounding.AwayFromZero);

        var outBuf = new double[outBufLen];

        int outPos = 0;
        for (int i = 0; i < frames.Count; i++)
        {
            int frameLen = (int)Math.Round(frames[i].Duration * mParms.SampleRate, MidpointRounding.AwayFromZero);
            var frameBuf = new double[frameLen];

            generator.GenerateFrame(frames[i], frameBuf);

            Array.Copy(frameBuf, 0, outBuf, outPos, frameLen);
            outPos += frameLen;
        }

        return outBuf;
    }

    private const double Eps = 1E-10;

    /// <summary>
    /// Returns overall vocal-tract transfer function (numerator/denominator polynomials in z^-1).
    /// </summary>
    public static (double[] Numerator, double[] Denominator) GetVocalTractTransferFunctionCoefficients(
        MainParameters mParms,
        FrameParameters fParms)
    {
        if (mParms == null) throw new ArgumentNullException(nameof(mParms));
        if (fParms == null) throw new ArgumentNullException(nameof(fParms));

        RationalPoly voice = RationalPoly.PassThrough; // glottal source

        var tiltFilter = new LpFilter1(mParms.SampleRate);
        KlattHelpers.SetTiltFilter(tiltFilter, fParms.TiltDb);
        voice = voice.Multiply(tiltFilter.GetTransferFunction(), Eps);

        RationalPoly cascadeTrans = fParms.CascadeEnabled
            ? GetCascadeBranchTransferFunctionCoefficients(mParms, fParms)
            : RationalPoly.Mute;

        RationalPoly parallelTrans = fParms.ParallelEnabled
            ? GetParallelBranchTransferFunctionCoefficients(mParms, fParms)
            : RationalPoly.Mute;

        RationalPoly branchesTrans = cascadeTrans.Add(parallelTrans, Eps);

        RationalPoly outTf = voice.Multiply(branchesTrans, Eps);

        var outputLpFilter = new Resonator(mParms.SampleRate);
        outputLpFilter.Set(0, mParms.SampleRate / 2);
        outTf = outTf.Multiply(outputLpFilter.GetTransferFunction(), Eps);

        double gainDb = double.IsNaN(fParms.GainDb) ? 0 : fParms.GainDb;
        double gainLin = MathUtil.DbToLin(gainDb);
        outTf = outTf.Multiply(new RationalPoly(new[] { gainLin }, new[] { 1.0 }), Eps);

        return (outTf.Num, outTf.Den);
    }

    private static RationalPoly GetCascadeBranchTransferFunctionCoefficients(MainParameters mParms, FrameParameters fParms)
    {
        double cascadeVoicingLin = MathUtil.DbToLin(fParms.CascadeVoicingDb);
        RationalPoly v = new RationalPoly(new[] { cascadeVoicingLin }, new[] { 1.0 });

        var nasalAntiformantCasc = new AntiResonator(mParms.SampleRate);
        KlattHelpers.SetNasalAntiformantCasc(nasalAntiformantCasc, fParms);
        v = v.Multiply(nasalAntiformantCasc.GetTransferFunction(), Eps);

        var nasalFormantCasc = new Resonator(mParms.SampleRate);
        KlattHelpers.SetNasalFormantCasc(nasalFormantCasc, fParms);
        v = v.Multiply(nasalFormantCasc.GetTransferFunction(), Eps);

        for (int i = 0; i < Constants.MaxOralFormants; i++)
        {
            var oral = new Resonator(mParms.SampleRate);
            KlattHelpers.SetOralFormantCasc(oral, fParms, i);
            v = v.Multiply(oral.GetTransferFunction(), Eps);
        }

        return v;
    }

    private static RationalPoly GetParallelBranchTransferFunctionCoefficients(MainParameters mParms, FrameParameters fParms)
    {
        double parallelVoicingLin = MathUtil.DbToLin(fParms.ParallelVoicingDb);
        RationalPoly source = new RationalPoly(new[] { parallelVoicingLin }, new[] { 1.0 });

        var diff = new DifferencingFilter();
        RationalPoly source2 = source.Multiply(diff.GetTransferFunction(), Eps);

        RationalPoly v = RationalPoly.Mute; // 0

        var nasalFormantPar = new Resonator(mParms.SampleRate);
        KlattHelpers.SetNasalFormantPar(nasalFormantPar, fParms);
        v = v.Add(source.Multiply(nasalFormantPar.GetTransferFunction(), Eps), Eps);

        for (int i = 0; i < Constants.MaxOralFormants; i++)
        {
            var oral = new Resonator(mParms.SampleRate);
            KlattHelpers.SetOralFormantPar(oral, mParms, fParms, i);

            RationalPoly formantIn = (i == 0) ? source : source2;
            RationalPoly formantOut = formantIn.Multiply(oral.GetTransferFunction(), Eps);

            double alternatingSign = (i % 2 == 0) ? 1.0 : -1.0;
            formantOut = formantOut.Multiply(new RationalPoly(new[] { alternatingSign }, new[] { 1.0 }), Eps);

            v = v.Add(formantOut, Eps);
        }

        double parallelBypassLin = MathUtil.DbToLin(fParms.ParallelBypassDb);
        RationalPoly bypass = source2.Multiply(new RationalPoly(new[] { parallelBypassLin }, new[] { 1.0 }), Eps);
        v = v.Add(bypass, Eps);

        return v;
    }
}

// -------------------------------------------------------------------------
// Minimal "PolyReal" replacement: polynomial fractions in z^-1
// -------------------------------------------------------------------------

internal readonly struct RationalPoly
{
    public RationalPoly(double[] num, double[] den)
    {
        Num = num ?? throw new ArgumentNullException(nameof(num));
        Den = den ?? throw new ArgumentNullException(nameof(den));
        if (den.Length == 0) throw new ArgumentException("Denominator must not be empty.", nameof(den));
    }

    public double[] Num { get; }
    public double[] Den { get; }

    public static RationalPoly PassThrough => new RationalPoly(new[] { 1.0 }, new[] { 1.0 });
    public static RationalPoly Mute => new RationalPoly(new[] { 0.0 }, new[] { 1.0 });

    public RationalPoly Multiply(RationalPoly other, double eps = 0)
    {
        var num = PolyOps.Convolve(Num, other.Num);
        var den = PolyOps.Convolve(Den, other.Den);
        if (eps > 0)
        {
            num = PolyOps.Trim(num, eps);
            den = PolyOps.Trim(den, eps);
        }
        return new RationalPoly(num, den);
    }

    public RationalPoly Add(RationalPoly other, double eps = 0)
    {
        // a/b + c/d = (a*d + c*b) / (b*d)
        var ad = PolyOps.Convolve(Num, other.Den);
        var cb = PolyOps.Convolve(other.Num, Den);
        var num = PolyOps.AddPolys(ad, cb);
        var den = PolyOps.Convolve(Den, other.Den);

        if (eps > 0)
        {
            num = PolyOps.Trim(num, eps);
            den = PolyOps.Trim(den, eps);
        }

        return new RationalPoly(num, den);
    }
}

internal static class PolyOps
{
    public static double[] Convolve(double[] a, double[] b)
    {
        int na = a.Length;
        int nb = b.Length;
        var y = new double[na + nb - 1];

        for (int i = 0; i < na; i++)
        {
            double ai = a[i];
            for (int j = 0; j < nb; j++)
                y[i + j] += ai * b[j];
        }

        return y;
    }

    public static double[] AddPolys(double[] a, double[] b)
    {
        int n = Math.Max(a.Length, b.Length);
        var y = new double[n];

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
        var q = (double[])p.Clone();

        for (int i = 0; i < q.Length; i++)
        {
            if (Math.Abs(q[i]) < eps) q[i] = 0;
        }

        int last = q.Length - 1;
        while (last > 0 && Math.Abs(q[last]) <= eps)
            last--;

        if (last == q.Length - 1)
            return q;

        var r = new double[last + 1];
        Array.Copy(q, 0, r, 0, last + 1);
        return r;
    }
}