using System.Text.Json.Serialization;

namespace UltimatePCOptimizerPro.Core.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TweakState { Unknown, Applied, NotApplied, Partial, Unsupported, Error }

/// <summary>Live status of a tweak, computed by reading the real current configuration.</summary>
public sealed class TweakStatus
{
    public TweakState State { get; set; }
    public string Message { get; set; } = "";
    public List<string> CurrentValues { get; set; } = new();
    public List<string> TargetValues { get; set; } = new();
    public List<string> Locations { get; set; } = new();
    public bool HasAppBackup { get; set; }
}

/// <summary>Result of evaluating one change before execution.</summary>
public sealed class Preflight
{
    public bool Supported { get; set; } = true;
    public bool AlreadyApplied { get; set; }
    public bool RequiresAdmin { get; set; }
    public string Current { get; set; } = "";
    public string Message { get; set; } = "";

    public static Preflight Unsupported(string message) => new() { Supported = false, Message = message };
}

public enum VerifyOutcome { Verified, PendingRestart, Mismatch }

public sealed record VerifyResult(VerifyOutcome Outcome, string Actual)
{
    public bool Ok => Outcome != VerifyOutcome.Mismatch;
    public static VerifyResult Match(string actual) => new(VerifyOutcome.Verified, actual);
    public static VerifyResult Pending(string actual) => new(VerifyOutcome.PendingRestart, actual);
    public static VerifyResult Fail(string actual) => new(VerifyOutcome.Mismatch, actual);
}
