using System.Text.Json.Serialization;

namespace UltimatePCOptimizerPro.Core.Models;

/// <summary>
/// A serialisable description of one atomic system change. Specs are stored inside operation
/// records so that a rollback can be performed after the application restarts.
/// The "$type" discriminator must be the first property in JSON.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(RegistryValueSpec), "registry")]
[JsonDerivedType(typeof(RegistryTokenSpec), "registryToken")]
[JsonDerivedType(typeof(RegistryFlagsSpec), "registryFlags")]
[JsonDerivedType(typeof(AppCompatLayerSpec), "appCompatLayer")]
[JsonDerivedType(typeof(PowerSchemeSpec), "powerScheme")]
[JsonDerivedType(typeof(PowerSettingSpec), "powerSetting")]
[JsonDerivedType(typeof(ServiceStartSpec), "service")]
[JsonDerivedType(typeof(ScheduledTaskSpec), "scheduledTask")]
[JsonDerivedType(typeof(StartupEntrySpec), "startupEntry")]
[JsonDerivedType(typeof(AppxRemoveSpec), "appxRemove")]
[JsonDerivedType(typeof(DisplayModeSpec), "displayMode")]
[JsonDerivedType(typeof(DevicePowerSpec), "devicePower")]
[JsonDerivedType(typeof(OptionalFeatureSpec), "optionalFeature")]
public abstract class ChangeSpec
{
    /// <summary>Human readable description of the target, e.g. a registry path.</summary>
    public abstract string Target { get; }
    /// <summary>Human readable description of the desired state.</summary>
    public abstract string Desired { get; }
    public virtual ChangeSpec Clone() => (ChangeSpec)MemberwiseClone();
}

public enum RegistryValueKindName { DWord, QWord, String, ExpandString, MultiString, Binary }

public sealed class RegistryValueSpec : ChangeSpec
{
    /// <summary>HKCU or HKLM.</summary>
    public string Hive { get; set; } = "HKCU";
    /// <summary>"Default" (native view) or "Registry32" (WOW6432Node view).</summary>
    public string View { get; set; } = "Default";
    public string Path { get; set; } = "";
    /// <summary>Value name. Empty string targets the key's (Default) value.</summary>
    public string Name { get; set; } = "";
    public RegistryValueKindName Kind { get; set; } = RegistryValueKindName.DWord;
    /// <summary>Invariant string form of the value. DWORD/QWORD accept decimal or 0x-prefixed hex.</summary>
    public string? Value { get; set; }
    /// <summary>When true the change deletes the value instead of writing it.</summary>
    public bool DeleteValue { get; set; }
    /// <summary>Optional live-refresh hook after writing, e.g. "Mouse".</summary>
    public string? Refresh { get; set; }
    /// <summary>
    /// The value Windows behaves as if set when the value is absent (documented default). When the value is absent and this
    /// equals the target, the setting is already in effect and is not rewritten.
    /// </summary>
    public string? WindowsDefault { get; set; }

    public override string Target => $@"{Hive}\{Path}\{(Name.Length == 0 ? "(Default)" : Name)}" + (View == "Registry32" ? " [32-bit view]" : "");
    public override string Desired => DeleteValue ? "(value removed)" : $"{Value} ({Kind})";
}

/// <summary>Sets one "key=value;" token inside a semicolon separated REG_SZ (e.g. DirectXUserGlobalSettings).</summary>
public sealed class RegistryTokenSpec : ChangeSpec
{
    public string Hive { get; set; } = "HKCU";
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public string Token { get; set; } = "";
    public string TokenValue { get; set; } = "";

    public override string Target => $@"{Hive}\{Path}\{Name} → {Token}";
    public override string Desired => $"{Token}={TokenValue};";
}

/// <summary>Sets or clears bits in a numeric value stored as REG_SZ or REG_DWORD (e.g. accessibility Flags).</summary>
public sealed class RegistryFlagsSpec : ChangeSpec
{
    public string Hive { get; set; } = "HKCU";
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public long SetBits { get; set; }
    public long ClearBits { get; set; }
    public long DefaultValue { get; set; }

