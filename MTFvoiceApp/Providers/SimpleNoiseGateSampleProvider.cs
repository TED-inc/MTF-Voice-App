using NAudio.Wave;

namespace MTFvoiceApp.Providers
{
    internal sealed class SimpleNoiseGateSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly float _thresholdLinear;
        private readonly float _attenuationLinear;
        private readonly float _attackCoef;
        private readonly float _releaseCoef;

        private float _currentGain = 1f;

        public SimpleNoiseGateSampleProvider(
            ISampleProvider source,
            float thresholdDb = -45f,
            float attenuationDb = -18f,
            float attackMs = 10f,
            float releaseMs = 200f)
        {
            if (source.WaveFormat.Channels != 1)
                throw new ArgumentException("SimpleNoiseGateSampleProvider expects mono input.");

            _source = source;
            _thresholdLinear = AudioMath.DbToLinear(thresholdDb);
            _attenuationLinear = AudioMath.DbToLinear(attenuationDb);

            _attackCoef = TimeToCoef(attackMs, source.WaveFormat.SampleRate);
            _releaseCoef = TimeToCoef(releaseMs, source.WaveFormat.SampleRate);
        }

        public WaveFormat WaveFormat => _source.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            int read = _source.Read(buffer, offset, count);
            for (int i = 0; i < read; i++)
            {
                buffer[offset + i] = Transform(buffer[offset + i]);
            }
            return read;
        }

        private float Transform(float input)
        {
            float absInput = Math.Abs(input);
            float target = absInput >= _thresholdLinear ? 1f : _attenuationLinear;
            float diff = target - _currentGain;
            float coef = diff > 0f ? _attackCoef : _releaseCoef;
            _currentGain += diff * coef;

            return input * _currentGain;
        }

        private static float TimeToCoef(float ms, int sampleRate)
        {
            if (ms <= 0)
            {
                return 1f;
            }

            float t = ms / 1000f;
            return 1f - (float)Math.Exp(-1.0 / (sampleRate * t));
        }
    }
}
