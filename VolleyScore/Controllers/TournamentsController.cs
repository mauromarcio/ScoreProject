// File: VolleyScore/Controllers/TournamentsController.cs
// Purpose: Full tournament management – creation, team enrollment, pool setup,
//          round-robin schedule generation, knockout bracket, and standings report.

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using VolleyScore.Data;
using VolleyScore.Models;
using VolleyScore.ViewModels;

namespace VolleyScore.Controllers;

public class TournamentsController : Controller
{
    private readonly VolleyScoreContext _context;

    public TournamentsController(VolleyScoreContext context)
    {
        _context = context;
    }

    // ── Index ─────────────────────────────────────────────────────────────────

    public async Task<IActionResult> Index()
    {
        var tournaments = await _context.Tournaments
            .Include(t => t.TournamentTeams)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();
        return View(tournaments);
    }

    // ── Create ────────────────────────────────────────────────────────────────

    public IActionResult Create()
    {
        return View(new Tournament { MaxTeams = 50, SetsPerMatch = 1, InitialScore = 0 });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Tournament tournament)
    {
        if (!ModelState.IsValid) return View(tournament);

        tournament.Status = TournamentStatus.Setup;
        tournament.CreatedAt = DateTime.Now;
        _context.Tournaments.Add(tournament);
        await _context.SaveChangesAsync();

        TempData["Success"] = $"Tournament '{tournament.Name}' created. Enroll teams to get started.";
        return RedirectToAction(nameof(Manage), new { id = tournament.Id });
    }

    // ── Manage ────────────────────────────────────────────────────────────────

    public async Task<IActionResult> Manage(int id)
    {
        var tournament = await _context.Tournaments
            .Include(t => t.TournamentTeams).ThenInclude(tt => tt.Team)
            .Include(t => t.TournamentTeams).ThenInclude(tt => tt.PoolTeams)
            .Include(t => t.Pools).ThenInclude(p => p.PoolTeams).ThenInclude(pt => pt.TournamentTeam).ThenInclude(tt => tt!.Team)
            .Include(t => t.TournamentMatches).ThenInclude(tm => tm.Match).ThenInclude(m => m!.Sets)
            .Include(t => t.TournamentMatches).ThenInclude(tm => tm.HomeTeam)
            .Include(t => t.TournamentMatches).ThenInclude(tm => tm.AwayTeam)
            .Include(t => t.TournamentMatches).ThenInclude(tm => tm.Pool)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (tournament == null) return NotFound();

        // Teams not yet in any pool (for pool assignment UI)
        var pooledTournamentTeamIds = tournament.Pools
            .SelectMany(p => p.PoolTeams)
            .Select(pt => pt.TournamentTeamId)
            .ToHashSet();

        ViewBag.UnpooledTeams = tournament.TournamentTeams
            .Where(tt => !pooledTournamentTeamIds.Contains(tt.Id))
            .OrderBy(tt => tt.SeedPosition)
            .ToList();

        // Available teams to add (not yet enrolled)
        var enrolledTeamIds = tournament.TournamentTeams.Select(tt => tt.TeamId).ToHashSet();
        ViewBag.AvailableTeams = await _context.Teams
            .Where(t => !enrolledTeamIds.Contains(t.Id))
            .OrderBy(t => t.Name)
            .Select(t => new SelectListItem { Value = t.Id.ToString(), Text = t.Name })
            .ToListAsync();

        return View(tournament);
    }

    // ── Team enrollment ───────────────────────────────────────────────────────

