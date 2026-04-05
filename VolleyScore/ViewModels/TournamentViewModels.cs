// File: VolleyScore/ViewModels/TournamentViewModels.cs
// Purpose: View models and standings DTO for tournament pool play and bracket views

using VolleyScore.Models;

namespace VolleyScore.ViewModels;

/// <summary>Accumulated pool-play statistics for a single team</summary>
public class TeamStanding
{
    public int TeamId { get; set; }
    public string TeamName { get; set; } = string.Empty;
    public int SeedOrder { get; set; }
    public int CourtNumber { get; set; } = 1;
    public int MatchesPlayed { get; set; }
    public int SetsWon { get; set; }
    public int PointsMade { get; set; }
    public int PointsAgainst { get; set; }
    public double PointsRatio => PointsAgainst == 0
        ? (PointsMade > 0 ? double.MaxValue : 1.0)
        : Math.Round((double)PointsMade / PointsAgainst, 4);
}

public class TournamentPoolViewModel
{
    public Tournament Tournament { get; set; } = null!;
    public List<TournamentMatch> PoolMatches { get; set; } = new();
    public List<TeamStanding> Standings { get; set; } = new();
}

public class TournamentBracketViewModel
{
    public Tournament Tournament { get; set; } = null!;
    public List<TeamStanding> Standings { get; set; } = new();
    public List<TournamentMatch> Semifinals { get; set; } = new();
    public TournamentMatch? ThirdPlaceMatch { get; set; }
    public TournamentMatch? FinalMatch { get; set; }
}
