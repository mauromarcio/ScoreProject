// File: VolleyScore/Models/Enums.cs
// Purpose: Enumeration types used throughout the application

namespace VolleyScore.Models;

/// <summary>Match lifecycle status</summary>
public enum MatchStatus
{
    Setup = 0,      // Teams and players being configured
    InProgress = 1, // Match is actively being played
    Completed = 2   // Match has concluded
}

/// <summary>Identifies which side of the court a team occupies</summary>
public enum CourtSide
{
    Left = 0,
    Right = 1
}

/// <summary>Tournament lifecycle status</summary>
public enum TournamentStatus
{
    Setup = 0,       // Teams being added / pool order being arranged
    PoolPlay = 1,    // Pool (round-robin) matches in progress
    Playoffs = 2,    // Semi-finals / finals in progress
    Completed = 3    // Tournament concluded
}

/// <summary>Which stage of the tournament a match belongs to</summary>
public enum TournamentStage
{
    Pool = 0,
    Semifinal = 1,
    ThirdPlace = 2,
    Final = 3
}
