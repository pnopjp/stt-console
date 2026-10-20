using SttConsole.Models;

namespace SttConsole.Services.Speech;

public interface ISpeechService : IAsyncDisposable
{
    event EventHandler<SpeechPartialResult>? PartialResultReceived;

    event EventHandler<SpeechFinalResult>? FinalResultReceived;

    event EventHandler<string>? ErrorOccurred;

    bool IsRunning { get; }

    Task StartAsync(CancellationToken ct = default);

    Task StopAsync(CancellationToken ct = default);

}
