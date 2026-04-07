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
        var allTeamIds = teams.Select(tt => tt.TeamId).ToList();
        int courts = Math.Max(1, tournament.NumberOfCourts);

        // Full round-robin across ALL teams, distributed across courts for parallel play.
        // Every team still plays every other team exactly once (N*(N-1)/2 total matches).
        // Courts are purely organisational — they let multiple matches run in parallel.
        var schedule = BuildMultiCourtSchedule(allTeamIds, courts);

        int matchNumber = 1;
        foreach (var (homeId, awayId, refId, courtNum, homeLong, awayLong) in schedule)
        {
            var match = new Match
            {
                MatchReference = courts > 1
                    ? $"{tournament.Name} – C{courtNum}·{matchNumber}: {teamLookup[homeId].Name} vs {teamLookup[awayId].Name}"
                    : $"{tournament.Name} – Pool {matchNumber}: {teamLookup[homeId].Name} vs {teamLookup[awayId].Name}",
                HomeTeamId = homeId,
                AwayTeamId = awayId,
                TotalSets = tournament.PoolSetsPerMatch,
                InitialScore = tournament.InitialScore,
                SetCap = (tournament.PoolSetCap.HasValue && tournament.PoolSetCap.Value > 0)
                             ? tournament.PoolSetCap
                             : null,
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
                RefereeTeamId = refId,
                CourtNumber = courtNum,
                HomeTeamLongWait = homeLong,
                AwayTeamLongWait = awayLong
            });

            matchNumber++;
        }

        tournament.Status = TournamentStatus.PoolPlay;
        await _context.SaveChangesAsync();

        int total = schedule.Count;
        var courtDesc = courts > 1 ? $" distributed across {courts} courts" : "";
        TempData["Success"] = $"Generated {total} pool matches{courtDesc}.";
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
        var tournament = await _context.Tournaments
            .Include(t => t.TournamentMatches)
            .FirstOrDefaultAsync(t => t.Id == id);
        if (tournament == null) return NotFound();

        // Soft-delete every match linked to this tournament before removing the tournament record
        var matchIds = tournament.TournamentMatches.Select(tm => tm.MatchId).ToList();
        var matches = await _context.Matches
            .Where(m => matchIds.Contains(m.Id))
            .ToListAsync();

        foreach (var m in matches)
        {
            m.IsDeleted = true;
            m.DeletedAt = DateTime.Now;
        }
        await _context.SaveChangesAsync();

        _context.Tournaments.Remove(tournament);
        await _context.SaveChangesAsync();

        TempData["Success"] = $"Tournament '{tournament.Name}' deleted ({matches.Count} match(es) archived).";
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

    // GET: Tournaments/PoolPlayReport/5  – printable pool-play schedule grouped by court
    public async Task<IActionResult> PoolPlayReport(int id)
    {
        var tournament = await LoadTournament(id);
        if (tournament == null) return NotFound();
        return View(BuildPoolViewModel(tournament));
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
    ///
    /// - Every team plays every other team exactly once (N*(N-1)/2 total matches).
    /// - Courts are for time management only — multiple courts run in parallel.
    /// - Matches are consumed from circle-method rounds in order.  Within each
    ///   time slot K conflict-free matches are picked (one per court), so no team
    ///   appears twice in the same slot across courts.
    /// - When N = 3K (e.g. 6 teams / 2 courts) every team is always either playing
    ///   or refereeing — zero idle slots for all teams except the very last slot.
    /// - Referee assignments: idle teams (not playing this slot) are the candidates;
    ///   court-play affinity is preferred, then even distribution by refCount.
    /// - ★ flag (HomeTeamLongWait / AwayTeamLongWait): set when a team has been
    ///   idle for 3 or more consecutive slots before this match.
    /// </summary>
    private static List<(int home, int away, int referee, int court, bool homeLong, bool awayLong)> BuildMultiCourtSchedule(
        List<int> allTeamIds, int numCourts)
    {
        numCourts = Math.Max(1, numCourts);

        // ── Step 1: All match pairs in circle-method round order ─────────────
        var rounds    = GenerateRoundRobinRounds(allTeamIds);
        var remaining = rounds.SelectMany(r => r).ToList();

        // ── Step 2: Build time slots with score-based selection ──────────────
        // Score function enforces:
        //   mustPlay (sitStreak >= 2)  → +2000 per team in pair (force max-2-sit rule)
        //   hardRest (playStreak >= 2) → -5000 per team (prevent 3-in-a-row)
        //   softRest (playStreak == 1) → -300 per team  (prefer short rest)
        // For K=2 courts: all conflict-free pairs enumerated to find globally optimal.
        // For K=1 or K>2: greedy by score.
        var teamSitStreak  = allTeamIds.ToDictionary(t => t, _ => 0);
        var teamPlayStreak = allTeamIds.ToDictionary(t => t, _ => 0);
        var slots          = new List<List<(int a, int b, int court, bool aLong, bool bLong)>>();

        while (remaining.Count > 0)
        {
            var mustPlay = allTeamIds.Where(t => teamSitStreak[t]  >= 2).ToHashSet();
            var hardRest = allTeamIds.Where(t => teamPlayStreak[t] >= 2).ToHashSet();
            var softRest = allTeamIds.Where(t => teamPlayStreak[t] == 1).ToHashSet();

            int MatchScore((int a, int b) p) =>
                (mustPlay.Contains(p.a) ?  2000 : 0) + (mustPlay.Contains(p.b) ?  2000 : 0) +
                (hardRest.Contains(p.a) ? -5000 : 0) + (hardRest.Contains(p.b) ? -5000 : 0) +
                (softRest.Contains(p.a) ?  -300 : 0) + (softRest.Contains(p.b) ?  -300 : 0);

            var picks     = PickBestMatches(remaining, numCourts, MatchScore);
            var slotTeams = picks.SelectMany(p => new[] { p.a, p.b }).ToHashSet();

            slots.Add(picks.Select((p, i) =>
                (p.a, p.b, i + 1, teamSitStreak[p.a] >= 3, teamSitStreak[p.b] >= 3)).ToList());

            foreach (var t in allTeamIds)
            {
                if (slotTeams.Contains(t)) { teamPlayStreak[t]++; teamSitStreak[t]  = 0; }
                else                       { teamSitStreak[t]++;  teamPlayStreak[t] = 0; }
            }
        }

        // ── Step 3: Court-play affinity ───────────────────────────────────────
        // How many times has each team played on each court?
        // Referees are drawn preferentially from teams with affinity for that court.
        var courtAffinity = allTeamIds.ToDictionary(t => t, _ => new int[numCourts]);
        foreach (var slot in slots)
            foreach (var (a, b, c, _, _) in slot)
            {
                courtAffinity[a][c - 1]++;
                courtAffinity[b][c - 1]++;
            }

        // ── Step 4: Assign referees per slot ─────────────────────────────────
        // Only idle teams (not playing this slot) are candidates.
        // For N = 3K all idle teams become referees (zero idle).
        // Priority: no back-to-back ref on same court → court affinity → refCount.
        var refCount        = allTeamIds.ToDictionary(t => t, _ => 0);
        var lastRefPerCourt = new int[numCourts]; // 0 = no previous ref
        var result          = new List<(int home, int away, int referee, int court, bool homeLong, bool awayLong)>();

        foreach (var slot in slots)
        {
            var playingAtSlot = slot.SelectMany(m => new[] { m.a, m.b }).ToHashSet();
            var refsAtSlot    = new HashSet<int>();

            foreach (var (a, b, court, aLong, bLong) in slot)
            {
                int prevRef = lastRefPerCourt[court - 1];

                var idle  = allTeamIds
                    .Where(t => !playingAtSlot.Contains(t) && !refsAtSlot.Contains(t))
                    .ToList();
                var valid = idle.Where(t => prevRef == 0 || t != prevRef).ToList();
                if (!valid.Any()) valid = idle;
                if (!valid.Any()) valid = allTeamIds.Where(t => t != a && t != b).ToList();

                int referee = valid
                    .OrderByDescending(t => courtAffinity[t][court - 1])
                    .ThenBy(t => refCount[t])
                    .ThenBy(t => t)
                    .First();

                refCount[referee]++;
                lastRefPerCourt[court - 1] = referee;
                refsAtSlot.Add(referee);
                result.Add((a, b, referee, court, aLong, bLong));
            }
        }

        return result;
    }

    /// <summary>
    /// Picks the globally best set of up to <paramref name="numCourts"/> conflict-free
    /// matches from <paramref name="remaining"/> (mutating the list) using the provided
    /// score function.
    ///
    /// K=1 or single remaining: best single match by score.
    /// K=2: exhaustive O(N²) enumeration for globally optimal pair.
    /// K>2: greedy by score (N is large enough that enumeration is prohibitive).
    /// </summary>
    private static List<(int a, int b)> PickBestMatches(
        List<(int a, int b)> remaining,
        int numCourts,
        Func<(int a, int b), int> matchScore)
    {
        if (remaining.Count == 0) return new();

        if (numCourts == 1 || remaining.Count == 1)
        {
            var best = remaining
                .Select((p, i) => (p, i))
                .OrderByDescending(x => matchScore(x.p))
                .ThenBy(x => x.i)
                .First().p;
            remaining.Remove(best);
            return new() { best };
        }

        if (numCourts == 2)
        {
            // Exhaustively find the conflict-free pair with the highest combined score
            int bestScore = int.MinValue, bestIdxSum = int.MaxValue;
            int bi = -1, bj = -1;
            for (int i = 0; i < remaining.Count; i++)
            {
                int s1 = matchScore(remaining[i]);
                var (a1, b1) = remaining[i];
                for (int j = i + 1; j < remaining.Count; j++)
                {
                    var (a2, b2) = remaining[j];
                    if (a1 == a2 || a1 == b2 || b1 == a2 || b1 == b2) continue; // conflict
                    int s = s1 + matchScore(remaining[j]);
                    if (s > bestScore || (s == bestScore && i + j < bestIdxSum))
                    {
                        bestScore = s; bestIdxSum = i + j; bi = i; bj = j;
                    }
                }
            }
            if (bi >= 0)
            {
                var r1 = remaining[bi]; var r2 = remaining[bj];
                remaining.RemoveAt(bj); remaining.RemoveAt(bi); // higher index first!
                return new() { r1, r2 };
            }
            // No conflict-free pair found — return single best
            var solo = remaining
                .Select((p, i) => (p, i))
                .OrderByDescending(x => matchScore(x.p))
                .ThenBy(x => x.i)
                .First().p;
            remaining.Remove(solo);
            return new() { solo };
        }

        // K>2: greedy
        var result = new List<(int a, int b)>();
        var used   = new HashSet<int>();
        foreach (var p in remaining
            .Select((p, i) => (p, i))
            .OrderByDescending(x => matchScore(x.p))
            .ThenBy(x => x.i)
            .Select(x => x.p)
            .ToList())
        {
            if (result.Count >= numCourts) break;
            if (used.Contains(p.a) || used.Contains(p.b)) continue;
            result.Add(p);
            used.Add(p.a);
            used.Add(p.b);
            remaining.Remove(p);
        }
        return result;
    }

    // POST: Tournaments/ReorderMatches
    // Accepts an ordered array of TournamentMatch IDs and reassigns MatchNumbers
    // starting from the smallest MatchNumber in the group (so court positions stay stable).
    [HttpPost]
    public async Task<IActionResult> ReorderMatches([FromBody] ReorderMatchesRequest req)
    {
        if (req?.TournamentMatchIds == null || req.TournamentMatchIds.Length == 0)
            return BadRequest(new { success = false });

        var matches = await _context.TournamentMatches
            .Where(tm => req.TournamentMatchIds.Contains(tm.Id))
            .ToListAsync();

        if (matches.Count == 0) return BadRequest(new { success = false });

        int baseNumber = matches.Min(m => m.MatchNumber);
        var updated    = new List<object>();

        for (int i = 0; i < req.TournamentMatchIds.Length; i++)
        {
            var tm = matches.FirstOrDefault(m => m.Id == req.TournamentMatchIds[i]);
            if (tm == null) continue;
            tm.MatchNumber = baseNumber + i;
            updated.Add(new { id = tm.Id, matchNumber = tm.MatchNumber });
        }

        await _context.SaveChangesAsync();
        return Ok(new { success = true, matches = updated });
    }

    // POST: Tournaments/SwapReferees
    // Swaps the referee assignments between two TournamentMatch rows (AJAX).
    [HttpPost]
    public async Task<IActionResult> SwapReferees(int tm1Id, int tm2Id)
    {
        var tm1 = await _context.TournamentMatches
            .Include(m => m.RefereeTeam)
            .FirstOrDefaultAsync(m => m.Id == tm1Id);
        var tm2 = await _context.TournamentMatches
            .Include(m => m.RefereeTeam)
            .FirstOrDefaultAsync(m => m.Id == tm2Id);

        if (tm1 == null || tm2 == null) return NotFound();

        (tm1.RefereeTeamId, tm2.RefereeTeamId) = (tm2.RefereeTeamId, tm1.RefereeTeamId);
        await _context.SaveChangesAsync();

        // Reload navigation so we can return updated names
        await _context.Entry(tm1).Reference(m => m.RefereeTeam).LoadAsync();
        await _context.Entry(tm2).Reference(m => m.RefereeTeam).LoadAsync();

        return Json(new
        {
            tm1Id,
            tm1Ref = tm1.RefereeTeam?.Name ?? "—",
            tm2Id,
            tm2Ref = tm2.RefereeTeam?.Name ?? "—"
        });
    }

    /// <summary>
    /// Generates a round-robin fixture list using the circle/polygon method.
    /// Each returned inner list is one "round": all its pairs are non-conflicting
    /// (every team appears at most once). This guarantees matches in the same round
    /// can be played simultaneously without any team being double-booked.
    /// N even → N-1 rounds of N/2 matches each.
    /// N odd  → N rounds of (N-1)/2 matches each (one team sits out per round).
    /// </summary>
    private static List<List<(int a, int b)>> GenerateRoundRobinRounds(List<int> teamIds)
    {
        var rounds = new List<List<(int, int)>>();
        int n = teamIds.Count;
        if (n < 2) return rounds;

        var teams = new List<int>(teamIds);
        if (n % 2 == 1) teams.Add(-1); // -1 = phantom bye (odd teams)
        int total = teams.Count;       // always even after this point

        // The first team is fixed; the remaining total-1 teams rotate each round.
        var rotating = teams.Skip(1).ToList();

        for (int round = 0; round < total - 1; round++)
        {
            var current = new List<int> { teams[0] };
            current.AddRange(rotating);

            var roundMatches = new List<(int, int)>();
            for (int i = 0; i < total / 2; i++)
            {
                int home = current[i];
                int away = current[total - 1 - i];
                if (home != -1 && away != -1) // skip phantom bye
                    roundMatches.Add((home, away));
            }
            if (roundMatches.Any()) rounds.Add(roundMatches);

            // Rotate: move last element of rotating to the front
            var last = rotating[^1];
            rotating.RemoveAt(rotating.Count - 1);
            rotating.Insert(0, last);
        }

        return rounds;
    }

    /// <summary>
    /// Reorders a court's match list so that no team plays two matches in a row
    /// on that court. Uses a greedy "longest-wait-first" approach: at each step,
    /// skip any pair that shares a team with the previous match, then among valid
    /// candidates pick the one whose players have waited the most. Falls back to
    /// least-bad option if no fully valid candidate exists (unavoidable for very
    /// small queues).
    /// </summary>
    private static List<(int a, int b)> ReorderToAvoidConsecutive(
        List<(int a, int b)> pairs, List<int> allTeamIds)
    {
        if (pairs.Count <= 1) return pairs;

        var remaining = new List<(int a, int b)>(pairs);
        var result    = new List<(int a, int b)>();
        var offStreak = allTeamIds.ToDictionary(t => t, _ => 0);
        var lastTeams = new HashSet<int>();

        while (remaining.Count > 0)
        {
            // Prefer pairs where neither team just played on this court
            var candidates = remaining
                .Where(p => !lastTeams.Contains(p.a) && !lastTeams.Contains(p.b))
                .ToList();

            // Fallback: if every remaining pair shares a player with the last match
            if (!candidates.Any()) candidates = remaining.ToList();

            // Among valid candidates, pick the pair whose players have waited longest
            var pick = candidates
                .OrderByDescending(p => offStreak[p.a] + offStreak[p.b])
                .ThenByDescending(p => Math.Max(offStreak[p.a], offStreak[p.b]))
                .First();

            remaining.Remove(pick);
            result.Add(pick);

            lastTeams = new HashSet<int> { pick.a, pick.b };
            foreach (var t in allTeamIds)
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
                SeedOrder = tt.SeedOrder,
                CourtNumber = tt.CourtNumber
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

/// <summary>AJAX body for reordering pool matches via drag-drop</summary>
public class ReorderMatchesRequest
{
    public int[] TournamentMatchIds { get; set; } = Array.Empty<int>();
}
