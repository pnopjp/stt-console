using NAudio.Wave;

namespace SttConsole.Services.Audio;

/// <summary>
/// Averages all channels into mono. The stock StereoToMono provider breaks on 5.1/7.1 loopback devices.
/// </summary>
internal sealed class DownmixSampleProvider(ISampleProvider source) : ISampleProvider
{
    private float[] _scratch = [];

    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 1);

    public int Read(Span<float> buffer)
    {
        var channels = source.WaveFormat.Channels;
        var needed = buffer.Length * channels;
        if (_scratch.Length < needed)
        {
            _scratch = new float[needed];
        }

        var read = source.Read(_scratch.AsSpan(0, needed));
        var frames = read / channels;
        for (var i = 0; i < frames; i++)
        {
            float sum = 0;
            for (var c = 0; c < channels; c++)
            {
                sum += _scratch[(i * channels) + c];
            }

            buffer[i] = sum / channels;
        }

        return frames;
    }
}
