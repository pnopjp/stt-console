using Azure.Core;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Translation;
using SttConsole.Models;
using SttConsole.Services.Audio;
using SttConsole.Services.Auth;
using SttConsole.Services.Settings;

namespace SttConsole.Services.Speech;

public sealed class SpeechService : ISpeechService
{
    // Tokens last about an hour while sessions can run longer, so the recognizer gets a fresh one earlier.
    private static readonly TimeSpan TokenRefreshInterval = TimeSpan.FromMinutes(30);

    private readonly ISettingsRepository _repo;
    private readonly IAuthService _auth;
    private readonly IAudioInputFactory _audioFactory;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private IAudioInput? _input;
    private Func<Task>? _stopRecognition;
    private CancellationTokenSource? _refreshCts;

    // The latest interim result that hasn't been finalized yet; promoted to a final result on stop.
    private SpeechPartialResult? _pending;

    public SpeechService(ISettingsRepository repo, IAuthService auth, IAudioInputFactory audioFactory)
    {
        _repo = repo;
        _auth = auth;
        _audioFactory = audioFactory;
    }

    public event EventHandler<SpeechPartialResult>? PartialResultReceived;

    public event EventHandler<SpeechFinalResult>? FinalResultReceived;

    public event EventHandler<string>? ErrorOccurred;

    public bool IsRunning { get; private set; }

