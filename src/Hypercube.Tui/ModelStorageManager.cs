using System.Net.Http;
using Serilog;
using Spectre.Console;

namespace Hypercube.Tui;

internal static class ModelStorageManager
{
    private static readonly string UserProfileModelDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".hypercube",
        "models",
        "phi3.5-mini");

    private static readonly string TempModelDir = Path.Combine(
        Path.GetTempPath(),
        ".hypercube",
        "models",
        "phi3.5-mini");

    private static readonly string[] FilesToDownload =
    [
        "genai_config.json",
        "phi-3.5-mini-instruct-cpu-int4-awq-block-128-acc-level-4.onnx",
        "phi-3.5-mini-instruct-cpu-int4-awq-block-128-acc-level-4.onnx.data",
        "tokenizer.json",
        "tokenizer_config.json",
        "special_tokens_map.json",
        "config.json"
    ];

    private const string BaseUrl = "https://huggingface.co/microsoft/Phi-3.5-mini-instruct-onnx/resolve/main/cpu_and_mobile/cpu-int4-awq-block-128-acc-level-4/";

    public static string GetModelPathOrFallback()
    {
        var envPath = Environment.GetEnvironmentVariable("HYPERCUBE_ONNX_MODEL_PATH");
        if (!string.IsNullOrWhiteSpace(envPath) && Directory.Exists(envPath))
        {
            return envPath;
        }

        if (Directory.Exists(UserProfileModelDir))
        {
            return UserProfileModelDir;
        }

        return Directory.Exists(TempModelDir) ? TempModelDir : string.Empty;
    }

    public static async Task DownloadModelWeightsAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(UserProfileModelDir);

        using var client = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(30)
        };

        try
        {
            await AnsiConsole.Progress()
                .StartAsync(async ctx =>
                {
                    foreach (var fileName in FilesToDownload)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        Log.Information("Downloading model file {FileName}", fileName);
                        var task = ctx.AddTask($"[cyan]Downloading {fileName}[/]", maxValue: 100);
                        var destination = Path.Combine(UserProfileModelDir, fileName);

                        if (File.Exists(destination))
                        {
                            task.Description = $"[green]Already present {fileName}[/]";
                            task.Increment(task.MaxValue);
                            task.StopTask();
                            continue;
                        }

                        await using var stream = await client.GetStreamAsync(
                            BaseUrl + fileName,
                            cancellationToken);
                        await using var file = File.Create(destination);
                        var buffer = new byte[81920];

                        while (true)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            var read = await stream.ReadAsync(buffer, cancellationToken);
                            if (read == 0)
                            {
                                break;
                            }

                            await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                            task.Increment(1);
                        }

                        task.StopTask();
                    }
                });
        }
        catch (HttpRequestException ex)
        {
            var status = ex.StatusCode.HasValue ? $"{(int)ex.StatusCode.Value}" : "HTTP";
            Log.Warning(ex, "Model download failed with status {Status}", status);
            AnsiConsole.MarkupLine($"[red]Model download failed: {status} {ex.Message}[/]");
            AnsiConsole.MarkupLine("[grey]The CPU Phi-3.5 ONNX files are hosted under cpu_and_mobile/cpu-int4-awq-block-128-acc-level-4 on Hugging Face.[/]");
            return;
        }

        AnsiConsole.MarkupLine($"[green]Local LLM engine weights are available at {Markup.Escape(UserProfileModelDir)}.[/]");
        AnsiConsole.MarkupLine("[grey]Set HYPERCUBE_ONNX_MODEL_PATH to this directory, or rerun with the same local path detected by the TUI.[/]");
    }
}
