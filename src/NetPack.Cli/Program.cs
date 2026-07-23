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
            Console.WriteLine(ex.Message);
            return 1;
        }
    }

    static int ShowError(IEnumerable<Error> errs)
    {
        if (errs.Where(err => err is not HelpVerbRequestedError && err is not VersionRequestedError).Any())
        {
            Console.WriteLine("That did not work.");
            return 1;
        }

        return 0;
    }

    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(BundleCommand))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(GraphCommand))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(InspectCommand))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ServeCommand))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(AnalyzeCommand))]
    static void Main(string[] args)
    {
        RegisterAssetProcessors();

        Parser.Default.ParseArguments<BundleCommand, GraphCommand, InspectCommand, ServeCommand, AnalyzeCommand>(args)
            .MapResult(
                (BundleCommand opts) => Run(opts),
                (GraphCommand opts) => Run(opts),
                (InspectCommand opts) => Run(opts),
                (ServeCommand opts) => Run(opts),
                (AnalyzeCommand opts) => Run(opts),
                ShowError
            );
    }
}
