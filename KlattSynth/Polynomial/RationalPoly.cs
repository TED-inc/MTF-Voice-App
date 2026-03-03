using System;

namespace MTFVoiceTools.KlattSynth.Polynomial;

internal readonly struct RationalPoly
{
    public RationalPoly(double[] num, double[] den)
    {
        Num = num ?? throw new ArgumentNullException(nameof(num));
        Den = den ?? throw new ArgumentNullException(nameof(den));
        if (den.Length == 0)
        {
            throw new ArgumentException("Denominator must not be empty.", nameof(den));
        }
    }

    public double[] Num { get; }
    public double[] Den { get; }

    public static RationalPoly PassThrough => new(new[] { 1.0 }, new[] { 1.0 });
    public static RationalPoly Mute => new(new[] { 0.0 }, new[] { 1.0 });

    public RationalPoly Multiply(RationalPoly other, double eps = 0)
    {
        double[] num = PolyOps.Convolve(Num, other.Num);
        double[] den = PolyOps.Convolve(Den, other.Den);
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
        double[] ad = PolyOps.Convolve(Num, other.Den);
        double[] cb = PolyOps.Convolve(other.Num, Den);
        double[] num = PolyOps.AddPolys(ad, cb);
        double[] den = PolyOps.Convolve(Den, other.Den);

        if (eps > 0)
        {
            num = PolyOps.Trim(num, eps);
            den = PolyOps.Trim(den, eps);
        }

        return new RationalPoly(num, den);
    }
}