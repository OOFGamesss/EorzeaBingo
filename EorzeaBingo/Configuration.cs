using Dalamud.Configuration;
using System;
using System.Collections.Generic;

namespace EorzeaBingo;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public bool AutoMarkNumbers { get; set; } = false;
    public bool DefaultEnableBingoLingo { get; set; } = true;

    public OutputChatChannel DefaultChatChannel { get; set; } = OutputChatChannel.Party;

    // Per-view window state persistence (keyed by "Lobby", "Host", "Player")
    public Dictionary<string, float[]> WindowPositions { get; set; } = new();
    public Dictionary<string, float[]> WindowSizes { get; set; } = new();

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}

public enum OutputChatChannel
{
    Say,
    Party,
    Alliance
}
