using System;
using System.IO;
using System.Linq;
using GG2.Core;
using Xunit;

namespace GG2.Server.Tests;

public sealed class ServerLaunchOptionsTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "gg2-server-launch-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Load_UsesSharedIniDefaults()
    {
        Directory.CreateDirectory(_tempDirectory);
        var configPath = Path.Combine(_tempDirectory, "gg2.ini");
        var preferences = CreatePreferences();
        preferences.Save(configPath);

        var options = ServerLaunchOptions.Load(["--config", configPath], ServerSettings.Load);

        Assert.Equal(configPath, options.ResolvedConfigPath);
        Assert.Equal(9001, options.Port);
        Assert.Equal("Config Server", options.ServerName);
        Assert.Equal("cfg-password", options.ServerPassword);
        Assert.False(options.UseLobbyServer);
        Assert.Equal("lobby.example", options.LobbyHost);
        Assert.Equal(32000, options.LobbyPort);
        Assert.Equal("cfg-rotation.txt", options.MapRotationFile);
        Assert.Null(options.RequestedMap);
        Assert.Equal(60, options.TickRate);
        Assert.False(options.AutoBalanceEnabled);
        Assert.Equal(22, options.TimeLimitMinutesOverride);
        Assert.Equal(7, options.CapLimitOverride);
        Assert.Equal(9, options.RespawnSecondsOverride);
        Assert.Equal(7, options.MaxPlayableClients);
        Assert.Equal(9, options.MaxTotalClients);
        Assert.Equal(2, options.MaxSpectatorClients);
        Assert.Equal(["Egypt", "Truefort"], options.StockMapRotation.Take(2).ToArray());
    }

    [Fact]
    public void Load_AppliesCliOverridesAfterIniDefaults()
    {
        Directory.CreateDirectory(_tempDirectory);
        var configPath = Path.Combine(_tempDirectory, "gg2.ini");
        CreatePreferences().Save(configPath);

        var options = ServerLaunchOptions.Load(
        [
            "--config", configPath,
            "--port", "7777",
            "--map", "ctf_orange",
            "--map-rotation", "override-rotation.txt",
            "--name", "CLI Server",
            "--password", "cli-password",
            "--slots", "12",
            "--tickrate", "90",
            "--lobby",
            "--lobby-host", "cli.example",
            "--lobby-port", "33000",
            "--no-auto-balance",
            "--time-limit", "18",
            "--cap-limit", "6",
            "--respawn-seconds", "4",
        ], ServerSettings.Load);

        Assert.Equal(7777, options.Port);
        Assert.Equal("CLI Server", options.ServerName);
        Assert.Equal("cli-password", options.ServerPassword);
        Assert.Equal("ctf_orange", options.RequestedMap);
        Assert.Equal("override-rotation.txt", options.MapRotationFile);
        Assert.Equal(90, options.TickRate);
        Assert.True(options.UseLobbyServer);
        Assert.Equal("cli.example", options.LobbyHost);
        Assert.Equal(33000, options.LobbyPort);
        Assert.False(options.AutoBalanceEnabled);
        Assert.Equal(18, options.TimeLimitMinutesOverride);
        Assert.Equal(6, options.CapLimitOverride);
        Assert.Equal(4, options.RespawnSecondsOverride);
        Assert.Equal(12, options.MaxPlayableClients);
        Assert.Equal(12, options.MaxTotalClients);
        Assert.Equal(12, options.MaxSpectatorClients);
        Assert.Equal(["Egypt", "Truefort"], options.StockMapRotation.Take(2).ToArray());
    }

    private static Gg2PreferencesDocument CreatePreferences()
    {
        var preferences = new Gg2PreferencesDocument
        {
            LobbyHost = "lobby.example",
            LobbyPort = 32000,
            MaxPlayableClients = 7,
            MaxTotalClients = 9,
            MaxSpectatorClients = 2,
            HostSettings = new Gg2HostSettings
            {
                ServerName = "Config Server",
                Port = 9001,
                Slots = 7,
                Password = "cfg-password",
                TimeLimitMinutes = 22,
                CapLimit = 7,
                RespawnSeconds = 9,
                TickRate = 60,
                LobbyAnnounceEnabled = false,
                AutoBalanceEnabled = false,
                DedicatedModeEnabled = true,
                MapRotationFile = "cfg-rotation.txt",
                StockMapRotation = Gg2StockMapCatalog.CreateDefaultEntries(),
            },
        };
        preferences.HostSettings.StockMapRotation.First(entry => entry.LevelName == "Egypt").Order = 1;
        preferences.HostSettings.StockMapRotation.First(entry => entry.LevelName == "Truefort").Order = 2;
        preferences.HostSettings.StockMapRotation.First(entry => entry.LevelName == "TwodFortTwo").Order = 0;
        return preferences;
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}
