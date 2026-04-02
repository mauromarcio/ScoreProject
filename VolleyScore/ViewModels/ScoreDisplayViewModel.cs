// File: VolleyScore/ViewModels/ScoreDisplayViewModel.cs
// Purpose: Minimal view model for the Part 2 big-screen score display

namespace VolleyScore.ViewModels;

public class ScoreDisplayViewModel
{
    public int MatchId { get; set; }
    public string HomeTeamName { get; set; } = string.Empty;
    public string AwayTeamName { get; set; } = string.Empty;
    public int HomeScore { get; set; }
    public int AwayScore { get; set; }
    public int HomeSetsWon { get; set; }
    public int AwaySetsWon { get; set; }
    public int CurrentSetNumber { get; set; }
    public int TotalSets { get; set; }
    public string MatchReference { get; set; } = string.Empty;
    public bool HomeIsServing { get; set; } = true;
}
