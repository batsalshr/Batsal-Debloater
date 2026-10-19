using UltimatePCOptimizerPro.Core.Models;

namespace UltimatePCOptimizerPro.Core.Engine;

/// <summary>
/// Implements one kind of system change. Handlers are only ever invoked by the OptimizationEngine, which guarantees
/// the order Preflight → Capture → (restore point) → Apply → Verify, and Restore → VerifyRestored for rollback.
/// </summary>
public interface IChangeHandler
{
    bool CanHandle(ChangeSpec spec);
    /// <summary>Reads current state, support and permissions. Must not modify anything.</summary>
    Preflight Preflight(ChangeSpec spec);
    /// <summary>Captures everything needed to restore the exact original state.</summary>
    BackupState Capture(ChangeSpec spec);
    /// <summary>Applies the change. May add values to the backup (e.g. ids of objects created).</summary>
    void Apply(ChangeSpec spec, BackupState backup);
    VerifyResult Verify(ChangeSpec spec, BackupState backup);
    void Restore(ChangeSpec spec, BackupState backup);
    VerifyResult VerifyRestored(ChangeSpec spec, BackupState backup);
    /// <summary>True when Restore returns the system to the captured state and that can be verified immediately.</summary>
    bool HasVerifiedRollback(ChangeSpec spec);
    bool RequiresAdmin(ChangeSpec spec);
    bool RequiresRestorePoint(ChangeSpec spec);
}

public abstract class ChangeHandler<T> : IChangeHandler where T : ChangeSpec
{
    public bool CanHandle(ChangeSpec spec) => spec is T;
    public Preflight Preflight(ChangeSpec spec) => Preflight((T)spec);
    public BackupState Capture(ChangeSpec spec) => Capture((T)spec);
    public void Apply(ChangeSpec spec, BackupState backup) => Apply((T)spec, backup);
    public VerifyResult Verify(ChangeSpec spec, BackupState backup) => Verify((T)spec, backup);
    public void Restore(ChangeSpec spec, BackupState backup) => Restore((T)spec, backup);
    public VerifyResult VerifyRestored(ChangeSpec spec, BackupState backup) => VerifyRestored((T)spec, backup);
    public bool HasVerifiedRollback(ChangeSpec spec) => HasVerifiedRollback((T)spec);
    public bool RequiresAdmin(ChangeSpec spec) => RequiresAdmin((T)spec);
    public bool RequiresRestorePoint(ChangeSpec spec) => RequiresRestorePoint((T)spec);

    protected abstract Preflight Preflight(T spec);
    protected abstract BackupState Capture(T spec);
    protected abstract void Apply(T spec, BackupState backup);
    protected abstract VerifyResult Verify(T spec, BackupState backup);
    protected abstract void Restore(T spec, BackupState backup);
    protected abstract VerifyResult VerifyRestored(T spec, BackupState backup);
    protected virtual bool HasVerifiedRollback(T spec) => true;
    protected abstract bool RequiresAdmin(T spec);
    protected virtual bool RequiresRestorePoint(T spec) => true;
}

public sealed class HandlerRegistry
{
    private readonly List<IChangeHandler> _handlers;
    public HandlerRegistry(IEnumerable<IChangeHandler> handlers) => _handlers = handlers.ToList();

    public IChangeHandler Get(ChangeSpec spec)
        => _handlers.FirstOrDefault(h => h.CanHandle(spec)) ?? throw new NotSupportedException($"No handler registered for {spec.GetType().Name}.");

    public static HandlerRegistry CreateDefault(Services.LoggingService log) => new(new IChangeHandler[]
    {
        new Handlers.RegistryValueHandler(),
        new Handlers.RegistryTokenHandler(),
        new Handlers.RegistryFlagsHandler(),
        new Handlers.AppCompatLayerHandler(),
        new Handlers.PowerSchemeHandler(),
        new Handlers.PowerSettingHandler(),
        new Handlers.ServiceStartHandler(),
        new Handlers.ScheduledTaskHandler(),
        new Handlers.StartupEntryHandler(),
        new Handlers.AppxRemoveHandler(),
        new Handlers.DisplayModeHandler(),
        new Handlers.DevicePowerHandler(),
        new Handlers.OptionalFeatureHandler(),
    });
}
