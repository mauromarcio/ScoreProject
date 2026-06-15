// File: VolleyScore/Models/Match.cs
// Purpose: Represents a volleyball match between two teams, tracking state and configuration

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace VolleyScore.Models;

public class Match
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Match reference is required")]
    [StringLength(100)]
    [Display(Name = "Match Reference")]
    public string MatchReference { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Home Team")]
    public int HomeTeamId { get; set; }

    [Required]
    [Display(Name = "Away Team")]
    public int AwayTeamId { get; set; }

    [Required]
    [Range(1, 5, ErrorMessage = "Sets must be between 1 and 5")]
    [Display(Name = "Number of Sets")]
    public int TotalSets { get; set; } = 3;

    [Range(0, 24, ErrorMessage = "Initial score must be between 0 and 24")]
    [Display(Name = "Initial Score (Fair Play)")]
    public int InitialScore { get; set; } = 0;

    [Display(Name = "Current Set")]
    public int CurrentSetNumber { get; set; } = 1;

    public MatchStatus Status { get; set; } = MatchStatus.Setup;

    /// <summary>True = Home team plays on the LEFT side of the court display</summary>
    public bool HomeTeamOnLeft { get; set; } = true;

    /// <summary>Which team currently has serve (null = not yet assigned)</summary>
    public int? ServingTeamId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>Soft delete — match is hidden from normal views but data is preserved</summary>
    public bool IsDeleted { get; set; } = false;

    public DateTime? DeletedAt { get; set; }

    /// <summary>
    /// Optional point cap per set (e.g. 21 for capped pool play).
    /// Null or 0 = use standard thresholds (25 pts regular sets, 15 pts deciding set).
    /// </summary>
    public int? SetCap { get; set; }

    /// <summary>
    /// True for Doubles tournament matches (2v2). Only 2 players per side are required
    /// to start the match instead of the standard 6.
    /// </summary>
    public bool IsDoubles { get; set; } = false;

    // Navigation properties
    [ForeignKey("HomeTeamId")]
    public Team? HomeTeam { get; set; }

    [ForeignKey("AwayTeamId")]
    public Team? AwayTeam { get; set; }

    public ICollection<GameSet> Sets { get; set; } = new List<GameSet>();
    public ICollection<PlayerPosition> PlayerPositions { get; set; } = new List<PlayerPosition>();

    // Computed helpers
    [NotMapped]
    public int HomeSetsWon => Sets.Count(s => s.WinnerTeamId == HomeTeamId);
    [NotMapped]
    public int AwaySetsWon => Sets.Count(s => s.WinnerTeamId == AwayTeamId);
    [NotMapped]
    public int SetsToWin => (TotalSets / 2) + 1;
    [NotMapped]
    public int MaxSets => TotalSets;
}
