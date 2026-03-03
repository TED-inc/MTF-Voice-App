using System;
using MTFVoiceTools.KlattSynth.Filters;
using MTFVoiceTools.KlattSynth.MainGenerator;
using MTFVoiceTools.KlattSynth.Params;

namespace MTFVoiceTools.KlattSynth.Utils;

internal static class KlattHelpers
{
    public static void SetTiltFilter(LpFilter1 tiltFilter, double tiltDb)
    {
        if (tiltDb == 0 || double.IsNaN(tiltDb))
        {
            tiltFilter.SetPassthrough();
        }
        else
        {
            // Attenuation at 3 kHz: set LP gain at 3000 Hz
            tiltFilter.Set(3000, MathUtil.DbToLin(-tiltDb));
        }
    }

    public static void SetNasalFormantCasc(Resonator nasalFormantCasc, FrameParameters fParms)
    {
        if (MathUtil.IsUsableFreqBw(fParms.NasalFormantFreq, fParms.NasalFormantBw))
        {
            nasalFormantCasc.Set(fParms.NasalFormantFreq, fParms.NasalFormantBw);
        }
        else
        {
            nasalFormantCasc.SetPassthrough();
        }
    }

    public static void SetNasalAntiformantCasc(AntiResonator nasalAntiformantCasc, FrameParameters fParms)
    {
        if (MathUtil.IsUsableFreqBw(fParms.NasalAntiformantFreq, fParms.NasalAntiformantBw))
        {
            nasalAntiformantCasc.Set(fParms.NasalAntiformantFreq, fParms.NasalAntiformantBw);
        }
        else
        {
            nasalAntiformantCasc.SetPassthrough();
        }
    }

    public static void SetOralFormantCasc(Resonator oralFormantCasc, FrameParameters fParms, int i)
    {
        double f = (fParms.OralFormantFreq != null && i < fParms.OralFormantFreq.Length) ? fParms.OralFormantFreq[i] : double.NaN;
        double bw = (fParms.OralFormantBw != null && i < fParms.OralFormantBw.Length) ? fParms.OralFormantBw[i] : double.NaN;

        if (MathUtil.IsUsableFreqBw(f, bw))
        {
            oralFormantCasc.Set(f, bw);
        }
        else
        {
            oralFormantCasc.SetPassthrough();
        }
    }

    public static void SetNasalFormantPar(Resonator nasalFormantPar, FrameParameters fParms)
    {
        double peakGain = MathUtil.DbToLin(fParms.NasalFormantDb);

        if (MathUtil.IsUsableFreqBw(fParms.NasalFormantFreq, fParms.NasalFormantBw) && peakGain > 0)
        {
            nasalFormantPar.Set(fParms.NasalFormantFreq, fParms.NasalFormantBw);
            nasalFormantPar.AdjustPeakGain(peakGain);
        }
        else
        {
            nasalFormantPar.SetMute();
        }
    }

    public static void SetOralFormantPar(Resonator oralFormantPar, MainParameters mParms, FrameParameters fParms, int i)
    {
        int formant = i + 1;

        double f = (fParms.OralFormantFreq != null && i < fParms.OralFormantFreq.Length) ? fParms.OralFormantFreq[i] : double.NaN;
        double bw = (fParms.OralFormantBw != null && i < fParms.OralFormantBw.Length) ? fParms.OralFormantBw[i] : double.NaN;
        double db = (fParms.OralFormantDb != null && i < fParms.OralFormantDb.Length) ? fParms.OralFormantDb[i] : double.NaN;

        double peakGain = MathUtil.DbToLin(db);

        if (MathUtil.IsUsableFreqBw(f, bw) && peakGain > 0)
        {
            oralFormantPar.Set(f, bw);

            double w = 2 * Math.PI * f / mParms.SampleRate;
            double diffGain = Math.Sqrt(2 - 2 * Math.Cos(w)); // differencing filter gain

            double filterGain = (formant >= 2) ? (peakGain / diffGain) : peakGain;
            oralFormantPar.AdjustPeakGain(filterGain);
        }
        else
        {
            oralFormantPar.SetMute();
        }
    }
}