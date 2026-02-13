using NAudio.Dsp;
using NAudio.Wave;

namespace MTFvoiceApp.Providers
{
    internal sealed class BiQuadFilterSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly BiQuadFilter _filter;

        public BiQuadFilterSampleProvider(ISampleProvider source, BiQuadFilter filter)
        {
            _source = source;
            _filter = filter;
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
            return _filter.Transform(input);
        }

        public static BiQuadFilter CreateDefaultHighPassFilter(float sampleRate)
        {
            return BiQuadFilter.HighPassFilter(
                sampleRate,
                cutoffFrequency: 80f,
                q: 0.707f);
        }
    }
}
