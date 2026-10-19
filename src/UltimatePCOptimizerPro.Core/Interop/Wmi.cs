using System.Globalization;
using System.Management;

namespace UltimatePCOptimizerPro.Core.Interop;

/// <summary>Small, exception-safe helper around System.Management (WMI/CIM).</summary>
public static class Wmi
{
    public const string Cimv2 = @"root\cimv2";
    public const string Storage = @"root\Microsoft\Windows\Storage";
    public const string RootWmi = @"root\wmi";
    public const string Default = @"root\default";
    public const string SecurityCenter = @"root\SecurityCenter2";
    public const string DeviceGuard = @"root\Microsoft\Windows\DeviceGuard";

    public static List<ManagementBaseObject> Query(string scope, string wql, int timeoutSeconds = 20)
    {
        var list = new List<ManagementBaseObject>();
        var options = new System.Management.EnumerationOptions
        {
            Timeout = TimeSpan.FromSeconds(timeoutSeconds),
            ReturnImmediately = true,
            Rewindable = false
        };
        using var searcher = new ManagementObjectSearcher(new ManagementScope(scope), new ObjectQuery(wql), options);
        using var results = searcher.Get();
        foreach (var o in results) list.Add(o);
        return list;
    }

    /// <summary>Query that returns an empty list instead of throwing (missing namespace, access denied, timeout).</summary>
    public static List<ManagementBaseObject> TryQuery(string scope, string wql, out string? error, int timeoutSeconds = 20)
    {
        try
        {
            error = null;
            return Query(scope, wql, timeoutSeconds);
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or TimeoutException)
        {
            error = ex.Message.Trim();
            return new List<ManagementBaseObject>();
        }
    }

    public static object? Raw(this ManagementBaseObject o, string property)
    {
        try { return o[property]; }
        catch (ManagementException) { return null; }
    }

    public static string? Str(this ManagementBaseObject o, string property)
    {
        var v = o.Raw(property)?.ToString()?.Trim();
        return string.IsNullOrWhiteSpace(v) ? null : v;
    }

    public static long? Long(this ManagementBaseObject o, string property)
    {
        var v = o.Raw(property);
        if (v is null) return null;
        try { return Convert.ToInt64(v, CultureInfo.InvariantCulture); }
        catch { return null; }
    }

    public static ulong? ULong(this ManagementBaseObject o, string property)
    {
        var v = o.Raw(property);
        if (v is null) return null;
        try { return Convert.ToUInt64(v, CultureInfo.InvariantCulture); }
        catch { return null; }
    }

    public static int? Int(this ManagementBaseObject o, string property)
    {
        var l = o.Long(property);
        return l is null ? null : (int)Math.Clamp(l.Value, int.MinValue, int.MaxValue);
    }

    public static bool? Bool(this ManagementBaseObject o, string property)
    {
        var v = o.Raw(property);
        return v is bool b ? b : null;
    }

    public static DateTime? Date(this ManagementBaseObject o, string property)
    {
        var s = o.Str(property);
        if (s is null) return null;
        try { return ManagementDateTimeConverter.ToDateTime(s); }
        catch { return null; }
    }

    public static string WqlEscape(string value) => value.Replace("\\", "\\\\").Replace("'", "\\'");
}
