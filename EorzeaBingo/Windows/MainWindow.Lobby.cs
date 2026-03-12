using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;

namespace EorzeaBingo.Windows;

public partial class MainWindow
{
    private void DrawLobby(BingoGameState state)
    {
        var availWidth = ImGui.GetContentRegionAvail().X;
        var scale = ImGuiHelpers.GlobalScale;

        var iconImage = Plugin.TextureProvider.GetFromFile(_iconPath).GetWrapOrDefault();
        if (iconImage != null)
        {
            var imgSize = new Vector2(iconImage.Width, iconImage.Height) * scale * 0.65f;
            ImGui.SetCursorPosX((availWidth - imgSize.X) * 0.5f);
            ImGui.Image(iconImage.Handle, imgSize);
            ImGui.Spacing();
        }
        else
        {
            ImGui.Text("Loading icon...");
        }

        ImGui.SetWindowFontScale(1.0f);
        var subtitleText = "Brought to you by OOFGames";
        var subWidth = ImGui.CalcTextSize(subtitleText).X;
        ImGui.SetCursorPosX((availWidth - subWidth) * 0.5f);
        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), subtitleText);
        ImGui.Spacing();

        var oofImage = Plugin.TextureProvider.GetFromFile(_oofPath).GetWrapOrDefault();
        if (oofImage != null)
        {
            var oofScale = 0.10f;
            var oofSize = new Vector2(oofImage.Width, oofImage.Height) * scale * oofScale;
            var oofPos = (availWidth - oofSize.X) * 0.5f;
            ImGui.SetCursorPosX(oofPos > 0 ? oofPos : 0);
            ImGui.Image(oofImage.Handle, oofSize);
            ImGui.Spacing();
        }

        // Temp Alpha testing text
        ImGui.SetWindowFontScale(0.8f);
        var alphaText = "Please report bugs/enhancements to https://github.com/OOFGamesss/EorzeaBingo/issues";
        var alphaWidth = ImGui.CalcTextSize(alphaText).X;
        ImGui.SetCursorPosX((availWidth - alphaWidth) * 0.5f);
        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), alphaText);
        ImGui.Spacing();

        if (ImGui.BeginTabBar("LobbyTabs"))
        {
            if (ImGui.BeginTabItem("Join Game"))
            {
                ImGui.Text("Discovered Rooms:");
                ImGui.Spacing();

                var discovered = state.DiscoveredRooms.ToList();
                if (discovered.Count == 0)
                {
                    ImGui.Text("No active rooms discovered in chat yet.");
                }
                else
                {
                    foreach (var room in discovered)
                    {
                        if (ImGui.Button($"Join Room {room.Key} (Host: {room.Value})"))
                        {
                            _plugin.JoinBingoRoom(room.Key, room.Value);
                        }
                    }
                }
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Host Game"))
            {
                ImGui.Spacing();
                ImGui.Text("Ready to start?");

                var isCooldown = _plugin.IsChatOnCooldown;
                if (isCooldown) ImGui.BeginDisabled();
                var createBtnText = isCooldown ? $"Create Room ({_plugin.ChatCooldownSeconds})" : "Create Room";

                if (ImGui.Button(createBtnText))
                {
                    _plugin.StartBingoRoom();
                    _plugin.LastChatActionTime = DateTime.UtcNow;
                }

                if (isCooldown) ImGui.EndDisabled();

                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }

        var btnWidth = ImGui.CalcTextSize("Settings").X + ImGui.GetStyle().FramePadding.X * 2;
        ImGui.SetCursorPosY(ImGui.GetWindowContentRegionMax().Y - ImGui.GetFrameHeight());
        ImGui.SetCursorPosX(ImGui.GetWindowContentRegionMax().X - btnWidth);
        if (ImGui.Button("Settings"))
        {
            _plugin.ToggleConfigUi();
        }
    }
}
