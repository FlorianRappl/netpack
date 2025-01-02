namespace NetPack;

using NetPack.Commands;
using CommandLine;
using System.Diagnostics.CodeAnalysis;

static class Program
{
    static int Run(ICommand command)
    {
        try
        {
            var task = command.Run();
            task.Wait();
            return 0;
        }
        catch
        {
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
    static void Main(string[] args)
    {
        Parser.Default.ParseArguments<BundleCommand, GraphCommand, InspectCommand>(args)
            .MapResult(
                (BundleCommand opts) => Run(opts),
                (GraphCommand opts) => Run(opts),
                (InspectCommand opts) => Run(opts),
                ShowError
            );
    }
}
