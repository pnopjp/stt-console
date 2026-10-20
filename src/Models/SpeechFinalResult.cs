namespace SttConsole.Models;

/// <summary>A finalized recognition result. <paramref name="Translated"/> is null when not translating.</summary>
public record SpeechFinalResult(string Recognized, string? Translated, DateTime Timestamp);
