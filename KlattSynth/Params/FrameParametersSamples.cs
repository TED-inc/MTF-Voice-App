namespace MTFVoiceTools.KlattSynth.Params;

public static class FrameParametersSamples
{
    public static readonly FrameParameters Default = 
        new ()
    {
        Duration = 1,
        F0 = 247,
        FlutterLevel = 0.25,
        OpenPhaseRatio = 0.7,
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
    };

    public static readonly FrameParameters MaleI = 
        new ()
    {
        Duration = 1,
        F0 = 120,
        FlutterLevel = 0.25,
        OpenPhaseRatio = 0.7,
        BreathinessDb = -25,
        TiltDb = 0f,
        GainDb = double.NaN,
        AgcRmsLevel = 0.18,

        NasalFormantFreq = double.NaN,
        NasalFormantBw = double.NaN,

        OralFormantFreq = [270, 2290, 3010, 3400, 4000, 4800],
        OralFormantBw = [60, 110, 150, 200, 300, 450],

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
        FricationDb = -40,
        FricationMod = 0.5,
        ParallelBypassDb = -99,
        NasalFormantDb = double.NaN,

        OralFormantDb = [0, -7, -12, -20, -30, -40]
    };

    public static readonly FrameParameters FemaleI = 
        new ()
    {
        Duration = 1,
        F0 = 220,
        FlutterLevel = 0.25,
        OpenPhaseRatio = 0.7,
        BreathinessDb = -23,
        TiltDb = 0f,
        GainDb = double.NaN,
        AgcRmsLevel = 0.18,

        NasalFormantFreq = double.NaN,
        NasalFormantBw = double.NaN,

        OralFormantFreq = [310, 2700, 3400, 4100, 4800, 5500],
        OralFormantBw = [70, 120, 170, 230, 350, 500],

// Cascade branch:
        CascadeEnabled = true,
        CascadeVoicingDb = 0,
        CascadeAspirationDb = -23,
        CascadeAspirationMod = 0.5,
        NasalAntiformantFreq = double.NaN,
        NasalAntiformantBw = double.NaN,

// Parallel branch:
        ParallelEnabled = false,
        ParallelVoicingDb = 0,
        ParallelAspirationDb = -23,
        ParallelAspirationMod = 0.5,
        FricationDb = -40,
        FricationMod = 0.5,
        ParallelBypassDb = -99,
        NasalFormantDb = double.NaN,

        OralFormantDb = [0, -6, -10, -18, -28, -38]
    };

    public static readonly FrameParameters MaleO = 
        new ()
    {
        Duration = 1,
        F0 = 120,
        FlutterLevel = 0.25,
        OpenPhaseRatio = 0.7,
        BreathinessDb = -25,
        TiltDb = 0f,
        GainDb = double.NaN,
        AgcRmsLevel = 0.18,

        NasalFormantFreq = double.NaN,
        NasalFormantBw = double.NaN,

        OralFormantFreq = [450, 750, 2650, 3700, 5200, 5600],
        OralFormantBw = [80, 100, 130, 200, 320, 450],

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
        FricationDb = -40,
        FricationMod = 0.5,
        ParallelBypassDb = -99,
        NasalFormantDb = double.NaN,

        OralFormantDb = [0, -6, -12, -20, -30, -40]
    };

    public static readonly FrameParameters FemaleO =
        new ()
        {
            Duration = 1,
            F0 = 220,
            FlutterLevel = 0.25,
            OpenPhaseRatio = 0.7,
            BreathinessDb = -23,
            TiltDb = 0f,
            GainDb = double.NaN,
            AgcRmsLevel = 0.18,

            NasalFormantFreq = double.NaN,
            NasalFormantBw = double.NaN,

            OralFormantFreq = [560, 880, 2850, 3920, 5400, 6000],
            OralFormantBw = [90, 110, 150, 220, 350, 500],

// Cascade branch:
            CascadeEnabled = true,
            CascadeVoicingDb = 0,
            CascadeAspirationDb = -23,
            CascadeAspirationMod = 0.5,
            NasalAntiformantFreq = double.NaN,
            NasalAntiformantBw = double.NaN,

// Parallel branch:
            ParallelEnabled = false,
            ParallelVoicingDb = 0,
            ParallelAspirationDb = -23,
            ParallelAspirationMod = 0.5,
            FricationDb = -40,
            FricationMod = 0.5,
            ParallelBypassDb = -99,
            NasalFormantDb = double.NaN,

            OralFormantDb = [0, -5, -10, -18, -28, -38]
        };

    public static readonly FrameParameters MaleA =
        new ()
        {
            Duration = 1,
            F0 = 120,
            FlutterLevel = 0.25,
            OpenPhaseRatio = 0.7,
            BreathinessDb = -25,
            TiltDb = 0f,
            GainDb = double.NaN,
            AgcRmsLevel = 0.18,

            NasalFormantFreq = double.NaN,
            NasalFormantBw = double.NaN,

            OralFormantFreq = [730, 1090, 2440, 3200, 4000, 4800],
            OralFormantBw = [80, 90, 120, 200, 300, 450],

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
            FricationDb = -40,
            FricationMod = 0.5,
            ParallelBypassDb = -99,
            NasalFormantDb = double.NaN,

            OralFormantDb = [0, -7, -12, -20, -30, -40]
        };

