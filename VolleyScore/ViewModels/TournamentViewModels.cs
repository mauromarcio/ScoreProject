// File: VolleyScore/ViewModels/TournamentViewModels.cs
// Purpose: View models and DTOs for the tournament management and report screens.

namespace VolleyScore.ViewModels;

/// <summary>Computed team standing for display in pool tables and overall ranking.</summary>
public class TeamStanding
{
    public int TeamId { get; set; }
    public string TeamName { get; set; } = string.Empty;
    public int MatchesPlayed { get; set; }
    public int MatchesWon { get; set; }
    public int MatchesLost { get; set; }
    public int SetsWon { get; set; }
    public int SetsLost { get; set; }
    public int PointsScored { get; set; }
    public int PointsAllowed { get; set; }
    public double PointsRatio =>
        PointsAllowed > 0 ? Math.Round((double)PointsScored / PointsAllowed, 3) : PointsScored;
}
