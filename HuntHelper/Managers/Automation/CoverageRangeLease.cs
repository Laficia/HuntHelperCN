using System;

namespace HuntHelper.Managers.Automation;

/// <summary>Temporarily selects the longest normal object range without replacing a user's later choice.</summary>
public sealed class CoverageRangeLease : IDisposable
{
    private readonly Func<uint?> _read;
    private readonly Action<uint> _apply;
    private uint? _original;
    private bool _writing;
    private bool _changedExternally;

    public CoverageRangeLease(Func<uint?> read, Action<uint> apply)
    {
        _read = read;
        _apply = apply;
    }

    public bool Active => _original.HasValue;
    public bool ChangedExternally => _changedExternally;

    public bool TryAcquire(out string error)
    {
        error = string.Empty;
        if (Active) return true;
        try
        {
            if (_read() is not { } original || original > 2)
                throw new InvalidOperationException("无法读取游戏的角色与物体显示范围设置");
            _original = original;
            _changedExternally = false;
            if (original != 2) Apply(2);
            if (_read() != 2)
                throw new InvalidOperationException("游戏未接受最大显示范围设置，请在系统设置中选择最大显示范围后重试");
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            try { Release(); }
            catch (Exception restore) { error += $"；恢复原显示范围失败：{restore.Message}"; }
            return false;
        }
    }

    public void NotifyExternalChange()
    {
        if (Active && !_writing) _changedExternally = true;
    }

    public void Release()
    {
        if (_original is not { } original) return;
        // Both the event guard and the value guard matter: a user can change away
        // from 2 and later choose 2 again while this scan is still running.
        if (!_changedExternally && original != 2)
        {
            var current = _read() ?? throw new InvalidOperationException("暂时无法读取原显示范围，等待恢复");
            if (current == 2)
            {
                Apply(original);
                if (_read() != original)
                    throw new InvalidOperationException("游戏尚未恢复原显示范围，等待重试");
            }
        }
        _original = null;
        _changedExternally = false;
    }

    private void Apply(uint value)
    {
        _writing = true;
        try { _apply(value); }
        finally { _writing = false; }
    }

    public void Dispose() => Release();
}
