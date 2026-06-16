using Hypercube.Tui.Dashboard;
using Spectre.Console;

if (!AnsiConsole.Profile.Capabilities.Interactive)
{
    AnsiConsole.MarkupLine("[red]Hypercube.Tui requires an interactive terminal.[/]");
    return 1;
}

AnsiConsole.Write(new FigletText("Hypercube").Color(Color.Cyan1));
AnsiConsole.MarkupLine("[grey]Live rollup dashboard — synthetic stream demo[/]");
AnsiConsole.MarkupLine("[grey]Press [bold]Ctrl+C[/] or wait for auto-stop. Alerts: [bold]↑/↓[/] scroll, Home/End jump.[/]");
AnsiConsole.WriteLine();

var refreshMs = 500;
if (args.Length > 0 && int.TryParse(args[0], out var parsed) && parsed > 0)
{
    refreshMs = parsed;
}

var dashboard = new LiveRollupDashboard();
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cts.Cancel();
};

dashboard.Run(
    refreshInterval: TimeSpan.FromMilliseconds(refreshMs),
    duration: TimeSpan.FromMinutes(2),
    cancellationToken: cts.Token);

if (!cts.Token.IsCancellationRequested)
{
    AnsiConsole.MarkupLine("[grey]Demo complete. Press any key to exit.[/]");
    Console.ReadKey(intercept: true);
}

return 0;
