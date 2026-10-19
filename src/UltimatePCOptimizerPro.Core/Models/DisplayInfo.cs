namespace UltimatePCOptimizerPro.Core.Models;

public sealed class DisplayInfo
{
    public string DeviceName { get; set; } = "";
    public string FriendlyName { get; set; } = "Display";
    public string AdapterName { get; set; } = "";
    public bool IsPrimary { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int BitsPerPixel { get; set; }
    public int RefreshHz { get; set; }
    public int PositionX { get; set; }
    public int PositionY { get; set; }
    /// <summary>All refresh rates the driver reports for the current resolution and colour depth.</summary>
    public List<int> SupportedRefreshRates { get; set; } = new();
    public int MaxRefreshAtCurrentResolution => SupportedRefreshRates.Count == 0 ? RefreshHz : SupportedRefreshRates.Max();
    public bool RunningBelowMaxRefresh => MaxRefreshAtCurrentResolution > RefreshHz + 1;
    /// <summary>Windows exposes no public API that reliably reports VRR (G-SYNC/FreeSync) capability.</summary>
    public string VariableRefreshStatus { get; set; } = "Not detectable via public Windows APIs — check the GPU control panel";
}
