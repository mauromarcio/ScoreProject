// File: VolleyScore/Controllers/MatchesController.cs
// Purpose: Match creation/management and the main court operational view.
//          Also handles player position assignment (drag-drop save) and side switching.

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using VolleyScore.Data;
using VolleyScore.Hubs;
using VolleyScore.Models;
using VolleyScore.ViewModels;

namespace VolleyScore.Controllers;

public class MatchesController : Controller
{
    private readonly VolleyScoreContext _context;
    private readonly IHubContext<ScoreHub> _hubContext;

    public MatchesController(VolleyScoreContext context, IHubContext<ScoreHub> hubContext)
    {
        _context = context;
        _hubContext = hubContext;
    }

    // GET: Matches
    public async Task<IActionResult> Index()
    {
        var matches = await _context.Matches
            .Include(m => m.HomeTeam)
            .Include(m => m.AwayTeam)
            .Include(m => m.Sets)
            .OrderByDescending(m => m.CreatedAt)
            .ToListAsync();
        return View(matches);
    }

    // GET: Matches/Create
    public async Task<IActionResult> Create()
    {
        var teams = await _context.Teams.OrderBy(t => t.Name).ToListAsync();
        ViewBag.HomeTeams = new SelectList(teams, "Id", "Name");
        ViewBag.AwayTeams = new SelectList(teams, "Id", "Name");
        return View(new Match { TotalSets = 3, InitialScore = 0 });
    }

    // POST: Matches/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Match match)
    {
        if (match.HomeTeamId == match.AwayTeamId)
            ModelState.AddModelError("AwayTeamId", "Away team must be different from home team.");

        if (!ModelState.IsValid)
        {
            var teams = await _context.Teams.OrderBy(t => t.Name).ToListAsync();
            ViewBag.HomeTeams = new SelectList(teams, "Id", "Name", match.HomeTeamId);
            ViewBag.AwayTeams = new SelectList(teams, "Id", "Name", match.AwayTeamId);
            return View(match);
        }

        match.Status = MatchStatus.Setup;
        match.CurrentSetNumber = 1;
        match.HomeTeamOnLeft = true;
        match.ServingTeamId = match.HomeTeamId;

        _context.Matches.Add(match);
        await _context.SaveChangesAsync();

        // Create the first set record (apply handicap initial score if set)
        var firstSet = new GameSet
        {
            MatchId = match.Id,
            SetNumber = 1,
            HomeIsServing = true,
            HomeScore = match.InitialScore,
            AwayScore = match.InitialScore
        };
        _context.GameSets.Add(firstSet);
        await _context.SaveChangesAsync();

        TempData["Success"] = $"Match '{match.MatchReference}' created. Set up player positions to begin.";
        return RedirectToAction(nameof(Court), new { id = match.Id });
    }

    // GET: Matches/Court/5 - Main operational court screen
    public async Task<IActionResult> Court(int id)
    {
        var match = await _context.Matches
            .Include(m => m.HomeTeam).ThenInclude(t => t!.Players)
            .Include(m => m.AwayTeam).ThenInclude(t => t!.Players)
            .Include(m => m.Sets)
            .Include(m => m.PlayerPositions).ThenInclude(pp => pp.Player)
            .FirstOrDefaultAsync(m => m.Id == id);

        if (match == null) return NotFound();

        var currentSet = match.Sets.FirstOrDefault(s => s.SetNumber == match.CurrentSetNumber)
                        ?? match.Sets.OrderBy(s => s.SetNumber).Last();

        var vm = BuildCourtViewModel(match, currentSet);
        return View(vm);
    }

    // POST: Matches/SavePosition - Called via AJAX when a player is drag-dropped onto a position
    [HttpPost]
    public async Task<IActionResult> SavePosition(
        [FromBody] SavePositionRequest req)
    {
        if (req == null)
            return BadRequest(new { success = false, error = "Invalid request" });

        var match = await _context.Matches.FindAsync(req.MatchId);
        if (match == null)
            return NotFound(new { success = false, error = "Match not found" });

        // Remove any existing assignment for this player in this set
        var existing = await _context.PlayerPositions
            .Where(pp => pp.MatchId == req.MatchId
                      && pp.SetNumber == req.SetNumber
                      && pp.PlayerId == req.PlayerId)
            .ToListAsync();
        _context.PlayerPositions.RemoveRange(existing);

        // Remove any player already at this position/side
        var occupant = await _context.PlayerPositions
            .Where(pp => pp.MatchId == req.MatchId
                      && pp.SetNumber == req.SetNumber
                      && pp.Side == req.Side
                      && pp.Position == req.Position)
            .ToListAsync();
        _context.PlayerPositions.RemoveRange(occupant);

        if (req.Position > 0) // position = 0 means "remove from court"
        {
            _context.PlayerPositions.Add(new PlayerPosition
            {
                MatchId = req.MatchId,
                SetNumber = req.SetNumber,
                PlayerId = req.PlayerId,
                Position = req.Position,
                Side = req.Side
            });
        }

        await _context.SaveChangesAsync();
        return Ok(new { success = true });
    }

