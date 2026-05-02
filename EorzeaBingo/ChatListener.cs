using System;
using System.Text.RegularExpressions;
using Dalamud.Game.Chat;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace EorzeaBingo;

/// <summary>
/// Subscribes to chat events and parses incoming messages, including Bingo claims and native /random output, 
/// in order to synchronise and update the game state. Utilises regular expressions to identify relevant commands 
/// and ensures that only authorised hosts can trigger global number drawings.
/// </summary>
public sealed class ChatListener
{
    private readonly BingoGameState _state;
    private readonly IChatGui _chatGui;
    private readonly IPluginLog _log;
    private readonly IClientState _clientState;
    private readonly IDalamudPluginInterface _pluginInterface;

    private static readonly Regex HostDrawRegex = new(@"(?:/s\s+|/p\s+|/a\s+)?The\s+next\s+number\s+is\.\.\.\s+(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex NativeRollRegex = new(@"(?:(?:Random!\s*)?(?<name>.+?)\s+rolls?\s+a\s+(?<roll>\d+)\s*\(out\s+of\s+70\))|(?:Random!\s*\(1-70\)\s*(?<roll>\d+))", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PlayerJoinedRegex = new(@"joins\s+room\s+([A-Z0-9]{4})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex BingoClaimRegex = new(@"(?:/s\s+|/p\s+|/a\s+)?BINGO!", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RoomCreatedRegex = new(@"(?:/s\s+|/p\s+|/a\s+)?Bingo\s+Room\s+([A-Z0-9]{4})\s+created!", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PlayerLeftRegex = new(@"(?:/s\s+|/p\s+|/a\s+)?has\s+left\s+the\s+Bingo\s+Room\.", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RoundStartedRegex = new(@"(?:/s\s+|/p\s+|/a\s+)?Round\s+(\d+)\s+started\s+in\s+room\s+([A-Z0-9]{4})!", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RoomEndedRegex = new(@"(?:/s\s+|/p\s+|/a\s+)?The\s+bingo\s+game\s+has\s+ended\.", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly Plugin _plugin;

    public ChatListener(
        Plugin plugin,
        BingoGameState state,
        IChatGui chatGui,
        IPluginLog log,
        IClientState clientState,
        IDalamudPluginInterface pluginInterface)
    {
        _plugin = plugin;
        _state = state;
        _chatGui = chatGui;
        _log = log;
        _clientState = clientState;
        _pluginInterface = pluginInterface;
    }

    public void Subscribe()
    {
        _chatGui.ChatMessage += OnChatMessage;
    }

    public void Unsubscribe()
    {
        _chatGui.ChatMessage -= OnChatMessage;
    }

    private void OnChatMessage(IHandleableChatMessage chatMsg)
    {
        var playerName = ExtractFullPlayerName(chatMsg.Sender);
        var messageText = chatMsg.Message.TextValue;
        if (string.IsNullOrEmpty(messageText))
            return;

        if (chatMsg.LogKind == XivChatType.SystemMessage)
        {
            var lowerText = messageText.ToLowerInvariant();

            if (lowerText.Contains("leave") || lowerText.Contains("left") || lowerText.Contains("dissolve") || lowerText.Contains("disband"))
            {
                if (_plugin.IsCwProxyValid)
                {
                    _plugin.IsAllianceRaidLobby = false;
                    _plugin.IsCwProxyValid = false;
                }
            }
            else if (lowerText.Contains("join") || lowerText.Contains("form"))
            {
                if (!_plugin.IsCwProxyValid)
                {
                    _plugin.IsCwProxyValid = true;
                }
            }
        }

        var hostDrawMatch = HostDrawRegex.Match(messageText);
        if (hostDrawMatch.Success && _state.IsInRoom)
        {
            if (!IsFromHost(chatMsg.Sender)) return;
            if (int.TryParse(hostDrawMatch.Groups[1].Value, out var num) && num >= 1 && num <= 70)
                ScheduleStateUpdate(() => _state.CallNumber(num));
            return;
        }

        var bingoClaimMatch = BingoClaimRegex.Match(messageText);
        if (bingoClaimMatch.Success && _state.IsHost)
        {
            if (!string.IsNullOrEmpty(playerName))
                ScheduleStateUpdate(() => _state.ClaimBingo(playerName));
            return;
        }

        var rollMatch = NativeRollRegex.Match(messageText);
        if (rollMatch.Success && _state.IsInRoom)
        {
            var rollerName = rollMatch.Groups["name"].Value.Trim();
            var rollNumStr = rollMatch.Groups["roll"].Value;

            if (string.IsNullOrEmpty(rollerName))
            {
                rollerName = playerName;
            }

            bool isHostRoll = false;
            var cleanRoller = BingoGameState.SanitiseName(rollerName);
            var cleanHost = BingoGameState.SanitiseName(_state.HostName);

            if (_state.IsHost)
            {
                if (rollerName.Equals("You", StringComparison.OrdinalIgnoreCase) || cleanRoller == cleanHost)
                    isHostRoll = true;
            }
            else
            {
                if (cleanRoller == cleanHost || (!string.IsNullOrEmpty(cleanRoller) && cleanHost.StartsWith(cleanRoller + "@")))
                    isHostRoll = true;
            }

            if (!isHostRoll)
                isHostRoll = IsFromHost(chatMsg.Sender);

            if (!isHostRoll) return;

            if (int.TryParse(rollNumStr, out var num) && num >= 1 && num <= 70)
                ScheduleStateUpdate(() => _state.CallNumber(num));
            return;
        }

        var joinedMatch = PlayerJoinedRegex.Match(messageText);
        if (joinedMatch.Success && _state.IsHost)
        {
            var roomCode = joinedMatch.Groups[1].Value.ToUpperInvariant();
            if (roomCode != _state.RoomCode) return;
            if (!string.IsNullOrEmpty(playerName))
            {
                if (_state.CalledNumbers.Count > 0)
                {
                    ScheduleStateUpdate(() =>
                    {
                        var prefix = _plugin.GetChatPrefix(_plugin.ActiveChatChannel);
                        _chatGui.Print($"Room {roomCode} is in progress. Wait for the next round.");
                    });
                }
                else
                {
                    ScheduleStateUpdate(() => _state.AddPlayerReplica(playerName));
                }
            }
            return;
        }

        var leftMatch = PlayerLeftRegex.Match(messageText);
        if (leftMatch.Success && _state.IsHost)
        {
            if (!string.IsNullOrEmpty(playerName))
                ScheduleStateUpdate(() => _state.RemovePlayerReplica(playerName));
            return;
        }

        var roundMatch = RoundStartedRegex.Match(messageText);
        if (roundMatch.Success && _state.IsInRoom && !_state.IsHost)
        {
            var roundNumStr = roundMatch.Groups[1].Value;
            var roomCode = roundMatch.Groups[2].Value.ToUpperInvariant();
            if (roomCode != _state.RoomCode) return;

            if (int.TryParse(roundNumStr, out var roundNum) && _state.RoundNumber != roundNum)
                ScheduleStateUpdate(() => _state.ForceSyncRound(roundNum));
            return;
        }

        var roomCreatedMatch = RoomCreatedRegex.Match(messageText);
        if (roomCreatedMatch.Success && !_state.IsInRoom)
        {
            var roomCode = roomCreatedMatch.Groups[1].Value.ToUpperInvariant();
            var hostName = playerName;

            var hostChannel = GetOutputChannelFromXivChatType(chatMsg.LogKind);

            if (!string.IsNullOrEmpty(roomCode))
            {
                _plugin.KnownRoomChannels[roomCode] = hostChannel;

                ScheduleStateUpdate(() => {
                    lock (_state) _state.DiscoveredRooms[roomCode] = hostName;
                });
            }
            return;
        }

        var endedMatch = RoomEndedRegex.Match(messageText);
        if (endedMatch.Success)
        {
            ScheduleStateUpdate(() =>
            {
                var cleanSender = BingoGameState.SanitiseName(playerName);
                if (_state.IsInRoom && !_state.IsHost && cleanSender == BingoGameState.SanitiseName(_state.HostName))
                {
                    _state.RoomFinishedLocally = true;
                }

                string? toRemove = null;
                lock (_state)
                {
                    foreach (var room in _state.DiscoveredRooms)
                    {
                        if (BingoGameState.SanitiseName(room.Value) == cleanSender)
                        {
                            toRemove = room.Key;
                            break;
                        }
                    }
                    if (toRemove != null)
                        _state.DiscoveredRooms.Remove(toRemove);
                }
            });
            return;
        }
    }

    private bool IsFromHost(SeString sender)
    {
        if (string.IsNullOrEmpty(_state.HostName)) return false;
        var senderName = ExtractFullPlayerName(sender);

        var cleanSender = BingoGameState.SanitiseName(senderName);
        var cleanHost = BingoGameState.SanitiseName(_state.HostName);

        if (string.IsNullOrEmpty(cleanSender)) return false;

        return cleanSender == cleanHost || cleanHost.StartsWith(cleanSender + "@");
    }

    private string ExtractFullPlayerName(SeString sender)
    {
        foreach (var payload in sender.Payloads)
        {
            if (payload is PlayerPayload playerPayload)
            {
                var name = playerPayload.PlayerName;
                var world = playerPayload.World.Value.Name.ToString();
                if (!string.IsNullOrEmpty(world))
                    return $"{name}@{world}";
                return name;
            }
        }
        return sender.TextValue.Trim();
    }

    private void ScheduleStateUpdate(Action action)
    {
        _pluginInterface.UiBuilder.Draw += DrawHandler;

        void DrawHandler()
        {
            _pluginInterface.UiBuilder.Draw -= DrawHandler;
            try
            {
                action();
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Bingo state update failed.");
            }
        }
    }

    private OutputChatChannel GetOutputChannelFromXivChatType(XivChatType type)
    {
        return type switch
        {
            XivChatType.Party => OutputChatChannel.Party,
            XivChatType.Alliance => OutputChatChannel.Alliance,
            _ => OutputChatChannel.Say 
        };
    }
}