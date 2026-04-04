// File: VolleyScore/Controllers/ScoreController.cs
// Purpose: Handles score point additions/subtractions, rotation logic, set/match completion,
//          and serves the Part 2 big-screen display view.
//          All score-change actions return JSON; SignalR broadcasts to display clients.

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using VolleyScore.Data;
using VolleyScore.Hubs;
using VolleyScore.Models;
using VolleyScore.ViewModels;

namespace VolleyScore.Controllers;

public class ScoreController : Controller
{
    private readonly VolleyScoreContext _context;
    private readonly IHubContext<ScoreHub> _hubContext;

    public ScoreController(VolleyScoreContext context, IHubContext<ScoreHub> hubContext)
    {
        _context = context;
        _hubContext = hubContext;
    }

    // ── Part 2: Display Screen ────────────────────────────────────────────────

    // GET: Score/GetCurrentScore?matchId=5
    // Fallback polling endpoint for display screen if SignalR is unavailable
    [HttpGet]
    public async Task<IActionResult> GetCurrentScore(int matchId)
    {
        var match = await _context.Matches
            .Include(m => m.HomeTeam)
            .Include(m => m.AwayTeam)
            .Include(m => m.Sets)
            .FirstOrDefaultAsync(m => m.Id == matchId);

        if (match == null) return NotFound(new { success = false });

        var set = match.Sets.FirstOrDefault(s => s.SetNumber == match.CurrentSetNumber)
                 ?? match.Sets.OrderBy(s => s.SetNumber).Last();

        return Ok(new
        {
            success = true,
            homeScore = set.HomeScore,
            awayScore = set.AwayScore,
            homeSetsWon = match.Sets.Count(s => s.WinnerTeamId == match.HomeTeamId),
            awaySetsWon = match.Sets.Count(s => s.WinnerTeamId == match.AwayTeamId),
            currentSetNumber = match.CurrentSetNumber,
            homeIsServing = set.HomeIsServing,
            matchCompleted = match.Status == MatchStatus.Completed,
            homeTeamOnLeft = match.HomeTeamOnLeft
        });
    }

    // GET: Score/Display/5
    public async Task<IActionResult> Display(int id)
    {
        var match = await _context.Matches
            .Include(m => m.HomeTeam)
            .Include(m => m.AwayTeam)
            .Include(m => m.Sets)
            .FirstOrDefaultAsync(m => m.Id == id);

        if (match == null) return NotFound();

        var currentSet = match.Sets.FirstOrDefault(s => s.SetNumber == match.CurrentSetNumber)
                        ?? match.Sets.OrderBy(s => s.SetNumber).Last();

        var vm = new ScoreDisplayViewModel
        {
            MatchId = match.Id,
            HomeTeamName = match.HomeTeam!.Name,
            AwayTeamName = match.AwayTeam!.Name,
            HomeScore = currentSet.HomeScore,
            AwayScore = currentSet.AwayScore,
            HomeSetsWon = match.Sets.Count(s => s.WinnerTeamId == match.HomeTeamId),
            AwaySetsWon = match.Sets.Count(s => s.WinnerTeamId == match.AwayTeamId),
            CurrentSetNumber = match.CurrentSetNumber,
            TotalSets = match.TotalSets,
            MatchReference = match.MatchReference,
            HomeIsServing = currentSet.HomeIsServing,
            HomeTeamOnLeft = match.HomeTeamOnLeft
        };

        return View(vm);
    }

    // ── Score mutation endpoints ──────────────────────────────────────────────

    // POST: Score/AddPoint  {matchId, team:"Home"|"Away"}
    [HttpPost]
    public async Task<IActionResult> AddPoint([FromBody] ScoreRequest req)
    {
        return await MutateScore(req, +1);
    }

    // POST: Score/SubtractPoint  {matchId, team:"Home"|"Away"}
    [HttpPost]
    public async Task<IActionResult> SubtractPoint([FromBody] ScoreRequest req)
    {
        return await MutateScore(req, -1);
    }

