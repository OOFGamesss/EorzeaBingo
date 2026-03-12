using System;
using System.Linq;
using System.Numerics;
using Dalamud.Interface.Utility;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;

namespace EorzeaBingo.Windows;

public partial class MainWindow
{
    private void DrawHostGrid(BingoGameState state)
    {
        var availX = ImGui.GetContentRegionAvail().X;
        var scale = ImGuiHelpers.GlobalScale;

        // Let's fit 10 numbers per row to save space for the host
        var columns = 10;
        var dynamicCellSize = Math.Max(MinCellSize * 0.6f * scale, (availX - (ImGui.GetStyle().ItemSpacing.X * (columns - 1))) / (float)columns);
        var cellSize = new Vector2(dynamicCellSize, dynamicCellSize);

        if (!ImGui.BeginTable("HostGrid", columns, ImGuiTableFlags.Borders | ImGuiTableFlags.SizingStretchSame))
            return;

        for (var i = 1; i <= 70; i++)
        {
            if ((i - 1) % columns == 0)
                ImGui.TableNextRow();

            ImGui.TableNextColumn();
            var isCalled = state.CalledNumbers.Contains(i);

            if (isCalled)
                ImGui.PushStyleColor(ImGuiCol.Button, CalledColor);

            ImGui.Button(i.ToString(), new Vector2(-1, cellSize.Y));

            if (isCalled)
                ImGui.PopStyleColor();
        }

        ImGui.EndTable();
    }