    [HttpPost]
    public async Task<IActionResult> AddTeam(int id, int teamId)
    {
        var tournament = await _context.Tournaments
            .Include(t => t.TournamentTeams)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (tournament == null) return NotFound();
        if (tournament.TournamentTeams.Count >= tournament.MaxTeams)
        {
            TempData["Error"] = $"Tournament is full (max {tournament.MaxTeams} teams).";
            return RedirectToAction(nameof(Manage), new { id });
        }
        if (tournament.TournamentTeams.Any(tt => tt.TeamId == teamId))
        {
            TempData["Error"] = "Team is already enrolled.";
            return RedirectToAction(nameof(Manage), new { id });
        }

        int nextSeed = tournament.TournamentTeams.Any()
            ? tournament.TournamentTeams.Max(tt => tt.SeedPosition) + 1
            : 1;

        _context.TournamentTeams.Add(new TournamentTeam
        {
            TournamentId = id,
            TeamId = teamId,
            SeedPosition = nextSeed
        });
        await _context.SaveChangesAsync();

        return RedirectToAction(nameof(Manage), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> RemoveTeam(int id, int tournamentTeamId)
    {
        var tt = await _context.TournamentTeams.FindAsync(tournamentTeamId);
        if (tt != null && tt.TournamentId == id)
        {
            _context.TournamentTeams.Remove(tt);
            await _context.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Manage), new { id });
    }

    // ── Pool management ───────────────────────────────────────────────────────

    /// <summary>Auto-distribute enrolled teams into pools of the requested size.</summary>
    [HttpPost]
    public async Task<IActionResult> SuggestPools(int id, int poolSize = 4)
    {
        poolSize = Math.Clamp(poolSize, 2, 20);

        var tournament = await _context.Tournaments
            .Include(t => t.TournamentTeams)
            .Include(t => t.Pools).ThenInclude(p => p.PoolTeams)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (tournament == null) return NotFound();

        // Remove existing pool structure
        _context.Pools.RemoveRange(tournament.Pools);
        await _context.SaveChangesAsync();

        var teams = tournament.TournamentTeams.OrderBy(tt => tt.SeedPosition).ToList();
        int poolCount = (int)Math.Ceiling((double)teams.Count / poolSize);
        char poolLetter = 'A';

        for (int p = 0; p < poolCount; p++)
        {
            var pool = new Pool
            {
                TournamentId = id,
                Name = $"Pool {poolLetter++}",
                SortOrder = p
            };
            _context.Pools.Add(pool);
            await _context.SaveChangesAsync();

            // Snake seeding: Pool A gets seeds 1,4,5,8…  Pool B gets 2,3,6,7…
            // Simple slice: take teams[p*poolSize .. (p+1)*poolSize]
            var slice = teams.Skip(p * poolSize).Take(poolSize).ToList();
            for (int i = 0; i < slice.Count; i++)
            {
                _context.PoolTeams.Add(new PoolTeam
                {
                    PoolId = pool.Id,
                    TournamentTeamId = slice[i].Id,
                    SortOrder = i
                });
            }
            await _context.SaveChangesAsync();
        }

        TempData["Success"] = $"{poolCount} pool(s) created.";
        return RedirectToAction(nameof(Manage), new { id });
    }

    /// <summary>AJAX endpoint: persists drag-drop reordering of teams within pools.</summary>
    [HttpPost]
    public async Task<IActionResult> SavePoolOrder([FromBody] SavePoolOrderRequest req)
    {
        if (req?.Pools == null) return BadRequest(new { success = false });

        foreach (var poolData in req.Pools)
        {
            var poolTeams = await _context.PoolTeams
                .Where(pt => pt.PoolId == poolData.PoolId)
                .ToListAsync();

            for (int i = 0; i < poolData.TournamentTeamIds.Count; i++)
            {
                var pt = poolTeams.FirstOrDefault(x => x.TournamentTeamId == poolData.TournamentTeamIds[i]);
                if (pt != null) pt.SortOrder = i;
            }
        }

        await _context.SaveChangesAsync();
        return Ok(new { success = true });
    }

    // ── Schedule generation ───────────────────────────────────────────────────

    /// <summary>Generate round-robin TournamentMatch entries for every pool.</summary>
    [HttpPost]
    public async Task<IActionResult> GenerateSchedule(int id)
    {
        var tournament = await _context.Tournaments
            .Include(t => t.Pools).ThenInclude(p => p.PoolTeams).ThenInclude(pt => pt.TournamentTeam).ThenInclude(tt => tt!.Team)
            .Include(t => t.TournamentMatches)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (tournament == null) return NotFound();

        // Remove existing pool-stage matches
        var existing = tournament.TournamentMatches.Where(tm => tm.Phase == TournamentPhase.Pool).ToList();
        _context.TournamentMatches.RemoveRange(existing);
        await _context.SaveChangesAsync();

        int sort = 0;
        foreach (var pool in tournament.Pools.OrderBy(p => p.SortOrder))
        {
            var teamIds = pool.PoolTeams
                .OrderBy(pt => pt.SortOrder)
                .Select(pt => pt.TournamentTeam!.TeamId)
                .ToList();

            var pairs = RoundRobin(teamIds);
            int matchNum = 1;
            foreach (var (home, away) in pairs)
            {
                _context.TournamentMatches.Add(new TournamentMatch
                {
                    TournamentId = id,
                    PoolId = pool.Id,
                    Phase = TournamentPhase.Pool,
                    HomeTeamId = home,
                    AwayTeamId = away,
                    Label = $"{pool.Name} M{matchNum++}",
                    SortOrder = sort++
                });
            }
        }

        tournament.Status = TournamentStatus.PoolStage;
        await _context.SaveChangesAsync();

        TempData["Success"] = "Pool schedule generated. Open each match to record results.";
        return RedirectToAction(nameof(Manage), new { id });
    }

    // ── Start / open a tournament match ──────────────────────────────────────

    /// <summary>Creates the Match entity for a TournamentMatch and redirects to the court.</summary>
    public async Task<IActionResult> StartTournamentMatch(int id, int tournamentMatchId)
    {
        var tournament = await _context.Tournaments.FindAsync(id);
        var tm = await _context.TournamentMatches
            .Include(x => x.HomeTeam)
            .Include(x => x.AwayTeam)
            .FirstOrDefaultAsync(x => x.Id == tournamentMatchId);

        if (tournament == null || tm == null) return NotFound();

        // Resolve teams for knockout placeholders
        int? homeTeamId = tm.HomeTeamId;
        int? awayTeamId = tm.AwayTeamId;

        if (homeTeamId == null && tm.HomeSourceMatchId.HasValue)
            homeTeamId = await ResolveTeamFromSource(tm.HomeSourceMatchId.Value, tm.HomeFromWinner);
        if (awayTeamId == null && tm.AwaySourceMatchId.HasValue)
            awayTeamId = await ResolveTeamFromSource(tm.AwaySourceMatchId.Value, tm.AwayFromWinner);

        if (homeTeamId == null || awayTeamId == null)
        {
            TempData["Error"] = "Source matches have not completed yet — cannot start this match.";
            return RedirectToAction(nameof(Manage), new { id });
        }

        // Create the Match entity
        var match = new Match
        {
            MatchReference = tm.Label,
            HomeTeamId = homeTeamId.Value,
            AwayTeamId = awayTeamId.Value,
            TotalSets = tournament.SetsPerMatch,
            InitialScore = tournament.InitialScore,
            Status = MatchStatus.Setup,
            CurrentSetNumber = 1,
            HomeTeamOnLeft = true,
            ServingTeamId = homeTeamId.Value
        };
        _context.Matches.Add(match);
        await _context.SaveChangesAsync();

        // First set with handicap
        _context.GameSets.Add(new GameSet
        {
            MatchId = match.Id,
            SetNumber = 1,
            HomeIsServing = true,
            HomeScore = tournament.InitialScore,
            AwayScore = tournament.InitialScore
        });

        // Link TournamentMatch → Match; update team IDs for knockout placeholders
        tm.MatchId = match.Id;
        tm.HomeTeamId = homeTeamId.Value;
        tm.AwayTeamId = awayTeamId.Value;

        await _context.SaveChangesAsync();

        return RedirectToAction("Court", "Matches", new { id = match.Id });
    }

    // ── Knockout generation ───────────────────────────────────────────────────

    /// <summary>
    /// Computes overall standings from all pool matches and creates:
    ///   SF1: 1st vs 4th, SF2: 2nd vs 3rd
    ///   Final: SF1 winner vs SF2 winner
    ///   3rd place: SF1 loser vs SF2 loser
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> GenerateKnockouts(int id)
    {
        var tournament = await _context.Tournaments
            .Include(t => t.Pools).ThenInclude(p => p.PoolTeams).ThenInclude(pt => pt.TournamentTeam)
            .Include(t => t.TournamentMatches).ThenInclude(tm => tm.Match).ThenInclude(m => m!.Sets)
            .Include(t => t.TournamentMatches).ThenInclude(tm => tm.HomeTeam)
            .Include(t => t.TournamentMatches).ThenInclude(tm => tm.AwayTeam)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (tournament == null) return NotFound();

        // Remove existing knockout matches
        var existingKo = tournament.TournamentMatches
            .Where(tm => tm.Phase != TournamentPhase.Pool).ToList();
        _context.TournamentMatches.RemoveRange(existingKo);
        await _context.SaveChangesAsync();

        // Compute overall standings across all pools
        var standings = await ComputeOverallStandings(tournament);

        if (standings.Count < 4)
        {
            TempData["Error"] = "Need at least 4 teams in standings to generate knockouts.";
            return RedirectToAction(nameof(Manage), new { id });
        }

        int sort = 1000;

        // Semi-final 1: 1st vs 4th
        var sf1 = new TournamentMatch
        {
            TournamentId = id,
            Phase = TournamentPhase.SemiFinal,
            Label = "Semi-Final 1",
            HomeTeamId = standings[0].TeamId,
            AwayTeamId = standings[3].TeamId,
            SortOrder = sort++
        };
        _context.TournamentMatches.Add(sf1);

        // Semi-final 2: 2nd vs 3rd
        var sf2 = new TournamentMatch
        {
            TournamentId = id,
            Phase = TournamentPhase.SemiFinal,
            Label = "Semi-Final 2",
            HomeTeamId = standings[1].TeamId,
            AwayTeamId = standings[2].TeamId,
            SortOrder = sort++
        };
        _context.TournamentMatches.Add(sf2);
        await _context.SaveChangesAsync();

        // Final: winner of SF1 vs winner of SF2
        _context.TournamentMatches.Add(new TournamentMatch
        {
            TournamentId = id,
            Phase = TournamentPhase.Final,
            Label = "Final",
            HomeSourceMatchId = sf1.Id,
            HomeFromWinner = true,
            AwaySourceMatchId = sf2.Id,
            AwayFromWinner = true,
            SortOrder = sort++
        });

        // 3rd place: loser of SF1 vs loser of SF2
        _context.TournamentMatches.Add(new TournamentMatch
        {
            TournamentId = id,
            Phase = TournamentPhase.ThirdPlace,
            Label = "3rd Place",
            HomeSourceMatchId = sf1.Id,
            HomeFromWinner = false,
            AwaySourceMatchId = sf2.Id,
            AwayFromWinner = false,
            SortOrder = sort++
        });

        tournament.Status = TournamentStatus.Knockouts;
        await _context.SaveChangesAsync();

        TempData["Success"] = "Knockout bracket generated.";
        return RedirectToAction(nameof(Manage), new { id });
    }

    // ── Report ────────────────────────────────────────────────────────────────

    public async Task<IActionResult> Report(int id)
    {
        var tournament = await _context.Tournaments
            .Include(t => t.TournamentTeams).ThenInclude(tt => tt.Team)
            .Include(t => t.Pools).ThenInclude(p => p.PoolTeams).ThenInclude(pt => pt.TournamentTeam).ThenInclude(tt => tt!.Team)
            .Include(t => t.TournamentMatches).ThenInclude(tm => tm.Match).ThenInclude(m => m!.Sets)
            .Include(t => t.TournamentMatches).ThenInclude(tm => tm.HomeTeam)
            .Include(t => t.TournamentMatches).ThenInclude(tm => tm.AwayTeam)
            .Include(t => t.TournamentMatches).ThenInclude(tm => tm.Pool)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (tournament == null) return NotFound();

        // Per-pool standings
        var poolStandings = new Dictionary<int, List<TeamStanding>>();
        foreach (var pool in tournament.Pools.OrderBy(p => p.SortOrder))
        {
            poolStandings[pool.Id] = ComputePoolStandings(pool, tournament.TournamentMatches.ToList());
        }

        ViewBag.PoolStandings = poolStandings;
        ViewBag.OverallStandings = await ComputeOverallStandings(tournament);

        return View(tournament);
    }

    // ── Round-robin schedule generator ───────────────────────────────────────

    /// <summary>Circle method – generates all matchups for a group of teams.</summary>
    private static List<(int Home, int Away)> RoundRobin(List<int> teamIds)
    {
        var teams = teamIds.ToList();
        if (teams.Count % 2 != 0) teams.Add(0); // 0 = bye
        int n = teams.Count;
        var pairs = new List<(int, int)>();

        for (int round = 0; round < n - 1; round++)
        {
            for (int i = 0; i < n / 2; i++)
            {
                int home = teams[i];
                int away = teams[n - 1 - i];
                if (home != 0 && away != 0) pairs.Add((home, away));
            }
            // Rotate: keep index 0 fixed, rotate indices 1..n-1
            int last = teams[n - 1];
            for (int i = n - 1; i > 1; i--) teams[i] = teams[i - 1];
            teams[1] = last;
        }
        return pairs;
    }

    // ── Standings helpers ─────────────────────────────────────────────────────

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
            .Where(tm => tm.PoolId == pool.Id
                      && tm.Match != null
                      && tm.Match.Status == MatchStatus.Completed)
            .ToList();

        foreach (var tm in poolMatches)
        {
            var match = tm.Match!;
            int homeId = match.HomeTeamId;
            int awayId = match.AwayTeamId;
            if (!dict.ContainsKey(homeId) || !dict.ContainsKey(awayId)) continue;

            int homeSets = match.Sets.Count(s => s.WinnerTeamId == homeId);
            int awaySets = match.Sets.Count(s => s.WinnerTeamId == awayId);
            int homePoints = match.Sets.Sum(s => s.HomeScore);
            int awayPoints = match.Sets.Sum(s => s.AwayScore);

            dict[homeId].MatchesPlayed++;
            dict[awayId].MatchesPlayed++;
            dict[homeId].SetsWon    += homeSets;
            dict[homeId].SetsLost   += awaySets;
            dict[awayId].SetsWon    += awaySets;
            dict[awayId].SetsLost   += homeSets;
            dict[homeId].PointsScored   += homePoints;
            dict[homeId].PointsAllowed  += awayPoints;
            dict[awayId].PointsScored   += awayPoints;
            dict[awayId].PointsAllowed  += homePoints;

            if (homeSets > awaySets) { dict[homeId].MatchesWon++; dict[awayId].MatchesLost++; }
            else                     { dict[awayId].MatchesWon++; dict[homeId].MatchesLost++; }
        }

        return dict.Values
            .OrderByDescending(s => s.SetsWon)
            .ThenByDescending(s => s.PointsRatio)
            .ThenByDescending(s => s.PointsScored)
            .ToList();
    }

