using Microsoft.Win32;
using UltimatePCOptimizerPro.Core.Models;
using UltimatePCOptimizerPro.Core.Services;

namespace UltimatePCOptimizerPro.Core.Engine.Handlers;

/// <summary>
/// Edits a single token of a "Key=Value;Key2=Value2;" string such as DirectXUserGlobalSettings, leaving the other
/// tokens (which the Windows Settings app also writes) untouched. Restore puts back only this token's previous value.
/// </summary>
public sealed class RegistryTokenHandler : ChangeHandler<RegistryTokenSpec>
{
    public static List<KeyValuePair<string, string>> Parse(string? s)
    {
        var list = new List<KeyValuePair<string, string>>();
        if (string.IsNullOrEmpty(s)) return list;
        foreach (var part in s.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var i = part.IndexOf('=');
            list.Add(i < 0 ? new(part, "") : new(part[..i], part[(i + 1)..]));
        }
        return list;
    }

    public static string Build(IEnumerable<KeyValuePair<string, string>> tokens) => string.Concat(tokens.Select(t => $"{t.Key}={t.Value};"));

    public static string SetToken(string? s, string token, string? value)
    {
        var list = Parse(s);
        var idx = list.FindIndex(t => t.Key.Equals(token, StringComparison.OrdinalIgnoreCase));
        if (value is null)
        {
            if (idx >= 0) list.RemoveAt(idx);
        }
        else if (idx >= 0) list[idx] = new(list[idx].Key, value);
        else list.Add(new(token, value));
        return Build(list);
    }

    public static string? GetToken(string? s, string token)
        => Parse(s).Where(t => t.Key.Equals(token, StringComparison.OrdinalIgnoreCase)).Select(t => t.Value).Cast<string?>().FirstOrDefault();

    protected override bool RequiresAdmin(RegistryTokenSpec s) => s.Hive == "HKLM";

    private static string? ReadString(RegistryTokenSpec s, out bool exists)
    {
        var snap = RegistryHelper.Read(s.Hive, "Default", s.Path, s.Name);
        exists = snap.ValueExists;
        return snap.Serialized;
    }

    protected override Preflight Preflight(RegistryTokenSpec s)
    {
        if (!SafetyPolicy.IsRegistryWriteAllowed(s.Hive, s.Path, s.Name, out var reason)) return Models.Preflight.Unsupported(reason);
        var str = ReadString(s, out var exists);
        var tok = GetToken(str, s.Token);
        return new Preflight
        {
            Current = exists ? $"\"{str}\"" + (tok is null ? $" ({s.Token} not set — Windows default)" : "") : "(value not set — Windows default)",
            AlreadyApplied = tok == s.TokenValue,
            RequiresAdmin = RequiresAdmin(s)
        };
    }

    protected override BackupState Capture(RegistryTokenSpec s)
    {
        var str = ReadString(s, out var exists);
        var b = new BackupState { Display = exists ? $"\"{str}\"" : "(value not set)" };
        b.Set("valueExisted", exists.ToString());
        b.Set("original", str);
        b.Set("tokenValue", GetToken(str, s.Token));
        b.Set("createdFrom", RegistryHelper.FirstMissingKey(s.Hive, "Default", s.Path));
        return b;
    }

    protected override void Apply(RegistryTokenSpec s, BackupState backup)
    {
        var str = ReadString(s, out _);
        RegistryHelper.Write(s.Hive, "Default", s.Path, s.Name, RegistryValueKind.String, SetToken(str, s.Token, s.TokenValue));
    }

    protected override VerifyResult Verify(RegistryTokenSpec s, BackupState backup)
    {
        var str = ReadString(s, out _);
        return GetToken(str, s.Token) == s.TokenValue ? VerifyResult.Match($"\"{str}\"") : VerifyResult.Fail($"\"{str}\"");
    }

    protected override void Restore(RegistryTokenSpec s, BackupState b)
    {
        var str = ReadString(s, out _);
        var restored = SetToken(str, s.Token, b.Get("tokenValue"));
        if (restored.Length == 0 && !b.GetBool("valueExisted"))
        {
            RegistryHelper.DeleteValue(s.Hive, "Default", s.Path, s.Name);
            RegistryHelper.DeleteCreatedKeysIfEmpty(s.Hive, "Default", s.Path, b.Get("createdFrom"));
        }
        else
        {
            RegistryHelper.Write(s.Hive, "Default", s.Path, s.Name, RegistryValueKind.String, restored);
        }
    }

    protected override VerifyResult VerifyRestored(RegistryTokenSpec s, BackupState b)
    {
        var str = ReadString(s, out _);
        return GetToken(str, s.Token) == b.Get("tokenValue") ? VerifyResult.Match($"\"{str}\"") : VerifyResult.Fail($"\"{str}\"");
    }
}
