using System;
using System.Text;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Game.Gui.PartyFinder.Types;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using EorzeaBingo.Windows;
using System.IO;

namespace EorzeaBingo;

/// <summary>
/// The primary entry point for the Eorzea Bingo plugin.
/// Manages window instancing, user configurations, and chat integrations within the game environment.
/// </summary>
public unsafe sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IPartyList PartyList { get; private set; } = null!;
    [PluginService] internal static IPartyFinderGui PartyFinder { get; private set; } = null!;

    private const string BingoCommand = "/bingo";
    private const string BingoConfigCommand = "/bingoconfig";

    public Configuration Configuration { get; init; }
    public BingoGameState GameState { get; init; } = new();

    public OutputChatChannel ActiveChatChannel { get; set; }
    public System.Collections.Generic.Dictionary<string, OutputChatChannel> KnownRoomChannels { get; } = new();
    public bool ActiveEnableBingoLingo { get; set; }
    public bool IsAllianceRaidLobby { get; set; } = false;
    public bool IsCwProxyValid { get; set; } = true;

    public DateTime LastChatActionTime { get; set; } = DateTime.MinValue;
    public bool IsChatOnCooldown => (DateTime.UtcNow - LastChatActionTime).TotalSeconds < 3;
    public int ChatCooldownSeconds => 3 - (int)(DateTime.UtcNow - LastChatActionTime).TotalSeconds;

    public readonly WindowSystem WindowSystem = new("EorzeaBingo");
    private ConfigWindow ConfigWindow { get; }
    private MainWindow MainWindow { get; }
    private ChatListener ChatListener { get; }
    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        GameState = new BingoGameState();

        ActiveChatChannel = Configuration.DefaultChatChannel;
        ActiveEnableBingoLingo = Configuration.DefaultEnableBingoLingo;

        var iconImagePath = Path.Combine(PluginInterface.AssemblyLocation.Directory?.FullName!, "eorzea-bingo-icon.png");
        var oofImagePath = Path.Combine(PluginInterface.AssemblyLocation.Directory?.FullName!, "oofgames-logo.png");

        ConfigWindow = new ConfigWindow(this);
        MainWindow = new MainWindow(this, iconImagePath, oofImagePath);
        ChatListener = new ChatListener(this, GameState, ChatGui, Log, ClientState, PluginInterface);

        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(MainWindow);
        CommandManager.AddHandler(BingoCommand, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the EorzeaBingo lobby/host UI."
        });

        CommandManager.AddHandler(BingoConfigCommand, new CommandInfo(OnConfigCommand)
        {
            HelpMessage = "Open the EorzeaBingo settings menu."
        });

        ChatListener.Subscribe();

        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;
        PartyFinder.ReceiveListing += OnReceiveListing;

        Log.Information("EorzeaBingo plugin loaded.");
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;
        PartyFinder.ReceiveListing -= OnReceiveListing;

        ChatListener.Unsubscribe();

        WindowSystem.RemoveAllWindows();

        MainWindow.Dispose();

        CommandManager.RemoveHandler(BingoCommand);
        CommandManager.RemoveHandler(BingoConfigCommand);
    }
    private void OnCommand(string command, string args)
    {
        MainWindow.Toggle();
    }

    private void OnConfigCommand(string command, string args)
    {
        ConfigWindow.Toggle();
    }



    /// <summary>
    /// Processes a request from the local player to join an actively broadcasted Bingo lobby.
    /// Initiates a state synchronization request to visually generate their bingo card.
    /// </summary>
    /// <param name="roomCode">The unique 4-character identifier of the lobby to join.</param>
    /// <param name="hostName">The registered name of the player hosting the lobby.</param>
    public void JoinBingoRoom(string roomCode, string hostName)
    {
        var player = ObjectTable.LocalPlayer;
        var playerName = player != null ? $"{player.Name.TextValue}@{player.HomeWorld.Value.Name.ToString()}" : "Unknown";
        GameState.JoinRoom(roomCode, playerName, isHost: false, hostName);

        var prefix = GetRoomPrefix(roomCode);
        
        SendChatMessage($"{prefix} joins room {roomCode}.");
        Log.Information($"Player {playerName} joined room {roomCode} via {prefix}");
    }

    /// <summary>
    /// Initializes a fresh Bingo lobby hosted by the local player.
    /// Generates a randomised 4-character room code and broadcasts availability to the configured chat channel.
    /// </summary>
    public void StartBingoRoom()
    {
        var roomCode = GenerateRoomCode();
        var player = ObjectTable.LocalPlayer;
        var playerName = player != null ? $"{player.Name.TextValue}@{player.HomeWorld.Value.Name.ToString()}" : "Unknown";
        GameState.CreateRoom(roomCode, playerName);

        var prefix = GetChatPrefix(ActiveChatChannel);
        Log.Information($"Host {playerName} created room {roomCode}");
    }

    public static void SendChatMessage(string command)
    {
        var bytes = Encoding.UTF8.GetBytes(command);
        if (bytes.Length == 0) return;
        if (bytes.Length > 500) return;

        var sanitised = SanitiseText(command);
        if (command.Length != sanitised.Length) return;

        var mes = Utf8String.FromSequence(bytes);
        UIModule.Instance()->ProcessChatBoxEntry(mes);
        mes->Dtor(true);
    }

    private static string SanitiseText(string text)
    {
        var uText = Utf8String.FromString(text);

        uText->SanitizeString((AllowedEntities)0x27F);
        var sanitised = uText->ToString();
        uText->Dtor(true);

        return sanitised;
    }

    private void OnReceiveListing(IPartyFinderListing listing, IPartyFinderListingEventArgs args)
    {
        var localPlayer = ObjectTable.LocalPlayer;
        if (localPlayer != null && listing.Name.TextValue == localPlayer.Name.TextValue)
        {
            IsAllianceRaidLobby = listing.SearchArea.HasFlag(SearchAreaFlags.AllianceRaid);
            return;
        }

        foreach (var member in PartyList)
        {
            if (listing.Name.TextValue == member.Name.TextValue)
            {
                IsAllianceRaidLobby = listing.SearchArea.HasFlag(SearchAreaFlags.AllianceRaid);
                return;
            }
        }
    }

    /// <summary>
    /// Checks if the player is currently in any type of alliance, 
    /// accounting for empty sub-parties and bypassing stale ghost memory.
    /// </summary>
    public bool IsInAlliance()
    {
        if (PartyList.IsAlliance) return true;

        if (IsAllianceRaidLobby) return true;

        if (IsCwProxyValid)
        {
            var cwProxy = InfoProxyCrossRealm.Instance();
            if (cwProxy != null && cwProxy->LocalPlayerGroupIndex != 255)
            {
                return cwProxy->GroupCount > 1;
            }
        }

        return false;
    }

    /// <summary>
    /// Retrieves the appropriate slash-command prefix (e.g., /p, /a, /s) mapped to the player's selected 
    /// configuration channel, falling back automatically to /s if the player is not currently in a matching party composition.
    /// </summary>
    public string GetChatPrefix(OutputChatChannel channel)
    {
        return channel switch
        {
            OutputChatChannel.Alliance => IsInAlliance() ? "/alliance" : "/s",
            OutputChatChannel.Party => PartyList.Length > 0 ? "/p" : "/s",
            OutputChatChannel.Say => "/s",
            _ => "/s"
        };
    }

    /// <summary>
    /// Retrieves the appropriate slash-command prefix based on the host's original channel for the room.
    /// Falls back to the player's active channel if the room is not found, and safely verifies party/alliance status.
    /// </summary>
    public string GetRoomPrefix(string roomCode)
    {
        var channelToUse = KnownRoomChannels.TryGetValue(roomCode, out var savedChannel) 
            ? savedChannel 
            : ActiveChatChannel;

        return GetChatPrefix(channelToUse);
    }

    private static string GenerateRoomCode()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var rng = new Random();
        var code = new char[4];
        for (var i = 0; i < 4; i++)
            code[i] = chars[rng.Next(chars.Length)];
        return new string(code);
    }

    public void ToggleConfigUi() => ConfigWindow.Toggle();
    public void ToggleMainUi() => MainWindow.Toggle();
}
