using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using MTFvoiceApp.Providers;

namespace MTFvoiceApp
{
    internal class RecordTest
    {
        public static async Task MakeRecord(string outputFolder, int sampleRate = 16000, double durationSec = 2d)
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
            return new SimpleNoiseGateSampleProvider(pipeline);
        }

        private static ISampleProvider AddNormalizeToPeak(ISampleProvider pipeline)
        {
            return new NormalizeToPeakSampleProvider(pipeline);
        }
    }
}
