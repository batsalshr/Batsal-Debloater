namespace UltimatePCOptimizerPro.Core.Services;

/// <summary>
/// Hard safety rules enforced by every change handler. These are compiled in (not data driven) so that an edited
/// JSON file cannot be used to disable security features through this application.
/// </summary>
public static class SafetyPolicy
{
    private static readonly string[] ForbiddenRegistryPrefixes =
    {
        @"SOFTWARE\Microsoft\Windows Defender",
        @"SOFTWARE\Policies\Microsoft\Windows Defender",
        @"SOFTWARE\Policies\Microsoft\Microsoft Antimalware",
        @"SOFTWARE\Microsoft\Windows Security Health",
        @"SOFTWARE\Policies\Microsoft\Windows Defender Security Center",
        @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate",
        @"SOFTWARE\Microsoft\WindowsUpdate",
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate",
        @"SOFTWARE\Policies\Microsoft\WindowsFirewall",
        @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy",
        @"SYSTEM\CurrentControlSet\Control\SecureBoot",
        @"SYSTEM\CurrentControlSet\Control\Lsa",
        @"SYSTEM\CurrentControlSet\Control\DeviceGuard",
        @"SYSTEM\CurrentControlSet\Control\CI",
        @"SYSTEM\CurrentControlSet\Control\SecurityProviders",
        @"SOFTWARE\Policies\Microsoft\Windows\System\Audit",
        @"SOFTWARE\Microsoft\Cryptography",
        @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management\FeatureSettings",
        @"SYSTEM\Setup",
        @"BCD00000000"
    };

    /// <summary>Prefixes that are only forbidden in HKEY_LOCAL_MACHINE (the same path in HKCU holds harmless per-user values).</summary>
    private static readonly string[] ForbiddenMachineOnlyPrefixes =
    {
        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon"
    };

    private static readonly string[] ForbiddenValueNames =
    {
        "FeatureSettingsOverride", "FeatureSettingsOverrideMask", "EnableLUA", "ConsentPromptBehaviorAdmin",
        "DisableAntiSpyware", "DisableRealtimeMonitoring", "Debugger", "AppInit_DLLs", "LoadAppInit_DLLs",
        "EnableVirtualizationBasedSecurity", "HypervisorEnforcedCodeIntegrity", "Userinit", "Shell"
    };

    /// <summary>Services that this application will never stop, disable or reconfigure.</summary>
    public static readonly HashSet<string> ProtectedServices = new(StringComparer.OrdinalIgnoreCase)
    {
        "WinDefend", "WdNisSvc", "WdNisDrv", "WdFilter", "WdBoot", "Sense", "SecurityHealthService", "wscsvc", "SgrmBroker",
        "mpssvc", "BFE", "MpsSvc", "IKEEXT", "PolicyAgent",
        "wuauserv", "UsoSvc", "WaaSMedicSvc", "TrustedInstaller", "BITS", "DoSvc", "InstallService", "msiserver",
        "CryptSvc", "KeyIso", "VaultSvc", "SamSs", "Netlogon", "LSM", "lsass", "NgcSvc", "NgcCtnrSvc", "TokenBroker", "wlidsvc", "WinHttpAutoProxySvc",
        "RpcSs", "RpcEptMapper", "DcomLaunch", "BrokerInfrastructure", "SystemEventsBroker", "TimeBrokerSvc", "CoreMessagingRegistrar",
        "StateRepository", "AppXSvc", "ClipSVC", "LicenseManager", "camsvc", "AppIDSvc", "Appinfo", "UserManager", "ProfSvc", "gpsvc",
        "Winmgmt", "EventLog", "EventSystem", "Schedule", "SENS", "Power", "PlugPlay", "DeviceInstall", "DsmSvc", "DeviceAssociationService",
        "Dhcp", "Dnscache", "NlaSvc", "nsi", "netprofm", "LanmanWorkstation", "Wcmsvc", "WlanSvc", "WwanSvc", "netman", "NcbService", "iphlpsvc",
        "Audiosrv", "AudioEndpointBuilder", "Themes", "ShellHWDetection", "FontCache", "UxSms",
        "VSS", "swprv", "SDRSVC", "vds", "StorSvc", "BDESVC", "EFS", "Winlogon", "seclogon", "W32Time", "TimeBroker",
        "CDPSvc", "WpnService", "hidserv", "TabletInputService", "TextInputManagementService",
        "MDCoreSvc", "webthreatdefsvc", "webthreatdefusersvc"
    };

