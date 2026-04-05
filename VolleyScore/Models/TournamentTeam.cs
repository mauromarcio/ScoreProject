// File: VolleyScore/Models/TournamentTeam.cs
// Purpose: Links a team to a tournament with pool seeding order (user-adjustable)

using System.ComponentModel.DataAnnotations.Schema;

namespace VolleyScore.Models;

public class TournamentTeam
{
    public int Id { get; set; }

    public int TournamentId { get; set; }

    public int TeamId { get; set; }

    /// <summary>Display/seed order in the pool (1-based, user can reorder)</summary>
    public int SeedOrder { get; set; }

    /// <summary>Court this team is assigned to (assigned when pool matches are generated)</summary>
    public int CourtNumber { get; set; } = 1;

    // Navigation
    [ForeignKey("TournamentId")]
    public Tournament? Tournament { get; set; }

    [ForeignKey("TeamId")]
    public Team? Team { get; set; }
}
