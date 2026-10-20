using Azure.Core;
using Azure.Identity;
using SttConsole.Models;
using SttConsole.Services.Settings;

namespace SttConsole.Services.Auth;

public sealed class AuthService : IAuthService
{
    public const string SpeechScope = "https://cognitiveservices.azure.com/.default";

    // Changing this name orphans the persisted token cache and signs everyone out.
    private const string CacheName = "SttConsole";

    private readonly ISettingsRepository _repo;
    private readonly string _recordPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SttConsole",
        "auth-record.json");

    private TokenCredential? _credential;

    public AuthService(ISettingsRepository repo)
    {
        _repo = repo;

        // IsSignedIn is persisted, but without the record the token cache can't be located again.
        if (_repo.Settings.IsSignedIn && !TryRestore())
        {
            _repo.Settings.IsSignedIn = false;
        }
    }

    public bool IsSignedIn => _repo.Settings.IsSignedIn && _credential is not null;

    public TokenCredential? Credential => IsSignedIn ? _credential : null;

    public async Task<bool> SignInAsync(
        bool useDeviceCodeFlow = false,
        Action<string, string>? onDeviceCode = null,
        CancellationToken ct = default)
    {
        var settings = _repo.Settings;
        if (string.IsNullOrWhiteSpace(settings.TenantId) || string.IsNullOrWhiteSpace(settings.ClientId))
        {
            throw new InvalidOperationException("Tenant ID and Client ID are required.");
        }

        var cache = new TokenCachePersistenceOptions { Name = CacheName };
        var scopes = new TokenRequestContext([SpeechScope]);

        AuthenticationRecord record;
        if (useDeviceCodeFlow)
        {
            var credential = new DeviceCodeCredential(new DeviceCodeCredentialOptions
            {
                TenantId = settings.TenantId,
                ClientId = settings.ClientId,
                TokenCachePersistenceOptions = cache,
                DeviceCodeCallback = (info, _) =>
                {
                    onDeviceCode?.Invoke(info.VerificationUri.ToString(), info.UserCode);
                    return Task.CompletedTask;
                },
            });
            record = await credential.AuthenticateAsync(scopes, ct).ConfigureAwait(false);
        }
        else
        {
            var options = new InteractiveBrowserCredentialOptions
            {
                TenantId = settings.TenantId,
                ClientId = settings.ClientId,
                TokenCachePersistenceOptions = cache,

                // Settings saved before the default existed hold "", so fall back here too
                RedirectUri = new Uri(string.IsNullOrWhiteSpace(settings.RedirectUri)
                    ? AppSettings.DefaultRedirectUri
                    : settings.RedirectUri),
            };
            record = await new InteractiveBrowserCredential(options).AuthenticateAsync(scopes, ct).ConfigureAwait(false);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_recordPath)!);
        await using (var stream = File.Create(_recordPath))
        {
            await record.SerializeAsync(stream, ct).ConfigureAwait(false);
        }

        _credential = CreateCredential(record, useDeviceCodeFlow);
        settings.IsSignedIn = true;
        _repo.Save();
        return true;
    }

    public Task SignOutAsync()
    {
        // Entra tokens can't be revoked from the client; dropping the record makes the cached token unreachable.
        _credential = null;
        _repo.Settings.IsSignedIn = false;
        _repo.Save();
        try
        {
            File.Delete(_recordPath);
        }
        catch (IOException)
        {
            // A leftover record is harmless: IsSignedIn is already false.
        }

        return Task.CompletedTask;
    }

    private bool TryRestore()
    {
        try
        {
            if (!File.Exists(_recordPath))
            {
                return false;
            }

            using var stream = File.OpenRead(_recordPath);
            var record = AuthenticationRecord.Deserialize(stream);
            _credential = CreateCredential(record, _repo.Settings.DeviceCodeFlow);
            return true;
        }
        catch (Exception)
        {
            // A corrupt record or cache is treated as signed out; the user signs in again.
            return false;
        }
    }

    // Both credential types are given the record so GetToken refreshes silently from the persisted cache.
    // A refresh failure falls back to prompting, which can't be shown in the TUI; the error surfaces as a
    // recognizer cancellation and the user signs in again.
    private TokenCredential CreateCredential(AuthenticationRecord record, bool deviceCode)
    {
        var settings = _repo.Settings;
        var cache = new TokenCachePersistenceOptions { Name = CacheName };

        if (deviceCode)
        {
            return new DeviceCodeCredential(new DeviceCodeCredentialOptions
            {
                TenantId = settings.TenantId,
                ClientId = settings.ClientId,
                AuthenticationRecord = record,
                TokenCachePersistenceOptions = cache,
                DeviceCodeCallback = (_, _) => throw new AuthenticationFailedException("Session expired. Sign in again."),
            });
        }

        return new InteractiveBrowserCredential(new InteractiveBrowserCredentialOptions
        {
            TenantId = settings.TenantId,
            ClientId = settings.ClientId,
            AuthenticationRecord = record,
            TokenCachePersistenceOptions = cache,
            DisableAutomaticAuthentication = true,
        });
    }
}
