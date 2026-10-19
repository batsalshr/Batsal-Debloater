using UltimatePCOptimizerPro.Core.Models;

namespace UltimatePCOptimizerPro.Core.Engine;

public sealed record ReviewItem(
    string Title,
    string Category,
    RiskLevel Risk,
    string Target,
    string Current,
    string Desired,
    RestartRequirement Restart,
    bool HasVerifiedRollback,
    bool RequiresAdmin,
    string? Notes,
    string? SkipReason);

/// <summary>Everything the user is shown before any change is made.</summary>
public sealed class PlanReview
{
    public string Label { get; init; } = "";
    public bool IsRollback { get; init; }
    public List<ReviewItem> Items { get; init; } = new();
    public bool RestorePointRequired { get; init; }
    public string ProposedRestorePointName { get; init; } = "";
    public SystemProtectionStatus? Protection { get; init; }
    public RestartRequirement MaxRestart => Items.Where(i => i.SkipReason is null).Select(i => i.Restart).DefaultIfEmpty(RestartRequirement.None).Max();
    public int ActionableCount => Items.Count(i => i.SkipReason is null);
}

public sealed class RestorePointFailureReview
{
    public string Label { get; init; } = "";
    public RestorePointAttempt Attempt { get; init; } = new();
    public bool OverrideAllowedBySettings { get; init; }
    public bool AllChangesHaveVerifiedRollback { get; init; }
    public List<string> ChangesWithoutVerifiedRollback { get; init; } = new();
    public bool OverridePossible => OverrideAllowedBySettings && AllChangesHaveVerifiedRollback;
    public string Instructions { get; init; } = Services.RestorePointService.EnableInstructions;
}

/// <summary>Implemented by the UI. The engine never proceeds without an explicit positive answer.</summary>
public interface IApprovalProvider
{
    Task<bool> ConfirmPlanAsync(PlanReview review, CancellationToken ct);
    /// <summary>Called when no restore point could be verified. Must return false unless the user explicitly chose the backup-only override.</summary>
    Task<bool> ConfirmBackupOnlyOverrideAsync(RestorePointFailureReview review, CancellationToken ct);
}

/// <summary>Non-interactive provider for tests/automation: approves plans, never overrides restore point failures.</summary>
public sealed class AutoApproveProvider : IApprovalProvider
{
    public bool AllowOverride { get; init; }
    public Task<bool> ConfirmPlanAsync(PlanReview review, CancellationToken ct) => Task.FromResult(true);
    public Task<bool> ConfirmBackupOnlyOverrideAsync(RestorePointFailureReview review, CancellationToken ct) => Task.FromResult(AllowOverride && review.OverridePossible);
}
