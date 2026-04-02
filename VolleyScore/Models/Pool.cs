// File: VolleyScore/Models/Pool.cs
// Purpose: Represents a pool (group) within a tournament for round-robin play

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace VolleyScore.Models;

public class Pool
{
    public int Id { get; set; }
    public int TournamentId { get; set; }

    [Required]
    [StringLength(50)]
    [Display(Name = "Pool Name")]
    public string Name { get; set; } = string.Empty; // e.g. "Pool A"

    public int SortOrder { get; set; }

    [ForeignKey("TournamentId")]
    public Tournament? Tournament { get; set; }

    public ICollection<PoolTeam> PoolTeams { get; set; } = new List<PoolTeam>();
    public ICollection<TournamentMatch> Matches { get; set; } = new List<TournamentMatch>();
}
