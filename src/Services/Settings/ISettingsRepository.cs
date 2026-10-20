using SttConsole.Models;

namespace SttConsole.Services.Settings;

public interface ISettingsRepository
{
    AppSettings Settings { get; }

    void Save();
}
