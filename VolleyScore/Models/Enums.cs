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
