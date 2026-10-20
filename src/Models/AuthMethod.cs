namespace SttConsole.Models;

/// <summary>
/// Persisted as an integer in settings.json, so existing members must keep their order.
/// </summary>
public enum AuthMethod
{
    ApiKey,
    ManagedIdentity,
}
