using MTFVoiceTools.KlattSynth.MainGenerator;
using MTFVoiceTools.KlattSynth.Polynomial;

namespace MTFVoiceTools.KlattSynth.Filters;

/// <summary>First-order FIR differencing filter: y[n] = x[n] - x[n-1].</summary>
internal sealed class DifferencingFilter
{
    private double _x1;

    public DifferencingFilter() => _x1 = 0;

    public RationalPoly GetTransferFunction() => new(new[] { 1.0, -1.0 }, new[] { 1.0 });

    public double Step(double x)
    {
        double y = x - _x1;
        _x1 = x;
        return y;
    }
}