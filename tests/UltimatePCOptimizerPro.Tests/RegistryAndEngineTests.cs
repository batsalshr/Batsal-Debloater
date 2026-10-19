using Microsoft.Win32;
using UltimatePCOptimizerPro.Core.Engine;
using UltimatePCOptimizerPro.Core.Engine.Handlers;
using UltimatePCOptimizerPro.Core.Models;
using UltimatePCOptimizerPro.Core.Services;

namespace UltimatePCOptimizerPro.Tests;

public class RegistryHandlerTests
{
    [Fact]
    public void New_value_in_new_key_is_fully_removed_on_restore()
    {
        using var key = new TestEnvironment.TempKey();
        var h = new RegistryValueHandler();
        var spec = new RegistryValueSpec { Hive = "HKCU", Path = key.Path + @"\Deep\Child", Name = "Value", Kind = RegistryValueKindName.DWord, Value = "0x10" };

        Assert.False(h.Preflight(spec).AlreadyApplied);
        var backup = h.Capture(spec);
        Assert.Equal("False", backup.Get("valueExisted"));
        h.Apply(spec, backup);
        Assert.True(h.Verify(spec, backup).Ok);
        Assert.Equal(16, Registry.CurrentUser.OpenSubKey(spec.Path)!.GetValue("Value"));
        Assert.True(h.Preflight(spec).AlreadyApplied);

        h.Restore(spec, backup);
        Assert.True(h.VerifyRestored(spec, backup).Ok);
        Assert.Null(Registry.CurrentUser.OpenSubKey(key.Path + @"\Deep"));
    }

    [Fact]
    public void Existing_value_type_and_data_are_restored_exactly()
    {
        using var key = new TestEnvironment.TempKey();
        using (var k = Registry.CurrentUser.CreateSubKey(key.Path)) k.SetValue("Mode", "%SystemRoot%\\x", RegistryValueKind.ExpandString);
        var h = new RegistryValueHandler();
        var spec = new RegistryValueSpec { Hive = "HKCU", Path = key.Path, Name = "Mode", Kind = RegistryValueKindName.DWord, Value = "1" };
        var backup = h.Capture(spec);
        h.Apply(spec, backup);
        Assert.Equal(RegistryValueKind.DWord, Registry.CurrentUser.OpenSubKey(key.Path)!.GetValueKind("Mode"));
        h.Restore(spec, backup);
        using var after = Registry.CurrentUser.OpenSubKey(key.Path)!;
        Assert.Equal(RegistryValueKind.ExpandString, after.GetValueKind("Mode"));
        Assert.Equal("%SystemRoot%\\x", after.GetValue("Mode", null, RegistryValueOptions.DoNotExpandEnvironmentNames));
    }

    [Fact]
    public void Absent_value_matching_windows_default_counts_as_applied()
    {
        using var key = new TestEnvironment.TempKey();
        var h = new RegistryValueHandler();
        var matching = new RegistryValueSpec { Hive = "HKCU", Path = key.Path, Name = "Mode", Kind = RegistryValueKindName.DWord, Value = "1", WindowsDefault = "1" };
        var different = new RegistryValueSpec { Hive = "HKCU", Path = key.Path, Name = "Mode", Kind = RegistryValueKindName.DWord, Value = "0", WindowsDefault = "1" };
        Assert.True(h.Preflight(matching).AlreadyApplied);
        Assert.False(h.Preflight(different).AlreadyApplied);
        Assert.Null(Registry.CurrentUser.OpenSubKey(key.Path));
    }

    [Fact]
    public void Flags_handler_preserves_unrelated_bits()
    {
        using var key = new TestEnvironment.TempKey();
        using (var k = Registry.CurrentUser.CreateSubKey(key.Path)) k.SetValue("Flags", "511", RegistryValueKind.String); // feature ON (bit 0)
        var h = new RegistryFlagsHandler();
        var spec = new RegistryFlagsSpec { Hive = "HKCU", Path = key.Path, Name = "Flags", ClearBits = 4, DefaultValue = 510 };
        var b = h.Capture(spec);
        h.Apply(spec, b);
        Assert.Equal("507", Registry.CurrentUser.OpenSubKey(key.Path)!.GetValue("Flags"));
        h.Restore(spec, b);
        Assert.Equal("511", Registry.CurrentUser.OpenSubKey(key.Path)!.GetValue("Flags"));
    }