    // MainWindow calls Start/Stop without awaiting, so failures are reported through ErrorOccurred.
    public async Task StartAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await StopCoreAsync().ConfigureAwait(false);
            await StartCoreAsync().ConfigureAwait(false);
            IsRunning = true;
        }
        catch (Exception ex)
        {
            await StopCoreAsync().ConfigureAwait(false);
            ErrorOccurred?.Invoke(this, $"Failed to start recognition: {ex.Message}");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await StopCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _gate.Dispose();
    }

    // Translation uses language codes, not locales: "en-US" -> "en", but Chinese keeps its script.
    private static string ToTranslationCode(string locale) => locale switch
    {
        "zh-CN" or "zh-SG" => "zh-Hans",
        "zh-TW" or "zh-HK" => "zh-Hant",
        _ => locale.Split('-')[0],
    };

    // "2Pass" in the UI. The service runs a second recognition pass in parallel and replaces only the
    // final text of each segment, so partial-result latency is unchanged.
    private static void ApplyPostProcessing(SpeechConfig config, AppSettings settings)
    {
        if (settings.UseTwoPass)
        {
            config.SetProperty(PropertyId.SpeechServiceResponse_PostProcessingOption, "PostRefinement");
        }
    }

    private static bool HasEndpoint(AppSettings settings) => !string.IsNullOrWhiteSpace(settings.SpeechEndpoint);

    // With a resource ID the regional endpoint is used and the token is passed as "aad#<resourceId>#<token>",
    // which avoids needing a custom-domain endpoint. Without one, FromEndpoint(Uri, TokenCredential) is used.
    private static bool UsesResourceId(AppSettings settings) =>
        settings.SpeechAuth == AuthMethod.ManagedIdentity && !string.IsNullOrWhiteSpace(settings.SpeechResourceId);

    // Entra tokens are only accepted by a custom-domain endpoint, not the regional one.
    private static Uri RequireEndpoint(AppSettings settings) =>
        HasEndpoint(settings)
            ? new Uri(settings.SpeechEndpoint)
            : throw new InvalidOperationException("Endpoint (custom domain) is required for Entra ID sign-in.");

    private async Task StartCoreAsync()
    {
        var settings = _repo.Settings;
        var source = settings.RecognitionLang;
        var target = ToTranslationCode(settings.TranslationLang);
        var aadToken = UsesResourceId(settings) ? await GetAadTokenAsync(settings).ConfigureAwait(false) : null;

        // The input owns the SDK AudioConfig and must outlive this method: disposing it early
        // cuts the recognizer off from its audio.
        _input = _audioFactory.Open(settings.AudioDevice);

        if (target != ToTranslationCode(source))
        {
            await StartTranslationAsync(settings, aadToken, source, target).ConfigureAwait(false);
        }
        else
        {
            await StartRecognitionAsync(settings, aadToken, source).ConfigureAwait(false);
        }

        _input.Start();
    }

    private async Task StartTranslationAsync(AppSettings settings, string? aadToken, string source, string target)
    {
        var config = CreateTranslationConfig(settings, aadToken);
        config.SpeechRecognitionLanguage = source;
        config.AddTargetLanguage(target);
        ApplyPostProcessing(config, settings);

        var recognizer = new TranslationRecognizer(config, _input!.AudioConfig);
        recognizer.Recognizing += (_, e) =>
            RaisePartial(e.Result.Text, e.Result.Translations.GetValueOrDefault(target));
        recognizer.Recognized += (_, e) =>
        {
            if (e.Result.Reason == ResultReason.TranslatedSpeech)
            {
                RaiseFinal(e.Result.Text, e.Result.Translations.GetValueOrDefault(target));
            }
        };
        recognizer.Canceled += (_, e) => OnCanceled(e.Reason, e.ErrorDetails);

        _stopRecognition = async () =>
        {
            await recognizer.StopContinuousRecognitionAsync().ConfigureAwait(false);
            recognizer.Dispose();
        };
        if (aadToken is not null)
        {
            StartTokenRefresh(settings, token => recognizer.AuthorizationToken = token);
        }

        await recognizer.StartContinuousRecognitionAsync().ConfigureAwait(false);
    }

    private async Task StartRecognitionAsync(AppSettings settings, string? aadToken, string source)
    {
        var config = CreateSpeechConfig(settings, aadToken);
        config.SpeechRecognitionLanguage = source;
        ApplyPostProcessing(config, settings);

        var recognizer = new SpeechRecognizer(config, _input!.AudioConfig);
        recognizer.Recognizing += (_, e) => RaisePartial(e.Result.Text, null);
        recognizer.Recognized += (_, e) =>
        {
            if (e.Result.Reason == ResultReason.RecognizedSpeech)
            {
                RaiseFinal(e.Result.Text, null);
            }
        };
        recognizer.Canceled += (_, e) => OnCanceled(e.Reason, e.ErrorDetails);

        _stopRecognition = async () =>
        {
            await recognizer.StopContinuousRecognitionAsync().ConfigureAwait(false);
            recognizer.Dispose();
        };
        if (aadToken is not null)
        {
            StartTokenRefresh(settings, token => recognizer.AuthorizationToken = token);
        }

        await recognizer.StartContinuousRecognitionAsync().ConfigureAwait(false);
    }

    // Order matters: the recognizer must stop before the push stream closes, or it reports a spurious error.
    private async Task StopCoreAsync()
    {
        IsRunning = false;

        _refreshCts?.Cancel();
        _refreshCts?.Dispose();
        _refreshCts = null;

        var stop = _stopRecognition;
        _stopRecognition = null;
        if (stop is not null)
        {
            try
            {
                await stop().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, $"Failed to stop recognition: {ex.Message}");
            }
        }

        _input?.Dispose();
        _input = null;

        // Anything the service returned during stop has already cleared _pending, so this never duplicates a result.
        if (_pending is { Text.Length: > 0 } pending)
        {
            RaiseFinal(pending.Text, pending.Translation);
        }

        _pending = null;
    }

    private void RaisePartial(string text, string? translation)
    {
        var partial = new SpeechPartialResult(text, translation, DateTime.Now);
        _pending = partial;
        PartialResultReceived?.Invoke(this, partial);
    }

    private void RaiseFinal(string text, string? translation)
    {
        _pending = null;
        FinalResultReceived?.Invoke(this, new SpeechFinalResult(text, translation, DateTime.Now));
    }

    private void OnCanceled(CancellationReason reason, string details)
    {
        if (reason == CancellationReason.Error)
        {
            ErrorOccurred?.Invoke(this, $"Recognition error: {details}");
        }
    }

    private SpeechConfig CreateSpeechConfig(AppSettings settings, string? aadToken) => settings.SpeechAuth switch
    {
        AuthMethod.ManagedIdentity when aadToken is not null =>
            SpeechConfig.FromAuthorizationToken(aadToken, settings.SpeechRegion),
        AuthMethod.ApiKey when HasEndpoint(settings) =>
            SpeechConfig.FromEndpoint(new Uri(settings.SpeechEndpoint), settings.SpeechApiKey),
        AuthMethod.ApiKey =>
            SpeechConfig.FromSubscription(settings.SpeechApiKey, settings.SpeechRegion),
        _ => SpeechConfig.FromEndpoint(RequireEndpoint(settings), RequireCredential()),
    };

    private SpeechTranslationConfig CreateTranslationConfig(AppSettings settings, string? aadToken) => settings.SpeechAuth switch
    {
        AuthMethod.ManagedIdentity when aadToken is not null =>
            SpeechTranslationConfig.FromAuthorizationToken(aadToken, settings.SpeechRegion),
        AuthMethod.ApiKey when HasEndpoint(settings) =>
            SpeechTranslationConfig.FromEndpoint(new Uri(settings.SpeechEndpoint), settings.SpeechApiKey),
        AuthMethod.ApiKey =>
            SpeechTranslationConfig.FromSubscription(settings.SpeechApiKey, settings.SpeechRegion),
        _ => SpeechTranslationConfig.FromEndpoint(RequireEndpoint(settings), RequireCredential()),
    };

    private async Task<string> GetAadTokenAsync(AppSettings settings, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(settings.SpeechRegion))
        {
            throw new InvalidOperationException("Region is required when a Speech Resource ID is set.");
        }

        var token = await RequireCredential()
            .GetTokenAsync(new TokenRequestContext([AuthService.SpeechScope]), ct)
            .ConfigureAwait(false);
        return $"aad#{settings.SpeechResourceId.Trim()}#{token.Token}";
    }

    private void StartTokenRefresh(AppSettings settings, Action<string> apply)
    {
        _refreshCts = new CancellationTokenSource();
        var ct = _refreshCts.Token;
        _ = Task.Run(
            async () =>
            {
                using var timer = new PeriodicTimer(TokenRefreshInterval);
                try
                {
                    while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
                    {
                        apply(await GetAadTokenAsync(settings, ct).ConfigureAwait(false));
                    }
                }
                catch (OperationCanceledException)
                {
                    // Stopped by StopCoreAsync.
                }
                catch (Exception ex)
                {
                    ErrorOccurred?.Invoke(this, $"Failed to refresh token: {ex.Message}");
                }
            },
            ct);
    }

    private TokenCredential RequireCredential() =>
        _auth.Credential ?? throw new InvalidOperationException("Not signed in. Sign in from the Speech Service settings.");
}
