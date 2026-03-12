using System;
using System.Collections.Generic;

namespace EorzeaBingo;

/// <summary>
/// Generates a deterministic 5x5 bingo grid from a seed string.
/// Distributes numbers accurately into B(1-14) I(15-28) N(29-42) G(43-56) O(57-70).
/// Elements within each column are sorted sequentially top-to-bottom.
/// Cell (2,2) is the free space placeholder (0).
/// </summary>
public static class BingoBoard
{
    private const int GridSize = 5;
    private const int CenterRow = 2;
    private const int CenterCol = 2;
    private const int ColumnRange = 14;
    private const int FreeSpaceSentinel = 0;

    /// <summary>
    /// Generates a 5x5 mathematical B-I-N-G-O grid for the given room, player, and round.
    /// Seed format: "{roomCode}-{playerName}-{roundNumber}"
    /// </summary>
    public static int[,] Generate(string roomCode, string playerName, int roundNumber)
    {
        var seedString = $"{roomCode}-{playerName}-{roundNumber}";
        var seed = CryptoUtils.SeedToInt32(seedString);
        var rng = new Random(seed);

        var grid = new int[GridSize, GridSize];

        for (var col = 0; col < GridSize; col++)
        {
            var rangeMin = (col * ColumnRange) + 1;
            var rangeMax = rangeMin + ColumnRange - 1; 

            var availableSubset = new List<int>();
            for (var index = rangeMin; index <= rangeMax; index++)
                availableSubset.Add(index);
                
            var elementsNeeded = (col == CenterCol) ? 4 : 5;
            var chosenSubset = new List<int>();

            for (var picks = 0; picks < elementsNeeded; picks++)
            {
                var randIndex = rng.Next(availableSubset.Count);
                chosenSubset.Add(availableSubset[randIndex]);
                availableSubset.RemoveAt(randIndex);
            }

            chosenSubset.Sort();

            var pickIndex = 0;
            for (var row = 0; row < GridSize; row++)
            {
                if (col == CenterCol && row == CenterRow)
                {
                    grid[row, col] = FreeSpaceSentinel;
                    continue;
                }
                grid[row, col] = chosenSubset[pickIndex];
                pickIndex++;
            }
        }

        return grid;
    }
}