    public static readonly FrameParameters FemaleA =
        new ()
        {
            Duration = 1,
            F0 = 220,
            FlutterLevel = 0.25,
            OpenPhaseRatio = 0.7,
            BreathinessDb = -23,
            TiltDb = 0f,
            GainDb = double.NaN,
            AgcRmsLevel = 0.18,

            NasalFormantFreq = double.NaN,
            NasalFormantBw = double.NaN,

            OralFormantFreq = [850, 1220, 2900, 3800, 4800, 5500],
            OralFormantBw = [90, 100, 150, 220, 350, 500],

// Cascade branch:
            CascadeEnabled = true,
            CascadeVoicingDb = 0,
            CascadeAspirationDb = -23,
            CascadeAspirationMod = 0.5,
            NasalAntiformantFreq = double.NaN,
            NasalAntiformantBw = double.NaN,

// Parallel branch:
            ParallelEnabled = false,
            ParallelVoicingDb = 0,
            ParallelAspirationDb = -23,
            ParallelAspirationMod = 0.5,
            FricationDb = -40,
            FricationMod = 0.5,
            ParallelBypassDb = -99,
            NasalFormantDb = double.NaN,

            OralFormantDb = [0, -6, -10, -18, -28, -38]
        };

    public static readonly FrameParameters MaleE =
        new ()
        {
            Duration = 1,
            F0 = 120,
            FlutterLevel = 0.25,
            OpenPhaseRatio = 0.7,
            BreathinessDb = -25,
            TiltDb = 0f,
            GainDb = double.NaN,
            AgcRmsLevel = 0.18,

            NasalFormantFreq = double.NaN,
            NasalFormantBw = double.NaN,

            OralFormantFreq = [530, 1840, 2480, 3300, 4100, 4800],
            OralFormantBw = [70, 100, 120, 200, 320, 450],

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
            FricationDb = -40,
            FricationMod = 0.5,
            ParallelBypassDb = -99,
            NasalFormantDb = double.NaN,

            OralFormantDb = [0, -7, -12, -20, -30, -40]
        };

    public static readonly FrameParameters FemaleE =
        new ()
        {
            Duration = 1,
            F0 = 220,
            FlutterLevel = 0.25,
            OpenPhaseRatio = 0.7,
            BreathinessDb = -23,
            TiltDb = 0f,
            GainDb = double.NaN,
            AgcRmsLevel = 0.18,

            NasalFormantFreq = double.NaN,
            NasalFormantBw = double.NaN,

            OralFormantFreq = [600, 2050, 2900, 3800, 4800, 5500],
            OralFormantBw = [80, 110, 150, 220, 350, 500],

// Cascade branch:
            CascadeEnabled = true,
            CascadeVoicingDb = 0,
            CascadeAspirationDb = -23,
            CascadeAspirationMod = 0.5,
            NasalAntiformantFreq = double.NaN,
            NasalAntiformantBw = double.NaN,

// Parallel branch:
            ParallelEnabled = false,
            ParallelVoicingDb = 0,
            ParallelAspirationDb = -23,
            ParallelAspirationMod = 0.5,
            FricationDb = -40,
            FricationMod = 0.5,
            ParallelBypassDb = -99,
            NasalFormantDb = double.NaN,

            OralFormantDb = [0, -6, -10, -18, -28, -38]
        };

    public static readonly FrameParameters MaleU =
        new ()
        {
            Duration = 1,
            F0 = 120,
            FlutterLevel = 0.25,
            OpenPhaseRatio = 0.7,
            BreathinessDb = -25,
            TiltDb = 0f,
            GainDb = double.NaN,
            AgcRmsLevel = 0.18,

            NasalFormantFreq = double.NaN,
            NasalFormantBw = double.NaN,

            OralFormantFreq = [300, 870, 2240, 3000, 3600, 4300],
            OralFormantBw = [60, 90, 120, 200, 300, 400],

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
            FricationDb = -40,
            FricationMod = 0.5,
            ParallelBypassDb = -99,
            NasalFormantDb = double.NaN,

            OralFormantDb = [0, -7, -12, -20, -30, -40]
        };

    public static readonly FrameParameters FemaleU =
        new ()
        {
            Duration = 1,
            F0 = 220,
            FlutterLevel = 0.25,
            OpenPhaseRatio = 0.7,
            BreathinessDb = -23,
            TiltDb = 0f,
            GainDb = double.NaN,
            AgcRmsLevel = 0.18,

            NasalFormantFreq = double.NaN,
            NasalFormantBw = double.NaN,

            OralFormantFreq = [370, 1050, 2700, 3500, 4500, 5200],
            OralFormantBw = [70, 100, 150, 220, 350, 450],

// Cascade branch:
            CascadeEnabled = true,
            CascadeVoicingDb = 0,
            CascadeAspirationDb = -23,
            CascadeAspirationMod = 0.5,
            NasalAntiformantFreq = double.NaN,
            NasalAntiformantBw = double.NaN,

// Parallel branch:
            ParallelEnabled = false,
            ParallelVoicingDb = 0,
            ParallelAspirationDb = -23,
            ParallelAspirationMod = 0.5,
            FricationDb = -40,
            FricationMod = 0.5,
            ParallelBypassDb = -99,
            NasalFormantDb = double.NaN,

            OralFormantDb = [0, -6, -10, -18, -28, -38]
        };
}