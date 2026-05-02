using System;
using System.Numerics;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Bindings.ImGui;

namespace EorzeaBingo.Windows;

public partial class MainWindow
{
    private void DrawBingoGrid(BingoGameState state)
    {
        var board = state.MyBoard;
        if (board == null) return;

        if (state.RoomFinishedLocally)
        {
            ImGui.SetWindowFontScale(1.2f);
            ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), "The Host has finished the game.");
            ImGui.SetWindowFontScale(1.0f);
            ImGui.Spacing();
        }

        var avail = ImGui.GetContentRegionAvail();
        var scale = ImGuiHelpers.GlobalScale;

        var reservedHeight = 85 * scale;
        var usableHeight = Math.Max(MinCellSize * 6 * scale, avail.Y - reservedHeight);

        var effectiveSize = Math.Min(avail.X, usableHeight);

        effectiveSize = Math.Max(effectiveSize, MinCellSize * 6 * scale);

        var dynamicCellSize = (effectiveSize - (ImGui.GetStyle().ItemSpacing.X * 5)) / 6f;
        var cellSize = new Vector2(-1, dynamicCellSize);

        {
            using var bingoGrid = ImRaii.Table("BingoGrid", 5, ImGuiTableFlags.Borders | ImGuiTableFlags.SizingStretchSame, new Vector2(effectiveSize, 0));
            if (!bingoGrid) return;

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
                    var isFree = value == 0;

                    var autoMarkEnabled = _plugin.Configuration.AutoMarkNumbers;

                    var isCalled = isFree || state.PlayerMarkedNumbers.Contains(value) || (autoMarkEnabled && state.CalledNumbers.Contains(value));

                    if (isCalled)
                        ImGui.PushStyleColor(ImGuiCol.Button, CalledColor);
                    else if (isFree)
                        ImGui.PushStyleColor(ImGuiCol.Button, FreeSpaceColor);

                    if (ImGui.Button(label, cellSize))
                    {
                        if (!isFree)
                        {
                            if (state.PlayerMarkedNumbers.Contains(value))
                                state.PlayerMarkedNumbers.Remove(value);
                            else
                                state.PlayerMarkedNumbers.Add(value);
                        }
                    }

                    if (isCalled || isFree)
                        ImGui.PopStyleColor();
                }
            }
        }

        ImGui.Spacing();
        var autoMark = _plugin.Configuration.AutoMarkNumbers;
        if (ImGui.Checkbox("Auto-mark called numbers", ref autoMark))
        {
            if (!autoMark)
            {
                foreach (var calledNum in state.CalledNumbers)
                {
                    if (!state.PlayerMarkedNumbers.Contains(calledNum))
                    {
                        state.PlayerMarkedNumbers.Add(calledNum);
                    }
                }
            }

            _plugin.Configuration.AutoMarkNumbers = autoMark;
            _plugin.Configuration.Save();
        }
    }
}