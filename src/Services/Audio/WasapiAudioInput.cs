using System.Runtime.Versioning;
using Microsoft.CognitiveServices.Speech.Audio;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace SttConsole.Services.Audio;

/// <summary>
/// Captures a WASAPI device (microphone or loopback) and feeds 16 kHz / 16-bit / mono PCM
/// into a Speech SDK push stream, which is the only format the SDK accepts for custom input.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WasapiAudioInput : IAudioInput
{
    private const int SampleRate = 16000;

    private readonly MMDeviceEnumerator _enumerator = new();
#pragma warning disable CS0618 // See the Why-not note in the constructor
    private readonly WasapiCapture _capture;
#pragma warning restore CS0618
    private readonly BufferedWaveProvider _buffer;
    private readonly SampleToWaveProvider16 _pcm16;
    private readonly byte[] _chunk = new byte[SampleRate * 2 / 10];
    private readonly PushAudioInputStream _stream;

    public WasapiAudioInput(string label)
    {
        var (device, loopback) = WasapiAudioSources.Resolve(label, _enumerator);

        // Why not WasapiRecorder: WasapiCapture/WasapiLoopbackCapture are marked obsolete in NAudio 3, but the
        // replacement was not adopted because this path is verified against real devices (including 192 kHz loopback).
#pragma warning disable CS0618
        _capture = loopback ? new WasapiLoopbackCapture(device) : new WasapiCapture(device);
#pragma warning restore CS0618

        _buffer = new BufferedWaveProvider(_capture.WaveFormat)
        {
            DiscardOnBufferOverflow = true,
            ReadFully = false,
        };
        var mono = new DownmixSampleProvider(_buffer.ToSampleProvider());
        var resampled = new WdlResamplingSampleProvider(mono, SampleRate);
        _pcm16 = new SampleToWaveProvider16(resampled);

        _stream = AudioInputStream.CreatePushStream(AudioStreamFormat.GetWaveFormatPCM(SampleRate, 16, 1));
        AudioConfig = AudioConfig.FromStreamInput(_stream);

        _capture.DataAvailable += OnDataAvailable;
    }

    public AudioConfig AudioConfig { get; }

    public void Start() => _capture.StartRecording();

    public void Dispose()
    {
        _capture.DataAvailable -= OnDataAvailable;
        try
        {
            _capture.StopRecording();
        }
        catch (Exception)
        {
            // The device may already be gone (e.g. unplugged); disposal continues regardless.
        }

        _capture.Dispose();
        _stream.Close();
        AudioConfig.Dispose();
        _stream.Dispose();
        _enumerator.Dispose();
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded == 0)
        {
            return;
        }

        _buffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
        int read;
        while ((read = _pcm16.Read(_chunk.AsSpan())) > 0)
        {
            _stream.Write(_chunk[..read]);
        }
    }
}
