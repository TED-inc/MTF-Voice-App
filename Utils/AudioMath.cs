using System;

namespace MTFVoiceTools.Utils;

internal static class AudioMath
{
    public static float DbToLinear(float db)
    {
        return (float)Math.Pow(10.0, db / 20.0);
    }
}