namespace NetPack.Commands;

using CommandLine;
using NetPack.Graph;

[Verb("analyze", HelpText = "Analyzes the generated bundles.")]
public class AnalyzeCommand : ICommand
{
    [Value(0, HelpText = "The entry point file where the bundler should start.")]
    public string? FilePath { get; set; }

    [Option("outfile", HelpText = "The optional file where the inspection data should be stored as a JSON.")]
    public string? OutFile { get; set; }

    [Option("port", Default = 8080, HelpText = "The port where the server should be running in case of --interactive.")]
    public int Port { get; set; } = 8080;

    [Option("interactive", Default = false, HelpText = "Indicates if a server should be started to inspect the analyzer data.")]
    public bool IsInteractive { get; set; } = false;

    public async Task Run()
    {
        //TODO
    }
}
