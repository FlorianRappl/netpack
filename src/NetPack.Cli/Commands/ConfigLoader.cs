namespace NetPack.Commands;

using NetPack.Graph;

/// <summary>
/// Loads netpack configuration from a config file (JS, MJS, or TS).
/// The config file must export a default object or return one.
/// </summary>
public static class ConfigLoader
{
    private static readonly string[] ConfigFileNames = ["netpack.config.js", "netpack.config.mjs", "netpack.config.ts"];

    /// <summary>
    /// Finds and loads the config file from the given directory.
    /// Returns null if no config file exists.
    /// </summary>
    public static NetpackConfig? LoadFromDirectory(string directory)
    {
        foreach (var name in ConfigFileNames)
        {
            var path = Path.Combine(directory, name);

            if (File.Exists(path))
            {
                return Load(path);
            }
        }

        return null;
    }

    /// <summary>
    /// Loads a netpack config from the specified file path.
    /// </summary>
    public static NetpackConfig Load(string configPath)
    {
        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException($"Config file not found: {configPath}");
        }

        var ext = Path.GetExtension(configPath).ToLowerInvariant();

        return ext switch
        {
            ".js" or ".mjs" => LoadJsConfig(configPath),
            ".ts" => LoadTsConfig(configPath),
            _ => throw new InvalidOperationException($"Unsupported config file extension: {ext}. Use .js, .mjs, or .ts.")
        };
    }

    private static NetpackConfig LoadJsConfig(string configPath)
    {
        var json = EvalConfigWithNode(configPath);
        return NetpackConfig.ParseJson(json);
    }

    private static NetpackConfig LoadTsConfig(string configPath)
    {
        var json = EvalConfigWithNode(configPath, useTsx: true);
        return NetpackConfig.ParseJson(json);
    }

    private static string EvalConfigWithNode(string configPath, bool useTsx = false)
    {
        if (useTsx)
        {
            return EvalConfigWithNodeTsx(configPath);
        }

        var configFile = configPath.Replace("\\", "/");
        var startInfo = new System.Diagnostics.ProcessStartInfo("node")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            Arguments = $"-e \"const m = require('{configFile}'); const c = m.default || m; process.stdout.write(JSON.stringify(c));\"",
            WorkingDirectory = Path.GetDirectoryName(configPath),
        };

        using var process = System.Diagnostics.Process.Start(startInfo)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Failed to load config file {configPath}:\n{stderr}");
        }

        return stdout;
    }

    private static string EvalConfigWithNodeTsx(string configPath)
    {
        var tempFile = Path.GetTempFileName() + ".mjs";
        var configFile = configPath.Replace("\\", "/").Replace("'", "\\'");

        try
        {
            File.WriteAllText(tempFile, $"import config from '{configFile}'; process.stdout.write(JSON.stringify(config));");

            var startInfo = new System.Diagnostics.ProcessStartInfo("tsx")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                Arguments = $"\"{tempFile}\"",
                WorkingDirectory = Path.GetDirectoryName(configPath),
            };

            using var process = System.Diagnostics.Process.Start(startInfo)!;
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"Failed to load config file {configPath} with tsx:\n{stderr}");
            }

            return stdout;
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
