// File: VolleyScore/Models/TournamentMatch.cs
// Purpose: Links a Match to a tournament phase (pool round-robin or knockout).
//          For knockouts, HomeTeamSourceMatchId / AwayTeamSourceMatchId resolve the
//          teams after their source semi-final is completed.

using System.ComponentModel.DataAnnotations.Schema;

namespace VolleyScore.Models;

public class TournamentMatch
{
    public int Id { get; set; }
    public int TournamentId { get; set; }

    /// <summary>Linked Match entity (created when schedule is generated). Null = not yet created.</summary>
    public int? MatchId { get; set; }

    /// <summary>Pool this match belongs to (null for knockout matches)</summary>
    public int? PoolId { get; set; }

    public TournamentPhase Phase { get; set; }

    /// <summary>Display label, e.g. "Semi-Final 1", "Final"</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Home team (null for knockout placeholders resolved from source matches)</summary>
    public int? HomeTeamId { get; set; }

    /// <summary>Away team (null for knockout placeholders)</summary>
    public int? AwayTeamId { get; set; }

    /// <summary>Set after the match is completed</summary>
    public int? WinnerTeamId { get; set; }

    /// <summary>For knockout: the TournamentMatch that provides the home team</summary>
    public int? HomeSourceMatchId { get; set; }

    /// <summary>True = home team is the winner of source match; False = loser</summary>
    public bool HomeFromWinner { get; set; } = true;

    /// <summary>For knockout: the TournamentMatch that provides the away team</summary>
    public int? AwaySourceMatchId { get; set; }

    /// <summary>True = away team is the winner of source match; False = loser</summary>
    public bool AwayFromWinner { get; set; } = true;

    public int SortOrder { get; set; }

    /// <summary>Sequential match number within the pool (used on print schedule and court display).</summary>
    public int MatchNumber { get; set; }

    /// <summary>Court assignment for parallel-play schedules (1-based; 0 = unassigned).</summary>
    public int CourtNumber { get; set; } = 1;

    /// <summary>Optional referee team for pool-stage matches.</summary>
    public int? RefereeTeamId { get; set; }

    /// <summary>True when the home team had 3+ consecutive idle slots before this match.</summary>
    public bool HomeTeamLongWait { get; set; }

    /// <summary>True when the away team had 3+ consecutive idle slots before this match.</summary>
    public bool AwayTeamLongWait { get; set; }

    // Navigation
    [ForeignKey("TournamentId")]
    public Tournament? Tournament { get; set; }

    [ForeignKey("MatchId")]
    public Match? Match { get; set; }

    [ForeignKey("PoolId")]
    public Pool? Pool { get; set; }

    [ForeignKey("HomeTeamId")]
    public Team? HomeTeam { get; set; }

    [ForeignKey("AwayTeamId")]
    public Team? AwayTeam { get; set; }

    [ForeignKey("RefereeTeamId")]
    public Team? RefereeTeam { get; set; }
}
