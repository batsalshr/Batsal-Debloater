using UltimatePCOptimizerPro.Core.Models;

namespace UltimatePCOptimizerPro.Core.Services;

public sealed class SettingsService
{
    private readonly LoggingService _log;
    public AppSettings Current { get; private set; }
    public event Action<AppSettings>? Changed;

    public SettingsService(LoggingService log)
    {
        _log = log;
        Current = Load();
        _log.DetailedLogging = Current.DetailedLogging;
    }

    private AppSettings Load()
    {
        try
        {
            return JsonDefaults.ReadFile<AppSettings>(AppPaths.SettingsFile) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            _log.Warn("Settings", "Settings file was unreadable; defaults loaded.", ex);
            return new AppSettings();
        }
    }

    public void Save()
    {
        JsonDefaults.WriteFile(AppPaths.SettingsFile, Current);
        _log.DetailedLogging = Current.DetailedLogging;
        Changed?.Invoke(Current);
    }

    public void Reset()
    {
        Current = new AppSettings();
        Save();
    }

    public void Export(string path) => JsonDefaults.WriteFile(path, Current);

    public void Import(string path)
    {
        Current = JsonDefaults.ReadFile<AppSettings>(path) ?? throw new InvalidDataException("The file does not contain settings.");
        Save();
    }
}
