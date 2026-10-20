using Microsoft.Extensions.DependencyInjection;
using SttConsole.Services.Audio;
using SttConsole.Services.Auth;
using SttConsole.Services.Settings;
using SttConsole.Services.Speech;
using SttConsole.UI;
using Terminal.Gui.App;

var (elapsed, logPath) = await RunAppAsync();

// Printed only after the application is disposed: while Terminal.Gui is still active the output
// goes to the alternate screen and is lost (or overwritten) when the original screen is restored.
Console.WriteLine();
Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
Console.WriteLine("Recognition session ended");
Console.WriteLine($"  Elapsed : {(int)elapsed.TotalHours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}");
Console.WriteLine(File.Exists(logPath)
    ? $"  Log file: {logPath}"
    : "  Log file: (not created because there were no recognition results)");
Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

static async Task<(TimeSpan Elapsed, string LogPath)> RunAppAsync()
{
    using var app = Application.Create();
    app.Init();

    await using var provider = BuildServiceProvider(app);

    using var window = new MainWindow(
        app,
        provider.GetRequiredService<ISettingsRepository>(),
        provider.GetRequiredService<IAuthService>(),
        provider.GetRequiredService<ISpeechService>(),
        provider.GetRequiredService<IAudioInputFactory>());

    app.Run(window);

    return (window.GetTotalElapsed(), window.LogFilePath);
}

static ServiceProvider BuildServiceProvider(IApplication app)
{
    var services = new ServiceCollection();

    services.AddSingleton<IApplication>(app);
    services.AddSingleton<ISettingsRepository, SettingsRepository>();
    services.AddSingleton<IAuthService, AuthService>();
    services.AddSingleton<ISpeechService, SpeechService>();

    // NAudio (WASAPI) is Windows-only; other platforms fall back to the Speech SDK's own microphone capture
    services.AddSingleton<IAudioInputFactory>(_ => OperatingSystem.IsWindows()
        ? new WasapiAudioInputFactory()
        : new DefaultMicrophoneAudioInputFactory());

    return services.BuildServiceProvider();
}
