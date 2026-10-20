using Azure.Core;

namespace SttConsole.Services.Auth;

public interface IAuthService
{
    bool IsSignedIn { get; }

    /// <summary>Gets the credential for Speech Service. Null until signed in.</summary>
    TokenCredential? Credential { get; }

    /// <summary>
    /// Signs in with the Entra ID settings. With device code flow, <paramref name="onDeviceCode"/>
    /// receives (verificationUrl, userCode) as soon as they are issued.
    /// </summary>
    Task<bool> SignInAsync(
        bool useDeviceCodeFlow = false,
        Action<string, string>? onDeviceCode = null,
        CancellationToken ct = default);

    Task SignOutAsync();
}
