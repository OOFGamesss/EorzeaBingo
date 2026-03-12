using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace EorzeaBingo.Windows;

public class ConfigWindow : Window, IDisposable
{
    private readonly Plugin _plugin;
    private readonly Configuration _configuration;

    public ConfigWindow(Plugin plugin) : base("Eorzea Bingo Settings###BingoConfigWindow")
    {
        _plugin = plugin;
        _configuration = plugin.Configuration;
        Flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollWithMouse;

        Size = new Vector2(300, 120);
        SizeCondition = ImGuiCond.Always;
    }

    public void Dispose() { }

    public override void Draw()
    {
        ImGui.Text("Default Bingo Output Channel:");
        var channelNames = Enum.GetNames<OutputChatChannel>();
        var currentChannelIndex = (int)_configuration.DefaultChatChannel;
        ImGui.SetNextItemWidth(150);
        if (ImGui.Combo("##DefaultChatChannel", ref currentChannelIndex, channelNames, channelNames.Length))
        {
            _configuration.DefaultChatChannel = (OutputChatChannel)currentChannelIndex;
            _plugin.ActiveChatChannel = (OutputChatChannel)currentChannelIndex;
            _configuration.Save();
        }

        ImGui.Spacing();
        var includeLingo = _configuration.DefaultEnableBingoLingo;
        if (ImGui.Checkbox("Include Bingo Lingo by default", ref includeLingo))
        {
            _configuration.DefaultEnableBingoLingo = includeLingo;
            _plugin.ActiveEnableBingoLingo = includeLingo;
            _configuration.Save();
        }
    }
}
