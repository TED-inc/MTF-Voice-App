using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Styling;
using MTFVoiceTools.KlattSynth;
using MTFVoiceTools.KlattSynth.Params;
using MTFVoiceTools.Samples;
using NAudio.Wave;

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
        double[] samples = Klatt.GenerateSound(
            mainParams, [
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
            ]);
        
        using WaveFileWriter writer = new(Path.Combine(DOWNLOADS_PATH, "MTFvoiceTools_record.wav"), WaveFormat.CreateIeeeFloatWaveFormat(44100, 1));
        writer.WriteSamples(samples.Select(s => (float)s).ToArray(), 0, samples.Length);
        Console.WriteLine(samples.Length);
        
        AvaPlot.Plot.Add.Signal(samples, 1 / mainParams.SampleRate);
        
        AvaPlot.Plot.Axes.AutoScale();
        AvaPlot.Refresh();
    }
}