using Hypercube.AI.Onnx;
using Hypercube.Tui;
using Hypercube.Tui.Dashboard;
using Hypercube.Tui.Demo;
using Serilog;
using Spectre.Console;
using System.Globalization;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

Log.Logger = CreateLogger();

try
{
    return await RunAsync(args);
}
catch (Exception ex)
{
    Log.Fatal(ex, "Unhandled TUI exception");
    AnsiConsole.MarkupLine("[red]Unhandled exception. See logs/hypercube-tui-.log for details.[/]");
    return 1;
}
finally
{
    Log.CloseAndFlush();
}

static async Task<int> RunAsync(string[] args)
{
    Log.Information("Hypercube.Tui starting. Args={Args}", string.Join(' ', args));

    if (!AnsiConsole.Profile.Capabilities.Interactive)
    {
        Log.Warning("Hypercube.Tui requires an interactive terminal.");
        AnsiConsole.MarkupLine("[red]Hypercube.Tui requires an interactive terminal.[/]");
        return 1;
    }

    var downloadModels = false;
    var campaignCount = 10_000_000;

    for (var i = 0; i < args.Length; i++)
    {
        var arg = args[i].Trim();

        if (arg.Equals("--campaign", StringComparison.OrdinalIgnoreCase))
        {
            if (i + 1 >= args.Length)
            {
                continue;
            }

            if (!int.TryParse(args[++i], out var parsedCampaign) || parsedCampaign <= 0)
            {
                Log.Warning("Invalid --campaign argument.");
                AnsiConsole.MarkupLine("[red]--campaign requires a positive message count.[/]");
                return 1;
            }

            campaignCount = parsedCampaign;
            continue;
        }

        if (arg.Equals("--download-models", StringComparison.OrdinalIgnoreCase) ||
            arg.Equals("--setup-ai", StringComparison.OrdinalIgnoreCase))
        {
            downloadModels = true;
            continue;
        }

        Log.Warning("Unknown argument: {Argument}", arg);
        AnsiConsole.MarkupLine($"[red]Unknown argument: {Markup.Escape(arg)}[/]");
        AnsiConsole.MarkupLine("[grey]Usage: hypercube-tui [--campaign [count]] [--download-models] [--setup-ai][/]");
        return 1;
    }

    AnsiConsole.Write(new FigletText("Hypercube").Color(Color.Cyan1));
    AnsiConsole.MarkupLine("[grey]SMS campaign build demo — deterministic synthetic stream[/]");
    AnsiConsole.MarkupLine("[grey]Press [bold]Ctrl+C[/] to cancel. Alerts: [bold]↑/↓[/] scroll, Home/End jump.[/]");
    AnsiConsole.WriteLine();

    if (downloadModels)
    {
        Log.Information("Starting AI model setup.");
        await ModelStorageManager.DownloadModelWeightsAsync();
        return 0;
    }

    using var cts = new CancellationTokenSource();
    using var onnxGenerator = TryCreateOnnxGenerator();
    Console.CancelKeyPress += (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        cts.Cancel();
    };

    var subject = new DemoSubject("sender-demo", "Vodacom", "Standard", "sms", "ZA", "100k+");
    using var dashboard = new CampaignBuildDashboard(subject, onnxGenerator);
    await dashboard.RunAsync(campaignCount, cts.Token);

    return 0;
}

static OnnxTextGenerator? TryCreateOnnxGenerator()
{
    var modelPath = ModelStorageManager.GetModelPathOrFallback();
    if (string.IsNullOrWhiteSpace(modelPath))
    {
        Log.Information("ONNX model path not found. Falling back to deterministic AI.");
        return null;
    }

    try
    {
        Log.Information("Loading ONNX model from {ModelPath}", modelPath);
        return new OnnxTextGenerator(modelPath);
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "Failed to load ONNX model from {ModelPath}. Falling back to deterministic AI.", modelPath);
        return null;
    }
}

static ILogger CreateLogger()
{
    Directory.CreateDirectory("logs");
    return new LoggerConfiguration()
        .MinimumLevel.Debug()
        .WriteTo.File("logs/hypercube-tui-.log", rollingInterval: RollingInterval.Day)
        .CreateLogger();
}
