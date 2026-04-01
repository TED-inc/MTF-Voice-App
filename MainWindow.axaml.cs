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
    private Marker? _selectedVowelMarker;
    private LinePlot _selectedVowelLine;

    private CancellationTokenSource? _recordingCts;
    private readonly QueueReadOnlyList<double> _recordingData = new(); // todo try queue
    private readonly List<Coordinates> _recordingFormantsData = new();
    private readonly Signal _recordingPlot;
    private int _recordingOffset;
    
    private MainWindowModel WindowModel => DataContext as MainWindowModel;
    
    public MainWindow()
    {
        Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        InitializeComponent();
        RunKlattSynth();
        
        RecordingPlot.UserInputProcessor.IsEnabled = false;
        _recordingPlot = RecordingPlot.Plot.Add.Signal(_recordingData);
        RecordingSpectrogramPlot.UserInputProcessor.IsEnabled = false;
        RecordingSpectrogramPlot.Plot.Add.ScatterPoints(_recordingFormantsData);
        
        DataContext = new MainWindowModel();
        
        MMDeviceEnumerator enumerator = new ();
        MMDeviceCollection devices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
        foreach (MMDevice device in devices)
        {
            WindowModel.Items.Add(device);
        }

        WindowModel.SelectedItem = WindowModel.Items.First();
    }
    
    private void ImportAudio(object sender, RoutedEventArgs args)
    {
        //SelectNextFrame();
    }

    private void RecordClickHandler(object sender, RoutedEventArgs args)
    {
        WindowModel.SelectedItem = WindowModel.SelectedItem;
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

            recorder = new(WindowModel.SelectedItem, 44100);
            double windowDuration = 5;
            int windowFramesCount = (int)(windowDuration * recorder.WaveFormat.SampleRate);
            double sampleDuration = 1d / recorder.WaveFormat.SampleRate;
            _recordingPlot.Data.Period = sampleDuration;
            int fftSize = FFTUtils.ClosestLowerPowerOfTwo(recorder.WaveFormat.SampleRate);
            double fftPeriod = (double)recorder.WaveFormat.SampleRate / fftSize;
            int totalSamplesCount = 0;

            float[] samplesBuffer = new float [1024];

            string path = Path.Combine(DOWNLOADS_PATH, "MTFvoiceTools_test_record.wav");

            if (File.Exists(path))
            {
                File.Delete(path);
            }

            await using WaveFileWriter writer = new(path, recorder.WaveFormat);

            recorder.StartRecording();

            while (token.IsCancellationRequested == false)
            {
                IEnumerable<int> reads = await recorder.ReadAsync(samplesBuffer, 0, samplesBuffer.Length)
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
                    writer.WriteSamples(samplesBuffer, 0, read);
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
                double windowRecordingTimeEnd = windowRecordingTimeStart + windowDuration;
                _recordingPlot.Data.XOffset = windowRecordingTimeStart;
                RecordingPlot.Plot.Axes.SetLimits(left: windowRecordingTimeStart, right: windowRecordingTimeEnd, bottom: -1, top: 1);
                RecordingPlot.Refresh();
                

                double[] samples = _recordingData.TakeLast(totalInFrameRead).ToArray();
                
                double[] magnitudeLinear = FFTUtils.FFTMagnitudeLinearSpectrumFromSample(samples, fftSize);
        
                double[] magnitudeDb = magnitudeLinear.ToArray();
                FFTUtils.FromLinearToDb(magnitudeDb);
        
                double f0 = PitchCalculator.EstimateF0(magnitudeLinear, fftPeriod, recorder.WaveFormat.SampleRate);
        
                double[] formants = FormantLpc.CalcualteFormantsWithLpc(
                    samples,
                    recorder.WaveFormat.SampleRate
                );
                
                SignalPlot.Plot.Clear();
                SignalPlot.Plot.Add.Signal(magnitudeDb, fftPeriod);
                SignalPlot.Plot.Add.VerticalLine(f0);
                foreach (double formant in formants)
                {
                    SignalPlot.Plot.Add.VerticalLine(formant);
                    _recordingFormantsData.Add(new(totalSamplesCount * sampleDuration, formant));
                }
        
                SignalPlot.Plot.Axes.SetLimits(left: 0, right: 6000, bottom: -100, top: 0);
                SignalPlot.Refresh();
                
                RecordingSpectrogramPlot.Plot.Axes.SetLimits(left: windowRecordingTimeStart, right: windowRecordingTimeEnd, bottom: 0, top: 5000);
                RecordingSpectrogramPlot.Refresh();
                
                if (formants.Length >= 2)
                {
                    _selectedVowelMarker.Position = new Coordinates(formants[0], formants[1]);
                    VowelPlot.Refresh();
                }
            }

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
        
        void OnDeviceChange(object? obj, PropertyChangedEventArgs property)
        {
            try
            {
                if (property.PropertyName != nameof(WindowModel.SelectedItem))
                {
                    return;
                }

                recorder?.SetDevice(WindowModel.SelectedItem);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }
    }

    private void RunKlattSynth()
    {
        MainParameters mainParams = new (sampleRate: 44100, GlottalSourceType.Impulsive);
        
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
        
        //AvaPlot.Plot.Add.Signal(samples, period: 1 / mainParams.SampleRate);
        
        foreach (Vowel vowel in Vowels)
        {
            VowelPlot.Plot.Add.Marker(vowel.Formants[0], vowel.Formants[1], MarkerShape.FilledDiamond, color: Colors.Black);
            Text label = VowelPlot.Plot.Add.Text("  " + vowel.PhoneticSymbol, vowel.Formants[0], vowel.Formants[1]);
            label.Alignment = Alignment.MiddleLeft;
            label.LabelFontSize = 20;
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
                mainParams.SampleRate
            );
            _framesAnalysis[i] = new (f0,  formants, magnitudeDb, period);
        
            Console.WriteLine($"[{i}] Pitch measured (Hz): {f0:F0}");
            Console.WriteLine($"[{i}] Pitch expected (Hz): {frame.F0:F0}");
            Console.WriteLine($"[{i}] Formants measured (Hz): " + string.Join(", ", formants.Select(v => v.ToString("F0"))));
            Console.WriteLine($"[{i}] Formants expected (Hz): " + string.Join(", ", frame.OralFormantFreq.Select(v => v.ToString("F0"))));
            
            VowelPlot.Plot.Add.Marker(frame.OralFormantFreq[0], frame.OralFormantFreq[1], color: Colors.LightPink);
            VowelPlot.Plot.Add.Marker(formants[0], formants[1], color: Colors.Red);
            Text label = VowelPlot.Plot.Add.Text($"  {i}", formants[0], formants[1]);
            label.Alignment = Alignment.MiddleLeft;
            label.LabelFontSize = 20;
            label.LabelFontColor = Colors.Red;
        }

        SelectNextFrame();
        
        VowelPlot.Plot.Axes.AutoScale();
        VowelPlot.Refresh();
    }

    private void SelectNextFrame()
    {
        _selectedFrameIndex++;
        _selectedFrameIndex %= _frames.Count;
        
        Message.Text = $"Display frame: {_selectedFrameIndex}";

        (double pitch, IReadOnlyList<double> formants, IReadOnlyList<double> magnitudeDb, double period) = _framesAnalysis[_selectedFrameIndex];
        FrameParameters frame = _frames[_selectedFrameIndex];
        
        SignalPlot.Plot.Clear();
        SignalPlot.Plot.Add.Signal(magnitudeDb, period);
        SignalPlot.Plot.Add.VerticalLine(pitch);
        foreach (double formant in formants)
        {
            SignalPlot.Plot.Add.VerticalLine(formant);
        }
        
        SignalPlot.Plot.Axes.AutoScale();
        SignalPlot.Refresh();

        Coordinates fromPosition = new (formants[0], formants[1]);
        Coordinates toPosition = new (frame.OralFormantFreq[0], frame.OralFormantFreq[1]);

        if (_selectedVowelMarker == null)
        {
            _selectedVowelMarker = new Marker()
            {
                Position = fromPosition,
                Color = Colors.Cyan,
                MarkerShape = MarkerShape.FilledDiamond,
                MarkerSize = 20f
            };
            
            _selectedVowelLine = new ()
            {
                Start = fromPosition,
                End = toPosition,
                LineStyle =
                {
                    Color = Colors.Cyan
                },
                MarkerStyle =
                {
                    FillColor = Colors.Cyan
                }
            };
            
            VowelPlot.Plot.PlottableList.Insert(0, _selectedVowelMarker);
            VowelPlot.Plot.PlottableList.Insert(0, _selectedVowelLine);
        }
        _selectedVowelMarker.Position = fromPosition;
        _selectedVowelLine.Start = fromPosition;
        _selectedVowelLine.End = toPosition;

        VowelPlot.Refresh();
    }

    private record Vowel(string PhoneticSymbol, string PhoneticTranscription, IReadOnlyList<double> Formants);

    private record FrameAnalysis(double Pitch, IReadOnlyList<double> Formants, IReadOnlyList<double> MagnitudeDb, double Period);
    
    private static readonly IReadOnlyList<Vowel> Vowels =
    [
        new("i", "i", [294, 2343, 3251]),
        new("I", "I", [360, 2187, 2830]),
        new("e", "e", [434, 2148, 2763]),

        new("\u025B", "E", [581, 1840, 2429]),
        new("\u00E6", "{", [766, 1782, 2398]),

        new("a_f", "a_f", [806, 1632, 2684]),
        new("a_c", "a_c", [784, 1211, 2702]),

        new("\u0251", "A", [781, 1065, 2158]),
        new("\u0252", "Q", [652, 843, 2011]),
        new("\u0254", "O", [541, 830, 2221]),
        new("o", "o", [406, 727, 2090]),

        new("\u028A", "U", [334, 910, 2300]),
        new("u", "u", [295, 750, 2342])
    ];
}