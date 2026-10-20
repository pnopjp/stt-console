namespace SttConsole.Models;

/// <summary>An interim recognition result. <paramref name="Translation"/> is null when not translating or not yet available.</summary>
public record SpeechPartialResult(string Text, string? Translation, DateTime Timestamp);
