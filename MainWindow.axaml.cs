using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Styling;
using MTFVoiceTools.KlattSynth;
using MTFVoiceTools.KlattSynth.MainGenerator;
using MTFVoiceTools.KlattSynth.Params;
using MTFVoiceTools.Samples;
using NAudio.Wave;
using ScottPlot.Avalonia;

namespace MTFVoiceTools;

public partial class MainWindow : Window
{
    private static readonly string DOWNLOADS_PATH = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    
    public MainWindow()
    {
        Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        InitializeComponent();
        double[] dataX = { 1, 2, 3, 4, 5 };
        double[] dataY = { 1, 4, 9, 16, 25 };

        AvaPlot avaPlot1 = this.Find<AvaPlot>("AvaPlot1");
        avaPlot1.Plot.Add.Scatter(dataX, dataY);
        avaPlot1.Refresh();

        TryRunKlatt();
    }
    
    public void ClickHandler(object sender, RoutedEventArgs args)
    {
        _ = ProgramSample.Main();
        Message.Text = "Button clicked!";
    }

    private void TryRunKlatt()
    {
        double[] samples = Klatt.GenerateSound(
            new MainParameters(sampleRate: 44100, GlottalSourceType.Impulsive), [
                new FrameParameters()
                {
                    Duration = 1,
                    F0 = 247,
                    FlutterLevel = 0.25,
                    OpenPhaseRatio =  0.7,
                    BreathinessDb = -25,
                    TiltDb = 0f,
                    GainDb = double.NaN,
                    AgcRmsLevel = 0.18,
                    NasalFormantFreq = double.NaN,
                    NasalFormantBw = double.NaN,
                    OralFormantFreq = [520, 1006, 2831, 3168, 4135, 5020],
                    OralFormantBw = [76, 102, 72, 102, 816, 596],

                    // Cascade branch:
                    CascadeEnabled = true,
                    CascadeVoicingDb = 0,
                    CascadeAspirationDb = -25,
                    CascadeAspirationMod = 0.5,
                    NasalAntiformantFreq = double.NaN,
                    NasalAntiformantBw = double.NaN,

                    // Parallel branch:
                    ParallelEnabled = false,
                    ParallelVoicingDb = 0,
                    ParallelAspirationDb = -25,
                    ParallelAspirationMod = 0.5,
                    FricationDb = -30,
                    FricationMod = 0.5,
                    ParallelBypassDb = -99,
                    NasalFormantDb = double.NaN,
                    OralFormantDb = [0, -8, -15, -19, -30, -35]
                }
            ]);
        
        using WaveFileWriter writer = new(Path.Combine(DOWNLOADS_PATH, "MTFvoiceTools_record.wav"), WaveFormat.CreateIeeeFloatWaveFormat(44100, 1));
        writer.WriteSamples(samples.Select(s => (float)s).ToArray(), 0, samples.Length);
        Console.WriteLine(samples.Length);
    }
}