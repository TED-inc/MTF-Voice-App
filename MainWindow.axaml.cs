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
using MTFVoiceTools.Samples;
using NAudio.Wave;
using NWaves.Transforms;
using ScottPlot.Plottables;

namespace MTFVoiceTools;

public partial class MainWindow : Window
{
    private static readonly string DOWNLOADS_PATH = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    
    public MainWindow()
    {
        
        Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        InitializeComponent();
        RunKlattSynth();
    }
    
    public void ClickHandler(object sender, RoutedEventArgs args)
    {
        _ = ProgramSample.Main();
        Message.Text = "Button clicked!";
    }

    private void RunKlattSynth()
    {
        MainParameters mainParams = new (sampleRate: 44100, GlottalSourceType.Impulsive);
        IReadOnlyList<FrameParameters> frames =
        [
            //FrameParametersSamples.MaleI,
            FrameParametersSamples.FemaleI,
            //FrameParametersSamples.MaleE,
            //FrameParametersSamples.FemaleE,
            //FrameParametersSamples.MaleA,
            //FrameParametersSamples.FemaleA,
            //FrameParametersSamples.MaleO,
            //FrameParametersSamples.FemaleO,
            //FrameParametersSamples.MaleU,
            //FrameParametersSamples.FemaleU,
        ];
        
        double[] samples = Klatt.GenerateSound(mainParams, frames);

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

        int fftSize = FFTUtils.ClosestLowerPowerOfTwo(mainParams.SampleRate);
        double period = (double)mainParams.SampleRate / fftSize;
        double maxX = 6000;
        double minY = -100;
        double[] magnitudeLinear = FFTUtils.FFTMagnitudeLinearSpectrumFromSample(samples, fftSize);
        double[] magnitudeDb = magnitudeLinear.ToArray();
        FFTUtils.FromLinearToDb(magnitudeDb);
        
        magnitudeDb = magnitudeDb.Take((int)(maxX / period)).Select(x => Math.Max(x, minY)).ToArray();
        
        
        AvaPlot.Plot.Add.Signal(magnitudeDb, period);
        
        double f0 = PitchCalculator.EstimateF0(magnitudeLinear, period, mainParams.SampleRate);
        
        double[] formants = FormantLpc.CalcualteFormantsWithLpc(
            samples,
            mainParams.SampleRate,
            formantCeilingHz: 5500.0,
            maxFormants: 5,
            lpcWindowLengthSeconds: 0.025,
            lpcWindowCenterSecond: null,
            preemphFromHz: 50.0
        );
        
        Console.WriteLine($"Pitch (Hz): {f0:F0}");
        Console.WriteLine("Formants (Hz): " + string.Join(", ", formants.Select(v => v.ToString("F0"))));
        
        AvaPlot.Plot.Add.VerticalLine(f0);
        foreach (double formant in formants)
        {
            AvaPlot.Plot.Add.VerticalLine(formant);
        }
        
        AvaPlot.Plot.Axes.AutoScale();
        AvaPlot.Refresh();
    }
}