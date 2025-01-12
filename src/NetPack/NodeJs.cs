namespace NetPack;

using System;
using System.Buffers;
using System.Diagnostics;
using System.Text;
using System.Threading.Channels;

class NodeJs : IDisposable
{
    private static readonly byte[] zero = [0x0];
    private readonly string _root;
    private readonly Channel<Func<Stream, Stream, Task>> _channel;
    private readonly CancellationTokenSource _cts;

    public NodeJs(string root)
    {
        _root = root;
        _cts = new();
        _channel = Channel.CreateUnbounded<Func<Stream, Stream, Task>>();

        // Start processing queued tasks
        Task.Run(ProcessQueueAsync, _cts.Token);
    }

    private Process Start()
    {
        var command =
            "const commands = { " +
                "tsc: (content) => require('typescript').transpile(content, { jsx: 1, module: 99, target: 99 }), " +
                "sass: (content) => require('sass').compileString(content).css, " +
            "}; " +
            "const buffers = []; " +
            "let remaining = 0; " +
            "process.stdin.on('data', (data) => { " +
                "if (!remaining) { " +
                    "const l = data.indexOf(0); " +
                    "if (l === -1) return; " +
                    "buffers.push(data.subarray(0, l)); " +
                    "remaining = data.readInt32LE(l + 1); " +
                    "if (remaining > 0) data = data.subarray(l + 5); " +
                "} " +
                "if (remaining > 0) { " +
                    "remaining -= data.length; " +
                    "buffers.push(data); " +
                "} " +
                "if (!remaining) { " +
                    "const cmd = buffers[0].toString('utf8'); " +
                    "const content = Buffer.concat(buffers.slice(1)).toString('utf8'); " +
                    "buffers.splice(0, buffers.length); " +
                    "let ret = ''; " +
                    "try { ret = commands[cmd](content) || ''; } catch {} " +
                    "const res = Buffer.from(ret, 'utf8'); " +
                    "const num = Buffer.alloc(4); " +
                    "const total = res.byteLength; " +
                    "num.writeInt32LE(total); " +
                    "process.stdout.write(num); " +
                    "process.stdout.write(res); " +
                "} " +
            "});";
        var startInfo = new ProcessStartInfo("node")
        {
            UseShellExecute = false,
            WorkingDirectory = _root,
            CreateNoWindow = true,
            Arguments = $"-e \"{command}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
        };

        return Process.Start(startInfo)!;
    }

    public Task<string> RunCommand(string command, string content)
    {
        var tcs = new TaskCompletionSource<string>();

        async Task WorkItem(Stream input, Stream output)
        {
            var numBuffer = ArrayPool<byte>.Shared.Rent(4);
            var preamble = Encoding.UTF8.GetBytes(command);
            var parameter = Encoding.UTF8.GetBytes(content);
            var remaining = BitConverter.GetBytes(parameter.Length);

            try
            {
                var task = output.ReadExactlyAsync(numBuffer, 0, 4);

                await input.WriteAsync(preamble, 0, preamble.Length);
                await input.WriteAsync(zero, 0, zero.Length);
                await input.WriteAsync(remaining, 0, remaining.Length);
                await input.WriteAsync(parameter, 0, parameter.Length);
                await input.FlushAsync();

                await task;
                var result = "";
                var length = BitConverter.ToInt32(numBuffer, 0);

                if (length > 0)
                {
                    var buffer = ArrayPool<byte>.Shared.Rent(length);

                    try
                    {
                        await output.ReadExactlyAsync(buffer, 0, length);
                        result = Encoding.UTF8.GetString(buffer, 0, length);
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(buffer);
                    }
                }

                tcs.SetResult(result);
            }
            catch (EndOfStreamException)
            {
                throw;
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(numBuffer);
            }
        }

        _channel.Writer.TryWrite(WorkItem);
        return tcs.Task;
    }

    private async Task ProcessQueueAsync()
    {
        var token = _cts.Token;
        var process = Start();
        var registration = token.Register(() => 
        {    
            process?.Kill();
            process?.Dispose();
        });

        await foreach (var workItem in _channel.Reader.ReadAllAsync(token))
        {
            while (true)
            {
                try
                {
                    await workItem(process.StandardInput.BaseStream, process.StandardOutput.BaseStream);
                    break;
                }
                catch (EndOfStreamException)
                {
                    registration.Unregister();
                    process = Start();
                    registration = token.Register(() => 
                    {
                        process?.Kill();
                        process?.Dispose();
                    });
                }
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}