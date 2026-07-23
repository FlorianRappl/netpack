namespace NetPack.Server;

using Microsoft.Extensions.Hosting;

sealed class NoopConsoleLifetime : IHostLifetime, IDisposable
{
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task WaitForStartAsync(CancellationToken cancellationToken)
    {
        Console.CancelKeyPress += OnCancelKeyPressed;
        return Task.CompletedTask;
    }

    private void OnCancelKeyPressed(object? sender, ConsoleCancelEventArgs e)
    {
        e.Cancel = true;
        Task.Run(() => Environment.Exit(0));
    }

    public void Dispose()
    {
        Console.CancelKeyPress -= OnCancelKeyPressed;
    }
}