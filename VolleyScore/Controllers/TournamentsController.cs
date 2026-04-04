// File: VolleyScore/Controllers/TournamentsController.cs
// Purpose: Tournament management – creation, pool play scheduling, standings, and playoff bracket.

using Microsoft.AspNetCore.Mvc;
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

    // GET: Tournaments
    public async Task<IActionResult> Index()
    {
        var tournaments = await _context.Tournaments
            .Include(t => t.TournamentTeams)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();
        return View(tournaments);
    }

    // GET: Tournaments/Create
    public async Task<IActionResult> Create()
    {
        var teams = await _context.Teams.OrderBy(t => t.Name).ToListAsync();
        ViewBag.AllTeams = teams;
        return View(new Tournament { PoolSetsPerMatch = 2, PlayoffSetsPerMatch = 3 });
    }

    // POST: Tournaments/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Tournament tournament, int[] selectedTeamIds)
    {
        if (selectedTeamIds == null || selectedTeamIds.Length < 2)
            ModelState.AddModelError("", "Please select at least 2 teams.");

        if (selectedTeamIds != null && selectedTeamIds.Length > 50)
            ModelState.AddModelError("", "A tournament can have at most 50 teams.");

        if (!ModelState.IsValid)
        {
            ViewBag.AllTeams = await _context.Teams.OrderBy(t => t.Name).ToListAsync();
            return View(tournament);
        }

        tournament.Status = TournamentStatus.Setup;
        _context.Tournaments.Add(tournament);
        await _context.SaveChangesAsync();

        // Add teams with initial seed order
        for (int i = 0; i < selectedTeamIds!.Length; i++)
        {
            _context.TournamentTeams.Add(new TournamentTeam
            {
                TournamentId = tournament.Id,
                TeamId = selectedTeamIds[i],
                SeedOrder = i + 1
            });
        }
        await _context.SaveChangesAsync();

        TempData["Success"] = $"Tournament '{tournament.Name}' created with {selectedTeamIds.Length} teams.";
        return RedirectToAction(nameof(Details), new { id = tournament.Id });
    }

    // GET: Tournaments/Details/5
    public async Task<IActionResult> Details(int id)
    {
        var tournament = await LoadTournament(id);
        if (tournament == null) return NotFound();
        return View(tournament);
    }

    // POST: Tournaments/ReorderTeams
    // Saves user-reordered pool seed positions (drag-drop)
    [HttpPost]
    public async Task<IActionResult> ReorderTeams([FromBody] ReorderTeamsRequest req)
    {
        if (req?.TeamIds == null || req.TeamIds.Length == 0)
            return BadRequest(new { success = false });

        var ttEntries = await _context.TournamentTeams
            .Where(tt => tt.TournamentId == req.TournamentId)
            .ToListAsync();

        for (int i = 0; i < req.TeamIds.Length; i++)
        {
            var tt = ttEntries.FirstOrDefault(t => t.TeamId == req.TeamIds[i]);
            if (tt != null) tt.SeedOrder = i + 1;
        }

        await _context.SaveChangesAsync();
        return Ok(new { success = true });
    }

    // POST: Tournaments/GeneratePoolMatches/5
    // Creates all round-robin pool matches for the tournament
    [HttpPost]
    public async Task<IActionResult> GeneratePoolMatches(int id)
    {
        var tournament = await LoadTournament(id);
        if (tournament == null) return NotFound();

        if (tournament.Status != TournamentStatus.Setup)
            return BadRequest(new { success = false, error = "Pool matches already generated." });

        // Check that existing pool matches don't exist
        var existingPool = tournament.TournamentMatches
            .Any(tm => tm.Stage == TournamentStage.Pool);
        if (existingPool)
            return BadRequest(new { success = false, error = "Pool matches already exist." });

        var teams = tournament.TournamentTeams.OrderBy(tt => tt.SeedOrder).ToList();
        var teamLookup = teams.ToDictionary(tt => tt.TeamId, tt => tt.Team!);
        var teamIds = teams.Select(tt => tt.TeamId).ToList();

        // Build a schedule that assigns a referee to every match with these rules:
        //  1. The team that just refereed cannot play in the very next match.
        //  2. Among eligible options, schedule the teams that have waited longest first
        //     (keeps consecutive "off" streaks to ≤ 2 for typical pool sizes).
        var schedule = BuildPoolSchedule(teamIds);

        for (int i = 0; i < schedule.Count; i++)
        {
            var (homeId, awayId, refId) = schedule[i];
            int matchNumber = i + 1;

            var match = new Match
            {
                MatchReference = $"{tournament.Name} – Pool {matchNumber}: {teamLookup[homeId].Name} vs {teamLookup[awayId].Name}",
                HomeTeamId = homeId,
                AwayTeamId = awayId,
                TotalSets = tournament.PoolSetsPerMatch,
                InitialScore = tournament.InitialScore,
                Status = MatchStatus.Setup,
                CurrentSetNumber = 1,
                HomeTeamOnLeft = true,
                ServingTeamId = homeId
            };
            _context.Matches.Add(match);
            await _context.SaveChangesAsync();

            _context.GameSets.Add(new GameSet
            {
                MatchId = match.Id,
                SetNumber = 1,
                HomeIsServing = true,
                HomeScore = tournament.InitialScore,
                AwayScore = tournament.InitialScore
            });

            _context.TournamentMatches.Add(new TournamentMatch
            {
                TournamentId = tournament.Id,
                MatchId = match.Id,
                Stage = TournamentStage.Pool,
                MatchNumber = matchNumber,
                RefereeTeamId = refId
            });
        }

        tournament.Status = TournamentStatus.PoolPlay;
        await _context.SaveChangesAsync();

        TempData["Success"] = $"Generated {schedule.Count} pool matches.";
        return RedirectToAction(nameof(PoolPlay), new { id });
    }

    // GET: Tournaments/PoolPlay/5
    public async Task<IActionResult> PoolPlay(int id)
    {
        var tournament = await LoadTournament(id);
        if (tournament == null) return NotFound();

        var vm = BuildPoolViewModel(tournament);
        return View(vm);
    }

    // GET: Tournaments/Bracket/5
    public async Task<IActionResult> Bracket(int id)
    {
        var tournament = await LoadTournament(id);
        if (tournament == null) return NotFound();

        var vm = BuildBracketViewModel(tournament);
        return View(vm);
    }

    // POST: Tournaments/GeneratePlayoffs/5
    // Creates semi-final matches based on pool standings (top 4 teams)
    [HttpPost]
    public async Task<IActionResult> GeneratePlayoffs(int id)
    {
        var tournament = await LoadTournament(id);
        if (tournament == null) return NotFound();

        // Verify all pool matches are completed
        var poolMatches = tournament.TournamentMatches
            .Where(tm => tm.Stage == TournamentStage.Pool)
            .ToList();

        if (!poolMatches.Any())
            return BadRequest(new { success = false, error = "No pool matches found." });

        var incompletePool = poolMatches
            .Where(tm => tm.Match?.Status != MatchStatus.Completed)
            .Count();
        if (incompletePool > 0)
        {
            TempData["Warning"] = $"{incompletePool} pool match(es) not yet completed. Semi-finals generated anyway.";
        }

        // Check semi-finals don't already exist
        if (tournament.TournamentMatches.Any(tm => tm.Stage == TournamentStage.Semifinal))
            return BadRequest(new { success = false, error = "Playoffs already generated." });

        var standings = ComputeStandings(tournament);
        if (standings.Count < 4)
        {
            TempData["Error"] = "Need at least 4 teams to generate playoffs.";
            return RedirectToAction(nameof(PoolPlay), new { id });
        }

        // Semi-final 1: seed 1 vs seed 4
        await CreatePlayoffMatch(tournament, standings[0].TeamId, standings[3].TeamId,
            TournamentStage.Semifinal, 1,
            $"{tournament.Name} – SF1: {standings[0].TeamName} vs {standings[3].TeamName}");

        // Semi-final 2: seed 2 vs seed 3
        await CreatePlayoffMatch(tournament, standings[1].TeamId, standings[2].TeamId,
            TournamentStage.Semifinal, 2,
            $"{tournament.Name} – SF2: {standings[1].TeamName} vs {standings[2].TeamName}");

        tournament.Status = TournamentStatus.Playoffs;
        await _context.SaveChangesAsync();

        TempData["Success"] = "Semi-final matches generated.";
        return RedirectToAction(nameof(Bracket), new { id });
    }

    // POST: Tournaments/GenerateFinals/5
    // Creates the final and 3rd-place match once both semi-finals are complete
    [HttpPost]
    public async Task<IActionResult> GenerateFinals(int id)
    {
        var tournament = await LoadTournament(id);
        if (tournament == null) return NotFound();

        var semis = tournament.TournamentMatches
            .Where(tm => tm.Stage == TournamentStage.Semifinal)
            .OrderBy(tm => tm.MatchNumber)
            .ToList();

        if (semis.Count < 2)
        {
            TempData["Error"] = "Semi-finals not yet generated.";
            return RedirectToAction(nameof(Bracket), new { id });
        }

        var incomplete = semis.Where(tm => tm.Match?.Status != MatchStatus.Completed).Count();
        if (incomplete > 0)
        {
            TempData["Warning"] = $"{incomplete} semi-final(s) not complete. Finals generated anyway.";
        }

        // Already has finals?
        if (tournament.TournamentMatches.Any(tm => tm.Stage == TournamentStage.Final))
        {
            TempData["Error"] = "Finals already generated.";
            return RedirectToAction(nameof(Bracket), new { id });
        }

        var sf1 = semis[0].Match!;
        var sf2 = semis[1].Match!;

        // Determine winners and losers (use first/second team if not completed yet)
        int sf1WinnerId = sf1.Status == MatchStatus.Completed
            ? (sf1.Sets.Count(s => s.WinnerTeamId == sf1.HomeTeamId) >= sf1.SetsToWin ? sf1.HomeTeamId : sf1.AwayTeamId)
            : sf1.HomeTeamId;
        int sf1LoserId  = sf1WinnerId == sf1.HomeTeamId ? sf1.AwayTeamId : sf1.HomeTeamId;

        int sf2WinnerId = sf2.Status == MatchStatus.Completed
            ? (sf2.Sets.Count(s => s.WinnerTeamId == sf2.HomeTeamId) >= sf2.SetsToWin ? sf2.HomeTeamId : sf2.AwayTeamId)
            : sf2.HomeTeamId;
        int sf2LoserId  = sf2WinnerId == sf2.HomeTeamId ? sf2.AwayTeamId : sf2.HomeTeamId;

        var sf1Winner = await _context.Teams.FindAsync(sf1WinnerId);
        var sf1Loser  = await _context.Teams.FindAsync(sf1LoserId);
        var sf2Winner = await _context.Teams.FindAsync(sf2WinnerId);
        var sf2Loser  = await _context.Teams.FindAsync(sf2LoserId);

        // 3rd-place match: SF1 loser vs SF2 loser
        await CreatePlayoffMatch(tournament, sf1LoserId, sf2LoserId,
            TournamentStage.ThirdPlace, 1,
            $"{tournament.Name} – 3rd Place: {sf1Loser!.Name} vs {sf2Loser!.Name}");

        // Final: SF1 winner vs SF2 winner
        await CreatePlayoffMatch(tournament, sf1WinnerId, sf2WinnerId,
            TournamentStage.Final, 1,
            $"{tournament.Name} – Final: {sf1Winner!.Name} vs {sf2Winner!.Name}");

        await _context.SaveChangesAsync();

        TempData["Success"] = "Final and 3rd-place match generated.";
        return RedirectToAction(nameof(Bracket), new { id });
    }

    // POST: Tournaments/Delete/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var tournament = await _context.Tournaments.FindAsync(id);
        if (tournament == null) return NotFound();

        _context.Tournaments.Remove(tournament);
        await _context.SaveChangesAsync();

        TempData["Success"] = $"Tournament '{tournament.Name}' deleted.";
        return RedirectToAction(nameof(Index));
    }

    // GET: Tournaments/Manage/5  – combined management view (teams, matches, status)
    public async Task<IActionResult> Manage(int id)
    {
        var tournament = await LoadTournament(id);
        if (tournament == null) return NotFound();
        return View(tournament);
    }

    // GET: Tournaments/Report/5  – printable results report
    public async Task<IActionResult> Report(int id)
    {
        var tournament = await LoadTournament(id);
        if (tournament == null) return NotFound();
        var vm = BuildBracketViewModel(tournament);
        return View(vm);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<Tournament?> LoadTournament(int id)
    {
        return await _context.Tournaments
            .Include(t => t.TournamentTeams).ThenInclude(tt => tt.Team)
            .Include(t => t.TournamentMatches).ThenInclude(tm => tm.Match!.HomeTeam)
            .Include(t => t.TournamentMatches).ThenInclude(tm => tm.Match!.AwayTeam)
            .Include(t => t.TournamentMatches).ThenInclude(tm => tm.Match!.Sets)
            .Include(t => t.TournamentMatches).ThenInclude(tm => tm.RefereeTeam)
            .FirstOrDefaultAsync(t => t.Id == id);
    }

    private async Task CreatePlayoffMatch(Tournament tournament, int homeTeamId, int awayTeamId,
        TournamentStage stage, int matchNumber, string matchRef)
    {
        var match = new Match
        {
            MatchReference = matchRef,
            HomeTeamId = homeTeamId,
            AwayTeamId = awayTeamId,
            TotalSets = tournament.PlayoffSetsPerMatch,
            InitialScore = tournament.InitialScore,
            Status = MatchStatus.Setup,
            CurrentSetNumber = 1,
            HomeTeamOnLeft = true,
            ServingTeamId = homeTeamId
        };
        _context.Matches.Add(match);
        await _context.SaveChangesAsync();

        _context.GameSets.Add(new GameSet
        {
            MatchId = match.Id,
            SetNumber = 1,
            HomeIsServing = true,
            HomeScore = tournament.InitialScore,
            AwayScore = tournament.InitialScore
        });

        _context.TournamentMatches.Add(new TournamentMatch
        {
            TournamentId = tournament.Id,
            MatchId = match.Id,
            Stage = stage,
            MatchNumber = matchNumber
        });
    }

    /// <summary>
    /// Builds an ordered pool-play schedule where every match has one referee team.
    /// Rules enforced:
    ///   1. The team that just refereed cannot PLAY in the immediately following match.
    ///   2. At each step, the pair of teams that has waited longest (highest combined
    ///      consecutive-off streak) is scheduled first — this keeps any team's gap
    ///      between playing appearances to at most 2 matches in normal pool sizes.
    /// Returns a list of (homeTeamId, awayTeamId, refereeTeamId) in scheduled order.
    /// </summary>
    private static List<(int home, int away, int referee)> BuildPoolSchedule(List<int> teamIds)
    {
        // Generate all unique pairs
        var remaining = new List<(int a, int b)>();
        for (int i = 0; i < teamIds.Count; i++)
            for (int j = i + 1; j < teamIds.Count; j++)
                remaining.Add((teamIds[i], teamIds[j]));

        var result = new List<(int home, int away, int referee)>();
        // Consecutive matches each team has not been a player (ref counts as off for this streak)
        var offStreak = teamIds.ToDictionary(t => t, _ => 0);
        int? prevRef = null;

        while (remaining.Count > 0)
        {
            // Rule 1: skip any pair that would make the previous referee play
            var candidates = prevRef.HasValue
                ? remaining.Where(p => p.a != prevRef.Value && p.b != prevRef.Value).ToList()
                : remaining.ToList();

            // Edge case: if all remaining pairs involve prevRef (only possible with 3 teams at the
            // very end), relax the constraint rather than getting stuck.
            if (!candidates.Any())
                candidates = remaining.ToList();

            // Rule 2: pick the pair whose players have the largest combined waiting streak
            var pick = candidates
                .OrderByDescending(p => offStreak[p.a] + offStreak[p.b])
                .ThenByDescending(p => Math.Max(offStreak[p.a], offStreak[p.b]))
                .First();

            remaining.Remove(pick);

            // Referee: from non-playing teams, pick the one who has waited longest
            int referee = teamIds
                .Where(t => t != pick.a && t != pick.b)
                .OrderByDescending(t => offStreak[t])
                .First();

            result.Add((pick.a, pick.b, referee));
            prevRef = referee;

            // Update streaks: players reset to 0, everyone else increments
            foreach (var t in teamIds)
                offStreak[t] = (t == pick.a || t == pick.b) ? 0 : offStreak[t] + 1;
        }

        return result;
    }

    /// <summary>
    /// Computes pool standings sorted by: (1) sets won desc, (2) points ratio desc,
    /// (3) points made desc.
    /// </summary>
    public List<TeamStanding> ComputeStandings(Tournament tournament)
    {
        var teamDict = new Dictionary<int, TeamStanding>();

        foreach (var tt in tournament.TournamentTeams)
        {
            teamDict[tt.TeamId] = new TeamStanding
            {
                TeamId = tt.TeamId,
                TeamName = tt.Team?.Name ?? "Unknown",
                SeedOrder = tt.SeedOrder
            };
        }

        var poolMatches = tournament.TournamentMatches
            .Where(tm => tm.Stage == TournamentStage.Pool && tm.Match != null)
            .ToList();

        foreach (var tm in poolMatches)
        {
            var m = tm.Match!;
            if (m.Status != MatchStatus.Completed) continue;

            int homeId = m.HomeTeamId;
            int awayId = m.AwayTeamId;

            if (!teamDict.ContainsKey(homeId) || !teamDict.ContainsKey(awayId)) continue;

            var homeEntry = teamDict[homeId];
            var awayEntry = teamDict[awayId];

            homeEntry.MatchesPlayed++;
            awayEntry.MatchesPlayed++;

            foreach (var set in m.Sets.Where(s => s.IsCompleted))
            {
                homeEntry.PointsMade    += set.HomeScore;
                homeEntry.PointsAgainst += set.AwayScore;
                awayEntry.PointsMade    += set.AwayScore;
                awayEntry.PointsAgainst += set.HomeScore;

                if (set.WinnerTeamId == homeId) homeEntry.SetsWon++;
                else if (set.WinnerTeamId == awayId) awayEntry.SetsWon++;
            }
        }

        return teamDict.Values
            .OrderByDescending(s => s.SetsWon)
            .ThenByDescending(s => s.PointsRatio)
            .ThenByDescending(s => s.PointsMade)
            .ThenBy(s => s.SeedOrder)
            .ToList();
    }

    private TournamentPoolViewModel BuildPoolViewModel(Tournament tournament)
    {
        var poolMatches = tournament.TournamentMatches
            .Where(tm => tm.Stage == TournamentStage.Pool)
            .OrderBy(tm => tm.MatchNumber)
            .ToList();

        var standings = ComputeStandings(tournament);

        return new TournamentPoolViewModel
        {
            Tournament = tournament,
            PoolMatches = poolMatches,
            Standings = standings
        };
    }

    private TournamentBracketViewModel BuildBracketViewModel(Tournament tournament)
    {
        var standings = ComputeStandings(tournament);

        return new TournamentBracketViewModel
        {
            Tournament = tournament,
            Standings = standings,
            Semifinals = tournament.TournamentMatches
                .Where(tm => tm.Stage == TournamentStage.Semifinal)
                .OrderBy(tm => tm.MatchNumber).ToList(),
            ThirdPlaceMatch = tournament.TournamentMatches
                .FirstOrDefault(tm => tm.Stage == TournamentStage.ThirdPlace),
            FinalMatch = tournament.TournamentMatches
                .FirstOrDefault(tm => tm.Stage == TournamentStage.Final)
        };
    }
}

/// <summary>AJAX body for reordering pool teams via drag-drop</summary>
public class ReorderTeamsRequest
{
    public int TournamentId { get; set; }
    public int[] TeamIds { get; set; } = Array.Empty<int>();
}
