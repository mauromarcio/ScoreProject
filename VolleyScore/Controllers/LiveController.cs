// File: VolleyScore/Controllers/LiveController.cs
// Purpose: Read-only public display screen for live scores, pool standings, and results.
//          Accessible via QR code from the court view — no admin navigation.

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VolleyScore.Data;
using VolleyScore.Models;
using VolleyScore.ViewModels;

namespace VolleyScore.Controllers;

public class LiveController : Controller
{
    private readonly VolleyScoreContext _context;

    public LiveController(VolleyScoreContext context)
    {
        _context = context;
    }

    // GET /Live
    public async Task<IActionResult> Index()
    {
        var data = await BuildLiveData();
        return View(data);
    }

    // GET /Live/Team/5
    public async Task<IActionResult> Team(int id)
    {
        var team = await _context.Teams.FindAsync(id);
        if (team == null) return NotFound();

        // Gather all tournament appearances for this team
        var appearances = await _context.TournamentMatches
            .Include(tm => tm.Tournament)
            .Include(tm => tm.Pool)
            .Include(tm => tm.Match).ThenInclude(m => m!.Sets)
            .Where(tm => (tm.HomeTeamId == id || tm.AwayTeamId == id) && tm.Match != null)
            .ToListAsync();

        ViewBag.Team = team;
        ViewBag.Appearances = appearances;
        return View();
    }

    // GET /Live/Data  — JSON polling endpoint
    public async Task<IActionResult> Data()
    {
        var data = await BuildLiveData();
        return Json(data);
    }

    // ── Internal data builder ─────────────────────────────────────────────────

    private async Task<LiveDataViewModel> BuildLiveData()
    {
        var tournaments = await _context.Tournaments
            .Where(t => t.Status != TournamentStatus.Setup && t.ShowResultsOnLive)
            .Include(t => t.TournamentTeams).ThenInclude(tt => tt.Team)
            .Include(t => t.Pools).ThenInclude(p => p.PoolTeams)
                .ThenInclude(pt => pt.TournamentTeam).ThenInclude(tt => tt!.Team)
            .Include(t => t.TournamentMatches).ThenInclude(tm => tm.Match).ThenInclude(m => m!.Sets)
            .Include(t => t.TournamentMatches).ThenInclude(tm => tm.HomeTeam)
            .Include(t => t.TournamentMatches).ThenInclude(tm => tm.AwayTeam)
            .Include(t => t.TournamentMatches).ThenInclude(tm => tm.Pool)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();

        // Live matches across all tournaments
        var liveMatches = await _context.Matches
            .Include(m => m.HomeTeam)
            .Include(m => m.AwayTeam)
            .Include(m => m.Sets)
            .Where(m => m.Status == MatchStatus.InProgress)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync();

        // Pool standings per tournament
        var poolStandingsPerTournament = new List<TournamentPoolStandingsViewModel>();
        foreach (var t in tournaments)
        {
            var poolStandings = new List<PoolStandingsViewModel>();
            foreach (var pool in t.Pools.OrderBy(p => p.SortOrder))
            {
                var standings = ComputePoolStandings(pool, t.TournamentMatches.ToList());
                poolStandings.Add(new PoolStandingsViewModel
                {
                    PoolId = pool.Id,
                    PoolName = pool.Name,
                    Standings = standings
                });
            }
            poolStandingsPerTournament.Add(new TournamentPoolStandingsViewModel
            {
                TournamentId = t.Id,
                TournamentName = t.Name,
                PoolStandings = poolStandings,
                OverallStandings = ComputeOverallStandings(t)
            });
        }

        // Completed match results — only for tournaments where ShowResultsOnLive is enabled
        var completedResults = tournaments
            .Where(t => t.ShowResultsOnLive)
            .SelectMany(t => t.TournamentMatches)
            .Where(tm => tm.Match?.Status == MatchStatus.Completed)
            .OrderByDescending(tm => tm.SortOrder)
            .Select(tm => new CompletedResultViewModel
            {
                HomeTeamId   = tm.HomeTeam?.Id ?? 0,
                HomeTeamName = tm.HomeTeam?.Name ?? "?",
                AwayTeamId   = tm.AwayTeam?.Id ?? 0,
                AwayTeamName = tm.AwayTeam?.Name ?? "?",
                HomeSets = tm.Match!.Sets.Count(s => s.WinnerTeamId == tm.Match.HomeTeamId),
                AwaySets = tm.Match!.Sets.Count(s => s.WinnerTeamId == tm.Match.AwayTeamId),
                Label    = tm.Label,
                TournamentName = tm.Tournament?.Name ?? ""
            })
            .ToList();

        return new LiveDataViewModel
        {
            LiveMatches = liveMatches,
            TournamentStandings = poolStandingsPerTournament,
            CompletedResults = completedResults,
            GeneratedAt = DateTime.Now
        };
    }

