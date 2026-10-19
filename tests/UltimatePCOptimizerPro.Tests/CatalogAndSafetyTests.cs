using UltimatePCOptimizerPro.Core.Models;
using UltimatePCOptimizerPro.Core.Services;

namespace UltimatePCOptimizerPro.Tests;

public class CatalogAndSafetyTests
{
    private static readonly TweakCatalogService Catalog = new(new LoggingService());

    [Fact]
    public void Catalog_loads_without_errors()
    {
        Assert.Empty(Catalog.LoadErrors);
        Assert.True(Catalog.Tweaks.Count >= 80, $"Only {Catalog.Tweaks.Count} tweaks loaded");
    }

    [Fact]
    public void Every_tweak_is_documented_and_has_actions()
    {
        foreach (var t in Catalog.Tweaks)
        {
            Assert.False(string.IsNullOrWhiteSpace(t.Mechanism), t.Id);
            Assert.False(string.IsNullOrWhiteSpace(t.Benefit), t.Id);
            Assert.False(string.IsNullOrWhiteSpace(t.Drawback), t.Id);
            Assert.False(string.IsNullOrWhiteSpace(t.Evidence), t.Id);
            Assert.NotEmpty(t.Actions);
        }
    }

    [Fact]
    public void Five_profiles_exist_and_extreme_requires_opt_in()
    {
        Assert.Equal(new[] { "safe", "lowend", "balanced", "highend", "extreme" }, Catalog.Profiles.Select(p => p.Id));
        Assert.True(Catalog.GetProfile("extreme")!.RequiresExplicitOptIn);
        Assert.All(Catalog.Profiles.Where(p => p.Id != "extreme"), p => Assert.False(p.RequiresExplicitOptIn));
    }

    [Fact]
    public void Profiles_respect_their_risk_ceiling()
    {
        foreach (var p in Catalog.Profiles)
            foreach (var id in p.Tweaks)
                Assert.True(Catalog.Get(id)!.Risk <= p.MaxRisk, $"{id} in {p.Id}");
    }

    [Fact]
    public void Expert_network_folklore_is_not_in_any_profile()
    {
        foreach (var p in Catalog.Profiles)
        {
            Assert.DoesNotContain("net.throttlingindex.disable", p.Tweaks);
            Assert.DoesNotContain("net.tcpackfrequency", p.Tweaks);
        }
    }

    [Theory]
    [InlineData("HKLM", @"SOFTWARE\Policies\Microsoft\Windows Defender", "DisableAntiSpyware")]
    [InlineData("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "NoAutoUpdate")]
    [InlineData("HKLM", @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\StandardProfile", "EnableFirewall")]
    [InlineData("HKLM", @"SYSTEM\CurrentControlSet\Control\SecureBoot\State", "UEFISecureBootEnabled")]
    [InlineData("HKLM", @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity", "Enabled")]
    [InlineData("HKLM", @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon", "Shell")]
    [InlineData("HKLM", @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\game.exe", "Debugger")]
    [InlineData("HKLM", @"SYSTEM\CurrentControlSet\Services\WinDefend", "Start")]
    [InlineData("HKLM", @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", "FeatureSettingsOverride")]
    public void Safety_policy_blocks_security_critical_writes(string hive, string path, string name)
        => Assert.False(SafetyPolicy.IsRegistryWriteAllowed(hive, path, name, out _));

    [Theory]
    [InlineData("HKCU", @"Software\Microsoft\Windows NT\CurrentVersion\Winlogon", "RestartApps")]
    [InlineData("HKLM", @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\game.exe\PerfOptions", "CpuPriorityClass")]
    [InlineData("HKLM", @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{0000}", "TcpAckFrequency")]
    [InlineData("HKCU", @"System\GameConfigStore", "GameDVR_Enabled")]
    public void Safety_policy_allows_documented_tweaks(string hive, string path, string name)
        => Assert.True(SafetyPolicy.IsRegistryWriteAllowed(hive, path, name, out var reason), reason);

    [Theory]
    [InlineData("WinDefend")] [InlineData("mpssvc")] [InlineData("wuauserv")] [InlineData("BFE")] [InlineData("Dnscache")] [InlineData("VSS")]
    public void Critical_services_are_protected(string name) => Assert.True(SafetyPolicy.IsServiceProtected(name));

    [Theory]
    [InlineData("Microsoft.WindowsStore", true)]
    [InlineData("Microsoft.SecHealthUI", true)]
    [InlineData("Microsoft.Xbox.TCUI", true)]
    [InlineData("Microsoft.VCLibs.140.00", true)]
    [InlineData("Microsoft.Windows.DevHome", false)]
    [InlineData("Microsoft.BingNews", false)]
    [InlineData("king.com.CandyCrushSaga", false)]
    public void Package_protection(string name, bool expected) => Assert.Equal(expected, SafetyPolicy.IsPackageProtected(name, out _));

    [Fact]
    public void No_tweak_targets_protected_services_or_update_security_paths()
    {
        foreach (var t in Catalog.Tweaks)
        foreach (var a in t.Actions)
        {
            if (a is ServiceStartSpec s) Assert.False(SafetyPolicy.IsServiceProtected(s.ServiceName), t.Id);
            if (a is RegistryValueSpec r)
            {
                Assert.DoesNotContain("Defender", r.Path, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("WindowsUpdate", r.Path, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Firewall", r.Path, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void Restore_point_name_uses_required_format_and_local_time()
    {
        var t = new DateTimeOffset(2026, 10, 19, 18, 45, 32, TimeSpan.FromHours(5.75));
        Assert.Equal("ULTIMATE-PC-OPTIMIZER | Registry Tweaks | 2026-10-19 | 18-45-32", RestorePointService.BuildName("Registry Tweaks", t));
        Assert.Equal("ULTIMATE-PC-OPTIMIZER | A/B | 2026-10-19 | 18-45-32", RestorePointService.BuildName("A|B", t));
        Assert.True(RestorePointService.BuildName(new string('x', 400), t).Length <= 255);
    }

    [Fact]
    public void Applicability_respects_build_and_form_factor()
    {
        var hags = Catalog.Get("gpu.hags.enable")!;
        var snap = new SystemSnapshot { Os = new OsInfo { Build = 19045 } };
        Assert.False(TweakResolver.CheckApplicability(hags, snap).Applicable); // no dedicated GPU
        snap.Gpus.Add(new GpuInfo { Name = "RTX", Vendor = GpuVendor.Nvidia, DedicatedVideoMemoryBytes = 8UL << 30 });
        Assert.True(TweakResolver.CheckApplicability(hags, snap).Applicable);

        var ultimate = Catalog.Get("cpu.powerplan.ultimate")!;
        snap.Power.HasBattery = true;
        Assert.False(TweakResolver.CheckApplicability(ultimate, snap).Applicable);

        var windowed = Catalog.Get("gaming.windowed.optimizations")!;
        Assert.False(TweakResolver.CheckApplicability(windowed, snap).Applicable); // needs 22621
    }
}
