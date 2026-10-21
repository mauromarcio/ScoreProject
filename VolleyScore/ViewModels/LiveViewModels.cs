// File: VolleyScore/ViewModels/LiveViewModels.cs
// Purpose: View models for the public-facing live score display screen (/Live)

using VolleyScore.Models;

namespace VolleyScore.ViewModels;

public class LiveDataViewModel
{
    public List<Match> LiveMatches { get; set; } = new();
    public List<TournamentPoolStandingsViewModel> TournamentStandings { get; set; } = new();
    public List<CompletedResultViewModel> CompletedResults { get; set; } = new();
    public DateTime GeneratedAt { get; set; } = DateTime.Now;
}

public class TournamentPoolStandingsViewModel
{
    public int TournamentId { get; set; }
    public string TournamentName { get; set; } = string.Empty;
    public List<PoolStandingsViewModel> PoolStandings { get; set; } = new();
    public List<TeamStanding> OverallStandings { get; set; } = new();
}

public class PoolStandingsViewModel
{
    public int PoolId { get; set; }
    public string PoolName { get; set; } = string.Empty;
    public List<TeamStanding> Standings { get; set; } = new();
}

public class CompletedResultViewModel
{
    public int HomeTeamId { get; set; }
    public string HomeTeamName { get; set; } = string.Empty;
    public int AwayTeamId { get; set; }
    public string AwayTeamName { get; set; } = string.Empty;
    public int HomeSets { get; set; }
    public int AwaySets { get; set; }
    public string Label { get; set; } = string.Empty;
    public string TournamentName { get; set; } = string.Empty;
    public string WinnerName => HomeSets > AwaySets ? HomeTeamName : AwayTeamName;
}
