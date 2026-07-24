namespace NetPack.Server;

class FileWatcher<T> : IDisposable
    where T : IFileLocator
{
    private readonly FileSystemWatcher _watcher;
    private readonly Queue<Func<Task>> _taskQueue = new();
    private Task _current = Task.CompletedTask;
    private TaskCompletionSource _tcs = new();
    private T _result;

    public FileWatcher(T result, string? root = null)
    {
        _result = result;
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
        async Task Restart()
        {
            _result = await trigger();

            if (!_tcs.Task.IsCompleted)
            {
                var currentTcs = _tcs;
                _tcs = new TaskCompletionSource();
                currentTcs.SetResult();
            }

            await ProcessNext();
        }

        void OnChange(object sender, FileSystemEventArgs e)
        {
            if (_taskQueue.Count > 0)
            {
                return;
            }

            if (_result.HasFile(e.FullPath))
            {
                _taskQueue.Enqueue(Restart);

                if (_current.IsCompleted)
                {
                    _current = Task.Delay(200).ContinueWith(_ => ProcessNext());
                }
            }
        }

        _watcher.Changed += OnChange;
        _watcher.Deleted += OnChange;
        _watcher.Renamed += OnChange;
    }

    private Task ProcessNext()
    {
        if (_taskQueue.TryDequeue(out var processNext))
        {
            return processNext();
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _watcher.Dispose();
    }
}