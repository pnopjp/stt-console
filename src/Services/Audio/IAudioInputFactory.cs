namespace SttConsole.Services.Audio;

public interface IAudioInputFactory
{
    /// <summary>Lists the labels shown in the settings dialog. Settings persist the label itself.</summary>
    IReadOnlyList<string> ListSources();

    IAudioInput Open(string label);
}