    private async Task<List<TeamStanding>> ComputeOverallStandings(Tournament tournament)
    {
        // Aggregate across all pools using a single ranking per team
        var allMatches = tournament.TournamentMatches.ToList();
        var combined = new Dictionary<int, TeamStanding>();

        foreach (var pool in tournament.Pools.OrderBy(p => p.SortOrder))
        {
            var standings = ComputePoolStandings(pool, allMatches);
            foreach (var s in standings)
            {
                if (!combined.ContainsKey(s.TeamId))
                    combined[s.TeamId] = new TeamStanding { TeamId = s.TeamId, TeamName = s.TeamName };

                combined[s.TeamId].MatchesPlayed  += s.MatchesPlayed;
                combined[s.TeamId].MatchesWon     += s.MatchesWon;
                combined[s.TeamId].MatchesLost    += s.MatchesLost;
                combined[s.TeamId].SetsWon        += s.SetsWon;
                combined[s.TeamId].SetsLost       += s.SetsLost;
                combined[s.TeamId].PointsScored   += s.PointsScored;
                combined[s.TeamId].PointsAllowed  += s.PointsAllowed;
            }
        }

        return combined.Values
            .OrderByDescending(s => s.SetsWon)
            .ThenByDescending(s => s.PointsRatio)
            .ThenByDescending(s => s.PointsScored)
            .ToList();
    }

    private async Task<int?> ResolveTeamFromSource(int sourceMatchId, bool fromWinner)
    {
        var source = await _context.TournamentMatches
            .Include(tm => tm.Match)
            .FirstOrDefaultAsync(tm => tm.Id == sourceMatchId);

        if (source?.Match == null || source.Match.Status != MatchStatus.Completed)
            return null;

        if (source.WinnerTeamId == null) return null;

        if (fromWinner) return source.WinnerTeamId;

        // Loser = the team that is NOT the winner
        var match = source.Match;
        return source.WinnerTeamId == match.HomeTeamId ? match.AwayTeamId : match.HomeTeamId;
    }
}

// ── Request / DTO models ──────────────────────────────────────────────────────

public class SavePoolOrderRequest
{
    public List<PoolOrderItem> Pools { get; set; } = new();
}

public class PoolOrderItem
{
    public int PoolId { get; set; }
    public List<int> TournamentTeamIds { get; set; } = new();
}

// TeamStanding is in VolleyScore.ViewModels.TournamentViewModels
