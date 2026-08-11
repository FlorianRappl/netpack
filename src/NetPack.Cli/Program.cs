namespace NetPack;

using NetPack.Assets;
using NetPack.Commands;
using CommandLine;
using System.Diagnostics.CodeAnalysis;

static class Program
{
    // The core library ships no native asset processing; the CLI supplies the
    // SkiaSharp-based image processor (resize / re-encode) through the public
    // registry before any command runs.
    private static void RegisterAssetProcessors()
    {
        var image = new ImageAssetProcessor();

        foreach (var extension in new[] { ".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp", ".exif" })
        {
            AssetProcessorFactory.Register(extension, image);
        }
    }

    static int Run(ICommand command)
    {
        try
        {
            var task = command.Run();
            task.Wait();
            return 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex.Message);
            return 1;
        }
    }

    static int ShowError(IEnumerable<Error> errs)
    {
        if (errs.Where(err => err is not HelpVerbRequestedError && err is not VersionRequestedError).Any())
        {
            Log.Error("That did not work.");
            return 1;
        }

        return 0;
    }

    /// <summary>
    /// Peels off a global <c>--log-level</c> (value or <c>=</c> form) before the
    /// verb parser runs, applying it process-wide. It is handled here rather than
    /// per-command so it works for every verb without repetition.
    /// </summary>
    static string[] ApplyLogLevel(string[] args)
    {
        var remaining = new List<string>(args.Length);

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            string? value = null;

            if (arg == "--log-level" && i + 1 < args.Length)
            {
                value = args[++i];
            }
            else if (arg.StartsWith("--log-level=", StringComparison.Ordinal))
            {
                value = arg["--log-level=".Length..];
            }
            else
            {
                remaining.Add(arg);
                continue;
            }

            if (!Log.TryParse(value, out var level))
            {
                Console.Error.WriteLine($"[netpack] Unknown --log-level '{value}'. Use: {Log.Names}.");
            }

            Log.Apply(level);
        }

        return [.. remaining];
    }

    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(BundleCommand))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(GraphCommand))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(InspectCommand))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ServeCommand))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(AnalyzeCommand))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(PreviewCommand))]
    static void Main(string[] args)
    {
        RegisterAssetProcessors();

        // Global --log-level is consumed before anything else so all subsequent
        // output (including preset resolution errors) respects it.
        args = ApplyLogLevel(args);

        IReadOnlyList<string[]> allArgs;

        try
        {
            allArgs = PresetArgs.Apply(args);
        }
        catch (Exception ex)
        {
            Log.Error(ex.Message);
            Environment.ExitCode = 1;
            return;
        }

        var exitCode = 0;

        foreach (var variantArgs in allArgs)
        {
            var result = Parser.Default.ParseArguments<BundleCommand, GraphCommand, InspectCommand, ServeCommand, AnalyzeCommand, PreviewCommand>(variantArgs)
                .MapResult(
                    (BundleCommand opts) => Run(opts),
                    (GraphCommand opts) => Run(opts),
                    (InspectCommand opts) => Run(opts),
                    (ServeCommand opts) => Run(opts),
                    (AnalyzeCommand opts) => Run(opts),
                    (PreviewCommand opts) => Run(opts),
                    ShowError
                );

            if (result != 0)
            {
                exitCode = result;
            }
        }

        Environment.ExitCode = exitCode;
    }
}
