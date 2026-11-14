using HuntHelper.Managers.Automation;

internal static class CoverageRangeLeaseChecks
{
    public static readonly (string Name, Action Run)[] Tests =
    [
        ("DR display range leases restore original 0 and 1 without treating owned writes as user edits", RestoreOriginal),
        ("an already maximal display range acquires and releases without writing game settings", AlreadyMaximum),
        ("a failed or rejected DR range setup rolls its setting back before returning failure", AcquireRollback),
        ("DR range cleanup preserves user changes including a later user choice of maximum", ExternalChanges),
        ("DR range cleanup retains ownership when reading temporarily fails and can retry", RetryRead),
        ("DR range cleanup retries failed or rejected restores without losing the original value", RetryWrite),
        ("a failed setup with temporarily failed rollback retains a recoverable range lease", RetryAcquireRollback),
        ("repeated DR range release and disposal restore the setting only once", ReleaseOnce),
    ];

    private static void Check(bool condition, string message = "DR range lease assertion failed")
    { if (!condition) throw new Exception(message); }

    private static void Throws(Action action)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new Exception("Expected a recoverable display range error");
    }

    private sealed class Setting
    {
        public uint? Value;
        public Func<uint?>? ReadOverride;
        public Action<uint>? WriteOverride;
        public List<uint> Writes { get; } = new();
        public CoverageRangeLease Lease { get; }

        public Setting(uint original)
        {
            Value = original;
            Lease = new(() => ReadOverride == null ? Value : ReadOverride(), Apply);
        }

        private void Apply(uint value)
        {
            Writes.Add(value);
            if (WriteOverride == null) Value = value;
            else WriteOverride(value);
            // Real game setting events can fire synchronously during an owned write.
            Lease.NotifyExternalChange();
        }

        public void UserChanges(uint value)
        {
            Value = value;
            Lease.NotifyExternalChange();
        }
    }

    private static void RestoreOriginal()
    {
        foreach (var original in new uint[] { 0, 1 })
        {
            var setting = new Setting(original);
            Check(setting.Lease.TryAcquire(out var error), error);
            Check(setting.Value == 2 && setting.Lease.Active && !setting.Lease.ChangedExternally);
            Check(setting.Lease.TryAcquire(out error) && setting.Writes.SequenceEqual(new uint[] { 2 }), error);
            setting.Lease.Release();
            Check(setting.Value == original && !setting.Lease.Active && !setting.Lease.ChangedExternally);
            Check(setting.Writes.SequenceEqual(new[] { 2u, original }));
        }
    }

    private static void AlreadyMaximum()
    {
        var setting = new Setting(2);
        Check(setting.Lease.TryAcquire(out var error), error);
        Check(setting.Lease.Active && setting.Value == 2 && setting.Writes.Count == 0);
        setting.Lease.Release();
        Check(!setting.Lease.Active && setting.Value == 2 && setting.Writes.Count == 0);
    }

    private static void AcquireRollback()
    {
        var setting = new Setting(1);
        setting.WriteOverride = value =>
        {
            setting.Value = value;
            if (value == 2) throw new InvalidOperationException("setup failed after changing the value");
        };
        Check(!setting.Lease.TryAcquire(out var error) && error.Contains("setup failed"));
        Check(!setting.Lease.Active && setting.Value == 1);
        Check(setting.Writes.SequenceEqual(new uint[] { 2, 1 }));

        var rejected = new Setting(0) { WriteOverride = _ => { } };
        Check(!rejected.Lease.TryAcquire(out error) && !string.IsNullOrWhiteSpace(error));
        Check(!rejected.Lease.Active && rejected.Value == 0 && rejected.Writes.SequenceEqual(new uint[] { 2 }));
    }

    private static void ExternalChanges()
    {
        var changed = new Setting(1);
        Check(changed.Lease.TryAcquire(out var error), error);
        changed.UserChanges(0);
        Check(changed.Lease.ChangedExternally);
        changed.Lease.Release();
        Check(changed.Value == 0 && !changed.Lease.Active && changed.Writes.SequenceEqual(new uint[] { 2 }));

        var changedBack = new Setting(1);
        Check(changedBack.Lease.TryAcquire(out error), error);
        changedBack.UserChanges(0);
        changedBack.UserChanges(2);
        changedBack.Lease.Release();
        Check(changedBack.Value == 2 && !changedBack.Lease.Active && changedBack.Writes.SequenceEqual(new uint[] { 2 }),
            "The user's later choice of maximum was overwritten by the stale original setting");

        var noEvent = new Setting(1);
        Check(noEvent.Lease.TryAcquire(out error), error);
        noEvent.Value = 0; // The value guard also protects a change whose event was missed.
        noEvent.Lease.Release();
        Check(noEvent.Value == 0 && noEvent.Writes.SequenceEqual(new uint[] { 2 }));
    }

    private static void RetryRead()
    {
        var setting = new Setting(0);
        Check(setting.Lease.TryAcquire(out var error), error);
        setting.ReadOverride = () => null;
        Throws(setting.Lease.Release);
        Check(setting.Lease.Active && setting.Value == 2 && setting.Writes.SequenceEqual(new uint[] { 2 }));
        setting.ReadOverride = null;
        setting.Lease.Release();
        Check(!setting.Lease.Active && setting.Value == 0 && setting.Writes.SequenceEqual(new uint[] { 2, 0 }));
    }

    private static void RetryWrite()
    {
        foreach (var rejectsSilently in new[] { false, true })
        {
            var setting = new Setting(1);
            Check(setting.Lease.TryAcquire(out var error), error);
            setting.WriteOverride = _ =>
            {
                if (!rejectsSilently) throw new InvalidOperationException("restore temporarily unavailable");
            };
            Throws(setting.Lease.Release);
            Check(setting.Lease.Active && setting.Value == 2 && setting.Writes.SequenceEqual(new uint[] { 2, 1 }));
            setting.WriteOverride = null;
            setting.Lease.Release();
            Check(!setting.Lease.Active && setting.Value == 1 && setting.Writes.SequenceEqual(new uint[] { 2, 1, 1 }));
        }
    }

    private static void RetryAcquireRollback()
    {
        var setting = new Setting(0);
        setting.WriteOverride = value =>
        {
            if (value == 2)
            {
                setting.Value = value;
                throw new InvalidOperationException("setup failed");
            }
            throw new InvalidOperationException("rollback temporarily unavailable");
        };
        Check(!setting.Lease.TryAcquire(out var error) && error.Contains("setup failed") && error.Contains("rollback temporarily unavailable"));
        Check(setting.Lease.Active && setting.Value == 2 && setting.Writes.SequenceEqual(new uint[] { 2, 0 }));
        setting.WriteOverride = null;
        setting.Lease.Release();
        Check(!setting.Lease.Active && setting.Value == 0 && setting.Writes.SequenceEqual(new uint[] { 2, 0, 0 }));
    }

    private static void ReleaseOnce()
    {
        var setting = new Setting(1);
        Check(setting.Lease.TryAcquire(out var error), error);
        setting.Lease.Release();
        setting.Lease.Release();
        setting.Lease.Dispose();
        setting.Lease.Dispose();
        Check(!setting.Lease.Active && setting.Value == 1 && setting.Writes.SequenceEqual(new uint[] { 2, 1 }));
    }
}
