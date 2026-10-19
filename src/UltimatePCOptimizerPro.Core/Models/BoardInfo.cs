namespace UltimatePCOptimizerPro.Core.Models;

public sealed class BoardInfo
{
    public string? SystemManufacturer { get; set; }
    public string? SystemModel { get; set; }
    public string? BoardManufacturer { get; set; }
    public string? BoardProduct { get; set; }
    public string? BiosVendor { get; set; }
    public string? BiosVersion { get; set; }
    public DateTime? BiosReleaseDate { get; set; }
    public string BootMode { get; set; } = "Unknown";
    public bool? SecureBootEnabled { get; set; }
    public bool IsLaptop { get; set; }
    public string? ChassisDescription { get; set; }
}

public sealed class PowerStatusInfo
{
    public bool HasBattery { get; set; }
    public bool OnAcPower { get; set; } = true;
    public int? BatteryPercent { get; set; }
    public string ActivePlanName { get; set; } = "Unknown";
    public Guid? ActivePlanGuid { get; set; }
}
