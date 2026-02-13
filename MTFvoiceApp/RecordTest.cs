using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace MTFvoiceApp
{
    internal class RecordTest
    {
        public static async Task MakeARecord(string outputFolder, int sampleRate = 16000, double durationSec = 2d)
        {
            string recordPath = Path.Combine(outputFolder, "MTFvoiceApp_record.wav");
            string gatedPath = Path.Combine(outputFolder, "MTFvoiceApp_gated.wav");
            string normalizedPath = Path.Combine(outputFolder, "MTFvoiceApp_normalized.wav");
            string gatedNormalizedPath = Path.Combine(outputFolder, "MTFvoiceApp_gated_normalized.wav");

            Console.WriteLine($"Recording {durationSec}s mono @ {sampleRate} Hz ...");
            await RecordMonoWasapiToFloatWav(recordPath, sampleRate, durationSec);

            Console.WriteLine("Processing normalize...");
            ProcessNormalize(recordPath, normalizedPath);
            Console.WriteLine("Processing gate...");
            ProcessGate(recordPath, gatedPath);
            Console.WriteLine("Processing gate normalize...");
            ProcessNormalize(gatedPath, gatedNormalizedPath);

            Console.WriteLine("Done");
            Console.ReadLine();
        }

        private static async Task RecordMonoWasapiToFloatWav(string path, int sampleRate, double durationSec)
        {

            if (File.Exists(path))
            {
                File.Delete(path);
            }

            using WasapiCapture capture = new();
            capture.ShareMode = AudioClientShareMode.Shared;

            WaveFormat desired = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1);
            capture.WaveFormat = desired;

            using WaveFileWriter writer = new(path, capture.WaveFormat);

            TaskCompletionSource tcs = new();


            capture.DataAvailable += (s, e) =>
            {
                writer.Write(e.Buffer, 0, e.BytesRecorded);
                writer.Flush();
            };

            capture.RecordingStopped += (s, e) =>
            {
                if (e.Exception != null)
                {
                    Console.Error.WriteLine("Recording error: " + e.Exception);
                }

                tcs.SetResult();
            };

            capture.StartRecording();

            await Task.Delay(TimeSpan.FromSeconds(durationSec));

            capture.StopRecording();

            await tcs.Task;
        }

        private static void ProcessGate(string inPath, string outPath)
        {
            using AudioFileReader reader = new(inPath);
            ISampleProvider pipeline = reader;

            if (reader.WaveFormat.Channels == 2)
            {
                pipeline = AddStereoToMonoConversion(pipeline);
            }

            pipeline = AddHighPassFilterToRemoveRumble(pipeline);
            pipeline = AddNoiseGateForQuetSections(pipeline);

            WaveFileWriter.CreateWaveFile16(outPath, pipeline);
        }

        private static void ProcessNormalize(string inPath, string outPath)
        {
            using AudioFileReader reader = new(inPath);
            ISampleProvider pipeline = reader;

            if (reader.WaveFormat.Channels == 2)
            {
                pipeline = AddStereoToMonoConversion(pipeline);
            }

            pipeline = AddNormalizeToPeak(pipeline);

            WaveFileWriter.CreateWaveFile16(outPath, pipeline);
        }

        private static ISampleProvider AddStereoToMonoConversion(ISampleProvider pipeline)
        {
            return new StereoToMonoSampleProvider(pipeline)
            {
                LeftVolume = 0.5f,
                RightVolume = 0.5f
            };
        }

        private static ISampleProvider AddHighPassFilterToRemoveRumble(ISampleProvider pipeline)
        {
            return new BiQuadFilterSampleProvider(
                pipeline,
                BiQuadFilter.HighPassFilter(
                    pipeline.WaveFormat.SampleRate,
                    cutoffFrequency: 80f,
                    q: 0.707f));
        }

        private static ISampleProvider AddNoiseGateForQuetSections(ISampleProvider pipeline)
        {
            return new SimpleNoiseGateSampleProvider(
                    pipeline,
                    thresholdDb: -45f,
                    attenuationDb: -18f,
                    attackMs: 10f,
                    releaseMs: 200f);
        }

        private static ISampleProvider AddNormalizeToPeak(ISampleProvider pipeline)
        {
            const float TARGET_PEAK_DB = -1.0f; // common choice; avoids inter-sample-ish clipping risk

            float max = FindPeak(pipeline);

            bool isSilent = max <= 0;
            if (isSilent)
            {
                return pipeline;
            }

            float targetLinear = AudioMath.DbToLinear(TARGET_PEAK_DB);
            float gain = targetLinear / max;

            if (pipeline is AudioFileReader afr)
            {
                afr.Position = 0;
                return new VolumeSampleProvider(afr) { Volume = gain };
            }

            throw new InvalidOperationException("NormalizeToPeak requires a seekable source (e.g., AudioFileReader).");
        }

        private static float FindPeak(ISampleProvider source)
        {
            float max = 0f;
            float[] buffer = new float[source.WaveFormat.SampleRate * source.WaveFormat.Channels]; // ~1 second
            int read;
            while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                for (int n = 0; n < read; n++)
                {
                    float abs = Math.Abs(buffer[n]);
                    if (abs > max)
                    {
                        max = abs;
                    }
                }
            }
            return max;
        }
    }
}
