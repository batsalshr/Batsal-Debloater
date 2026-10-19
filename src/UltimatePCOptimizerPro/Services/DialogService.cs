using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using UltimatePCOptimizerPro.Core.Engine;
using UltimatePCOptimizerPro.Core.Models;
using UltimatePCOptimizerPro.Dialogs;

namespace UltimatePCOptimizerPro.Services;

/// <summary>All user-facing dialogs, including the engine's approval prompts.</summary>
public sealed class DialogService : IApprovalProvider
{
    private static Brush Res(string key) => (Brush)Application.Current.FindResource(key);

    private static (string Icon, Brush Bg, Brush Fg) ToneVisual(DialogTone tone) => tone switch
    {
        DialogTone.Success => ("", Res("SuccessSoftBrush"), Res("SuccessBrush")),
        DialogTone.Warning => ("", Res("WarningSoftBrush"), Res("WarningBrush")),
        DialogTone.Danger => ("", Res("DangerSoftBrush"), Res("DangerBrush")),
        DialogTone.Question => ("", Res("AccentSoftBrush"), Res("AccentBrush")),
        _ => ("", Res("AccentSoftBrush"), Res("AccentBrush"))
    };

    public DialogResultKind Show(string title, string message, DialogTone tone = DialogTone.Info, string primary = "OK", string? secondary = null,
        string? tertiary = null, IEnumerable<DialogRow>? rows = null, string? acknowledgement = null, bool dangerPrimary = false, Action<AppDialog>? configure = null)
    {
        return Application.Current.Dispatcher.Invoke(() =>
        {
            var (icon, bg, fg) = ToneVisual(tone);
            var model = new AppDialogModel
            {
                Title = title, Message = message, Icon = icon, IconBackground = bg, IconForeground = fg,
                PrimaryText = primary, SecondaryText = secondary, TertiaryText = tertiary, Acknowledgement = acknowledgement,
                PrimaryStyle = (Style)Application.Current.FindResource(dangerPrimary ? "DangerButton" : "PrimaryButton")
            };
            if (rows is not null) foreach (var r in rows) model.Items.Add(r);
            var dlg = new AppDialog(model);
            var owner = Application.Current.MainWindow;
            if (owner is not null && owner.IsVisible) dlg.Owner = owner;
            else dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            configure?.Invoke(dlg);
            dlg.ShowDialog();
            return dlg.Result;
        });
    }

    public void Info(string title, string message) => Show(title, message, DialogTone.Info);
    public void Success(string title, string message) => Show(title, message, DialogTone.Success);
    public void Error(string title, string message) => Show(title, message, DialogTone.Danger);
    public bool Confirm(string title, string message, string yes = "Continue", string no = "Cancel", bool danger = false, IEnumerable<DialogRow>? rows = null, string? acknowledgement = null)
        => Show(title, message, danger ? DialogTone.Warning : DialogTone.Question, yes, no, rows: rows, acknowledgement: acknowledgement, dangerPrimary: danger) == DialogResultKind.Primary;

    public static DialogRow Row(string title, string? subtitle = null, string? detail = null, string? badge = null, string tone = "Neutral", bool dim = false) => new()
    {
        Title = title, Subtitle = subtitle, Detail = detail, Badge = badge, Opacity = dim ? 0.55 : 1,
        BadgeBackground = tone == "Neutral" ? Res("NeutralSoftBrush") : Res(tone + "SoftBrush"),
        BadgeForeground = tone == "Neutral" ? Res("TextDimBrush") : Res(tone + "Brush")
    };

    private static string RiskTone(RiskLevel r) => r switch { RiskLevel.Low => "Success", RiskLevel.Medium => "Warning", RiskLevel.High => "Danger", _ => "Purple" };

    public static string RestartText(RestartRequirement r) => r switch
    {
        RestartRequirement.None => "Takes effect immediately",
        RestartRequirement.AppRestart => "Restart the affected app/game",
        RestartRequirement.ExplorerRestart => "Explorer restart",
        RestartRequirement.SignOut => "Sign out and back in",
        RestartRequirement.Restart => "PC restart",
        _ => r.ToString()
    };

    // ------------------------------------------------------------------------------------ IApprovalProvider

