using NAudio.CoreAudioApi;
using NAudio.Wave;
using System.IO;

namespace MTFvoiceApp
{
    internal class LiveRecorder
    {
        public static async Task MakeRecord(string path, int sampleRate = 16000, double durationSec = 2d)
        {
            Console.WriteLine($"Recording {durationSec}s mono @ {sampleRate} Hz ...");
            await RecordMonoWasapiToFloatWav(path, sampleRate, durationSec);
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

            BufferedWaveProvider provider = new(capture.WaveFormat) 
            { 
                DiscardOnBufferOverflow = true
            };

            TaskCompletionSource tcs = new();


            capture.DataAvailable += (s, e) =>
            {
                provider.AddSamples(e.Buffer, 0, e.BytesRecorded);
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
    }
}