    // POST: Score/SetServing  {matchId, team:"Home"|"Away"}
    [HttpPost]
    public async Task<IActionResult> SetServing([FromBody] ScoreRequest req)
    {
        var (match, currentSet, error) = await LoadMatchSet(req.MatchId);
        if (error != null) return error;

        currentSet!.HomeIsServing = req.Team == "Home";
        match!.ServingTeamId = req.Team == "Home" ? match.HomeTeamId : match.AwayTeamId;
        await _context.SaveChangesAsync();

        var result = await BuildResult(match, currentSet, false, false);
        await BroadcastScore(match.Id, result);
        return Ok(result);
    }

    // ── Core mutation logic ───────────────────────────────────────────────────

    private async Task<IActionResult> MutateScore(ScoreRequest req, int delta)
    {
        if (req == null)
            return BadRequest(new ScoreUpdateResult { Success = false, Error = "Invalid request" });

        var (match, currentSet, errorResult) = await LoadMatchSet(req.MatchId);
        if (errorResult != null) return errorResult;

        bool isHome = req.Team == "Home";
        int minScore = match!.InitialScore;

        // Apply delta (never go below InitialScore)
        if (isHome)
            currentSet!.HomeScore = Math.Max(minScore, currentSet.HomeScore + delta);
        else
            currentSet!.AwayScore = Math.Max(minScore, currentSet.AwayScore + delta);

        bool setCompleted = false;
        bool matchCompleted = false;
        string? winnerName = null;

        // ── Rotation logic (only on point gain, not subtract) ─────────────────
        if (delta > 0)
        {
            bool scorerIsHome = isHome;
            bool serverIsHome = currentSet.HomeIsServing;

            // If the team that was NOT serving wins the rally → they earn the serve and rotate
            if (scorerIsHome != serverIsHome)
            {
                if (scorerIsHome)
                {
                    // Home team earns serve: rotate home players
                    currentSet.HomeIsServing = true;
                    currentSet.HomeRotationIndex = (currentSet.HomeRotationIndex + 1) % 6;
                    match!.ServingTeamId = match.HomeTeamId;
                    await RotatePlayers(match.Id, currentSet.SetNumber, "Home", currentSet.HomeRotationIndex);
                }
                else
                {
                    // Away team earns serve: rotate away players
                    currentSet.HomeIsServing = false;
                    currentSet.AwayRotationIndex = (currentSet.AwayRotationIndex + 1) % 6;
                    match!.ServingTeamId = match.AwayTeamId;
                    await RotatePlayers(match.Id, currentSet.SetNumber, "Away", currentSet.AwayRotationIndex);
                }
            }

            // ── Check for set win ─────────────────────────────────────────────
            // Deciding set (odd TotalSets > 1, last set) plays to 15.
            // Even TotalSets (best-of-2 / best-of-4): all sets play to 25 — no tiebreaker.
            // A SetCap (e.g. 21) caps the threshold for pool play.
            bool isDecidingSet = match!.TotalSets % 2 == 1
                                  && match.TotalSets > 1
                                  && match.CurrentSetNumber == match.MaxSets;
            int winThreshold = isDecidingSet ? 15 : 25;
            if (match.SetCap.HasValue && match.SetCap.Value > 0)
                winThreshold = Math.Min(winThreshold, match.SetCap.Value);

            int homeS = currentSet.HomeScore;
            int awayS = currentSet.AwayScore;

            bool homeWinsSet = homeS >= winThreshold && (homeS - awayS) >= 2;
            bool awayWinsSet = awayS >= winThreshold && (awayS - homeS) >= 2;

            if (homeWinsSet || awayWinsSet)
            {
                currentSet.IsCompleted = true;
                currentSet.CompletedAt = DateTime.Now;
                currentSet.WinnerTeamId = homeWinsSet ? match.HomeTeamId : match.AwayTeamId;
                setCompleted = true;

                int homeSets = match.Sets.Count(s => s.WinnerTeamId == match.HomeTeamId);
                int awaySets = match.Sets.Count(s => s.WinnerTeamId == match.AwayTeamId);

                if (homeSets >= match.SetsToWin || awaySets >= match.SetsToWin)
                {
                    // Clear winner reached the required sets
                    match.Status = MatchStatus.Completed;
                    matchCompleted = true;
                    winnerName = homeWinsSet ? match.HomeTeam!.Name : match.AwayTeam!.Name;
                }
                else if (match.CurrentSetNumber >= match.MaxSets)
                {
                    // All sets played, no team reached SetsToWin (even-set draw).
                    // Standings are computed from individual set results.
                    match.Status = MatchStatus.Completed;
                    matchCompleted = true;
                    // winnerName stays null — the UI will show "Match complete" without a winner banner
                }
                else
                {
                    // Start next set
                    int nextSetNumber = match.CurrentSetNumber + 1;
                    match.CurrentSetNumber = nextSetNumber;

                    // Server in next set: team that lost previous set serves first
                    bool nextHomeServes = !homeWinsSet;

                    var nextSet = new GameSet
                    {
                        MatchId = match.Id,
                        SetNumber = nextSetNumber,
                        HomeIsServing = nextHomeServes,
                        HomeScore = match.InitialScore,
                        AwayScore = match.InitialScore
                    };
                    _context.GameSets.Add(nextSet);
                    match.ServingTeamId = nextHomeServes ? match.HomeTeamId : match.AwayTeamId;

                    // Copy positions from previous set into next set
                    await CopyPositionsToNextSet(match.Id, currentSet.SetNumber, nextSetNumber);
                }
            }
        }

        await _context.SaveChangesAsync();

        // Reload to get fresh set data
        var updatedMatch = await _context.Matches
            .Include(m => m.HomeTeam)
            .Include(m => m.AwayTeam)
            .Include(m => m.Sets)
            .FirstOrDefaultAsync(m => m.Id == req.MatchId);

        var activeSet = updatedMatch!.Sets.FirstOrDefault(s => s.SetNumber == updatedMatch.CurrentSetNumber)
                       ?? updatedMatch.Sets.OrderBy(s => s.SetNumber).Last();

        var result = await BuildResult(updatedMatch, activeSet, setCompleted, matchCompleted);
        result.WinnerName = winnerName;

        await BroadcastScore(updatedMatch.Id, result);
        return Ok(result);
    }

