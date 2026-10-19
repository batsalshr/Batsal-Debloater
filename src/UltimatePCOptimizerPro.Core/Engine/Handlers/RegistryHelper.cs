using System.Globalization;
using System.Text.Json;
using Microsoft.Win32;
using UltimatePCOptimizerPro.Core.Models;

namespace UltimatePCOptimizerPro.Core.Engine.Handlers;

/// <summary>Registry primitives used by all registry-backed handlers. Uses the Win32 registry API (never .reg text editing).</summary>
internal static class RegistryHelper
{
    public static RegistryKey OpenBase(string hive, string view = "Default")
    {
        var h = hive.Equals("HKLM", StringComparison.OrdinalIgnoreCase) ? RegistryHive.LocalMachine
              : hive.Equals("HKCU", StringComparison.OrdinalIgnoreCase) ? RegistryHive.CurrentUser
              : throw new NotSupportedException($"Hive {hive} is not supported.");
        var v = view == "Registry32" ? RegistryView.Registry32 : Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Default;
        return RegistryKey.OpenBaseKey(h, v);
    }

    public sealed record ValueSnapshot(bool KeyExists, bool ValueExists, RegistryValueKind Kind, string? Serialized, string Display);

    public static ValueSnapshot Read(string hive, string view, string path, string name)
    {
        using var root = OpenBase(hive, view);
        using var key = root.OpenSubKey(path, writable: false);
        if (key is null) return new ValueSnapshot(false, false, RegistryValueKind.Unknown, null, "(key does not exist)");
        var raw = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (raw is null) return new ValueSnapshot(true, false, RegistryValueKind.Unknown, null, "(value not set)");
        var kind = key.GetValueKind(name);
        var serialized = Serialize(kind, raw);
        return new ValueSnapshot(true, true, kind, serialized, Display(kind, serialized));
    }

    public static string Serialize(RegistryValueKind kind, object raw) => kind switch
    {
        RegistryValueKind.DWord => unchecked((uint)Convert.ToInt32(raw)).ToString(CultureInfo.InvariantCulture),
        RegistryValueKind.QWord => unchecked((ulong)Convert.ToInt64(raw)).ToString(CultureInfo.InvariantCulture),
        RegistryValueKind.MultiString => JsonSerializer.Serialize((string[])raw),
        RegistryValueKind.Binary => Convert.ToHexString((byte[])raw),
        _ => raw.ToString() ?? ""
    };

    public static object Deserialize(RegistryValueKind kind, string serialized) => kind switch
    {
        RegistryValueKind.DWord => unchecked((int)ParseUInt32(serialized)),
        RegistryValueKind.QWord => unchecked((long)ParseUInt64(serialized)),
        RegistryValueKind.MultiString => JsonSerializer.Deserialize<string[]>(serialized) ?? Array.Empty<string>(),
        RegistryValueKind.Binary => Convert.FromHexString(serialized),
        _ => serialized
    };

    public static string Display(RegistryValueKind kind, string serialized) => kind switch
    {
        RegistryValueKind.DWord => $"{serialized} (0x{ParseUInt32(serialized):X}) REG_DWORD",
        RegistryValueKind.QWord => $"{serialized} REG_QWORD",
        RegistryValueKind.String => $"\"{serialized}\" REG_SZ",
        RegistryValueKind.ExpandString => $"\"{serialized}\" REG_EXPAND_SZ",
        RegistryValueKind.MultiString => $"{serialized} REG_MULTI_SZ",
        RegistryValueKind.Binary => $"{(serialized.Length > 48 ? serialized[..48] + "…" : serialized)} REG_BINARY",
        _ => serialized
    };

    public static RegistryValueKind ToKind(RegistryValueKindName k) => k switch
    {
        RegistryValueKindName.DWord => RegistryValueKind.DWord,
        RegistryValueKindName.QWord => RegistryValueKind.QWord,
        RegistryValueKindName.String => RegistryValueKind.String,
        RegistryValueKindName.ExpandString => RegistryValueKind.ExpandString,
        RegistryValueKindName.MultiString => RegistryValueKind.MultiString,
        RegistryValueKindName.Binary => RegistryValueKind.Binary,
        _ => RegistryValueKind.String
    };

    /// <summary>Normalises a spec value (decimal or 0x hex for numbers) to the same form produced by Serialize.</summary>
    public static string Normalize(RegistryValueKind kind, string value) => kind switch
    {
        RegistryValueKind.DWord => ParseUInt32(value).ToString(CultureInfo.InvariantCulture),
        RegistryValueKind.QWord => ParseUInt64(value).ToString(CultureInfo.InvariantCulture),
        RegistryValueKind.Binary => value.Replace(" ", "").Replace(",", "").ToUpperInvariant(),
        _ => value
    };

    public static uint ParseUInt32(string s)
    {
        s = s.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) return uint.Parse(s[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        if (s.StartsWith('-')) return unchecked((uint)int.Parse(s, CultureInfo.InvariantCulture));
        return uint.Parse(s, CultureInfo.InvariantCulture);
    }

    public static ulong ParseUInt64(string s)
    {
        s = s.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) return ulong.Parse(s[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        if (s.StartsWith('-')) return unchecked((ulong)long.Parse(s, CultureInfo.InvariantCulture));
        return ulong.Parse(s, CultureInfo.InvariantCulture);
    }

    /// <summary>Returns the shallowest key in the path that does not exist yet (it will be created by a write).</summary>
    public static string? FirstMissingKey(string hive, string view, string path)
    {
        using var root = OpenBase(hive, view);
        var parts = path.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 1; i <= parts.Length; i++)
        {
            var sub = string.Join('\\', parts.Take(i));
            using var k = root.OpenSubKey(sub);
            if (k is null) return sub;
        }
        return null;
    }

    public static void Write(string hive, string view, string path, string name, RegistryValueKind kind, object value)
    {
        using var root = OpenBase(hive, view);
        using var key = root.CreateSubKey(path, writable: true) ?? throw new IOException($@"Cannot open or create {hive}\{path}.");
        key.SetValue(name, value, kind);
    }

    public static void DeleteValue(string hive, string view, string path, string name)
    {
        using var root = OpenBase(hive, view);
        using var key = root.OpenSubKey(path, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }

    /// <summary>Deletes keys that this application created, deepest first, but only while they are completely empty.</summary>
    public static void DeleteCreatedKeysIfEmpty(string hive, string view, string path, string? createdFrom)
    {
        if (string.IsNullOrEmpty(createdFrom)) return;
        using var root = OpenBase(hive, view);
        var current = path.Trim('\\');
        while (current.Length >= createdFrom.Length && current.StartsWith(createdFrom, StringComparison.OrdinalIgnoreCase))
        {
            using (var k = root.OpenSubKey(current))
            {
                if (k is null) { }
                else if (k.ValueCount > 0 || k.SubKeyCount > 0) return;
            }
            root.DeleteSubKey(current, throwOnMissingSubKey: false);
            var idx = current.LastIndexOf('\\');
            if (idx < 0) return;
            current = current[..idx];
        }
    }
}
