using System;
using MTFVoiceTools.KlattSynth.Polynomial;
using MTFVoiceTools.KlattSynth.Utils;

namespace MTFVoiceTools.KlattSynth.Filters;

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
        {
            throw new ArgumentException("Invalid resonator peak gain.");
        }

        _a = peakGain * (1 - _r);
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

        return new RationalPoly(new[] { _a }, new[] { 1.0, -_b, -_c });
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

        double y = _a * x + _b * _y1 + _c * _y2;
        _y2 = _y1;
        _y1 = y;
        return y;
    }
}