    // ── Rotation helper ───────────────────────────────────────────────────────

    /// <summary>
    /// Rotates player positions clockwise for the given side.
    /// Uses delete + re-insert instead of in-place UPDATE to avoid the EF Core
    /// circular-dependency error that occurs when all 6 rows shift simultaneously
    /// against the unique index on (MatchId, SetNumber, Side, Position).
    /// EF Core always executes DELETEs before INSERTs in the same SaveChanges batch,
    /// so there is no transient unique-constraint conflict.
    /// Rotation map: 1→6, 2→1, 3→2, 4→3, 5→4, 6→5
    /// </summary>
    private async Task RotatePlayers(int matchId, int setNumber, string side, int rotationIndex)
    {
        var positions = await _context.PlayerPositions
            .Where(pp => pp.MatchId == matchId && pp.SetNumber == setNumber && pp.Side == side)
            .ToListAsync();

        if (!positions.Any()) return;

        // Stage all deletes first
        _context.PlayerPositions.RemoveRange(positions);

        // Stage re-inserts with rotated position numbers
        foreach (var pp in positions)
        {
            _context.PlayerPositions.Add(new PlayerPosition
            {
                MatchId   = pp.MatchId,
                SetNumber = pp.SetNumber,
                PlayerId  = pp.PlayerId,
                Side      = pp.Side,
                Position  = pp.Position switch
                {
                    1 => 6,
                    2 => 1,
                    3 => 2,
                    4 => 3,
                    5 => 4,
                    6 => 5,
                    _ => pp.Position
                }
            });
        }
    }

