using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;

namespace EorzeaBingo.Windows;

public partial class MainWindow : Window, IDisposable
{
    private const float MinCellSize = 44f;
    private const uint CalledColor = 0xFF2D7D2D;
    private const uint FreeSpaceColor = 0xFF404040;

    private static readonly Dictionary<string, Vector2> DefaultSizes = new()
    {
        { "Lobby", new Vector2(400, 500) },
        { "Host", new Vector2(900, 600) },
        { "Player", new Vector2(420, 550) }
    };

    private readonly Plugin _plugin;
    private readonly string _iconPath;
    private readonly string _oofPath;
    private string _lastViewKey = "";
    private int _saveFrameCounter;
    private const int SaveIntervalFrames = 120;

    public MainWindow(Plugin plugin, string imagePath, string oofImagePath)
        : base("Eorzea Bingo##EorzeaBingo_Main", ImGuiWindowFlags.NoScrollbar)
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(280, 320),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        _plugin = plugin;
        _iconPath = imagePath;
        _oofPath = oofImagePath;
    }

    public void Dispose() { }

    private string GetCurrentViewKey()
    {
        var state = _plugin.GameState;
        if (!state.IsInRoom) return "Lobby";
        return state.IsHost ? "Host" : "Player";
    }

    public override void PreDraw()
    {
        var viewKey = GetCurrentViewKey();

        if (viewKey != _lastViewKey)
        {
            var config = _plugin.Configuration;
            if (config.WindowPositions.TryGetValue(viewKey, out var pos) && pos.Length == 2)
            {
                ImGui.SetNextWindowPos(new Vector2(pos[0], pos[1]), ImGuiCond.Always);
            }

            if (config.WindowSizes.TryGetValue(viewKey, out var size) && size.Length == 2)
            {
                ImGui.SetNextWindowSize(new Vector2(size[0], size[1]), ImGuiCond.Always);
            }
            else
            {
                var defaultSize = DefaultSizes.GetValueOrDefault(viewKey, new Vector2(400, 500));
                ImGui.SetNextWindowSize(defaultSize, ImGuiCond.Always);
            }

            _lastViewKey = viewKey;
        }
    }

    private void CaptureWindowState()
    {
        if (string.IsNullOrEmpty(_lastViewKey)) return;

        var config = _plugin.Configuration;
        var windowPos = ImGui.GetWindowPos();
        var windowSize = ImGui.GetWindowSize();

        config.WindowPositions[_lastViewKey] = new[] { windowPos.X, windowPos.Y };
        config.WindowSizes[_lastViewKey] = new[] { windowSize.X, windowSize.Y };

        _saveFrameCounter++;
        if (_saveFrameCounter >= SaveIntervalFrames)
        {
            _saveFrameCounter = 0;
            config.Save();
        }
    }

    public override void Draw()
    {
        var state = _plugin.GameState;

        if (!state.IsInRoom)
        {
            DrawLobby(state);
            CaptureWindowState();
            return;
        }

        ImGui.Text($"Room: {state.RoomCode}  |  Round: {state.RoundNumber}");
        ImGui.Spacing();

        if (state.IsHost)
        {
            if (ImGui.BeginTable("HostLayout", 2))
            {
                ImGui.TableSetupColumn("HostBoard", ImGuiTableColumnFlags.WidthStretch, 0.6f);
                ImGui.TableSetupColumn("PlayerList", ImGuiTableColumnFlags.WidthStretch, 0.4f);

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                DrawHostGrid(state);
                DrawHostPanel(state);

                ImGui.TableNextColumn();
                DrawPlayersTab(state);

                ImGui.EndTable();
            }
        }
        else
        {
            DrawBingoGrid(state);
            ImGui.Spacing();

            var isCooldown = _plugin.IsChatOnCooldown;
            if (isCooldown) ImGui.BeginDisabled();
            var bingoBtnText = isCooldown ? $"BINGO! ({_plugin.ChatCooldownSeconds})" : "BINGO!";
            var currentRoomCode = state.RoomCode ?? string.Empty;
            if (ImGui.Button(bingoBtnText))
            {
                var prefix = _plugin.GetRoomPrefix(currentRoomCode);
                
                Plugin.SendChatMessage($"{prefix} BINGO!");
                _plugin.LastChatActionTime = DateTime.UtcNow;
            }
            if (isCooldown) ImGui.EndDisabled();
            ImGui.SameLine();
            if (ImGui.Button("Leave Room"))
            {
                var prefix = _plugin.GetRoomPrefix(currentRoomCode);
                
                Plugin.SendChatMessage($"{prefix} has left the Bingo Room.");
                state.LeaveRoom();
            }
        }

        CaptureWindowState();
    }

}
