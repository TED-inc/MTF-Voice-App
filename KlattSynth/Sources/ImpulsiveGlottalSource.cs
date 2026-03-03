using MTFVoiceTools.KlattSynth.Filters;

namespace MTFVoiceTools.KlattSynth.Sources;

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
        if (_resonator == null)
        {
            return 0;
        }

        double pulse = (_positionInPeriod == 1) ? 1 :
            (_positionInPeriod == 2) ? -1 : 0;

        _positionInPeriod++;
        return _resonator.Step(pulse);
    }
}