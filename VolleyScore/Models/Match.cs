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
    [Display(Name = "Sets to Play")]
    public int TotalSets { get; set; } = 1;

    /// <summary>
    /// Both teams start each set with this score (fair-play handicap).
    /// 0 = normal start. Example: 4 means both start the set at 4–4.
    /// </summary>
    [Range(0, 24, ErrorMessage = "Initial score must be between 0 and 24")]
    [Display(Name = "Initial Score (handicap)")]
    public int InitialScore { get; set; } = 0;

    [Display(Name = "Current Set")]
    public int CurrentSetNumber { get; set; } = 1;

    public MatchStatus Status { get; set; } = MatchStatus.Setup;

    /// <summary>True = Home team plays on the LEFT side of the court display</summary>
    public bool HomeTeamOnLeft { get; set; } = true;

    /// <summary>Which team currently has serve (null = not yet assigned)</summary>
    public int? ServingTeamId { get; set; }

    /// <summary>True for Doubles (beach 2v2) matches – only 2 players per side required to start.</summary>
    public bool IsDoubles { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

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
}
