using System;
using System.Threading;
using System.Threading.Tasks;
using GG2.Core;

const string protocolUuidString = "71eb5496-492b-b186-4770-06ccb30d3f8f";
const int lobbyHeartbeatSeconds = 30;
const int lobbyResolveSeconds = 600;
const double clientTimeoutSeconds = 5;
const double passwordTimeoutSeconds = 30;
const double passwordRetrySeconds = 2;
const ulong transientEventReplayTicks = 6;
const int autoBalanceDelaySeconds = 10;
const int autoBalanceNewPlayerGraceSeconds = 60;

var launchOptions = ServerLaunchOptions.Load(args);
launchOptions.Settings.Save(launchOptions.ResolvedConfigPath);

var config = new SimulationConfig
{
    TicksPerSecond = SimulationConfig.DefaultTicksPerSecond,
    EnableLocalDummies = false,
};

var server = new GameServer(
    config,
    launchOptions.Port,
    launchOptions.ServerName,
    launchOptions.ServerPassword,
    launchOptions.UseLobbyServer,
    launchOptions.LobbyHost,
    launchOptions.LobbyPort,
    protocolUuidString,
    lobbyHeartbeatSeconds,
    lobbyResolveSeconds,
    launchOptions.RequestedMap,
    launchOptions.MapRotationFile,
    launchOptions.StockMapRotation,
    launchOptions.MaxPlayableClients,
    launchOptions.MaxTotalClients,
    launchOptions.MaxSpectatorClients,
    autoBalanceDelaySeconds,
    autoBalanceNewPlayerGraceSeconds,
    launchOptions.AutoBalanceEnabled,
    launchOptions.TimeLimitMinutesOverride,
    launchOptions.CapLimitOverride,
    launchOptions.RespawnSecondsOverride,
    clientTimeoutSeconds,
    passwordTimeoutSeconds,
    passwordRetrySeconds,
    transientEventReplayTicks);

using var shutdownCts = new CancellationTokenSource();
ConsoleCancelEventHandler cancelHandler = (_, e) =>
{
    e.Cancel = true;
    if (!shutdownCts.IsCancellationRequested)
    {
        Console.WriteLine("[server] shutdown requested via Ctrl+C.");
        shutdownCts.Cancel();
    }
};
Console.CancelKeyPress += cancelHandler;
var shutdownCommandTask = Task.Run(() => ListenForShutdownCommands(shutdownCts));

try
{
    server.Run(shutdownCts.Token);
}
finally
{
    shutdownCts.Cancel();
    Console.CancelKeyPress -= cancelHandler;
    try
    {
        shutdownCommandTask.Wait(250);
    }
    catch (AggregateException)
    {
    }
}

return;

static void ListenForShutdownCommands(CancellationTokenSource shutdownCts)
{
    try
    {
        while (!shutdownCts.IsCancellationRequested)
        {
            var line = Console.ReadLine();
            if (line is null)
            {
                if (Console.IsInputRedirected && !shutdownCts.IsCancellationRequested)
                {
                    Console.WriteLine("[server] stdin closed; shutting down.");
                    shutdownCts.Cancel();
                }

                break;
            }

            var command = line.Trim();
            if (command.Length == 0)
            {
                continue;
            }

            if (string.Equals(command, "shutdown", StringComparison.OrdinalIgnoreCase)
                || string.Equals(command, "exit", StringComparison.OrdinalIgnoreCase)
                || string.Equals(command, "quit", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("[server] shutdown requested.");
                shutdownCts.Cancel();
                break;
            }

            Console.WriteLine($"[server] unknown command \"{command}\". Type shutdown to stop.");
        }
    }
    catch (InvalidOperationException)
    {
    }
}
