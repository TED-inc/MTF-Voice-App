using System;
using MTFVoiceTools.KlattSynth.Filters;
using MTFVoiceTools.KlattSynth.Params;
using MTFVoiceTools.KlattSynth.Sources;
using MTFVoiceTools.KlattSynth.Utils;

namespace MTFVoiceTools.KlattSynth.MainGenerator;

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
        _outputLpFilter.Set(0, _mParms.SampleRate / 2d);

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
        {
            _oralFormantCasc[i] = new Resonator(_mParms.SampleRate);
        }

        // Parallel branch filters:
        _nasalFormantPar = new Resonator(_mParms.SampleRate);
        _oralFormantPar = new Resonator[Constants.MaxOralFormants];
        for (int i = 0; i < Constants.MaxOralFormants; i++)
        {
            _oralFormantPar[i] = new Resonator(_mParms.SampleRate);
        }

        _differencingFilterPar = new DifferencingFilter();
    }

    /// <summary>
    /// Generates a frame. The length is determined by <paramref name="outputBuffer"/>; FrameParameters.Duration is ignored.
    /// </summary>
    public void GenerateFrame(FrameParameters frameParams, double[] outputBuffer)
    {
        if (frameParams == null)
        {
            throw new ArgumentNullException(nameof(frameParams));
        }

        if (outputBuffer == null)
        {
            throw new ArgumentNullException(nameof(outputBuffer));
        }

        if (_fParms != null && ReferenceEquals(frameParams, _fParms))
        {
            throw new ArgumentException("FrameParameters structure must not be re-used (matches TS behavior).", nameof(frameParams));
        }

        _newFParms = frameParams;

        for (int outPos = 0; outPos < outputBuffer.Length; outPos++)
        {
            if (_pState == null || _pState.PositionInPeriod >= _pState.PeriodLength)
            {
                StartNewPeriod();
            }

            outputBuffer[outPos] = ComputeNextOutputSignalSample();
            _pState!.PositionInPeriod++;
            _absPosition++;
        }

        if (double.IsNaN(frameParams.GainDb))
        {
            // Automatic gain control (AGC)
            SignalUtil.AdjustSignalGain(outputBuffer, frameParams.AgcRmsLevel);
        }
    }

    private double ComputeNextOutputSignalSample()
    {
        FrameParameters fParms = _fParms!;
        FrameState fState = _fState;
        PeriodState pState = _pState!;

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
        FrameParameters fParms = _fParms!;
        FrameState fState = _fState;
        PeriodState pState = _pState!;

        double cascadeVoice = voice * fState.CascadeVoicingLin;

        double currentAspirationMod = (pState.PositionInPeriod >= pState.PeriodLength / 2.0)
            ? fParms.CascadeAspirationMod
            : 0;

        double aspiration = _aspirationSourceCasc.GetNext() * fState.CascadeAspirationLin * (1 - currentAspirationMod);

        double v = cascadeVoice + aspiration;
        v = _nasalAntiformantCasc.Step(v);
        v = _nasalFormantCasc.Step(v);

        for (int i = 0; i < Constants.MaxOralFormants; i++)
        {
            v = _oralFormantCasc[i].Step(v);
        }

        return v;
    }

    private double ComputeParallelBranch(double voice)
    {
        FrameParameters fParms = _fParms!;
        FrameState fState = _fState;
        PeriodState pState = _pState!;

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

        PeriodState? pState = _pState;
        MainParameters mParms = _mParms;
        FrameParameters fParms = _fParms!;

        double flutterTime = (_absPosition / (double)mParms.SampleRate) + _flutterTimeOffset;
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
        MainParameters mParms = _mParms;
        FrameParameters fParms = _fParms!;
        FrameState fState = _fState;

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
        {
            KlattHelpers.SetOralFormantCasc(_oralFormantCasc[i], fParms, i);
        }

        // Parallel branch:
        fState.ParallelVoicingLin = MathUtil.DbToLin(fParms.ParallelVoicingDb);
        fState.ParallelAspirationLin = MathUtil.DbToLin(fParms.ParallelAspirationDb);
        fState.FricationLin = MathUtil.DbToLin(fParms.FricationDb);
        fState.ParallelBypassLin = MathUtil.DbToLin(fParms.ParallelBypassDb);

        KlattHelpers.SetNasalFormantPar(_nasalFormantPar, fParms);

        for (int i = 0; i < Constants.MaxOralFormants; i++)
        {
            KlattHelpers.SetOralFormantPar(_oralFormantPar[i], mParms, fParms, i);
        }
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