    /// <summary>App packages that must never be removed (store infrastructure, security UI, frameworks, codecs, sign-in).</summary>
    private static readonly string[] ProtectedPackagePrefixes =
    {
        "Microsoft.WindowsStore", "Microsoft.StorePurchaseApp", "Microsoft.DesktopAppInstaller", "Microsoft.SecHealthUI",
        "Microsoft.Windows.", "MicrosoftWindows.", "Microsoft.UI.Xaml", "Microsoft.VCLibs", "Microsoft.NET.", "Microsoft.WindowsAppRuntime",
        "Microsoft.Services.Store", "Microsoft.XboxIdentityProvider", "Microsoft.Xbox.TCUI", "Microsoft.AAD.BrokerPlugin",
        "Microsoft.AccountsControl", "Microsoft.LockApp", "Microsoft.ECApp", "Microsoft.CredDialogHost", "Microsoft.AsyncTextService",
        "Microsoft.BioEnrollment", "Microsoft.Win32WebViewHost", "Microsoft.MicrosoftEdge", "Microsoft.MicrosoftEdgeDevToolsClient",
        "Microsoft.HEIFImageExtension", "Microsoft.HEVCVideoExtension", "Microsoft.VP9VideoExtensions", "Microsoft.WebMediaExtensions",
        "Microsoft.WebpImageExtension", "Microsoft.RawImageExtension", "Microsoft.AV1VideoExtension", "Microsoft.MPEG2VideoExtension",
        "Microsoft.AVCEncoderVideoExtension", "Microsoft.LanguageExperiencePack", "Microsoft.ApplicationCompatibilityEnhancements",
        "windows.", "Windows.", "Microsoft.WindowsTerminal", "Microsoft.WindowsCalculator", "Microsoft.WindowsNotepad", "Microsoft.Paint",
        "Microsoft.ScreenSketch", "Microsoft.Windows.Photos", "Microsoft.OneDriveSync", "MicrosoftCorporationII.WinAppRuntime",
        "NVIDIACorp.", "AdvancedMicroDevicesInc", "AppUp.Intel", "Intel.", "RealtekSemiconductorCorp.", "DolbyLaboratories.",
        "WavesAudio.", "SynapticsIncorporated.", "ELANMicroelectronicsCorpo.", "DTSInc.", "ThunderboltSoftware", "Microsoft.GamingServices"
    };

    public static bool IsRegistryWriteAllowed(string hive, string path, string valueName, out string reason)
    {
        var normalized = path.Trim('\\');
        foreach (var prefix in ForbiddenRegistryPrefixes)
        {
            if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                reason = $"Writing under '{prefix}' is blocked by the application safety policy (security/update/boot-critical area).";
                return false;
            }
        }
        if (hive.Equals("HKLM", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var prefix in ForbiddenMachineOnlyPrefixes)
            {
                if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    reason = $"Writing under HKLM\\{prefix} is blocked by the application safety policy (sign-in critical area).";
                    return false;
                }
            }
        }
        if (ForbiddenValueNames.Contains(valueName, StringComparer.OrdinalIgnoreCase))
        {
            reason = $"The value name '{valueName}' is blocked by the application safety policy.";
            return false;
        }
        if (normalized.Contains(@"Image File Execution Options", StringComparison.OrdinalIgnoreCase)
            && !normalized.EndsWith(@"\PerfOptions", StringComparison.OrdinalIgnoreCase))
        {
            reason = "Only the PerfOptions sub-key of Image File Execution Options may be modified.";
            return false;
        }
        if (normalized.StartsWith(@"SYSTEM\CurrentControlSet\Services\", StringComparison.OrdinalIgnoreCase))
        {
            var service = normalized.Split('\\')[3];
            if (ProtectedServices.Contains(service) && !normalized.Contains(@"\Tcpip\Parameters\Interfaces", StringComparison.OrdinalIgnoreCase))
            {
                reason = $"Service '{service}' is protected.";
                return false;
            }
        }
        if (!hive.Equals("HKCU", StringComparison.OrdinalIgnoreCase) && !hive.Equals("HKLM", StringComparison.OrdinalIgnoreCase))
        {
            reason = "Only HKCU and HKLM are supported.";
            return false;
        }
        reason = "";
        return true;
    }

    public static bool IsServiceProtected(string serviceName) =>
        ProtectedServices.Contains(serviceName) || serviceName.Contains('_') && ProtectedServices.Contains(serviceName.Split('_')[0]);

    /// <summary>Optional apps whose names happen to start with a protected prefix.</summary>
    private static readonly string[] RemovableDespitePrefix = { "Microsoft.Windows.DevHome" };

    public static bool IsPackageProtected(string packageName, out string reason)
    {
        if (RemovableDespitePrefix.Any(p => packageName.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            reason = "";
            return false;
        }
        foreach (var prefix in ProtectedPackagePrefixes)
        {
            if (packageName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                reason = prefix.EndsWith('.') && prefix.Length < 12 ? "Windows system component" : "Required component, framework, codec or hardware control panel";
                return true;
            }
        }
        reason = "";
        return false;
    }

    /// <summary>Optional features the application will never disable.</summary>
    public static readonly HashSet<string> ProtectedFeatures = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft-Hyper-V-All", "Microsoft-Hyper-V", "VirtualMachinePlatform", "HypervisorPlatform", "Containers",
        "Windows-Defender-Default-Definitions", "Windows-Defender-ApplicationGuard", "NetFx3", "NetFx4-AdvSrvs",
        "Microsoft-Windows-Subsystem-Linux", "Windows-Identity-Foundation", "SearchEngine-Client-Package", "Printing-PrintToPDFServices-Features"
    };
}
