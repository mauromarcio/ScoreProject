// File: VolleyScore/ViewModels/TournamentViewModels.cs
// Purpose: View models and DTOs for tournament management, pool play, bracket, and report screens.

using VolleyScore.Models;

namespace VolleyScore.ViewModels;

/// <summary>Computed team standing for pool tables, overall ranking, and bracket displays.</summary>
public class TeamStanding
{
    public int TeamId { get; set; }
    public string TeamName { get; set; } = string.Empty;
    public int MatchesPlayed { get; set; }
    public int MatchesWon { get; set; }
    public int MatchesLost { get; set; }
    public int SetsWon { get; set; }
    public int SetsLost { get; set; }
    public int PointsMade { get; set; }
    public int PointsAgainst { get; set; }

    public double PointsRatio =>
        PointsAgainst == 0 && PointsMade == 0 ? 0 :
        PointsAgainst == 0 ? double.MaxValue :
        Math.Round((double)PointsMade / PointsAgainst, 3);
}

/// <summary>ViewModel for pool-play management and printable pool schedule views.</summary>
public class TournamentPoolViewModel
{
    public Tournament Tournament { get; set; } = null!;
    public List<TournamentMatch> PoolMatches { get; set; } = new();
    public List<TeamStanding> Standings { get; set; } = new();
}

/// <summary>ViewModel for the playoff bracket and live-bracket display views.</summary>
public class TournamentBracketViewModel
{
    public Tournament Tournament { get; set; } = null!;
    public List<TeamStanding> Standings { get; set; } = new();
    public List<TournamentMatch> Semifinals { get; set; } = new();
    public TournamentMatch? FinalMatch { get; set; }
    public TournamentMatch? ThirdPlaceMatch { get; set; }
}
