// File: VolleyScore/Models/Enums.cs
// Purpose: Enumeration types used throughout the application

namespace VolleyScore.Models;

/// <summary>Match lifecycle status</summary>
public enum MatchStatus
{
    Setup = 0,
    InProgress = 1,
    Completed = 2
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
    Setup = 0,
    PoolStage = 1,
    Knockouts = 2,
    Completed = 3
}

/// <summary>Phase of a tournament match</summary>
public enum TournamentPhase
{
    Pool = 0,
    SemiFinal = 1,
    ThirdPlace = 2,
    Final = 3,
    QuarterFinal = 4
}

/// <summary>Determines the format of the tournament (indoor 6v6 or beach 2v2)</summary>
public enum TournamentType
{
    Singles = 0,
    Doubles = 1
}
