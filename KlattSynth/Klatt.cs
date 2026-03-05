using System;
using System.Collections.Generic;
using System.Linq;
using MTFVoiceTools.KlattSynth.Filters;
using MTFVoiceTools.KlattSynth.MainGenerator;
using MTFVoiceTools.KlattSynth.Params;
using MTFVoiceTools.KlattSynth.Polynomial;
using MTFVoiceTools.KlattSynth.Utils;

namespace MTFVoiceTools.KlattSynth;

public static class Klatt
{
    private const double Eps = 1E-10;
    
    /// <summary>Generates a sound consisting of multiple frames.</summary>
    public static double[] GenerateSound(MainParameters param, IReadOnlyList<FrameParameters> frames)
    {
        if (param == null)
        {
            throw new ArgumentNullException(nameof(param));
        }

        if (frames == null)
        {
            throw new ArgumentNullException(nameof(frames));
        }

        Generator generator = new(param);

        int outBufLen = frames.Sum(GetFeameBufferLength);

        double[] outBufffer = new double[outBufLen];

        int outPosition = 0;
        foreach (FrameParameters frame in frames)
        {
            int frameLength = GetFeameBufferLength(frame);
            double[] frameBuffer = new double[frameLength];

            generator.GenerateFrame(frame, frameBuffer);

            Array.Copy(frameBuffer, 0, outBufffer, outPosition, frameLength);
            outPosition += frameLength;
        }

        return outBufffer;

        int GetFeameBufferLength(FrameParameters frame)
        {
            return (int)Math.Round(frame.Duration * param.SampleRate, MidpointRounding.AwayFromZero);
        }
    }

    /// <summary>
    /// Returns overall vocal-tract transfer function (numerator/denominator polynomials in z^-1).
    /// </summary>
    public static (double[] Numerator, double[] Denominator) GetVocalTractTransferFunctionCoefficients(
        MainParameters param,
        FrameParameters frame)
    {
        if (param == null)
        {
            throw new ArgumentNullException(nameof(param));
        }

        if (frame == null)
        {
            throw new ArgumentNullException(nameof(frame));
        }

        RationalPoly voice = RationalPoly.PassThrough; // glottal source

        LpFilter1 tiltFilter = new(param.SampleRate);
        KlattHelpers.SetTiltFilter(tiltFilter, frame.TiltDb);
        voice = voice.Multiply(tiltFilter.GetTransferFunction(), Eps);

        RationalPoly cascadeTrans = frame.CascadeEnabled
            ? GetCascadeBranchTransferFunctionCoefficients(param, frame)
            : RationalPoly.Mute;

        RationalPoly parallelTrans = frame.ParallelEnabled
            ? GetParallelBranchTransferFunctionCoefficients(param, frame)
            : RationalPoly.Mute;

        RationalPoly branchesTrans = cascadeTrans.Add(parallelTrans, Eps);

        RationalPoly outTf = voice.Multiply(branchesTrans, Eps);

        Resonator outputLpFilter = new(param.SampleRate);
        outputLpFilter.Set(0, param.SampleRate / 2d);
        outTf = outTf.Multiply(outputLpFilter.GetTransferFunction(), Eps);

        double gainDb = double.IsNaN(frame.GainDb) ? 0 : frame.GainDb;
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