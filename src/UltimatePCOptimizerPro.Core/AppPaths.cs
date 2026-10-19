namespace UltimatePCOptimizerPro.Core;

/// <summary>Central location of every file the application writes. All data is per-user and local.</summary>
public static class AppPaths
{
    public const string AppName = "UltimatePCOptimizerPro";
    public const string RestorePointPrefix = "ULTIMATE-PC-OPTIMIZER";

    private static string? _rootOverride;

    /// <summary>Used by tests to redirect all storage to a temporary folder.</summary>
    public static void OverrideRoot(string? root) => _rootOverride = root;

    public static string Root
    {
        get
        {
            var root = _rootOverride ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppName);
            Directory.CreateDirectory(root);
            return root;
        }
    }

    public static string Logs => Ensure(Path.Combine(Root, "Logs"));
    public static string Operations => Ensure(Path.Combine(Root, "Operations"));
    public static string Benchmarks => Ensure(Path.Combine(Root, "Benchmarks"));
    public static string Exports => Ensure(Path.Combine(Root, "Exports"));
    public static string UserProfiles => Ensure(Path.Combine(Root, "Profiles"));
    public static string SettingsFile => Path.Combine(Root, "settings.json");
    public static string GamesFile => Path.Combine(Root, "games.json");
    public static string AuditFile => Path.Combine(Root, "history.jsonl");

    private static string Ensure(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }
}
