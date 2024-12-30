namespace NetPack.Commands;

using CommandLine;
using NetPack.Html;
using NetPack.Json;
using NetPack.Sass;
using NetPack.TypeScript;

[Verb("tokenize", HelpText = "Analyzes the provided file.")]
public class TokenizeCommand : ICommand
{
    private static readonly string[] htmlExtensions = [".htm", ".html"];
    private static readonly string[] tsExtensions = [".ts", ".tsx", ".js", ".jsx", ".mjs", ".cjs", ".mts", ".cts"];
    private static readonly string[] sassExtensions = [".scss", ".sass", ".css"];
    private static readonly string[] jsonExtensions = [".json"];

    [Value(0)]
    public string? FilePath { get; set; }

    public Task Run()
    {
        var file = Path.Combine(Environment.CurrentDirectory, FilePath!);
        var code = File.ReadAllText(file);
        var ext = Path.GetExtension(file);
            
        if (tsExtensions.Contains(ext))
        {
            var tokenizer = new TypeScriptTokenizer(code);

            while (tokenizer.IsActive)
            {
                var token = tokenizer.NextToken();
                Console.WriteLine(token.ToString());
            }
        }
        else if (sassExtensions.Contains(ext))
        {
            var tokenizer = new SassTokenizer(code);

            while (tokenizer.IsActive)
            {
                var token = tokenizer.NextToken();
                Console.WriteLine(token.ToString());
            }
        }
        else if (jsonExtensions.Contains(ext))
        {
            var tokenizer = new JsonTokenizer(code);

            while (tokenizer.IsActive)
            {
                var token = tokenizer.NextToken();
                Console.WriteLine(token.ToString());
            }
        }
        else if (htmlExtensions.Contains(ext))
        {
            var tokenizer = new HtmlTokenizer(code);

            while (tokenizer.IsActive)
            {
                var token = tokenizer.NextToken();
                Console.WriteLine(token.ToString());
            }
        }
        else
        {
            // we don't know ...
            throw new Exception("Unknown file extension.");
        }

        return Task.CompletedTask;
    }
}
