using Microsoft.CognitiveServices.Speech.Audio;

namespace SttConsole.Services.Audio;

/// <summary>An opened audio source feeding the Speech SDK. Disposing it releases the device and the SDK input.</summary>
public interface IAudioInput : IDisposable
{
    /// <summary>Gets the SDK input to give the recognizer. It stays valid until this object is disposed.</summary>
    AudioConfig AudioConfig { get; }

    void Start();
}
