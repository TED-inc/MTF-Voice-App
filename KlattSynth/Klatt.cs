using System;
using System.Collections.Generic;
using MTFVoiceTools.KlattSynth.Filters;
using MTFVoiceTools.KlattSynth.MainGenerator;
using MTFVoiceTools.KlattSynth.Params;
using MTFVoiceTools.KlattSynth.Polynomial;
using MTFVoiceTools.KlattSynth.Utils;

namespace MTFVoiceTools.KlattSynth;

public static class Klatt
{
    /// <summary>Generates a sound consisting of multiple frames.</summary>
    public static double[] GenerateSound(MainParameters mParms, IReadOnlyList<FrameParameters> frames)
    {
        if (mParms == null)
        {
            throw new ArgumentNullException(nameof(mParms));
        }

        if (frames == null)
        {
            throw new ArgumentNullException(nameof(frames));
        }

        Generator generator = new(mParms);

        int outBufLen = 0;
        for (int i = 0; i < frames.Count; i++)
        {
            outBufLen += (int)Math.Round(frames[i].Duration * mParms.SampleRate, MidpointRounding.AwayFromZero);
        }

        double[] outBuf = new double[outBufLen];

        int outPos = 0;
        for (int i = 0; i < frames.Count; i++)
        {
            int frameLen = (int)Math.Round(frames[i].Duration * mParms.SampleRate, MidpointRounding.AwayFromZero);
            double[] frameBuf = new double[frameLen];

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
        if (mParms == null)
        {
            throw new ArgumentNullException(nameof(mParms));
        }

        if (fParms == null)
        {
            throw new ArgumentNullException(nameof(fParms));
        }

        RationalPoly voice = RationalPoly.PassThrough; // glottal source

        LpFilter1 tiltFilter = new(mParms.SampleRate);
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

        Resonator outputLpFilter = new(mParms.SampleRate);
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
        RationalPoly v = new(new[] { cascadeVoicingLin }, new[] { 1.0 });

        AntiResonator nasalAntiformantCasc = new(mParms.SampleRate);
        KlattHelpers.SetNasalAntiformantCasc(nasalAntiformantCasc, fParms);
        v = v.Multiply(nasalAntiformantCasc.GetTransferFunction(), Eps);

        Resonator nasalFormantCasc = new(mParms.SampleRate);
        KlattHelpers.SetNasalFormantCasc(nasalFormantCasc, fParms);
        v = v.Multiply(nasalFormantCasc.GetTransferFunction(), Eps);

        for (int i = 0; i < Constants.MaxOralFormants; i++)
        {
            Resonator oral = new(mParms.SampleRate);
            KlattHelpers.SetOralFormantCasc(oral, fParms, i);
            v = v.Multiply(oral.GetTransferFunction(), Eps);
        }

        return v;
    }

    private static RationalPoly GetParallelBranchTransferFunctionCoefficients(MainParameters mParms, FrameParameters fParms)
    {
        double parallelVoicingLin = MathUtil.DbToLin(fParms.ParallelVoicingDb);
        RationalPoly source = new(new[] { parallelVoicingLin }, new[] { 1.0 });

        DifferencingFilter diff = new();
        RationalPoly source2 = source.Multiply(diff.GetTransferFunction(), Eps);

        RationalPoly v = RationalPoly.Mute; // 0

        Resonator nasalFormantPar = new(mParms.SampleRate);
        KlattHelpers.SetNasalFormantPar(nasalFormantPar, fParms);
        v = v.Add(source.Multiply(nasalFormantPar.GetTransferFunction(), Eps), Eps);

        for (int i = 0; i < Constants.MaxOralFormants; i++)
        {
            Resonator oral = new(mParms.SampleRate);
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