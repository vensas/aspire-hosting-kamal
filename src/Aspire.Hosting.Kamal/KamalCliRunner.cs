#pragma warning disable ASPIREPIPELINES001

using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Aspire.Hosting.Pipelines;

namespace Aspire.Hosting.Kamal;

internal static class KamalCliRunner
{
    public static async Task DeployAsync(
        KamalEnvironmentResource environment,
        string outputPath,
        PipelineStepContext context)
    {
        var configDirectory = Path.Combine(outputPath, "config");
        if (!Directory.Exists(configDirectory))
        {
            throw new InvalidOperationException(
                $"No Kamal config directory found at {configDirectory}. Run 'aspire publish' first.");
        }

        var configFiles = Directory.EnumerateFiles(configDirectory, "deploy*.yml")
            .OrderBy(f => Path.GetFileName(f) == "deploy.yml" ? 0 : 1)
            .ThenBy(f => f, StringComparer.Ordinal)
            .ToList();

        if (configFiles.Count == 0)
        {
            throw new InvalidOperationException($"No deploy*.yml files found in {configDirectory}.");
        }

        foreach (var configFile in configFiles)
        {
            var relativeConfig = Path.GetRelativePath(outputPath, configFile).Replace('\\', '/');
            var deployTask = await context.ReportingStep.CreateTaskAsync(
                new MarkdownString($"Running **kamal deploy** for `{relativeConfig}`"),
                context.CancellationToken).ConfigureAwait(false);

            await using (deployTask.ConfigureAwait(false))
            {
                var (exitCode, output) = await RunAsync(
                    "kamal",
                    ["deploy", "-c", relativeConfig],
                    outputPath,
                    context.CancellationToken).ConfigureAwait(false);

                if (exitCode != 0)
                {
                    var tail = string.Join('\n', output.Split('\n').TakeLast(20));
                    await deployTask.CompleteAsync(
                        $"kamal deploy failed for {relativeConfig} (exit code {exitCode}).",
                        CompletionState.CompletedWithError,
                        context.CancellationToken).ConfigureAwait(false);
                    throw new InvalidOperationException(
                        $"'kamal deploy -c {relativeConfig}' failed with exit code {exitCode}. Last output:\n{tail}");
                }

                await deployTask.CompleteAsync(
                    new MarkdownString($"Deployed `{relativeConfig}` with Kamal."),
                    CompletionState.Completed,
                    context.CancellationToken).ConfigureAwait(false);

                context.Summary.Add("🚀 Kamal", relativeConfig);
            }
        }
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(
        string fileName,
        string[] arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        var output = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) { lock (output) { output.AppendLine(e.Data); } } };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) { lock (output) { output.AppendLine(e.Data); } } };

        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException(
                "The 'kamal' CLI was not found on PATH. Install it with 'gem install kamal' " +
                "or disable CLI deployment via DeployWithKamalCli = false and deploy manually from the publish output.", ex);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        lock (output)
        {
            return (process.ExitCode, output.ToString());
        }
    }
}
