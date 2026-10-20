using System.Runtime.Versioning;

namespace SttConsole.Services.Audio;

[SupportedOSPlatform("windows")]
public sealed class WasapiAudioInputFactory : IAudioInputFactory
{
    public IReadOnlyList<string> ListSources() => WasapiAudioSources.List();

    public IAudioInput Open(string label) => new WasapiAudioInput(label);
}
