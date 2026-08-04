namespace NetPack.Server;

class FileWatcher<T> : IDisposable
    where T : IFileLocator
{
    private readonly FileSystemWatcher _watcher;
    private readonly Action<string>? _invalidateDirectory;
    private readonly object _lock = new();
    private CancellationTokenSource? _debounceCts;
    private Task _rebuild = Task.CompletedTask;
    private bool _pending;
    private volatile bool _disposed;
    private TaskCompletionSource _tcs = new();
    private T _result;

    private readonly int _debounceMs;

    public FileWatcher(T result, int debounceMs = 200, string? root = null, Action<string>? invalidateDirectory = null)
    {
        _result = result;
        _debounceMs = debounceMs;
        _invalidateDirectory = invalidateDirectory;
        _watcher = new FileSystemWatcher(root ?? Environment.CurrentDirectory)
        {
            IncludeSubdirectories = true,
            EnableRaisingEvents = true,
        };
    }

    public T Result => _result;

    public Task Next => _tcs.Task;

    public void Install(Func<Task<T>> trigger)
    {
        void OnChange(object sender, FileSystemEventArgs e)
        {
            var shouldInvalidate = e.ChangeType is WatcherChangeTypes.Created or WatcherChangeTypes.Deleted or WatcherChangeTypes.Renamed;
            var shouldRebuild = _result.HasFile(e.FullPath)
                || (shouldInvalidate && _result.HasDirectory(Path.GetDirectoryName(e.FullPath)!));

            if (shouldInvalidate)
            {
                _invalidateDirectory?.Invoke(Path.GetDirectoryName(e.FullPath)!);
            }

            if (!shouldRebuild)
            {
                return;
            }

            lock (_lock)
            {
                // Once disposed, ignore late FileSystemWatcher events so no rebuild
                // runs against files a caller may be tearing down.
                if (_disposed)
                {
                    return;
                }

                // Cancel any pending debounce timer — we just received a newer change.
                _debounceCts?.Cancel();

                // If a rebuild is already running, mark pending and we'll queue after.
                if (!_rebuild.IsCompleted)
                {
                    _pending = true;
                    return;
                }

                // Start a fresh debounce timer.
                _debounceCts = new CancellationTokenSource();
                var token = _debounceCts.Token;
                _pending = false;

                _rebuild = Task.Delay(_debounceMs, token).ContinueWith(_ =>
                {
                    if (token.IsCancellationRequested || _disposed) return Task.CompletedTask;
                    return DoRebuild(trigger);
                }, token).Unwrap();
            }
        }

        _watcher.Changed += OnChange;
        _watcher.Created += OnChange;
        _watcher.Deleted += OnChange;
        _watcher.Renamed += OnChange;
    }

    private async Task DoRebuild(Func<Task<T>> trigger)
    {
        try
        {
            Console.WriteLine("[netpack] File change detected — rebuilding ...");
            _result = await trigger();
            Console.WriteLine("[netpack] Rebuild complete.");

            // Signal any waiters (e.g. the dev server's Next) that a new result is ready.
            if (!_tcs.Task.IsCompleted)
            {
                var currentTcs = _tcs;
                _tcs = new TaskCompletionSource();
                currentTcs.SetResult();
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[netpack] Rebuild failed: {ex.Message}");
        }

        // If more changes arrived during the rebuild, start another — unless we're
        // shutting down.
        bool restart;
        lock (_lock)
        {
            restart = _pending && !_disposed;
            _pending = false;
        }

        if (restart)
        {
            Console.WriteLine("[netpack] Changes arrived during rebuild — triggering another ...");
            await DoRebuild(trigger);
        }
    }

    public void Dispose()
    {
        Task inFlight;

        lock (_lock)
        {
            _disposed = true;
            _debounceCts?.Cancel();
            inFlight = _rebuild;
        }

        // Stop new filesystem events first, then wait for any in-flight (or
        // just-elapsed) rebuild to finish, so nothing runs a build after Dispose
        // returns — e.g. against files the caller is about to delete.
        _watcher.Dispose();

        try
        {
            inFlight.Wait(TimeSpan.FromSeconds(5));
        }
        catch
        {
            // A rebuild that faulted (or was cancelled) is fine — we only need it
            // to have stopped running.
        }

        lock (_lock)
        {
            _debounceCts?.Dispose();
        }
    }
}
