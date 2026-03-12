using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace EorzeaBingo;

/// <summary>
/// Maintains the in-memory state of the active Bingo game, inclusive of room details, the current round, 
/// called numbers, the local player's board, and host replicas for validation purposes.
/// </summary>
public sealed class BingoGameState
{
    public string? RoomCode { get; private set; }
    public int RoundNumber { get; private set; } = 1;
    public bool IsHost { get; private set; }
    public string? HostName { get; private set; }
    public string? LocalPlayerName { get; private set; }

    public HashSet<int> CalledNumbers { get; } = new();
    public HashSet<int> PlayerMarkedNumbers { get; } = new();
    public int[,]? MyBoard { get; private set; }

    public bool RoomFinishedLocally { get; set; }

    public Dictionary<string, int[,]> HostReplicas { get; } = new();

    public Dictionary<string, ClaimState> PlayerClaims { get; } = new();

    public bool IsDrawLocked => PlayerClaims.Values.Any(s => s == ClaimState.Pending);

    public Dictionary<string, string> DiscoveredRooms { get; } = new();

    private readonly object _lock = new();

    public bool IsInRoom => !string.IsNullOrEmpty(RoomCode);

    /// <summary>
    /// Initialises a new Bingo room as the host, wiping previous state and generating a fresh board.
    /// </summary>
    public void CreateRoom(string roomCode, string localPlayerName)
    {
        lock (_lock)
        {
            RoomCode = roomCode;
            RoundNumber = 1;
            IsHost = true;
            HostName = localPlayerName;
            LocalPlayerName = localPlayerName;
            RoomFinishedLocally = false;
            CalledNumbers.Clear();
            PlayerMarkedNumbers.Clear();
            HostReplicas.Clear();
            PlayerClaims.Clear();
            var cleanName = SanitiseName(localPlayerName);
            MyBoard = BingoBoard.Generate(roomCode, cleanName, RoundNumber);
        }
    }

    /// <summary>
    /// Joins an existing Bingo room, resetting the local state and generating a board using the prescribed room parameters.
    /// </summary>
    public void JoinRoom(string roomCode, string localPlayerName, bool isHost, string hostName = "Unknown")
    {
        lock (_lock)
        {
            RoomCode = roomCode;
            RoundNumber = 1;
            IsHost = isHost;
            HostName = isHost ? localPlayerName : hostName;
            LocalPlayerName = localPlayerName;
            RoomFinishedLocally = false;
            CalledNumbers.Clear();
            PlayerMarkedNumbers.Clear();
            if (IsHost)
            {
                HostReplicas.Clear();
                PlayerClaims.Clear();
            }
            var cleanName = SanitiseName(localPlayerName);
            MyBoard = BingoBoard.Generate(roomCode, cleanName, RoundNumber);
        }
    }

    public void AddPlayerReplica(string playerName)
    {
        if (string.IsNullOrEmpty(RoomCode) || !IsHost || LocalPlayerName == null)
            return;
        lock (_lock)
        {
            var cleanName = SanitiseName(playerName);
            if (HostReplicas.ContainsKey(cleanName))
                return;
            HostReplicas[cleanName] = BingoBoard.Generate(RoomCode, cleanName, RoundNumber);
        }
    }

    public void RemovePlayerReplica(string playerName)
    {
        lock (_lock)
        {
            var cleanName = SanitiseName(playerName);
            if (HostReplicas.ContainsKey(cleanName))
                HostReplicas.Remove(cleanName);
            if (PlayerClaims.ContainsKey(cleanName))
                PlayerClaims.Remove(cleanName);
        }
    }

    public void ClaimBingo(string playerName)
    {
        lock (_lock)
        {
            var cleanName = SanitiseName(playerName);
            if (IsHost && HostReplicas.ContainsKey(cleanName))
            {
                if (!PlayerClaims.TryGetValue(cleanName, out var state) || state == ClaimState.Rejected)
                {
                    PlayerClaims[cleanName] = ClaimState.Pending;
                }
            }
        }
    }

    public void ApproveClaim(string playerName)
    {
        lock (_lock)
        {
            var cleanName = SanitiseName(playerName);
            if (IsHost && PlayerClaims.ContainsKey(cleanName))
                PlayerClaims[cleanName] = ClaimState.Approved;
        }
    }

    public void RejectClaim(string playerName)
    {
        lock (_lock)
        {
            var cleanName = SanitiseName(playerName);
            if (IsHost && PlayerClaims.ContainsKey(cleanName))
                PlayerClaims[cleanName] = ClaimState.Rejected;
        }
    }

