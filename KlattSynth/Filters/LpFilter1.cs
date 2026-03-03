using System;
using MTFVoiceTools.KlattSynth.MainGenerator;
using MTFVoiceTools.KlattSynth.Polynomial;
using MTFVoiceTools.KlattSynth.Utils;

namespace MTFVoiceTools.KlattSynth.Filters;

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
        if (_passthrough)
        {
            return RationalPoly.PassThrough;
        }

        if (_muted)
        {
            return RationalPoly.Mute;
        }

        return new RationalPoly(new[] { _a }, new[] { 1.0, -_b });
    }

    public double Step(double x)
    {
        if (_passthrough)
        {
            return x;
        }

        if (_muted)
        {
            return 0;
        }

        double y = _a * x + _b * _y1;
        _y1 = y;
        return y;
    }
}