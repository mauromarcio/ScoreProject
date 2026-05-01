// File: VolleyScore/Models/Tournament.cs
// Purpose: Represents a volleyball tournament with pools, schedule and knockout bracket

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
    public string? Description { get; set; }

    [Range(2, 50, ErrorMessage = "Max teams must be between 2 and 50")]
    [Display(Name = "Maximum Teams")]
    public int MaxTeams { get; set; } = 50;

    [Range(1, 5, ErrorMessage = "Sets per match must be 1 to 5")]
    [Display(Name = "Sets per Pool Match")]
    public int SetsPerMatch { get; set; } = 1;

    [Range(0, 24, ErrorMessage = "Initial score must be between 0 and 24")]
    [Display(Name = "Initial Score (handicap)")]
    public int InitialScore { get; set; } = 0;

    /// <summary>Number of courts available for simultaneous play in pool stage.</summary>
    [Range(1, 20)]
    [Display(Name = "Number of Courts")]
    public int NumberOfCourts { get; set; } = 1;

    public TournamentStatus Status { get; set; } = TournamentStatus.Setup;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    // Navigation
    public ICollection<TournamentTeam> TournamentTeams { get; set; } = new List<TournamentTeam>();
    public ICollection<Pool> Pools { get; set; } = new List<Pool>();
    public ICollection<TournamentMatch> TournamentMatches { get; set; } = new List<TournamentMatch>();
}
