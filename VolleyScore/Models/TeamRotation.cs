// File: VolleyScore/Models/TeamRotation.cs
using System.ComponentModel.DataAnnotations.Schema;

namespace VolleyScore.Models;

/// <summary>
/// Stores a team's saved initial court rotation (positions 1-6).
/// Used to quickly reset the court lineup at the start of a match or set.
/// </summary>
public class TeamRotation
{
    public int Id { get; set; }
    public int TeamId { get; set; }
    public int PlayerId { get; set; }

    /// <summary>Volleyball court position 1-6 (same numbering as PlayerPosition.Position)</summary>
    public int Position { get; set; }

    [ForeignKey("TeamId")]
    public Team? Team { get; set; }

    [ForeignKey("PlayerId")]
    public Player? Player { get; set; }
}