    [Fact]
    public void Token_handler_round_trip()
    {
        using var key = new TestEnvironment.TempKey();
        using (var k = Registry.CurrentUser.CreateSubKey(key.Path)) k.SetValue("Settings", "AutoHDREnable=1;", RegistryValueKind.String);
        var h = new RegistryTokenHandler();
        var spec = new RegistryTokenSpec { Hive = "HKCU", Path = key.Path, Name = "Settings", Token = "SwapEffectUpgradeEnable", TokenValue = "1" };
        var b = h.Capture(spec);
        h.Apply(spec, b);
        Assert.Equal("AutoHDREnable=1;SwapEffectUpgradeEnable=1;", Registry.CurrentUser.OpenSubKey(key.Path)!.GetValue("Settings"));
        h.Restore(spec, b);
        Assert.Equal("AutoHDREnable=1;", Registry.CurrentUser.OpenSubKey(key.Path)!.GetValue("Settings"));
    }
}

public class EngineTests
{
    private sealed class FakeRestorePoints : IRestorePointProvider
    {
        public RestorePointStatus Status { get; init; } = RestorePointStatus.Created;
        public int Calls { get; private set; }
        public Task<RestorePointAttempt> CreateAsync(string label, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new RestorePointAttempt
            {
                Status = Status, Verified = Status == RestorePointStatus.Created, Description = RestorePointService.BuildName(label, DateTimeOffset.Now),
                Message = Status.ToString(), SequenceNumber = Status == RestorePointStatus.Created ? 42u : null
            });
        }
        public SystemProtectionStatus GetProtectionStatus() => new() { Supported = true, Enabled = true };
    }

    private static (OptimizationEngine Engine, SettingsService Settings, BackupService Backups) Create(IRestorePointProvider rp, RestorePointPolicy policy = RestorePointPolicy.RequireVerifiedRestorePoint)
    {
        var log = new LoggingService();
        var settings = new SettingsService(log);
        settings.Current.RestorePointPolicy = policy;
        settings.Current.ExportRegistryKeysWithBackups = false;
        settings.Current.AutoRollbackOnFailure = true;
        var backups = new BackupService(log);
        var engine = new OptimizationEngine(HandlerRegistry.CreateDefault(log), rp, backups, new AuditLogService(), settings, log, () => true);
        return (engine, settings, backups);
    }

    private static PlannedChange Change(string path, string name, string value) => new()
    {
        Title = name, TweakId = "test." + name, Category = "Test",
        Spec = new RegistryValueSpec { Hive = "HKCU", Path = path, Name = name, Kind = RegistryValueKindName.DWord, Value = value }
    };

    [Fact]
    public async Task Applies_with_verified_restore_point_and_rolls_back()
    {
        using var key = new TestEnvironment.TempKey();
        var rp = new FakeRestorePoints();
        var (engine, _, backups) = Create(rp);
        var op = await engine.ExecuteAsync(new OperationRequest { Label = "Test", Changes = { Change(key.Path, "A", "1"), Change(key.Path, "B", "2") } }, new AutoApproveProvider());

        Assert.Equal(OperationStatus.Completed, op.Status);
        Assert.Equal(1, rp.Calls);
        Assert.True(op.RestorePoint!.Verified);
        Assert.Equal(2, op.AppliedCount);
        Assert.True(File.Exists(Path.Combine(op.FolderPath!, "operation.json")));
        Assert.Equal(1, Registry.CurrentUser.OpenSubKey(key.Path)!.GetValue("A"));

        var saved = backups.Load(op.Id)!;
        var rb = await engine.RollbackAsync(saved, null, new AutoApproveProvider());
        Assert.Equal(OperationStatus.Completed, rb.Status);
        Assert.All(rb.Changes, c => Assert.Equal(ChangeStatus.Restored, c.Status));
        Assert.Null(Registry.CurrentUser.OpenSubKey(key.Path)?.GetValue("A"));
        Assert.False(backups.Load(op.Id)!.HasActiveChanges);
    }

    [Fact]
    public async Task Blocks_when_restore_point_is_throttled_by_default()
    {
        using var key = new TestEnvironment.TempKey();
        var (engine, _, _) = Create(new FakeRestorePoints { Status = RestorePointStatus.Throttled });
        var op = await engine.ExecuteAsync(new OperationRequest { Label = "Test", Changes = { Change(key.Path, "A", "1") } }, new AutoApproveProvider { AllowOverride = true });

        Assert.Equal(OperationStatus.Blocked, op.Status);
        Assert.Null(Registry.CurrentUser.OpenSubKey(key.Path)?.GetValue("A"));
    }

    [Fact]
    public async Task Backup_only_override_requires_policy_and_explicit_consent()
    {
        using var key = new TestEnvironment.TempKey();
        var (engine, _, _) = Create(new FakeRestorePoints { Status = RestorePointStatus.Throttled }, RestorePointPolicy.AllowBackupOnlyOverride);

        var declined = await engine.ExecuteAsync(new OperationRequest { Label = "Test", Changes = { Change(key.Path, "A", "1") } }, new AutoApproveProvider { AllowOverride = false });
        Assert.Equal(OperationStatus.Blocked, declined.Status);

        var accepted = await engine.ExecuteAsync(new OperationRequest { Label = "Test", Changes = { Change(key.Path, "A", "1") } }, new AutoApproveProvider { AllowOverride = true });
        Assert.Equal(OperationStatus.Completed, accepted.Status);
        Assert.True(accepted.BackupOnlyOverrideUsed);
    }

    [Fact]
    public async Task Failed_change_rolls_back_the_whole_batch()
    {
        using var key = new TestEnvironment.TempKey();
        var (engine, _, _) = Create(new FakeRestorePoints());
        var op = await engine.ExecuteAsync(new OperationRequest
        {
            Label = "Test",
            Changes = { Change(key.Path, "Good", "1"), Change(key.Path, "Bad", "not-a-number") }
        }, new AutoApproveProvider());

        Assert.Equal(OperationStatus.RolledBack, op.Status);
        Assert.Equal(ChangeStatus.RolledBack, op.Changes[0].Status);
        Assert.Null(Registry.CurrentUser.OpenSubKey(key.Path)?.GetValue("Good"));
    }

    [Fact]
    public async Task Already_applied_changes_are_skipped_without_restore_point()
    {
        using var key = new TestEnvironment.TempKey();
        using (var k = Registry.CurrentUser.CreateSubKey(key.Path)) k.SetValue("A", 1, RegistryValueKind.DWord);
        var rp = new FakeRestorePoints();
        var (engine, _, _) = Create(rp);
        var op = await engine.ExecuteAsync(new OperationRequest { Label = "Test", Changes = { Change(key.Path, "A", "1") } }, new AutoApproveProvider());
        Assert.Equal(OperationStatus.NothingToDo, op.Status);
        Assert.Equal(0, rp.Calls);
    }

    [Fact]
    public void Operation_records_round_trip_through_json()
    {
        var op = new OperationRecord { Label = "X" };
        op.Changes.Add(new ChangeRecord { Title = "t", Spec = new PowerSettingSpec { Subgroup = Guid.NewGuid(), Setting = Guid.NewGuid(), AcValue = 0 }, Backup = new BackupState { Values = { ["ac"] = "1" } } });
        op.Changes.Add(new ChangeRecord { Title = "s", Spec = new ServiceStartSpec { ServiceName = "Fax", StartMode = ServiceStartMode.Disabled } });
        var json = System.Text.Json.JsonSerializer.Serialize(op, JsonDefaults.Indented);
        var back = System.Text.Json.JsonSerializer.Deserialize<OperationRecord>(json, JsonDefaults.Indented)!;
        Assert.IsType<PowerSettingSpec>(back.Changes[0].Spec);
        Assert.Equal(0u, ((PowerSettingSpec)back.Changes[0].Spec).AcValue);
        Assert.IsType<ServiceStartSpec>(back.Changes[1].Spec);
        Assert.Equal("1", back.Changes[0].Backup!.Get("ac"));
    }
}
