using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using MTFVoiceTools.AudioProcessing;
using MTFVoiceTools.KlattSynth;
using MTFVoiceTools.KlattSynth.Params;
using MTFVoiceTools.Librosa;
using MTFVoiceTools.Utils;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using ScottPlot;
using ScottPlot.Plottables;

namespace MTFVoiceTools;

public partial class MainWindow : Window
{
    const double WINDOW_DURATION = 5;
    private static readonly string DOWNLOADS_PATH = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    private FrameAnalysis[] _framesAnalysis;
    private readonly IReadOnlyList<FrameParameters> _frames =
    [
        FrameParametersSamples.FemaleI with { Duration = 0.5 },
        FrameParametersSamples.FemaleE with { Duration = 0.5 },
        FrameParametersSamples.FemaleA with { Duration = 0.5 },
        FrameParametersSamples.FemaleO with { Duration = 0.5 },
        FrameParametersSamples.FemaleU  with { Duration = 0.5 },
    ];
    private readonly IReadOnlyList<FrameParameters> _frames2 =
    [
        FrameParametersSamples.MaleI,
        FrameParametersSamples.FemaleI,
        FrameParametersSamples.MaleE,
        FrameParametersSamples.FemaleE,
        FrameParametersSamples.MaleA,
        FrameParametersSamples.FemaleA,
        FrameParametersSamples.MaleO,
        FrameParametersSamples.FemaleO,
        FrameParametersSamples.MaleU,
        FrameParametersSamples.FemaleU,
    ];
    private int _selectedFrameIndex = -1;
    private Marker? _selectedVowel12Marker;
    private Marker? _selectedVowel32Marker;

    private CancellationTokenSource? _recordingCts;
    private readonly QueueReadOnlyList<double> _recordingData = new(); // todo try queue
    private readonly Signal _recordingPlot;
    private int? _sampleRate;
    private int _recordingOffset;
    
    private MainWindowModel WindowModel => DataContext as MainWindowModel;
    
    public MainWindow()
    {
        Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        DataContext = new MainWindowModel();
        
        InitializeComponent();
        RunKlattSynth();
        
        RecordingPlot.UserInputProcessor.IsEnabled = false;
        _recordingPlot = RecordingPlot.Plot.Add.Signal(_recordingData);
        
        MMDeviceEnumerator enumerator = new ();
        MMDeviceCollection devices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
        foreach (MMDevice device in devices)
        {
            WindowModel.Devices.Add(device);
        }

        WindowModel.SelectedDevice = WindowModel.Devices.First();
        RecordingSlider.PropertyChanged += RecordingSliderValueChanged;
    }

    private void RecordingSliderValueChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        const int ANALYSIS_WINDOW = 1024;
        
        if (_sampleRate.HasValue == false || _recordingData.Count < ANALYSIS_WINDOW)
        {
            return;
        }
        
        int windowFramesCount = (int)(WINDOW_DURATION * _sampleRate);
        int analyzeFrom = (int)double.Lerp(0, windowFramesCount - ANALYSIS_WINDOW, RecordingSlider.Value);
        
