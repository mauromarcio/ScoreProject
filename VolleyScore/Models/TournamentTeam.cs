// File: VolleyScore/Models/TournamentTeam.cs
// Purpose: Represents a team enrolled in a tournament, with a seed/order position

using System.ComponentModel.DataAnnotations.Schema;

namespace VolleyScore.Models;

public class TournamentTeam
{
    public int Id { get; set; }
    public int TournamentId { get; set; }
    public int TeamId { get; set; }

    /// <summary>Overall enrollment order / seed (user-adjustable)</summary>
    public int SeedPosition { get; set; }

    /// <summary>Alias for SeedPosition — used by pool-play and report views.</summary>
    [NotMapped]
    public int SeedOrder => SeedPosition;

    [ForeignKey("TournamentId")]
    public Tournament? Tournament { get; set; }

    [ForeignKey("TeamId")]
    public Team? Team { get; set; }

    public ICollection<PoolTeam> PoolTeams { get; set; } = new List<PoolTeam>();
}
