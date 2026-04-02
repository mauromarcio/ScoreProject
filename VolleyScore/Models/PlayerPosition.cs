// File: VolleyScore/Models/PlayerPosition.cs
// Purpose: Maps a player to a court position for a given match and set.
//          Position values 1-6 correspond to standard volleyball rotation positions.

using System.ComponentModel.DataAnnotations.Schema;

namespace VolleyScore.Models;

public class PlayerPosition
{
    public int Id { get; set; }

    public int MatchId { get; set; }

    /// <summary>Set number this position applies to (1-based)</summary>
    public int SetNumber { get; set; }

    public int PlayerId { get; set; }

    /// <summary>
    /// Volleyball position 1-6:
    ///   4 | 3 | 2   (front row, near net)
    ///   5 | 6 | 1   (back row, server at pos 1)
    /// </summary>
    public int Position { get; set; }

    /// <summary>"Home" or "Away"</summary>
    public string Side { get; set; } = string.Empty;

    // Navigation
    [ForeignKey("MatchId")]
    public Match? Match { get; set; }

    [ForeignKey("PlayerId")]
    public Player? Player { get; set; }
}
