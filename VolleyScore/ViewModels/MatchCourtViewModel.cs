// File: VolleyScore/ViewModels/MatchCourtViewModel.cs
// Purpose: Aggregates all data needed to render the operational court view (Matches/Court.cshtml)

using VolleyScore.Models;

namespace VolleyScore.ViewModels;

/// <summary>Lightweight player dto for court rendering and JSON serialisation</summary>
public class PlayerDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Number { get; set; }
    public int TeamId { get; set; }
}

/// <summary>A player assigned to a specific court position</summary>
public class PositionDto
{
    /// <summary>Volleyball position 1-6</summary>
    public int Position { get; set; }
    public PlayerDto? Player { get; set; }
}

/// <summary>Full view model for the main court operational screen</summary>
public class MatchCourtViewModel
{
    public Match Match { get; set; } = null!;
    public GameSet CurrentSet { get; set; } = null!;

    // Team info
    public Team HomeTeam { get; set; } = null!;
    public Team AwayTeam { get; set; } = null!;

    // Full rosters for both teams
    public List<PlayerDto> HomePlayers { get; set; } = new();
    public List<PlayerDto> AwayPlayers { get; set; } = new();

    // Current positions on court (6 positions per side)
    public List<PositionDto> HomeCourtPositions { get; set; } = new();
    public List<PositionDto> AwayCourtPositions { get; set; } = new();

    // Sets overview for the header
    public int HomeSetsWon { get; set; }
    public int AwaySetsWon { get; set; }
    public int TotalSets { get; set; }

    // Which team is serving
    public bool HomeIsServing { get; set; }

    // Court side assignment
    public bool HomeTeamOnLeft { get; set; }
}

/// <summary>Payload returned from score AJAX calls</summary>
public class ScoreUpdateResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public int HomeScore { get; set; }
    public int AwayScore { get; set; }
    public int HomeSetsWon { get; set; }
    public int AwaySetsWon { get; set; }
    public int CurrentSetNumber { get; set; }
    public bool HomeIsServing { get; set; }
    public bool HomeTeamOnLeft { get; set; }
    public bool SetCompleted { get; set; }
    public bool MatchCompleted { get; set; }
    public string? WinnerName { get; set; }
    // Rotated positions when a team wins service
    public List<PositionDto>? HomeCourtPositions { get; set; }
    public List<PositionDto>? AwayCourtPositions { get; set; }
}
