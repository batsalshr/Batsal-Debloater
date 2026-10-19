using System.Text.Json;
using UltimatePCOptimizerPro.Core.Models;

namespace UltimatePCOptimizerPro.Core.Services;

/// <summary>Append-only audit trail of every operation (history.jsonl).</summary>
public sealed class AuditLogService
{
    private readonly object _lock = new();
    public event Action<AuditEntry>? Appended;

    public void Append(OperationRecord op)
    {
        var entry = new AuditEntry
        {
            Timestamp = op.FinishedAt ?? DateTimeOffset.Now,
            OperationId = op.Id,
            Kind = op.Kind,
            Label = op.Label,
            Status = op.Status,
            Planned = op.Changes.Count,
            Applied = op.Kind == OperationKind.Apply ? op.AppliedCount : op.Changes.Count(c => c.Status == ChangeStatus.Restored),
            Failed = op.FailedCount,
            RestorePoint = op.RestorePoint?.Description,
            RestorePointStatus = op.RestorePoint?.Status,
            Summary = op.BlockReason ?? string.Join("; ", op.Changes.Take(6).Select(c => $"{c.Title}: {c.Status}")) + (op.Changes.Count > 6 ? $"; +{op.Changes.Count - 6} more" : "")
        };
        lock (_lock) File.AppendAllText(AppPaths.AuditFile, JsonSerializer.Serialize(entry, JsonDefaults.Compact) + Environment.NewLine);
        Appended?.Invoke(entry);
    }

    public List<AuditEntry> ReadAll()
    {
        var list = new List<AuditEntry>();
        if (!File.Exists(AppPaths.AuditFile)) return list;
        string[] lines;
        lock (_lock) lines = File.ReadAllLines(AppPaths.AuditFile);
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var e = JsonSerializer.Deserialize<AuditEntry>(line, JsonDefaults.Compact);
                if (e is not null) list.Add(e);
            }
            catch (JsonException) { }
        }
        return list.OrderByDescending(e => e.Timestamp).ToList();
    }
}
