using NAudio.Wave;

namespace MTFvoiceApp
{
    internal sealed class SimpleNoiseGateSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly float _thresholdLin;
        private readonly float _attenuationLin;
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
            _thresholdLin = AudioMath.DbToLinear(thresholdDb);
            _attenuationLin = AudioMath.DbToLinear(attenuationDb);

            _attackCoef = TimeToCoef(attackMs, source.WaveFormat.SampleRate);
            _releaseCoef = TimeToCoef(releaseMs, source.WaveFormat.SampleRate);
        }

        public WaveFormat WaveFormat => _source.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            int read = _source.Read(buffer, offset, count);
            for (int i = 0; i < read; i++)
            {
                float x = buffer[offset + i];
                float abs = Math.Abs(x);

                float target = (abs >= _thresholdLin) ? 1f : _attenuationLin;

                if (target > _currentGain)
                {
                    _currentGain += (target - _currentGain) * _attackCoef;
                }
                else
                {
                    _currentGain += (target - _currentGain) * _releaseCoef;
                }

                buffer[offset + i] = x * _currentGain;
            }
            return read;
        }

        static float TimeToCoef(float ms, int sampleRate)
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
