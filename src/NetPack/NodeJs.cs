namespace NetPack;

using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using NetPack.Json;

class NodeJs : IDisposable
{
    private static readonly string command = @"const net = require('net');
const { pathToFileURL } = require('url');
const readline = require('readline/promises');

const client = net.createConnection({ port: +process.argv.pop(), host: '127.0.0.1' }, () => {
  console.log(`Using Node.js ${process.version} with netpack.`);
});

const rl = readline.createInterface(client);

const commands = {
  tsc: (content, file) => {
    const typescript = require('typescript');
    const opts = { jsx: 1, module: 99, target: 99 };
    return typescript.transpile(content, opts, file);
  },
  sass: (content, file) => {
    const sass = require('sass');
    const url = pathToFileURL(file);
    return sass.compileString(content, { url });
  },
  postCss: (content, file) => {
    const postcss = require('postcss').default([]);
    return postcss.process(content, { to: file, from: file });
  },
  codegen: async (file) => {
    const context = { name: file, options: {}, addDependency() {} };
    const res = await require(file).call(context);
    return typeof res === 'string' ? res : res.value;
  },
};

rl.on('line', async (data) => {
  const cmd = JSON.parse(data);
  const command = commands[cmd.type];
  const result = await command(...cmd.args);
  
  if (result) {
  	client.write(JSON.stringify(result));
	client.write('\n');
  }
});
";

    private readonly string _root;
    private readonly Channel<Func<StreamWriter, StreamReader, Task>> _channel;
    private readonly CancellationTokenSource _cts;

    public NodeJs(string root)
    {
        _root = root;
        _cts = new();
        _channel = Channel.CreateUnbounded<Func<StreamWriter, StreamReader, Task>>();

        // Start processing queued tasks
        Task.Run(ProcessQueueAsync, _cts.Token);
    }

    private Process Start(int port)
    {
        var startInfo = new ProcessStartInfo("node")
        {
            UseShellExecute = false,
            WorkingDirectory = _root,
            CreateNoWindow = true,
            Arguments = $"-e \"{command}\" {port}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
        };

        var process = Process.Start(startInfo)!;

        process.ErrorDataReceived += OnError;
        process.OutputDataReceived += OnOutput;
        
        process.BeginErrorReadLine();
        process.BeginOutputReadLine();
        return process;
    }

    private void OnError(object sender, DataReceivedEventArgs e)
    {
        Console.Error.WriteLine(e.Data);
    }

    private void OnOutput(object sender, DataReceivedEventArgs e)
    {
        Console.Out.WriteLine(e.Data);
    }

    private int calls = 0;

    public Task<JsonDocument> RunCommand(string command, params List<string> args)
    {
        var tcs = new TaskCompletionSource<JsonDocument>();

        async Task WorkItem(StreamWriter writer, StreamReader reader)
        {
            var cmd = new CommandDefinition
            {
                Type = command,
                Args = args,
            };

            var call = calls++;

            try
            {
                await writer.WriteLineAsync(JsonSerializer.Serialize(cmd, SourceGenerationContext.Default.CommandDefinition));
                await writer.FlushAsync();
                var message = await reader.ReadLineAsync();
                tcs.SetResult(JsonDocument.Parse(message!));
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        }

        _channel.Writer.TryWrite(WorkItem);
        return tcs.Task;
    }

    private async Task ProcessQueueAsync()
    {
        var port = 5000;
        var token = _cts.Token;
	    var utf8 = new UTF8Encoding(false);
	    var server = new TcpListener(IPAddress.Loopback, port);
        server.Start();

        var clientOpen = server.AcceptTcpClientAsync(token);
        var process = Start(port);
        var registration = token.Register(() => 
        {    
            process?.Kill();
            process?.Dispose();
        });
        using var client = await clientOpen;
	    using var stream = client.GetStream();

        await foreach (var workItem in _channel.Reader.ReadAllAsync(token))
        {
            using var writer = new StreamWriter(stream, utf8, -1, true);
            using var reader = new StreamReader(stream, utf8, false, -1, true);

            await workItem(writer, reader);
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}