    public override string Target => $@"{Hive}\{Path}\{Name}";
    public override string Desired => $"set 0x{SetBits:X} / clear 0x{ClearBits:X}";
}

/// <summary>Adds a compatibility layer flag for an executable (AppCompatFlags\Layers).</summary>
public sealed class AppCompatLayerSpec : ChangeSpec
{
    public string ExePath { get; set; } = "";
    public string Flag { get; set; } = "";

    public override string Target => $@"HKCU\Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers\{ExePath}";
    public override string Desired => $"contains {Flag}";
}

public enum PowerSchemeKind { Balanced, HighPerformance, UltimatePerformance, Custom }

public sealed class PowerSchemeSpec : ChangeSpec
{
    public PowerSchemeKind Scheme { get; set; }
    public Guid? CustomGuid { get; set; }

    public override string Target => "Active power plan";
    public override string Desired => Scheme == PowerSchemeKind.Custom ? CustomGuid?.ToString() ?? "" : Scheme.ToString();
}

public sealed class PowerSettingSpec : ChangeSpec
{
    public string DisplayName { get; set; } = "";
    public Guid Subgroup { get; set; }
    public Guid Setting { get; set; }
    public uint? AcValue { get; set; }
    public uint? DcValue { get; set; }

    public override string Target => $"Active power plan → {DisplayName}";
    public override string Desired => $"AC={AcValue?.ToString() ?? "unchanged"}, DC={DcValue?.ToString() ?? "unchanged"}";
}

public enum ServiceStartMode { Automatic, AutomaticDelayed, Manual, Disabled }

public sealed class ServiceStartSpec : ChangeSpec
{
    public string ServiceName { get; set; } = "";
    public ServiceStartMode StartMode { get; set; }

    public override string Target => $"Service '{ServiceName}' startup type";
    public override string Desired => StartMode.ToString();
}

public sealed class ScheduledTaskSpec : ChangeSpec
{
    public string TaskPath { get; set; } = "";
    public bool Enabled { get; set; }

    public override string Target => $"Scheduled task {TaskPath}";
    public override string Desired => Enabled ? "Enabled" : "Disabled";
}

public enum StartupLocation { CurrentUserRun, LocalMachineRun, LocalMachineRun32, UserStartupFolder, CommonStartupFolder }

public sealed class StartupEntrySpec : ChangeSpec
{
    public StartupLocation Location { get; set; }
    public string EntryName { get; set; } = "";
    public bool Enabled { get; set; }

    public override string Target => $"Startup entry '{EntryName}' ({Location})";
    public override string Desired => Enabled ? "Enabled" : "Disabled";
}

public sealed class AppxRemoveSpec : ChangeSpec
{
    public string PackageFullName { get; set; } = "";
    public string PackageFamilyName { get; set; } = "";
    public string DisplayName { get; set; } = "";

    public override string Target => $"App package {DisplayName} ({PackageFamilyName})";
    public override string Desired => "Removed for current user";
}

public sealed class DisplayModeSpec : ChangeSpec
{
    public string DeviceName { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
    public int RefreshHz { get; set; }

    public override string Target => $"Display {DeviceName}";
    public override string Desired => $"{Width}x{Height} @ {RefreshHz} Hz";
}

/// <summary>"Allow the computer to turn off this device to save power" (MSPower_DeviceEnable).</summary>
public sealed class DevicePowerSpec : ChangeSpec
{
    /// <summary>PnP device instance id, or the token {ActiveNetworkAdapter} resolved at plan time.</summary>
    public string DeviceInstanceId { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public bool AllowPowerOff { get; set; }

    public override string Target => $"Device power management: {DeviceName}";
    public override string Desired => AllowPowerOff ? "Windows may turn device off" : "Windows may not turn device off";
}

public sealed class OptionalFeatureSpec : ChangeSpec
{
    public string FeatureName { get; set; } = "";
    public bool Enabled { get; set; }

    public override string Target => $"Optional feature {FeatureName}";
    public override string Desired => Enabled ? "Enabled" : "Disabled";
}
