// File: VolleyScore/Models/GameSet.cs
// Purpose: Represents a single set within a match, tracking score and rotation state

using System.ComponentModel.DataAnnotations.Schema;

namespace VolleyScore.Models;

public class GameSet
{
    public int Id { get; set; }

    public int MatchId { get; set; }

    public int SetNumber { get; set; }

    public int HomeScore { get; set; } = 0;

    public int AwayScore { get; set; } = 0;

    /// <summary>Null until set is complete</summary>
    public int? WinnerTeamId { get; set; }

    /// <summary>
    /// Home team rotation index (0-5). Tracks how many times home team has rotated
    /// within this set. Used to determine current serving player position.
    /// </summary>
    public int HomeRotationIndex { get; set; } = 0;

    /// <summary>Away team rotation index (0-5).</summary>
    public int AwayRotationIndex { get; set; } = 0;

    /// <summary>True = home team is currently serving</summary>
    public bool HomeIsServing { get; set; } = true;

    public bool IsCompleted { get; set; } = false;

    public DateTime StartedAt { get; set; } = DateTime.Now;
    public DateTime? CompletedAt { get; set; }

    // Navigation
    [ForeignKey("MatchId")]
    public Match? Match { get; set; }
}
