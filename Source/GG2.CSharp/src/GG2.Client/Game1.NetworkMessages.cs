#nullable enable

using System;
using GG2.Core;
using GG2.Protocol;

namespace GG2.Client;

public partial class Game1
{
    private void ProcessNetworkMessages()
    {
        foreach (var message in _networkClient.ReceiveMessages())
        {
            switch (message)
            {
                case WelcomeMessage welcome:
                    if (welcome.Version != ProtocolVersion.Current)
                    {
                        AddConsoleLine($"protocol mismatch: server={welcome.Version} client={ProtocolVersion.Current}");
                        _networkClient.Disconnect();
                        _menuStatusMessage = "Protocol mismatch.";
                        break;
                    }

                    _networkClient.SetLocalPlayerSlot(welcome.PlayerSlot);
                    _networkClient.ClearPendingTeamSelection();
                    _networkClient.ClearPendingClassSelection();
                    ResetClientTimingState();
                    _world.TryLoadLevel(welcome.LevelName);
                    _pendingHostedConnectTicks = -1;
                    _lastAppliedSnapshotFrame = 0;
                    _hasReceivedSnapshot = false;
                    _lastSnapshotReceivedTimeSeconds = -1d;
                    _latestSnapshotServerTimeSeconds = -1d;
                    _latestSnapshotReceivedClockSeconds = -1d;
                    _smoothedSnapshotIntervalSeconds = 1f / SimulationConfig.DefaultTicksPerSecond;
                    _smoothedSnapshotJitterSeconds = 0f;
                    _remotePlayerInterpolationBackTimeSeconds = RemotePlayerMinimumInterpolationBackTimeSeconds;
                    ResetSnapshotStateHistory();
                    _localPlayerSnapshotEntityId = null;
                    _consoleOpen = false;
                    _mainMenuOpen = false;
                    _manualConnectOpen = false;
                    _optionsMenuOpen = false;
                    _optionsMenuOpenedFromGameplay = false;
                    _inGameMenuOpen = false;
                    _controlsMenuOpen = false;
                    _controlsMenuOpenedFromGameplay = false;
                    _pendingControlsBinding = null;
                    _teamSelectOpen = !_networkClient.IsSpectator;
                    _classSelectOpen = false;
                    _menuStatusMessage = _networkClient.IsSpectator ? "Connected as spectator." : string.Empty;
                    StopMenuMusic();
                    AddConsoleLine(
                        _networkClient.IsSpectator
                            ? $"connected to {welcome.ServerName} ({welcome.LevelName}) as spectator tickrate={welcome.TickRate}"
                            : $"connected to {welcome.ServerName} ({welcome.LevelName}) tickrate={welcome.TickRate}");
                    break;
                case ConnectionDeniedMessage denied:
                    ReturnToMainMenu(denied.Reason);
                    AddConsoleLine($"connect denied: {denied.Reason}");
                    break;
                case PasswordRequestMessage:
                    _passwordPromptOpen = true;
                    _passwordEditBuffer = string.Empty;
                    _passwordPromptMessage = "Server requires a password.";
                    _consoleOpen = false;
                    _inGameMenuOpen = false;
                    _optionsMenuOpen = false;
                    _controlsMenuOpen = false;
                    _teamSelectOpen = false;
                    _classSelectOpen = false;
                    AddConsoleLine("server requires a password");
                    break;
                case PasswordResultMessage passwordResult:
                    if (passwordResult.Accepted)
                    {
                        _passwordPromptOpen = false;
                        _passwordEditBuffer = string.Empty;
                        _passwordPromptMessage = string.Empty;
                        if (!_networkClient.IsSpectator)
                        {
                            _teamSelectOpen = true;
                        }
                        AddConsoleLine("password accepted");
                        break;
                    }

                    ReturnToMainMenu(string.IsNullOrWhiteSpace(passwordResult.Reason) ? "Password rejected." : passwordResult.Reason);
                    AddConsoleLine($"password rejected: {passwordResult.Reason}");
                    break;
                case ChatRelayMessage chatRelay:
                    AppendChatLine(chatRelay.PlayerName, chatRelay.Text, chatRelay.Team);
                    break;
                case AutoBalanceNoticeMessage notice:
                    if (notice.Kind == AutoBalanceNoticeKind.Pending)
                    {
                        var delaySeconds = Math.Max(1, notice.DelaySeconds);
                        var fromLabel = GetTeamLabel(notice.FromTeam);
                        var toLabel = GetTeamLabel(notice.ToTeam);
                        var label = fromLabel == "??" || toLabel == "??"
                            ? $"Auto-balance in {delaySeconds}s."
                            : $"Auto-balance in {delaySeconds}s (moving {fromLabel} to {toLabel}).";
                        ShowAutoBalanceNotice(label, delaySeconds);
                        AddConsoleLine(label);
                    }
                    else
                    {
                        var toLabel = GetTeamLabel(notice.ToTeam);
                        var label = string.IsNullOrWhiteSpace(notice.PlayerName)
                            ? "Auto-balance applied."
                            : toLabel == "??"
                                ? $"Auto-balance: {notice.PlayerName} moved."
                                : $"Auto-balance: {notice.PlayerName} moved to {toLabel}.";
                        ShowAutoBalanceNotice(label, 6);
                        AddConsoleLine(label);
                    }
                    break;
                case SessionSlotChangedMessage slotChanged:
                    var wasSpectator = _networkClient.IsSpectator;
                    _networkClient.SetLocalPlayerSlot(slotChanged.PlayerSlot);
                    if (_networkClient.IsSpectator)
                    {
                        _teamSelectOpen = false;
                        _classSelectOpen = false;
                        _menuStatusMessage = "Connected as spectator.";
                    }
                    else if (wasSpectator)
                    {
                        _teamSelectOpen = false;
                        _classSelectOpen = true;
                        _menuStatusMessage = string.Empty;
                    }

                    AddConsoleLine($"session slot changed to {_networkClient.LocalPlayerSlot}");
                    break;
                case ControlAckMessage ack:
                    _networkClient.AcknowledgeControlCommand(ack.Sequence, ack.Kind);
                    if (!ack.Accepted)
                    {
                        var description = ack.Kind switch
                        {
                            ControlCommandKind.SelectTeam => "team selection rejected",
                            ControlCommandKind.SelectClass => "class selection rejected",
                            ControlCommandKind.Spectate => "spectate request rejected",
                            _ => "control command rejected",
                        };
                        if (ack.Kind == ControlCommandKind.SelectTeam && _networkClient.IsSpectator)
                        {
                            _teamSelectOpen = true;
                            _classSelectOpen = false;
                        }

                        _menuStatusMessage = description;
                        AddConsoleLine(description);
                    }

                    break;
                case SnapshotMessage snapshot:
                    if (snapshot.Frame <= _lastAppliedSnapshotFrame)
                    {
                        break;
                    }

                    SnapshotMessage? baselineSnapshot = null;
                    if (snapshot.IsDelta && snapshot.BaselineFrame != 0
                        && !TryGetSnapshotState(snapshot.BaselineFrame, out baselineSnapshot))
                    {
                        AddConsoleLine($"snapshot {snapshot.Frame} missing baseline {snapshot.BaselineFrame}");
                        break;
                    }

                    SnapshotMessage resolvedSnapshot;
                    try
                    {
                        resolvedSnapshot = SnapshotDelta.ToFullSnapshot(snapshot, baselineSnapshot);
                    }
                    catch (InvalidOperationException ex)
                    {
                        AddConsoleLine($"snapshot {snapshot.Frame} rejected: {ex.Message}");
                        break;
                    }

                    var localSnapshotPlayer = resolvedSnapshot.Players.FirstOrDefault(player => player.Slot == _networkClient.LocalPlayerSlot);
                    _localPlayerSnapshotEntityId = localSnapshotPlayer?.PlayerId;

                    _lastAppliedSnapshotFrame = resolvedSnapshot.Frame;
                    if (!_world.ApplySnapshot(resolvedSnapshot, _networkClient.LocalPlayerSlot))
                    {
                        AddConsoleLine($"snapshot rejected for slot {_networkClient.LocalPlayerSlot}");
                        break;
                    }

                    RememberSnapshotState(resolvedSnapshot);
                    _networkClient.AcknowledgeSnapshot(resolvedSnapshot.Frame);
                    _pendingClassSelectTeam = null;
                    for (var visualIndex = 0; visualIndex < resolvedSnapshot.VisualEvents.Count; visualIndex += 1)
                    {
                        var visualEvent = resolvedSnapshot.VisualEvents[visualIndex];
                        if (!ShouldProcessNetworkEvent(visualEvent.EventId, _processedNetworkVisualEventIds, _processedNetworkVisualEventOrder))
                        {
                            continue;
                        }

                        _pendingNetworkVisualEvents.Add(visualEvent);
                    }
                    CaptureRemoteInterpolationTargets(resolvedSnapshot.Frame, resolvedSnapshot.TickRate);
                    ReconcileLocalPrediction(resolvedSnapshot.LastProcessedInputSequence);
                    break;
            }
        }

        if (_networkClient.TryConsumeDisconnectReason(out var disconnectReason))
        {
            ReturnToMainMenu(disconnectReason);
            AddConsoleLine($"network disconnected: {disconnectReason}");
        }
    }

    private static string GetTeamLabel(byte team)
    {
        return team switch
        {
            (byte)PlayerTeam.Red => "RED",
            (byte)PlayerTeam.Blue => "BLU",
            _ => "??",
        };
    }
}
