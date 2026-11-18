namespace SharpTimer;

// Kept independent of the game API so cancellation across enable periods can be tested.
internal sealed class RuntimeGate
{
    private volatile bool _enabled = true;
    private CancellationTokenSource _cancellation = new();
    private readonly AsyncLocal<CancellationToken?> _workToken = new();

    public bool Enabled => _enabled;
    public bool IsActive => _enabled && !CurrentToken.IsCancellationRequested;
    public CancellationToken CurrentToken => _workToken.Value ?? _cancellation.Token;

    public bool SetEnabled(bool enabled)
    {
        if (_enabled == enabled) return false;
        _enabled = enabled;
        _cancellation.Cancel();
        if (enabled) _cancellation = new CancellationTokenSource();
        return true;
    }

    public void CancelMapWork()
    {
        _cancellation.Cancel();
        if (_enabled) _cancellation = new CancellationTokenSource();
    }

    public void Invoke(CancellationToken token, Action callback)
    {
        if (!_enabled || token.IsCancellationRequested) return;
        var previous = _workToken.Value;
        _workToken.Value = token;
        try { callback(); }
        finally { _workToken.Value = previous; }
    }

    public Task Run(Action callback) => Run(() => { callback(); return Task.CompletedTask; });

    public Task Run(Func<Task> callback)
    {
        var token = CurrentToken;
        if (!IsActive) return Task.CompletedTask;
        return Task.Run(async () =>
        {
            _workToken.Value = token;
            if (!IsActive) return;
            try { await callback(); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        });
    }

    public Task<T?> Run<T>(Func<T?> callback)
    {
        var token = CurrentToken;
        if (!IsActive) return Task.FromResult<T?>(default);
        return Task.Run(() =>
        {
            _workToken.Value = token;
            return IsActive ? callback() : default;
        });
    }

    public async Task Await(Task task)
    {
        var token = CurrentToken;
        if (!IsActive) throw new OperationCanceledException("SharpTimer is disabled.");
        await task.WaitAsync(token);
        token.ThrowIfCancellationRequested();
    }

    public async Task<T> Await<T>(Task<T> task)
    {
        var token = CurrentToken;
        if (!IsActive) throw new OperationCanceledException("SharpTimer is disabled.");
        var result = await task.WaitAsync(token);
        token.ThrowIfCancellationRequested();
        return result;
    }
}
