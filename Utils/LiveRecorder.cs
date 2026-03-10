using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MTFVoiceTools.Providers;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MTFVoiceTools.Utils;

internal class LiveRecorder : IDisposable
{
    private WasapiCapture _audioCapturer;
    private readonly BufferedWaveProvider _bufferProvider;
    private readonly ISampleProvider _samplesProvider;
    private TaskCompletionSource<WaveInEventArgs> _recordingTcs;
    
    private bool _isRecording;
    
    public WaveFormat WaveFormat => _audioCapturer.WaveFormat;

    public LiveRecorder(MMDevice captureDevice, int sampleRate)
    {
        _audioCapturer = new(captureDevice)
        {
            ShareMode = AudioClientShareMode.Shared,
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1),
        };
        _bufferProvider = new(_audioCapturer.WaveFormat) 
        { 
            DiscardOnBufferOverflow = true,
            ReadFully = false,
        };
        _samplesProvider = _bufferProvider.ToSampleProvider();
        _samplesProvider = new BiQuadFilterSampleProvider(
            _samplesProvider,
            BiQuadFilterSampleProvider.CreateDefaultHighPassFilter(_samplesProvider.WaveFormat.SampleRate));
        _samplesProvider = new SimpleNoiseGateSampleProvider(_samplesProvider);
        _samplesProvider = new NormalizeToPeakSampleProvider(_samplesProvider);
    }

    public void StartRecording()
    {
        _isRecording = true;
        _audioCapturer.StartRecording();
    }

    public void StopRecording()
    {
        _isRecording = false;
        _audioCapturer.StopRecording();
    }

    public void SetDevice(MMDevice captureDevice)
    {
        _ = SetDeviceAsync();
        
        async Task SetDeviceAsync()
        {
            try
            {
                if (_isRecording)
                {
                    await _recordingTcs.Task;
                    _audioCapturer.StopRecording();
                }
                _audioCapturer.Dispose();
                _audioCapturer = new WasapiCapture(captureDevice);

                if (_isRecording)
                {
                    _audioCapturer.StartRecording();
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw e;
            }
        }
    }

    public async Task<IEnumerable<int>> ReadAsync(float[] buffer, int offset, int count)
    {
        _recordingTcs = new();
        _audioCapturer.DataAvailable += HandleData;
        
        WaveInEventArgs capturedData = await _recordingTcs.Task;
        
        return ReadCaptureadData(capturedData, buffer, offset, count);

        void HandleData(object? obj, WaveInEventArgs args)
        {
            _audioCapturer.DataAvailable -= HandleData;
            _recordingTcs.SetResult(args);
        }
    }

    private IEnumerable<int> ReadCaptureadData(WaveInEventArgs capturedData, float[] buffer, int offset, int count)
    {
        _bufferProvider.AddSamples(capturedData.Buffer, 0, capturedData.BytesRecorded);
        while (true)
        {
            int read = _samplesProvider.Read(buffer, offset, count);
            
            if (read == 0)
            {
                yield break;
            }
            
            yield return read;
        }
    }

    public void Dispose()
    {
        _audioCapturer.Dispose();
    }
}