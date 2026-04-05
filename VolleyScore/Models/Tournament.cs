// File: VolleyScore/Models/Tournament.cs
// Purpose: Represents a volleyball tournament with pool play and playoff bracket

using System.ComponentModel.DataAnnotations;

namespace VolleyScore.Models;

public class Tournament
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Tournament name is required")]
    [StringLength(100)]
    [Display(Name = "Tournament Name")]
    public string Name { get; set; } = string.Empty;

    [StringLength(300)]
    [Display(Name = "Description")]
    public string? Description { get; set; }

    /// <summary>Number of sets per pool match (default 2)</summary>
    [Range(1, 5)]
    [Display(Name = "Sets per Match (Pool)")]
    public int PoolSetsPerMatch { get; set; } = 2;

    /// <summary>Number of sets per playoff match (default 3)</summary>
    [Range(1, 5)]
    [Display(Name = "Sets per Match (Playoffs)")]
    public int PlayoffSetsPerMatch { get; set; } = 3;

    /// <summary>Initial score for fair play (0 = standard)</summary>
    [Range(0, 24)]
    [Display(Name = "Initial Score (Fair Play)")]
    public int InitialScore { get; set; } = 0;

    /// <summary>
    /// Point cap per set in pool play (0 or null = no cap, use standard 25/15).
    /// E.g. 21 means a set ends as soon as a team reaches 21 with a 2-point lead.
    /// </summary>
    [Range(0, 50)]
    [Display(Name = "Pool Set Cap (pts)")]
    public int? PoolSetCap { get; set; }

    /// <summary>Number of simultaneous courts used for pool play (1–10).</summary>
    [Range(1, 10)]
    [Display(Name = "Number of Courts")]
    public int NumberOfCourts { get; set; } = 1;

    public TournamentStatus Status { get; set; } = TournamentStatus.Setup;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    // Navigation
    public ICollection<TournamentTeam> TournamentTeams { get; set; } = new List<TournamentTeam>();
    public ICollection<TournamentMatch> TournamentMatches { get; set; } = new List<TournamentMatch>();
}
