using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Styling;
using MTFVoiceTools.AudioProcessing;
using MTFVoiceTools.KlattSynth;
using MTFVoiceTools.KlattSynth.Params;
using MTFVoiceTools.Librosa;
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
    
    public MainWindow()
    {
        
        Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        InitializeComponent();
        RunKlattSynth();
    }
    
    public void ClickHandler(object sender, RoutedEventArgs args)
    {
        SelectNextFrame();
    }

    private void RunKlattSynth()
    {
        MainParameters mainParams = new (sampleRate: 44100, GlottalSourceType.Impulsive);
        
        double[] samples = Klatt.GenerateSound(mainParams, _frames);

        using (WaveFileWriter writer = new(Path.Combine(DOWNLOADS_PATH, "MTFvoiceTools_record.wav"),
                   WaveFormat.CreateIeeeFloatWaveFormat(mainParams.SampleRate, 1)))
        {
            writer.WriteSamples(samples.Select(s => (float)s).ToArray(), 0, samples.Length);
        }
        
        //Console.WriteLine(samples.Length);
        
        //using WaveFileReader reader = new(Path.Combine(DOWNLOADS_PATH, "My_record.wav"));
        //ISampleProvider sampleProvider = reader.ToSampleProvider();
        //float[] buffer = new float[reader.SampleCount];
        //sampleProvider.Read(buffer, 0, buffer.Length);
        //double[] samples = buffer.Select(x => (double)x).ToArray();
        
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