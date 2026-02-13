using NAudio.Wave;

namespace MTFvoiceApp.Providers
{
    internal class NormalizeToPeakSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly float _targetLinear;
        private readonly float _avgSecPerTransform;
        private readonly float _gainLerpDuration;
        private readonly float _maxGain;

        private float _recordingLengthInSec;
        private float _maxInput;

        public NormalizeToPeakSampleProvider(
            ISampleProvider source, 
            float targetDb = -1f,
            float gainLerpDuration = 0.25f,
            float maxGain = 10f)
        {
            const int BYTES_PER_SAMPLE = 4;

            _source = source;
            _targetLinear = AudioMath.DbToLinear(targetDb);
            _gainLerpDuration = gainLerpDuration;
            _maxGain = maxGain;

            _avgSecPerTransform = 1f / (WaveFormat.AverageBytesPerSecond / BYTES_PER_SAMPLE);
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

        int x;

        private float Transform(float input)
        {
            _recordingLengthInSec += _avgSecPerTransform;
            _maxInput = Math.Max(_maxInput, Math.Abs(input));

            if (x++ % 100 == 0)
                Console.WriteLine(_recordingLengthInSec);

            float gain = Math.Min(_maxGain, _targetLinear / _maxInput);
            float t = Math.Clamp(_recordingLengthInSec / _gainLerpDuration, 0f, 1f);

            return float.Lerp(input, input * gain, 1f);
        }
    }
}
