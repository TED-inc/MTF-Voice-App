using System;
using MTFVoiceTools.KlattSynth.Polynomial;
using MTFVoiceTools.KlattSynth.Utils;

namespace MTFVoiceTools.KlattSynth.Filters;

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
        if (_passthrough)
        {
            return RationalPoly.PassThrough;
        }

        if (_muted)
        {
            return RationalPoly.Mute;
        }

        return new RationalPoly(new[] { _a, _b, _c }, new[] { 1.0 });
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

        double y = _a * x + _b * _x1 + _c * _x2;
        _x2 = _x1;
        _x1 = x;
        return y;
    }
}