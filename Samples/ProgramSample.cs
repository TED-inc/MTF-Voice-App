using System;
using System.IO;
using System.Threading.Tasks;
using NAudio.Dsp;
using NAudio.Wave;

namespace MTFVoiceTools.Samples;

internal class ProgramSample
{
    private static readonly string DOWNLOADS_PATH = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    public static async Task Main()
    {
        //await LiveRecorder.MakeRecord(Path.Combine(DOWNLOADS_PATH, "MTFvoiceApp_record_human.wav"), durationSec: 10f);

        GenerateVowelsMix(Path.Combine(DOWNLOADS_PATH, "MTFvoiceApp_record.wav"), variatePitch: true, variateResonace: true, variateWeight: true);
        //GenerateVowelsMix(Path.Combine(DOWNLOADS_PATH, "MTFvoiceApp_record_resonaceOnly.wav"), variatePitch: false, variateResonace: true, variateWeight: false);
        //GenerateVowelsMix(Path.Combine(DOWNLOADS_PATH, "MTFvoiceApp_record_pitchOnly.wav"), variatePitch: true, variateResonace: false, variateWeight: false);
        //GenerateVowelsMix(Path.Combine(DOWNLOADS_PATH, "MTFvoiceApp_record_weightOnly.wav"), variatePitch: false, variateResonace: false, variateWeight: true);
    }

    private static void GenerateVowelsMix(string path, bool variatePitch, bool variateResonace, bool variateWeight)
    {
        int sampleRate = 16000;
        float seconds = 10f;

        float F0 = 0f, harmonicFalloff = 0f;
        int maxHarmonic = 0;

        float[][] formants =
        [
            [310, 2790, 3310], // female - i (ee) 0
            [370, 950, 2670],  // female - u (oo) 1
            [450, 2330, 2850], // female - e (ay) 2
            [640, 920, 2700],  // female - o (oh) 3
            [850, 1220, 2810], // female - a (ah) 4

            [270, 2290, 3010], // male - i (ee) 5
            [300, 870, 2240],  // male - u (oo) 6
            [390, 1990, 2550], // male - e (ay) 7
            [570, 840, 2410],  // male - o (oh) 8
            [730, 1090, 2440], // male - a (ah) 9
        ];

        Console.WriteLine();
        Console.WriteLine(path);
        using WaveFileWriter writer = new(path, WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1));

        var lp = BiQuadFilter.LowPassFilter(sampleRate, 7000f, 0.707f);

        int totalSamples = (int)(seconds * sampleRate);
        int step = (int)(sampleRate * seconds / formants.Length);
        float[] samples = new float[step];
        Random random = new Random(Seed: 0);

        float F0driftValueA = 0f;
        float F0driftValueB = 0f;
        float F0driftTimeA = 0f;
        float F0driftTimeB = 0f;

        float F0jitter = 0f;
        float F0drift = 0f;
        float F0jitterRecalulationTime = 0f;

        for (int n = 0; n < formants.Length; n++)
        {
            bool isMale = n % 2 == 0;
            int formantIndex = n / 2 + (isMale && variateResonace ? 5 : 0);

            if (isMale)
            {
                F0 = 115f; // low pitch
                harmonicFalloff = 1.2f; // slow falloff - heavy weight
            }
            else
            {
                F0 = variatePitch ? 205 : F0;  // high pitch
                harmonicFalloff = variateWeight ? 2.6f : harmonicFalloff; // fast falloff - light weight
            }

            Console.WriteLine($"n: {n} isMale: {isMale} F0: {F0} harmonicFalloff: {harmonicFalloff} formantIndex: {formantIndex}");

            maxHarmonic = (int)Math.Min(5000 / F0, sampleRate / 2f / F0);

            float[] formantsOfVowel = formants[formantIndex];
            GetFilters(sampleRate, formantsOfVowel[0], formantsOfVowel[1], formantsOfVowel[2],
                out BiQuadFilter eq1, out BiQuadFilter eq2, out BiQuadFilter eq3);


            float max = 0f;
            float F0actual = F0;

            for (int sampleIndex = 0; sampleIndex < samples.Length; sampleIndex++)
            {
                float sampleValue = 0f;
                float time = (n * samples.Length + sampleIndex) / (float)sampleRate;

                if (F0driftTimeB <= time)
                {
                    F0driftTimeA = F0driftTimeB;
                    F0driftTimeB = time + NextRandom(0.2f, 0.35f);

                    F0driftValueA = F0driftValueB;
                    F0driftValueB = NextRandom(-0.002f, 0.002f);
                }

                if (F0jitterRecalulationTime <= time)
                {
                    float F0driftT = InverseLerp(F0driftTimeA, F0driftTimeB, time);
                    F0drift = Lerp(F0driftValueA, F0driftValueB, BezierBlend(F0driftT));

                    F0jitter += NextRandom(-0.00002f, 0.00002f);
                    F0jitter = Math.Clamp(F0jitter, -0.002f, 0.002f);
                    F0actual = F0 * (1f + F0drift + F0jitter);
                    F0jitterRecalulationTime = time + 1f / F0actual;

                    Console.WriteLine($"{time:F4} => {F0actual:F8} {(F0drift + F0jitter):F8}");
                }
                        

                for (int harmonic = 1; harmonic <= maxHarmonic; harmonic++)
                {
                    sampleValue += (float)(Math.Sin(2 * Math.PI * harmonic * time * F0actual) / Math.Pow(harmonic, harmonicFalloff));
                }

                sampleValue = eq1.Transform(sampleValue);
                sampleValue = eq2.Transform(sampleValue);
                sampleValue = eq3.Transform(sampleValue);
                sampleValue = lp.Transform(sampleValue);

                samples[sampleIndex] = sampleValue;
                max = Math.Max(max, Math.Abs(sampleValue));
            }

            max /= 0.7f;

            for (int sampleIndex = 0; sampleIndex < samples.Length; sampleIndex++)
            {
                samples[sampleIndex] /= max;
            }

            writer.WriteSamples(samples, 0, samples.Length);
        }

        float NextRandom(float from, float to)
        {
            return Lerp(from, to, random.NextSingle());
        }
    }

    private static void GetFilters(int sampleRate, 
        float F1, float F2, float F3,
        out BiQuadFilter eq1, out BiQuadFilter eq2, out BiQuadFilter eq3)
    {
        float BW1 = 120f, G1 = 20f;   // dB boost
        float BW2 = 180f, G2 = 14f;
        float BW3 = 250f, G3 = 7f;

        float Q1 = F1 / BW1;
        float Q2 = F2 / BW2;
        float Q3 = F3 / BW3;

        eq1 = BiQuadFilter.PeakingEQ(sampleRate, F1, Q1, G1);
        eq2 = BiQuadFilter.PeakingEQ(sampleRate, F2, Q2, G2);
        eq3 = BiQuadFilter.PeakingEQ(sampleRate, F3, Q3, G3);
    }

    private static float InverseLerp(float a, float b, float v)
    {
        return (v - a) / (b - a);
    }

    private static float Lerp(float a, float b, float t)
    {
        return float.Lerp(a, b, t);
    }

    private static float BezierBlend(float t)
    {
        return t * t * (3f - 2f * t);
    }
}