    // POST: Matches/SwitchSides/5 - Flip home/away court sides
    [HttpPost]
    public async Task<IActionResult> SwitchSides(int id)
    {
        var match = await _context.Matches
            .Include(m => m.HomeTeam)
            .Include(m => m.AwayTeam)
            .FirstOrDefaultAsync(m => m.Id == id);
        if (match == null) return NotFound();

        match.HomeTeamOnLeft = !match.HomeTeamOnLeft;
        await _context.SaveChangesAsync();

        // Broadcast side-switch to all display clients for this match
        await _hubContext.Clients.Group($"match-{id}").SendAsync("SidesSwitched", new
        {
            homeTeamOnLeft = match.HomeTeamOnLeft,
            homeTeamName   = match.HomeTeam!.Name,
            awayTeamName   = match.AwayTeam!.Name
        });

        return Ok(new { success = true, homeOnLeft = match.HomeTeamOnLeft });
    }

    // POST: Matches/StartMatch/5
    [HttpPost]
    public async Task<IActionResult> StartMatch(int id)
    {
        var match = await _context.Matches
            .Include(m => m.PlayerPositions)
            .FirstOrDefaultAsync(m => m.Id == id);
        if (match == null) return NotFound();

        // Doubles matches require 2 players per side; standard matches require 6
        int required = match.IsDoubles ? 2 : 6;

        var homeCount = match.PlayerPositions.Count(pp =>
            pp.SetNumber == match.CurrentSetNumber && pp.Side == "Home");
        var awayCount = match.PlayerPositions.Count(pp =>
            pp.SetNumber == match.CurrentSetNumber && pp.Side == "Away");

        if (homeCount < required || awayCount < required)
            return BadRequest(new
            {
                success = false,
                error = $"Both teams need {required} players on court. Home: {homeCount}/{required}, Away: {awayCount}/{required}"
            });

        match.Status = MatchStatus.InProgress;
        await _context.SaveChangesAsync();

        return Ok(new { success = true });
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static MatchCourtViewModel BuildCourtViewModel(Match match, GameSet currentSet)
    {
        var homePositions = match.PlayerPositions
            .Where(pp => pp.SetNumber == currentSet.SetNumber && pp.Side == "Home")
            .ToList();

        var awayPositions = match.PlayerPositions
            .Where(pp => pp.SetNumber == currentSet.SetNumber && pp.Side == "Away")
            .ToList();

        int posCount = match.IsDoubles ? 2 : 6;
        List<PositionDto> BuildPositions(List<PlayerPosition> positions) =>
            Enumerable.Range(1, posCount).Select(pos =>
            {
                var assigned = positions.FirstOrDefault(p => p.Position == pos);
                return new PositionDto
                {
                    Position = pos,
                    Player = assigned?.Player == null ? null : new PlayerDto
                    {
                        Id = assigned.Player.Id,
                        Name = assigned.Player.Name,
                        Number = assigned.Player.Number,
                        TeamId = assigned.Player.TeamId
                    }
                };
            }).ToList();

        return new MatchCourtViewModel
        {
            Match = match,
            CurrentSet = currentSet,
            HomeTeam = match.HomeTeam!,
            AwayTeam = match.AwayTeam!,
            HomePlayers = match.HomeTeam!.Players
                .Select(p => new PlayerDto { Id = p.Id, Name = p.Name, Number = p.Number, TeamId = p.TeamId })
                .OrderBy(p => p.Number).ToList(),
            AwayPlayers = match.AwayTeam!.Players
                .Select(p => new PlayerDto { Id = p.Id, Name = p.Name, Number = p.Number, TeamId = p.TeamId })
                .OrderBy(p => p.Number).ToList(),
            HomeCourtPositions = BuildPositions(homePositions),
            AwayCourtPositions = BuildPositions(awayPositions),
            HomeSetsWon = match.Sets.Count(s => s.WinnerTeamId == match.HomeTeamId),
            AwaySetsWon = match.Sets.Count(s => s.WinnerTeamId == match.AwayTeamId),
            TotalSets = match.TotalSets,
            HomeIsServing = currentSet.HomeIsServing,
            HomeTeamOnLeft = match.HomeTeamOnLeft
        };
    }
}

/// <summary>Body of the SavePosition AJAX request</summary>
public class SavePositionRequest
{
    public int MatchId { get; set; }
    public int SetNumber { get; set; }
    public int PlayerId { get; set; }
    public int Position { get; set; }
    public string Side { get; set; } = string.Empty;
}