    private void DrawPlayersTab(BingoGameState state)
    {
        var players = state.HostReplicas.Keys
            .Select(state.GetPlayerProgress)
            .OrderByDescending(p => p.ClaimState != ClaimState.None)
            .ThenByDescending(p => p.MaxLineSize)
            .ToList();

        if (players.Count == 0)
        {
            ImGui.Text("No players have joined yet.");
            return;
        }

        if (ImGui.BeginTable("PlayersTable", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
        {
            ImGui.TableSetupColumn("Player", ImGuiTableColumnFlags.WidthStretch, 0.4f);
            ImGui.TableSetupColumn("Best", ImGuiTableColumnFlags.WidthStretch, 0.2f);
            ImGui.TableSetupColumn("Bingo?", ImGuiTableColumnFlags.WidthStretch, 0.2f);
            ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthStretch, 0.2f);
            ImGui.TableHeadersRow();

            foreach (var p in players)
            {
                var isAmber = p.ClaimState == ClaimState.Rejected || (p.ClaimState == ClaimState.Pending && p.MaxLineSize < 5);
                var isGreen = p.ClaimState == ClaimState.Approved || (p.ClaimState == ClaimState.Pending && p.MaxLineSize >= 5);

                if (isAmber)
                    ImGui.PushStyleColor(ImGuiCol.Text, 0xFF00A5FF); 
                else if (isGreen)
                    ImGui.PushStyleColor(ImGuiCol.Text, CalledColor);

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text(p.PlayerName);

                ImGui.TableNextColumn();
                ImGui.Text($"{p.MaxLineSize}/5");

                ImGui.TableNextColumn();
                ImGui.Text(p.ClaimState.ToString());

                if (isAmber || isGreen)
                    ImGui.PopStyleColor();

                ImGui.TableNextColumn();

                if (ImGui.Button($"View##{p.PlayerName}", new Vector2(-1, 0)))
                {
                    _viewModalOpen = true;
                    ImGui.OpenPopup($"ViewCard_{p.PlayerName}");
                }

                if (ImGui.BeginPopupModal($"ViewCard_{p.PlayerName}", ref _viewModalOpen, ImGuiWindowFlags.AlwaysAutoResize))
                {
                    DrawPlayerCardModal(state, p.PlayerName);
                    if (ImGui.Button("Close")) ImGui.CloseCurrentPopup();
                    ImGui.EndPopup();
                }

                if (p.ClaimState == ClaimState.Pending)
                {
                    var isCooldown = _plugin.IsChatOnCooldown;
                    if (isCooldown) ImGui.BeginDisabled();

                    var approveTxt = isCooldown ? $"Valid Claim ({_plugin.ChatCooldownSeconds})" : "Valid Claim";
                    if (ImGui.Button($"{approveTxt}##Approve_{p.PlayerName}", new Vector2(-1, 0)))
                    {
                        var prefix = _plugin.GetChatPrefix(_plugin.ActiveChatChannel);
                        var validWinners = state.HostReplicas.Keys
                            .Select(state.GetPlayerProgress)
                            .Where(x => x.ClaimState == ClaimState.Pending && x.MaxLineSize >= 5)
                            .Select(x => x.PlayerName)
                            .ToList();

                        if (!validWinners.Contains(p.PlayerName))
                            validWinners.Add(p.PlayerName);

                        var names = validWinners.Count > 1
                            ? string.Join(", ", validWinners.Take(validWinners.Count - 1)).Split(" ")[0] + " and " + validWinners.Last().Split(" ")[0]
                            : validWinners[0].Split(" ")[0];

                        Plugin.SendChatMessage($"{prefix} {names} has won!");

                        foreach (var winner in validWinners)
                            state.ApproveClaim(winner);

                        _plugin.LastChatActionTime = DateTime.UtcNow;
                    }

                    var rejectTxt = isCooldown ? $"Invalid Claim ({_plugin.ChatCooldownSeconds})" : "Invalid Claim";
                    if (ImGui.Button($"{rejectTxt}##Reject_{p.PlayerName}", new Vector2(-1, 0)))
                    {
                        var prefix = _plugin.GetChatPrefix(_plugin.ActiveChatChannel);
                        Plugin.SendChatMessage($"{prefix} Invalid Bingo claim! We continue on!");
                        state.RejectClaim(p.PlayerName);
                        _plugin.LastChatActionTime = DateTime.UtcNow;
                    }

                    if (isCooldown) ImGui.EndDisabled();
                }
            }
            ImGui.EndTable();
        }
    }

    private bool _viewModalOpen = true;

    private void DrawPlayerCardModal(BingoGameState state, string playerName)
    {
        if (!state.HostReplicas.TryGetValue(playerName, out var board))
        {
            ImGui.Text("Could not load board data.");
            return;
        }

        var scale = ImGuiHelpers.GlobalScale;
        var cellSize = new Vector2(MinCellSize * 0.8f * scale, MinCellSize * 0.8f * scale);

        if (ImGui.BeginTable($"ModalGrid_{playerName}", 5, ImGuiTableFlags.Borders | ImGuiTableFlags.SizingFixedFit))
        {
            var headers = new[] { "B", "I", "N", "G", "O" };
            ImGui.TableNextRow();
            for (var col = 0; col < 5; col++)
            {
                var colColor = col switch
                {
                    0 => 0xFF4A4ADD, 
                    1 => 0xFF4A90E2, 
                    2 => 0xFF4AD24A, 
                    3 => 0xFFD24A4A, 
                    4 => 0xFFC04AD2, 
                    _ => FreeSpaceColor
                };

                ImGui.TableNextColumn();
                ImGui.SetWindowFontScale(1.5f);
                ImGui.PushStyleColor(ImGuiCol.Button, colColor);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, colColor);
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, colColor);
                ImGui.PushStyleColor(ImGuiCol.Text, 0xFFFFFFFF);
                ImGui.Button(headers[col], cellSize);
                ImGui.PopStyleColor(4);
                ImGui.SetWindowFontScale(1.0f);
            }
            for (var row = 0; row < 5; row++)
            {
                ImGui.TableNextRow();
                for (var col = 0; col < 5; col++)
                {
                    ImGui.TableNextColumn();
                    var value = board[row, col];
                    var label = value == 0 ? "FREE" : value.ToString();
                    var isCalled = state.CalledNumbers.Contains(value) || value == 0;

                    if (isCalled) ImGui.PushStyleColor(ImGuiCol.Button, CalledColor);
                    ImGui.Button(label, cellSize);
                    if (isCalled) ImGui.PopStyleColor();
                }
            }
            ImGui.EndTable();
        }
    }

    private void DrawHostPanel(BingoGameState state)
    {
        ImGui.Spacing();
        if (!ImGui.CollapsingHeader("Host controls", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        var isCooldown = _plugin.IsChatOnCooldown;
        var isLocked = state.IsDrawLocked;
        if (isCooldown || isLocked) ImGui.BeginDisabled();

        var announceText = isCooldown ? $"Announce Game ({_plugin.ChatCooldownSeconds})" : "Announce Game";
        if (ImGui.Button(announceText))
        {
            var prefix = _plugin.GetChatPrefix(_plugin.ActiveChatChannel);

            Plugin.SendChatMessage($"{prefix} Bingo Room {state.RoomCode} created!");

            _plugin.LastChatActionTime = DateTime.UtcNow;
        }

        var rollText = isCooldown ? $"Roll /random 70 ({_plugin.ChatCooldownSeconds})" : "Roll /random 70";
        if (ImGui.Button(rollText))
        {
            var prefix = _plugin.GetChatPrefix(_plugin.ActiveChatChannel);

            // Map the prefix directly to the correct FFXIV dice command
            string rollCommand = prefix switch
            {
                "/alliance" => "/dice al 70",
                "/p" => "/dice party 70",
                _ => "/random 70" // Fallback for Say (/s) or any other unhandled state
            };

            Plugin.SendChatMessage(rollCommand);

            _plugin.LastChatActionTime = DateTime.UtcNow;
        }

        ImGui.SameLine();
        var smartDrawText = isCooldown ? $"Smart Draw ({_plugin.ChatCooldownSeconds})" : "Smart Draw";
        if (ImGui.Button(smartDrawText))
        {
            var uncalled = state.GetUncalledNumbers().ToList();
            var prefix = _plugin.GetChatPrefix(_plugin.ActiveChatChannel);
            if (uncalled.Count == 0)
            {
                Plugin.SendChatMessage($"{prefix} All numbers have been called.");
            }
            else
            {
                var n = uncalled[new Random().Next(uncalled.Count)];
                state.CallNumber(n);

                var lingo = _plugin.ActiveEnableBingoLingo ? BingoLingo.GetPhrase(n, state.HostName ?? "Host") : "";
                var msg = string.IsNullOrEmpty(lingo)
                    ? $"{prefix} The next number is... {n}."
                    : $"{prefix} The next number is... {n}. {lingo}!";

                Plugin.SendChatMessage(msg);
            }
            _plugin.LastChatActionTime = DateTime.UtcNow;
        }

        if (isCooldown || isLocked) ImGui.EndDisabled();
        if (isCooldown) ImGui.BeginDisabled();
        var nrText = isCooldown ? $"Next Round ({_plugin.ChatCooldownSeconds})" : "Next Round";
        if (ImGui.Button(nrText))
        {
            state.NextRound();
            var prefix = _plugin.GetChatPrefix(_plugin.ActiveChatChannel);
            Plugin.SendChatMessage($"{prefix} Round {state.RoundNumber} started in room {state.RoomCode}!");
            _plugin.LastChatActionTime = DateTime.UtcNow;
        }
        if (isCooldown) ImGui.EndDisabled();

        ImGui.SameLine();
        if (isCooldown) ImGui.BeginDisabled();
        var egText = isCooldown ? $"End Game ({_plugin.ChatCooldownSeconds})" : "End Game";
        if (ImGui.Button(egText))
        {
            var prefix = _plugin.GetChatPrefix(_plugin.ActiveChatChannel);
            Plugin.SendChatMessage($"{prefix} The bingo game has ended.");
            state.LeaveRoom();
            _plugin.LastChatActionTime = DateTime.UtcNow;
        }
        if (isCooldown) ImGui.EndDisabled();

        ImGui.Spacing();
        ImGui.Text("Bingo Output Channel:");
        var channelNames = Enum.GetNames<OutputChatChannel>();
        var currentChannelIndex = (int)_plugin.ActiveChatChannel;
        ImGui.SetNextItemWidth(150);
        if (ImGui.Combo("##ChatChannel", ref currentChannelIndex, channelNames, channelNames.Length))
        {
            _plugin.ActiveChatChannel = (OutputChatChannel)currentChannelIndex;
        }

        ImGui.SameLine();
        var includeLingo = _plugin.ActiveEnableBingoLingo;
        if (ImGui.Checkbox("Include Bingo Lingo", ref includeLingo))
        {
            _plugin.ActiveEnableBingoLingo = includeLingo;
        }
    }
}
