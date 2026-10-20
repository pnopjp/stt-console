namespace SttConsole.Models;

/// <summary>
/// User settings persisted to settings.json. Property names are the JSON keys, so renaming one resets the saved value.
/// </summary>
public class AppSettings
{
    public const string DefaultRedirectUri = "http://localhost:60916";

    public string AudioDevice { get; set; } = "(Default)";

    public string SpeechEndpoint { get; set; } = "";

    public string SpeechRegion { get; set; } = "";

    public AuthMethod SpeechAuth { get; set; } = AuthMethod.ApiKey;

    // API Key
    public string SpeechApiKey { get; set; } = "";

    // Managed Identity (Entra ID user sign-in)
    public string TenantId { get; set; } = "";

    public string ClientId { get; set; } = "";

    public string RedirectUri { get; set; } = DefaultRedirectUri;

    // ARM resource ID of the Speech resource; enables regional-endpoint auth with an Entra token
    public string SpeechResourceId { get; set; } = "";

    public bool DeviceCodeFlow { get; set; }

    public bool IsSignedIn { get; set; }

    public string RecognitionLang { get; set; } = "ja-JP";

    public string TranslationLang { get; set; } = "en-US";

    public bool UseTwoPass { get; set; }
}
