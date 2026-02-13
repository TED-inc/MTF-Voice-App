using MTFvoiceApp.Providers;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MTFvoiceApp
{
    internal class LiveRecorder
    {
        public static async Task MakeRecord(string path, int sampleRate = 16000, double durationSec = 2d)
        {
            Console.WriteLine($"Recording {durationSec}s mono @ {sampleRate} Hz ...");
            await RecordMonoWasapiToFloatWav(path, sampleRate, durationSec);
            Console.WriteLine($"Done");
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

            BufferedWaveProvider provider = new(capture.WaveFormat) 
            { 
                DiscardOnBufferOverflow = true,
                ReadFully = false,
            };

            ISampleProvider samples = provider.ToSampleProvider();
            samples = new BiQuadFilterSampleProvider(
                samples,
                BiQuadFilterSampleProvider.CreateDefaultHighPassFilter(samples.WaveFormat.SampleRate));
            samples = new SimpleNoiseGateSampleProvider(samples);
            samples = new NormalizeToPeakSampleProvider(samples);

            IWaveProvider processedWaveProvider = samples.ToWaveProvider16();
            using WaveFileWriter writer = new(path, processedWaveProvider.WaveFormat);

            byte[] procedToWriterBuffer = new byte[processedWaveProvider.WaveFormat.AverageBytesPerSecond / 10];
            TaskCompletionSource tcs = new();

            capture.DataAvailable += (s, e) =>
            {
                provider.AddSamples(e.Buffer, 0, e.BytesRecorded);
                int read;
                while (true)
                {
                    read = processedWaveProvider.Read(procedToWriterBuffer, 0, procedToWriterBuffer.Length);
                    if (read == 0)
                    {
                        break;
                    }
                    writer.Write(procedToWriterBuffer, 0, read);
                }
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

            writer.Flush();
        }
    }
}