        AnalyzeFormants(_sampleRate.Value, analyzeFrom, ANALYSIS_WINDOW);
    }

    private void ImportAudio(object sender, RoutedEventArgs args)
    {
        _ = OpenAudioFile();
    }

    private async Task OpenAudioFile()
    {
        try
        {
            TopLevel? topLevel = GetTopLevel(this);
            
            IReadOnlyList<IStorageFile> files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open Audio File",
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("Wave Audio") { Patterns = ["*.wav"] } ],
            });

            if (files.Count >= 1)
            {
                await using Stream stream = await files[0].OpenReadAsync();
                await using WaveFileReader reader = new (stream);
                using CancellationTokenSource cts = new ();
                CancellationToken token = cts.Token;

                if (_sampleRate.HasValue && _sampleRate != reader.WaveFormat.SampleRate)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Mismatch of sampleRates {_sampleRate} != {reader.WaveFormat.SampleRate}");
                    Console.ResetColor();
                }
                
                await Process(reader.WaveFormat.SampleRate, ReadAsync, (_, _, _) => { }, token);

                async Task<IEnumerable<int>> ReadAsync(float[] buffer, int offset, int count)
                {
                    await Task.Delay(10);
                    
                    int read = 0;
                    for (; read < count; read++)
                    {
                        float[] sampleFrame = reader.ReadNextSampleFrame();
                        if (sampleFrame == null || sampleFrame.Length == 0)
                        {
                            cts.Cancel();
                            break;
                        }

                        buffer[read + offset] = sampleFrame[0];
                    }

                    return Enumerable.Repeat(read, 1);
                }
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }
    
    private void RecordClickHandler(object sender, RoutedEventArgs args)
    {
        WindowModel.SelectedDevice = WindowModel.SelectedDevice;
        _ = Record();
    }

    private async Task Record()
    {
        LiveRecorder? recorder = null;
        
        try
        {
            if (_recordingCts != null)
            {
                RecordButton.Content = "Stopping...";
                await _recordingCts.CancelAsync();
                _recordingCts.Dispose();
                _recordingCts = null;
                RecordButton.Content = "Record";
                return;
            }

            RecordButton.Content = "Stop";
            _recordingCts = new CancellationTokenSource();
            CancellationToken token = _recordingCts.Token;
            
            WindowModel.PropertyChanged += OnDeviceChange;

            _sampleRate ??= 44100;
            recorder = new(WindowModel.SelectedDevice, _sampleRate.Value);
            string path = Path.Combine(DOWNLOADS_PATH, "MTFvoiceTools_test_record.wav");
            
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            
            await using WaveFileWriter writer = new(path, recorder.WaveFormat);
            
            recorder.StartRecording();
            
            await Process(recorder.WaveFormat.SampleRate, recorder.ReadAsync, writer.WriteSamples, token);
            
            recorder.StopRecording();
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
        finally
        {
            recorder?.Dispose();
            WindowModel.PropertyChanged -= OnDeviceChange;
        }
        return;
        
        void OnDeviceChange(object? obj, PropertyChangedEventArgs property)
        {
            try
            {
                if (property.PropertyName != nameof(WindowModel.SelectedDevice))
                {
                    return;
                }

                recorder?.SetDevice(WindowModel.SelectedDevice);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }
    }

    private async Task Process(int sampleRate, Func<float[], int, int, Task<IEnumerable<int>>> readAsync, Action<float[], int, int> write, CancellationToken token)
    {
        int windowFramesCount = (int)(WINDOW_DURATION * sampleRate);
        double sampleDuration = 1d / sampleRate;
        _recordingPlot.Data.Period = sampleDuration;
        int totalSamplesCount = 0;

        float[] samplesBuffer = new float [1024];

        while (token.IsCancellationRequested == false)
        {
            IEnumerable<int> reads = await readAsync(samplesBuffer, 0, samplesBuffer.Length)
                .WaitAsync(token)
                .SupressCancelationThrow();

            if (token.IsCancellationRequested)
            {
                break;
            }

            int totalInFrameRead = 0;

            foreach (int read in reads)
            {
                totalInFrameRead += read;
                write(samplesBuffer, 0, read);
                foreach (float sample in samplesBuffer.Take(read))
                {
                    _recordingData.Enqueue(sample);
                }
            }

            totalSamplesCount += totalInFrameRead;

            int cleanupHead = Math.Max(0, _recordingData.Count - windowFramesCount);
            _recordingOffset += cleanupHead;
            for (int i = 0; i < cleanupHead; i++)
            {
                _recordingData.Dequeue();
            }


            double windowRecordingTimeStart = sampleDuration * _recordingOffset;
            double windowRecordingTimeEnd = windowRecordingTimeStart + WINDOW_DURATION;
            _recordingPlot.Data.XOffset = windowRecordingTimeStart;
            RecordingPlot.Plot.Axes.SetLimits(left: windowRecordingTimeStart, right: windowRecordingTimeEnd, bottom: -1,
                top: 1);
            RecordingPlot.Refresh();
            RecordingSlider.Value = (double)_recordingData.Count / windowFramesCount;
            AnalyzeFormants(sampleRate, _recordingData.Count - totalInFrameRead, totalInFrameRead);
        }
    }

    private void AnalyzeFormants(int sampleRate, int recordingFrameStart, int recordingFramesCount)
    {
        int fftSize = FFTUtils.ClosestLowerPowerOfTwo(sampleRate);
        double fftPeriod = (double)sampleRate / fftSize;
        
        double[] samples = _recordingData.Skip(recordingFrameStart).Take(recordingFramesCount).ToArray();

        double[] magnitudeLinear = FFTUtils.FFTMagnitudeLinearSpectrumFromSample(samples, fftSize);

        double[] magnitudeDb = magnitudeLinear.ToArray();
        FFTUtils.FromLinearToDb(magnitudeDb);
        
        double maxDb = magnitudeDb.Max();

        if (maxDb < -40)
        {
            return;
        }

        double f0 = PitchCalculator.EstimateF0(magnitudeLinear, fftPeriod, sampleRate);

        double[] formants = FormantLpc.CalcualteFormantsWithLpc(
            samples,
            sampleRate,
            maxFormants: WindowModel.SelectedFormant
        );

        SignalPlot.Plot.Clear();
        SignalPlot.Plot.Add.Signal(magnitudeDb, fftPeriod);
        SignalPlot.Plot.Add.VerticalLine(f0);
        foreach (double formant in formants)
        {
            SignalPlot.Plot.Add.VerticalLine(formant);
        }

        SignalPlot.Plot.Axes.SetLimits(left: 0, right: 6000, bottom: -100, top: 0);
        SignalPlot.Refresh();

        if (formants.Length >= 2)
        {
            _selectedVowel12Marker.Position = new Coordinates(formants[0], formants[1]);
            Formant12Plot.Refresh();
        }

        if (formants.Length >= 3)
        {
            _selectedVowel32Marker.Position = new Coordinates(formants[2], formants[1]);
            Formant32Plot.Refresh();
        }
    }

    private void RunKlattSynth()
    {
        _sampleRate ??= 44100;
        MainParameters mainParams = new (sampleRate: _sampleRate.Value, GlottalSourceType.Impulsive);
        
        double[] samples = Klatt.GenerateSound(mainParams, _frames);

        using (WaveFileWriter writer = new(
                   Path.Combine(DOWNLOADS_PATH, "MTFvoiceTools_record.wav"),
                   WaveFormat.CreateIeeeFloatWaveFormat(mainParams.SampleRate, 1)))
        {
            writer.WriteSamples(samples.Select(s => (float)s).ToArray(), 0, samples.Length);
        }
        
        using WaveFileReader reader = new(Path.Combine(DOWNLOADS_PATH, "eva_5x0.5.wav"));
        ISampleProvider sampleProvider = reader.ToSampleProvider();
        Console.WriteLine(sampleProvider.WaveFormat.SampleRate);
        float[] buffer = new float[reader.SampleCount];
        sampleProvider.Read(buffer, 0, buffer.Length);
        samples = buffer.Select(x => (double)x).ToArray();
        
        foreach (VovelPair pair in VowelPairs)
        {
            AddLines(pair.FemaleFormant.OralFormantFreq, pair.MaleFormant.OralFormantFreq, pair.FemaleColor);
            
            AddLabels(pair.PhoneticSymbol, pair.FemaleFormant.OralFormantFreq, pair.FemaleColor);
            AddLabels(pair.PhoneticSymbol, pair.MaleFormant.OralFormantFreq, pair.MaleColor);
            
            continue;

            void AddLines(IReadOnlyList<double> formantsFemale, IReadOnlyList<double> formantsMale, Color color)
            {
                Coordinates position12Female = new (formantsFemale[0], formantsFemale[1]);
                Coordinates position12Male = new (formantsMale[0], formantsMale[1]);
                Formant12Plot.Plot.Add.Line(position12Female, position12Male).Color = color;
                
                Coordinates position32Female = new (formantsFemale[2], formantsFemale[1]);
                Coordinates position32Male = new (formantsMale[2], formantsMale[1]);
                Formant32Plot.Plot.Add.Line(position32Female, position32Male).Color = color;
            }
            
            void AddLabels(string text, IReadOnlyList<double> formants, Color color)
            {
                AddLabel(Formant12Plot.Plot, text, formants[0], formants[1], color);
                AddLabel(Formant32Plot.Plot, text, formants[2], formants[1], color);
            }

            static void AddLabel(Plot plot, string text, double formantsX, double formantsY, Color color)
            {
                plot.Add.Marker(formantsX, formantsY, MarkerShape.FilledDiamond, color: color);
                Text label = plot.Add.Text("  " + text, formantsX, formantsY);
                label.Alignment = Alignment.MiddleLeft;
                label.LabelFontSize = 20;
            }
        }
        
        int fftSize = FFTUtils.ClosestLowerPowerOfTwo(mainParams.SampleRate);
        double period = (double)mainParams.SampleRate / fftSize;
        double maxX = 6000;
        double minY = -100;

        _framesAnalysis = new FrameAnalysis[_frames.Count];
        int totalLength = 0;
        for (int i = 0; i < _frames.Count; i++)
        {
            FrameParameters frame = _frames[i];
            int frameLength = frame.GetFeameBufferLength(mainParams.SampleRate);
            double[] frameSamples = samples.Skip(totalLength).Take(frameLength).ToArray();
            totalLength += frameLength;
            
            double[] magnitudeLinear = FFTUtils.FFTMagnitudeLinearSpectrumFromSample(frameSamples, fftSize);
        
            double[] magnitudeDb = magnitudeLinear.ToArray();
            FFTUtils.FromLinearToDb(magnitudeDb);
        
            magnitudeDb = magnitudeDb.Take((int)(maxX / period)).Select(x => Math.Max(x, minY)).ToArray();
        
            double f0 = PitchCalculator.EstimateF0(magnitudeLinear, period, mainParams.SampleRate);
        
            double[] formants = FormantLpc.CalcualteFormantsWithLpc(
                frameSamples,
                mainParams.SampleRate,
                maxFormants: WindowModel.SelectedFormant
            );
            _framesAnalysis[i] = new (f0,  formants, magnitudeDb, period);
        }

        SelectNextFrame();
        
        Formant12Plot.Plot.Axes.AutoScale();
        Formant12Plot.Refresh();
        
        Formant32Plot.Plot.Axes.AutoScale();
        Formant32Plot.Refresh();
    }

    private void SelectNextFrame()
    {
        _selectedFrameIndex++;
        _selectedFrameIndex %= _frames.Count;

        (double pitch, IReadOnlyList<double> formants, IReadOnlyList<double> magnitudeDb, double period) = _framesAnalysis[_selectedFrameIndex];
        
        SignalPlot.Plot.Clear();
        SignalPlot.Plot.Add.Signal(magnitudeDb, period);
        SignalPlot.Plot.Add.VerticalLine(pitch);
        foreach (double formant in formants)
        {
            SignalPlot.Plot.Add.VerticalLine(formant);
        }
        
        SignalPlot.Plot.Axes.AutoScale();
        SignalPlot.Refresh();

        Coordinates position12 = new (formants[0], formants[1]);
        Coordinates position32 = new (formants[2], formants[1]);

        if (_selectedVowel12Marker == null)
        {
            _selectedVowel12Marker = new Marker()
            {
                Position = position12,
                Color = Colors.Cyan,
                MarkerShape = MarkerShape.FilledDiamond,
                MarkerSize = 20f
            };
            
            Formant12Plot.Plot.PlottableList.Insert(0, _selectedVowel12Marker);
        }
        
        if (_selectedVowel32Marker == null)
        {
            _selectedVowel32Marker = new Marker()
            {
                Position = position32,
                Color = Colors.Cyan,
                MarkerShape = MarkerShape.FilledDiamond,
                MarkerSize = 20f
            };
            
            Formant32Plot.Plot.PlottableList.Insert(0, _selectedVowel32Marker);
        }
        
        _selectedVowel12Marker.Position = position12;
        _selectedVowel32Marker.Position = position32;

        Formant12Plot.Refresh();
        Formant32Plot.Refresh();
        
    }
    
    private record VovelPair(string PhoneticSymbol, FrameParameters FemaleFormant,  FrameParameters MaleFormant, Color FemaleColor, Color MaleColor);

    private record FrameAnalysis(double Pitch, IReadOnlyList<double> Formants, IReadOnlyList<double> MagnitudeDb, double Period);
    
    private static readonly IReadOnlyList<VovelPair> VowelPairs =
    [
        new("i", FrameParametersSamples.FemaleI,  FrameParametersSamples.MaleI, Colors.Green, Colors.C2),
        new("e", FrameParametersSamples.FemaleE,  FrameParametersSamples.MaleE, Colors.Gold, Colors.Yellow),
        new("a", FrameParametersSamples.FemaleA,  FrameParametersSamples.MaleA, Colors.C1, Colors.Orange),
        new("o", FrameParametersSamples.FemaleO,  FrameParametersSamples.MaleO, Colors.C3, Colors.Red),
        new("u", FrameParametersSamples.FemaleU,  FrameParametersSamples.MaleU, Colors.C6, Colors.Pink),
    ];
}