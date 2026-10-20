using Microsoft.CognitiveServices.Speech.Audio;

namespace SttConsole.Services.Audio;

/// <summary>
/// Uses the Speech SDK's own microphone capture, which works on Linux and macOS.
/// </summary>
public sealed class DefaultMicrophoneAudioInput : IAudioInput
{
    public AudioConfig AudioConfig { get; } = AudioConfig.FromDefaultMicrophoneInput();

    // The SDK owns the device and starts reading when recognition starts.
    public void Start()
    {
    }

    public void Dispose() => AudioConfig.Dispose();
}
