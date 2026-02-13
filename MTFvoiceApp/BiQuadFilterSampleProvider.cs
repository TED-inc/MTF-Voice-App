using NAudio.Dsp;
using NAudio.Wave;

namespace MTFvoiceApp
{
    internal sealed class BiQuadFilterSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider source;
        private readonly BiQuadFilter filter;

        public BiQuadFilterSampleProvider(ISampleProvider source, BiQuadFilter filter)
        {
            this.source = source;
            this.filter = filter;
        }

        public WaveFormat WaveFormat => source.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            int read = source.Read(buffer, offset, count);
            for (int n = 0; n < read; n++)
            {
                buffer[offset + n] = filter.Transform(buffer[offset + n]);
            }

            return read;
        }
    }
}
