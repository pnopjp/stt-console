namespace SttConsole.Services.Audio;

/// <summary>
/// Fallback for platforms without WASAPI. Only the default microphone is offered: the Speech SDK can't
/// enumerate devices, and capturing PC output has no OS-level equivalent on Linux/macOS (it needs a virtual
/// device such as BlackHole, which then appears as the default input).
/// </summary>
public sealed class DefaultMicrophoneAudioInputFactory : IAudioInputFactory
{
    public IReadOnlyList<string> ListSources() => [AudioSourceLabels.Default];

    // A label saved on another OS (e.g. "[PC Output] ...") is ignored rather than failing the start.
    public IAudioInput Open(string label) => new DefaultMicrophoneAudioInput();
}