    private static List<TeamStanding> ComputePoolStandings(Pool pool, List<TournamentMatch> allMatches)
    {
        var poolTeamIds = pool.PoolTeams
            .Select(pt => pt.TournamentTeam!.TeamId)
            .ToHashSet();

        var dict = pool.PoolTeams
            .Select(pt => pt.TournamentTeam!.Team!)
            .DistinctBy(t => t.Id)
            .ToDictionary(t => t.Id, t => new TeamStanding { TeamId = t.Id, TeamName = t.Name });

        var poolMatches = allMatches
            .Where(tm => tm.PoolId == pool.Id && tm.Match != null && tm.Match.Status != MatchStatus.Setup)
            .ToList();

        foreach (var tm in poolMatches)
        {
            var match = tm.Match!;
            int homeId = match.HomeTeamId, awayId = match.AwayTeamId;
            if (!dict.ContainsKey(homeId) || !dict.ContainsKey(awayId)) continue;

            int homeSets  = match.Sets.Count(s => s.WinnerTeamId == homeId);
            int awaySets  = match.Sets.Count(s => s.WinnerTeamId == awayId);
            int homePts   = match.Sets.Sum(s => s.HomeScore);
            int awayPts   = match.Sets.Sum(s => s.AwayScore);

            dict[homeId].MatchesPlayed++; dict[awayId].MatchesPlayed++;
            dict[homeId].SetsWon  += homeSets;  dict[homeId].SetsLost  += awaySets;
            dict[awayId].SetsWon  += awaySets;  dict[awayId].SetsLost  += homeSets;
            dict[homeId].PointsMade += homePts; dict[homeId].PointsAgainst += awayPts;
            dict[awayId].PointsMade += awayPts; dict[awayId].PointsAgainst += homePts;

            if (match.Status == MatchStatus.Completed)
            {
                if (homeSets > awaySets)      { dict[homeId].MatchesWon++; dict[awayId].MatchesLost++; }
                else if (awaySets > homeSets) { dict[awayId].MatchesWon++; dict[homeId].MatchesLost++; }
            }
        }

        return dict.Values
            .OrderByDescending(s => s.SetsWon)
            .ThenByDescending(s => s.PointsRatio)
            .ThenByDescending(s => s.PointsMade)
            .ToList();
    }

    private static List<TeamStanding> ComputeOverallStandings(Tournament tournament)
    {
        var allMatches = tournament.TournamentMatches.ToList();
        var combined   = new Dictionary<int, TeamStanding>();

        foreach (var pool in tournament.Pools.OrderBy(p => p.SortOrder))
        {
            foreach (var s in ComputePoolStandings(pool, allMatches))
            {
                if (!combined.ContainsKey(s.TeamId))
                    combined[s.TeamId] = new TeamStanding { TeamId = s.TeamId, TeamName = s.TeamName };
                combined[s.TeamId].MatchesPlayed  += s.MatchesPlayed;
                combined[s.TeamId].MatchesWon     += s.MatchesWon;
                combined[s.TeamId].MatchesLost    += s.MatchesLost;
                combined[s.TeamId].SetsWon        += s.SetsWon;
                combined[s.TeamId].SetsLost       += s.SetsLost;
                combined[s.TeamId].PointsMade     += s.PointsMade;
                combined[s.TeamId].PointsAgainst  += s.PointsAgainst;
            }
        }

        return combined.Values
            .OrderByDescending(s => s.SetsWon)
            .ThenByDescending(s => s.PointsRatio)
            .ThenByDescending(s => s.PointsMade)
            .ToList();
    }
}
