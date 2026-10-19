using UltimatePCOptimizerPro.Core.Interop;
using UltimatePCOptimizerPro.Core.Models;

namespace UltimatePCOptimizerPro.Core.Engine.Handlers;

/// <summary>Changes a display's resolution/refresh rate with ChangeDisplaySettingsEx (tested with CDS_TEST first).</summary>
public sealed class DisplayModeHandler : ChangeHandler<DisplayModeSpec>
{
    protected override bool RequiresAdmin(DisplayModeSpec s) => false;

    private static NativeMethods.DEVMODE? Current(string device)
    {
        var dm = NativeMethods.DEVMODE.Create();
        return NativeMethods.EnumDisplaySettings(device, NativeMethods.ENUM_CURRENT_SETTINGS, ref dm) ? dm : null;
    }

    private static bool ModeExists(DisplayModeSpec s, int bpp)
    {
        for (int i = 0; i < 4096; i++)
        {
            var dm = NativeMethods.DEVMODE.Create();
            if (!NativeMethods.EnumDisplaySettings(s.DeviceName, i, ref dm)) return false;
            if (dm.dmPelsWidth == s.Width && dm.dmPelsHeight == s.Height && dm.dmDisplayFrequency == s.RefreshHz && dm.dmBitsPerPel == bpp) return true;
        }
        return false;
    }

    private static string Describe(NativeMethods.DEVMODE dm) => $"{dm.dmPelsWidth}x{dm.dmPelsHeight} @ {dm.dmDisplayFrequency} Hz";

    protected override Preflight Preflight(DisplayModeSpec s)
    {
        var cur = Current(s.DeviceName);
        if (cur is null) return Models.Preflight.Unsupported($"Display {s.DeviceName} is not active.");
        if (!ModeExists(s, cur.Value.dmBitsPerPel)) return Models.Preflight.Unsupported($"The driver does not offer {s.Desired} on {s.DeviceName}.");
        return new Preflight
        {
            Current = Describe(cur.Value),
            AlreadyApplied = cur.Value.dmPelsWidth == s.Width && cur.Value.dmPelsHeight == s.Height && cur.Value.dmDisplayFrequency == s.RefreshHz
        };
    }

    protected override BackupState Capture(DisplayModeSpec s)
    {
        var cur = Current(s.DeviceName) ?? throw new InvalidOperationException("Display not active.");
        var b = new BackupState { Display = Describe(cur) };
        b.Set("width", cur.dmPelsWidth.ToString());
        b.Set("height", cur.dmPelsHeight.ToString());
        b.Set("hz", cur.dmDisplayFrequency.ToString());
        return b;
    }

    private static void SetMode(string device, int w, int h, int hz)
    {
        var dm = Current(device) ?? throw new InvalidOperationException("Display not active.");
        dm.dmPelsWidth = w;
        dm.dmPelsHeight = h;
        dm.dmDisplayFrequency = hz;
        dm.dmFields = NativeMethods.DM_PELSWIDTH | NativeMethods.DM_PELSHEIGHT | NativeMethods.DM_DISPLAYFREQUENCY;
        var test = NativeMethods.ChangeDisplaySettingsEx(device, ref dm, IntPtr.Zero, NativeMethods.CDS_TEST, IntPtr.Zero);
        if (test != NativeMethods.DISP_CHANGE_SUCCESSFUL) throw new InvalidOperationException($"The driver rejected the mode in test mode (code {test}).");
        var rc = NativeMethods.ChangeDisplaySettingsEx(device, ref dm, IntPtr.Zero, NativeMethods.CDS_UPDATEREGISTRY, IntPtr.Zero);
        if (rc is not (NativeMethods.DISP_CHANGE_SUCCESSFUL or NativeMethods.DISP_CHANGE_RESTART))
            throw new InvalidOperationException($"ChangeDisplaySettingsEx failed with code {rc}.");
    }

    protected override void Apply(DisplayModeSpec s, BackupState backup) => SetMode(s.DeviceName, s.Width, s.Height, s.RefreshHz);

    protected override VerifyResult Verify(DisplayModeSpec s, BackupState backup)
    {
        if (Current(s.DeviceName) is not { } cur) return VerifyResult.Fail("inactive");
        return cur.dmDisplayFrequency == s.RefreshHz ? VerifyResult.Match(Describe(cur)) : VerifyResult.Fail(Describe(cur));
    }

    protected override void Restore(DisplayModeSpec s, BackupState b)
        => SetMode(s.DeviceName, int.Parse(b.Get("width")!), int.Parse(b.Get("height")!), int.Parse(b.Get("hz")!));

    protected override VerifyResult VerifyRestored(DisplayModeSpec s, BackupState b)
    {
        if (Current(s.DeviceName) is not { } cur) return VerifyResult.Fail("inactive");
        return cur.dmDisplayFrequency.ToString() == b.Get("hz") ? VerifyResult.Match(Describe(cur)) : VerifyResult.Fail(Describe(cur));
    }
}