    // ── Copy positions to new set ─────────────────────────────────────────────

    private async Task CopyPositionsToNextSet(int matchId, int fromSet, int toSet)
    {
        var existingPositions = await _context.PlayerPositions
            .Where(pp => pp.MatchId == matchId && pp.SetNumber == fromSet)
            .ToListAsync();

        foreach (var pos in existingPositions)
        {
            _context.PlayerPositions.Add(new PlayerPosition
            {
                MatchId = matchId,
                SetNumber = toSet,
                PlayerId = pos.PlayerId,
                Position = pos.Position,
                Side = pos.Side
            });
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<(Match? match, GameSet? set, IActionResult? error)> LoadMatchSet(int matchId)
    {
        var match = await _context.Matches
            .Include(m => m.HomeTeam)
            .Include(m => m.AwayTeam)
            .Include(m => m.Sets)
            .FirstOrDefaultAsync(m => m.Id == matchId);

        if (match == null)
            return (null, null, NotFound(new ScoreUpdateResult { Success = false, Error = "Match not found" }));

        if (match.Status == MatchStatus.Completed)
            return (null, null, BadRequest(new ScoreUpdateResult { Success = false, Error = "Match is already completed" }));

        var set = match.Sets.FirstOrDefault(s => s.SetNumber == match.CurrentSetNumber);
        if (set == null)
            return (null, null, BadRequest(new ScoreUpdateResult { Success = false, Error = "Current set not found" }));

        return (match, set, null);
    }

    private async Task<ScoreUpdateResult> BuildResult(Match match, GameSet set, bool setCompleted, bool matchCompleted)
    {
        // Load current positions for court update
        var homePositions = await _context.PlayerPositions
            .Include(pp => pp.Player)
            .Where(pp => pp.MatchId == match.Id && pp.SetNumber == set.SetNumber && pp.Side == "Home")
            .ToListAsync();
        var awayPositions = await _context.PlayerPositions
            .Include(pp => pp.Player)
            .Where(pp => pp.MatchId == match.Id && pp.SetNumber == set.SetNumber && pp.Side == "Away")
            .ToListAsync();

        List<PositionDto> ToPositionDtos(List<PlayerPosition> positions) =>
            Enumerable.Range(1, 6).Select(pos =>
            {
                var pp = positions.FirstOrDefault(p => p.Position == pos);
                return new PositionDto
                {
                    Position = pos,
                    Player = pp?.Player == null ? null : new PlayerDto
                    {
                        Id = pp.Player.Id,
                        Name = pp.Player.Name,
                        Number = pp.Player.Number,
                        TeamId = pp.Player.TeamId
                    }
                };
            }).ToList();

        return new ScoreUpdateResult
        {
            Success = true,
            HomeScore = set.HomeScore,
            AwayScore = set.AwayScore,
            HomeSetsWon = match.Sets.Count(s => s.WinnerTeamId == match.HomeTeamId),
            AwaySetsWon = match.Sets.Count(s => s.WinnerTeamId == match.AwayTeamId),
            CurrentSetNumber = match.CurrentSetNumber,
            HomeIsServing = set.HomeIsServing,
            HomeTeamOnLeft = match.HomeTeamOnLeft,
            SetCompleted = setCompleted,
            MatchCompleted = matchCompleted,
            HomeCourtPositions = ToPositionDtos(homePositions),
            AwayCourtPositions = ToPositionDtos(awayPositions)
        };
    }

    private async Task BroadcastScore(int matchId, ScoreUpdateResult result)
    {
        await _hubContext.Clients.Group($"match-{matchId}").SendAsync("ScoreUpdated", result);
    }
}

/// <summary>Body for AddPoint / SubtractPoint / SetServing AJAX requests</summary>
public class ScoreRequest
{
    public int MatchId { get; set; }
    /// <summary>"Home" or "Away"</summary>
    public string Team { get; set; } = string.Empty;
}
