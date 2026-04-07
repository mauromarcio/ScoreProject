// File: VolleyScore/Models/TournamentMatch.cs
// Purpose: Links a Match to a Tournament and tracks which stage it belongs to

using System.ComponentModel.DataAnnotations.Schema;

namespace VolleyScore.Models;

public class TournamentMatch
{
    public int Id { get; set; }

    public int TournamentId { get; set; }

    public int MatchId { get; set; }

    /// <summary>Pool, Semifinal, ThirdPlace or Final</summary>
    public TournamentStage Stage { get; set; } = TournamentStage.Pool;

    /// <summary>
    /// Pool: sequential match number (1, 2, 3, …)
    /// Semifinal: 1 = seed1 vs seed4, 2 = seed2 vs seed3
    /// ThirdPlace/Final: 1
    /// </summary>
    public int MatchNumber { get; set; }

    /// <summary>Pool matches only: the team assigned to referee this match.</summary>
    public int? RefereeTeamId { get; set; }

    /// <summary>Court number this match is played on (pool play only; 1 when not multi-court)</summary>
    public int CourtNumber { get; set; } = 1;

    /// <summary>
    /// True when the home team had 3 consecutive idle slots before this match.
    /// Displayed as ★ in the schedule to indicate a long wait.
    /// </summary>
    public bool HomeTeamLongWait { get; set; } = false;

    /// <summary>
    /// True when the away team had 3 consecutive idle slots before this match.
    /// Displayed as ★ in the schedule to indicate a long wait.
    /// </summary>
    public bool AwayTeamLongWait { get; set; } = false;

    // Navigation
    [ForeignKey("TournamentId")]
    public Tournament? Tournament { get; set; }

    [ForeignKey("MatchId")]
    public Match? Match { get; set; }

    [ForeignKey("RefereeTeamId")]
    public Team? RefereeTeam { get; set; }
}
