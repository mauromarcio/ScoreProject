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

    /// <summary>Tournament format: Singles (6v6 indoor) or Doubles (2v2 beach/outdoor)</summary>
    [Display(Name = "Tournament Type")]
    public TournamentType TournamentType { get; set; } = TournamentType.Singles;

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

    /// <summary>Number of simultaneous courts used for Singles pool play (1-10).</summary>
    [Range(1, 10)]
    [Display(Name = "Number of Courts")]
    public int NumberOfCourts { get; set; } = 1;

    /// <summary>
    /// Number of pools to split doubles pairs into (Doubles only; 1 = all in one pool).
    /// Snake-seeding is used to distribute teams across pools.
    /// </summary>
    [Range(1, 20)]
    [Display(Name = "Number of Pools")]
    public int NumberOfPools { get; set; } = 1;

    /// <summary>
    /// Cross-pool match count: the top N teams from each pool also play the top N from every
    /// other pool. 0 = no cross-pool matches (within-pool round-robin only).
    /// Only applies when NumberOfPools > 1.
    /// </summary>
    [Range(0, 10)]
    [Display(Name = "Cross-Pool Teams")]
    public int CrossPoolMatchCount { get; set; } = 0;

    /// <summary>Teams advancing per pool to the knockout stage (default 2).</summary>
    [Range(1, 8)]
    [Display(Name = "Teams Advancing per Pool")]
    public int TeamsAdvancingPerPool { get; set; } = 2;

    public TournamentStatus Status { get; set; } = TournamentStatus.Setup;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    // Navigation
    public ICollection<TournamentTeam> TournamentTeams { get; set; } = new List<TournamentTeam>();
    public ICollection<TournamentMatch> TournamentMatches { get; set; } = new List<TournamentMatch>();
}