    public Task<bool> ConfirmPlanAsync(PlanReview review, CancellationToken ct)
    {
        var rows = review.Items.Select(i => Row(
            i.Title,
            $"{i.Target}",
            i.SkipReason is not null
                ? $"Skipped: {i.SkipReason}"
                : $"Current: {i.Current}   →   New: {i.Desired}   ·   {RestartText(i.Restart)}{(i.RequiresAdmin ? "   ·   admin" : "")}{(i.HasVerifiedRollback ? "" : "   ·   rollback not guaranteed")}{(i.Notes is null ? "" : "\n" + i.Notes)}",
            i.SkipReason is not null ? "Skip" : review.IsRollback ? "Restore" : i.Risk.ToString(),
            i.SkipReason is not null ? "Neutral" : review.IsRollback ? "Accent" : RiskTone(i.Risk),
            dim: i.SkipReason is not null)).ToList();

        string message;
        if (review.IsRollback)
        {
            message = $"The following {review.ActionableCount} setting(s) will be returned to the values recorded before they were changed.";
        }
        else
        {
            message = $"{review.ActionableCount} change(s) will be made. Original values are backed up first.";
            if (review.RestorePointRequired)
            {
                message += $"\n\nA System Restore point will be created and verified before anything changes:\n“{review.ProposedRestorePointName}”";
                if (review.Protection?.Enabled == false) message += "\n\n⚠ System Protection appears to be OFF — the operation will be blocked unless it is turned on.";
                else if (review.Protection?.NextAllowedCreation is { } next && next > DateTime.Now)
                    message += $"\n\n⚠ Windows normally allows one restore point per {review.Protection.FrequencyMinutes} minutes. The last one was created at {review.Protection.LastRestorePoint!.CreationTime:HH:mm}; Windows may skip this request, in which case the operation is blocked by default.";
            }
            if (review.MaxRestart > RestartRequirement.None) message += $"\n\nAfter applying: {RestartText(review.MaxRestart)} required for some changes.";
        }
        bool hasRisky = review.Items.Any(i => i.SkipReason is null && i.Risk >= RiskLevel.High);
        var result = Show(review.IsRollback ? "Review restore" : $"Review changes — {review.Label}", message,
            review.IsRollback ? DialogTone.Question : DialogTone.Warning,
            review.IsRollback ? "Restore" : "Apply changes", "Cancel", rows: rows,
            acknowledgement: hasRisky ? "I understand these are expert/high-risk changes and have read the drawbacks." : null);
        return Task.FromResult(result == DialogResultKind.Primary);
    }

    public Task<bool> ConfirmBackupOnlyOverrideAsync(RestorePointFailureReview review, CancellationToken ct)
    {
        var a = review.Attempt;
        string msg = $"No new restore point could be verified.\n\nStatus: {a.Status}\n{a.Message}\n\nNothing has been changed.";
        if (a.Status is RestorePointStatus.Disabled) msg += "\n\n" + review.Instructions;

        if (!review.OverridePossible)
        {
            var reason = !review.AllChangesHaveVerifiedRollback
                ? "An override is not available because these changes cannot be rolled back with a verified application backup:\n• " + string.Join("\n• ", review.ChangesWithoutVerifiedRollback)
                : "An application-backup-only override is disabled. It can be enabled in Settings → Restore point policy (advanced). An application backup is NOT equivalent to a full system restore point.";
            var r = Show("Operation blocked — no verified restore point", msg + "\n\n" + reason, DialogTone.Danger, "OK",
                tertiary: a.Status is RestorePointStatus.Disabled ? "Open System Protection" : null);
            if (r == DialogResultKind.Tertiary) Core.Services.RestorePointService.OpenSystemProtectionSettings();
            return Task.FromResult(false);
        }

        var res = Show("No verified restore point", msg +
                       "\n\nAdvanced override: every planned change has a verified application-level backup and can be rolled back from Backup & Recovery. " +
                       "This is NOT equivalent to a System Restore point — it cannot undo effects outside the recorded settings.",
            DialogTone.Danger, "Proceed with app backup only", "Cancel (recommended)",
            acknowledgement: "I understand that no new System Restore point protects this change.", dangerPrimary: true);
        return Task.FromResult(res == DialogResultKind.Primary);
    }

    // ------------------------------------------------------------------------------------ files

    public string? SaveFile(string title, string filter, string fileName)
    {
        var d = new SaveFileDialog { Title = title, Filter = filter, FileName = fileName, AddExtension = true };
        return d.ShowDialog(Application.Current.MainWindow) == true ? d.FileName : null;
    }

    public string? OpenFile(string title, string filter)
    {
        var d = new OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true };
        return d.ShowDialog(Application.Current.MainWindow) == true ? d.FileName : null;
    }
}
