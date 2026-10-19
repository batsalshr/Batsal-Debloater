namespace UltimatePCOptimizerPro.Core.Models;

/// <summary>Static knowledge about a supported title (Data/Games.json).</summary>
public sealed class GameDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public List<string> ProcessNames { get; set; } = new();
    public List<string> RelativeExePaths { get; set; } = new();
    public int? SteamAppId { get; set; }
    public string? EpicAppName { get; set; }
    public List<string> UninstallNameMatches { get; set; } = new();
    public List<string> KnownInstallPaths { get; set; } = new();
    public string? LaunchUri { get; set; }
    public string AntiCheat { get; set; } = "";
    public string Engine { get; set; } = "";
    public string Bound { get; set; } = "";
    public List<string> Recommendations { get; set; } = new();
    public List<string> LowEndSettings { get; set; } = new();
    public List<string> HighEndSettings { get; set; } = new();
}

public sealed class DetectedGame
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? ExecutablePath { get; set; }
    public string? InstallDirectory { get; set; }
    public string DetectedVia { get; set; } = "";
    public string? LaunchUri { get; set; }
    public bool IsCustom { get; set; }
    public bool IsRunning { get; set; }
    public GameDefinition? Definition { get; set; }
    public string ProcessName => ExecutablePath is null ? Definition?.ProcessNames.FirstOrDefault() ?? "" : System.IO.Path.GetFileName(ExecutablePath);
}

public sealed class CustomGameEntry
{
    public string Name { get; set; } = "";
    public string ExecutablePath { get; set; } = "";
}
