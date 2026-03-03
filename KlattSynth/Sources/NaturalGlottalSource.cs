namespace MTFVoiceTools.KlattSynth.Sources;

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