    public void CallNumber(int number)
    {
        lock (_lock)
        {
            CalledNumbers.Add(number);
        }
    }

    public void NextRound()
    {
        if (string.IsNullOrEmpty(RoomCode) || LocalPlayerName == null)
            return;
        lock (_lock)
        {
            RoundNumber++;
            CalledNumbers.Clear();
            PlayerMarkedNumbers.Clear();
            var cleanLocalName = SanitiseName(LocalPlayerName);
            MyBoard = BingoBoard.Generate(RoomCode, cleanLocalName, RoundNumber);
            if (IsHost)
            {
                PlayerClaims.Clear();
                var playerNames = new List<string>(HostReplicas.Keys);
                HostReplicas.Clear();
                foreach (var name in playerNames)
                {
                    HostReplicas[name] = BingoBoard.Generate(RoomCode, name, RoundNumber);
                }
            }
        }
    }

    public void ForceSyncRound(int roundNum)
    {
        lock (_lock)
        {
            RoundNumber = roundNum;
            CalledNumbers.Clear();
            PlayerMarkedNumbers.Clear();
            if (RoomCode != null && LocalPlayerName != null)
            {
                var cleanName = SanitiseName(LocalPlayerName);
                MyBoard = BingoBoard.Generate(RoomCode, cleanName, RoundNumber);
            }
        }
    }

    public void LeaveRoom()
    {
        lock (_lock)
        {
            RoomCode = null;
            RoundNumber = 1;
            IsHost = false;
            HostName = null;
            LocalPlayerName = null;
            RoomFinishedLocally = false;
            CalledNumbers.Clear();
            PlayerMarkedNumbers.Clear();
            MyBoard = null;
            HostReplicas.Clear();
            PlayerClaims.Clear();
        }
    }

    /// <summary>
    /// Aggressively strips FFXIV UI / Unicode characters (e.g. Party numbers 1-8 parsing as \uE082) 
    /// from player names to ensure deterministic seed keys. Retains standard letters, spaces, hyphens, 
    /// apostrophes (lore names), and the @ symbol (World tag).
    /// </summary>
    public static string SanitiseName(string? name)
    {
        if (string.IsNullOrEmpty(name)) return "Unknown";
        return Regex.Replace(name, @"[^\w\s'@-]", "").Trim();
    }

    public IEnumerable<int> GetUncalledNumbers()
    {
        lock (_lock)
        {
            for (var n = 1; n <= 70; n++)
            {
                if (!CalledNumbers.Contains(n))
                    yield return n;
            }
        }
    }

    /// <summary>
    /// Analyses the replicated board of a specific player to verify their current progress 
    /// against all called numbers. Returns their optimal line size and current claim state.
    /// </summary>
    public PlayerProgress GetPlayerProgress(string playerName)
    {
        lock (_lock)
        {
            var cleanName = SanitiseName(playerName);
            if (!HostReplicas.TryGetValue(cleanName, out var board))
                return new PlayerProgress { PlayerName = cleanName, MaxLineSize = 0, ClaimState = ClaimState.None };

            var bestLineSize = 0;
            var bestLine = new List<int>();

            void CheckLine(IEnumerable<int> lineCoordinates)
            {
                var matchCount = 0;
                var currentLine = new List<int>();
                foreach (var val in lineCoordinates)
                {
                    currentLine.Add(val);
                    if (val == 0 || CalledNumbers.Contains(val)) matchCount++;
                }
                if (matchCount > bestLineSize)
                {
                    bestLineSize = matchCount;
                    bestLine = currentLine;
                }
            }

            for (var i = 0; i < 5; i++)
            {
                CheckLine(Enumerable.Range(0, 5).Select(col => board[i, col])); // Row
                CheckLine(Enumerable.Range(0, 5).Select(row => board[row, i])); // Col
            }

            CheckLine(Enumerable.Range(0, 5).Select(i => board[i, i])); // Diag 1
            CheckLine(Enumerable.Range(0, 5).Select(i => board[i, 4 - i])); // Diag 2

            PlayerClaims.TryGetValue(cleanName, out var claimState);
            return new PlayerProgress
            {
                PlayerName = cleanName,
                MaxLineSize = bestLineSize,
                BestLine = bestLine,
                ClaimState = claimState
            };
        }
    }
}

public enum ClaimState
{
    None = 0,
    Pending,
    Approved,
    Rejected
}

public class PlayerProgress
{
    public string PlayerName { get; set; } = string.Empty;
    public int MaxLineSize { get; set; }
    public List<int> BestLine { get; set; } = new();
    public ClaimState ClaimState { get; set; } = ClaimState.None;
}
