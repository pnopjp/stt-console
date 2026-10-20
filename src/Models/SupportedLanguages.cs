namespace SttConsole.Models;

public static class SupportedLanguages
{
    public static readonly IReadOnlyList<LanguageOption> All =
    [
        new("Japanese", "ja-JP"),
        new("English", "en-US"),
        new("Chinese", "zh-CN"),
        new("Korean", "ko-KR"),
        new("French", "fr-FR"),
        new("German", "de-DE"),
        new("Spanish", "es-ES"),
    ];
}
