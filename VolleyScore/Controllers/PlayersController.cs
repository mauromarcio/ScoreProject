// File: VolleyScore/Controllers/PlayersController.cs
// Purpose: CRUD operations for players.  Players are always scoped to a team.

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using VolleyScore.Data;
using VolleyScore.Models;

namespace VolleyScore.Controllers;

public class PlayersController : Controller
{
    private readonly VolleyScoreContext _context;

    public PlayersController(VolleyScoreContext context)
    {
        _context = context;
    }

    // GET: Players  (optionally filtered by teamId)
    public async Task<IActionResult> Index(int? teamId)
    {
        var query = _context.Players
            .Include(p => p.Team)
            .AsQueryable();

        if (teamId.HasValue)
        {
            query = query.Where(p => p.TeamId == teamId.Value);
            var team = await _context.Teams.FindAsync(teamId.Value);
            ViewBag.FilterTeam = team;
        }

        var players = await query
            .OrderBy(p => p.Team!.Name)
            .ThenBy(p => p.Number)
            .ToListAsync();

        ViewBag.Teams = await _context.Teams.OrderBy(t => t.Name).ToListAsync();
        return View(players);
    }

    // GET: Players/Create?teamId=5
    public async Task<IActionResult> Create(int? teamId)
    {
        ViewBag.Teams = new SelectList(
            await _context.Teams.OrderBy(t => t.Name).ToListAsync(), "Id", "Name", teamId);
        return View(new Player { TeamId = teamId ?? 0 });
    }

    // POST: Players/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Player player)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.Teams = new SelectList(
                await _context.Teams.OrderBy(t => t.Name).ToListAsync(), "Id", "Name", player.TeamId);
            return View(player);
        }

        // Check jersey number uniqueness within team
        bool duplicate = await _context.Players
            .AnyAsync(p => p.TeamId == player.TeamId && p.Number == player.Number);
        if (duplicate)
        {
            ModelState.AddModelError("Number", "A player with this jersey number already exists in the team.");
            ViewBag.Teams = new SelectList(
                await _context.Teams.OrderBy(t => t.Name).ToListAsync(), "Id", "Name", player.TeamId);
            return View(player);
        }

        _context.Players.Add(player);
        await _context.SaveChangesAsync();
        TempData["Success"] = $"Player #{player.Number} {player.Name} added.";
        return RedirectToAction(nameof(Index), new { teamId = player.TeamId });
    }

    // GET: Players/Edit/5
    public async Task<IActionResult> Edit(int id)
    {
        var player = await _context.Players.Include(p => p.Team).FirstOrDefaultAsync(p => p.Id == id);
        if (player == null) return NotFound();

        ViewBag.Teams = new SelectList(
            await _context.Teams.OrderBy(t => t.Name).ToListAsync(), "Id", "Name", player.TeamId);
        return View(player);
    }

    // POST: Players/Edit/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Player player)
    {
        if (id != player.Id) return NotFound();
        if (!ModelState.IsValid)
        {
            ViewBag.Teams = new SelectList(
                await _context.Teams.OrderBy(t => t.Name).ToListAsync(), "Id", "Name", player.TeamId);
            return View(player);
        }

        // Check jersey uniqueness excluding self
        bool duplicate = await _context.Players
            .AnyAsync(p => p.TeamId == player.TeamId && p.Number == player.Number && p.Id != id);
        if (duplicate)
        {
            ModelState.AddModelError("Number", "A player with this jersey number already exists in the team.");
            ViewBag.Teams = new SelectList(
                await _context.Teams.OrderBy(t => t.Name).ToListAsync(), "Id", "Name", player.TeamId);
            return View(player);
        }

        try
        {
            _context.Update(player);
            await _context.SaveChangesAsync();
            TempData["Success"] = $"Player updated.";
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!await _context.Players.AnyAsync(p => p.Id == id))
                return NotFound();
            throw;
        }
        return RedirectToAction(nameof(Index), new { teamId = player.TeamId });
    }

    // GET: Players/Delete/5
    public async Task<IActionResult> Delete(int id)
    {
        var player = await _context.Players.Include(p => p.Team).FirstOrDefaultAsync(p => p.Id == id);
        if (player == null) return NotFound();
        return View(player);
    }

    // POST: Players/Delete/5
    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var player = await _context.Players.FindAsync(id);
        int? teamId = player?.TeamId;
        if (player != null)
        {
            try
            {
                // Remove court position history first (avoids FK constraint)
                var positions = await _context.PlayerPositions.Where(pp => pp.PlayerId == id).ToListAsync();
                _context.PlayerPositions.RemoveRange(positions);
                _context.Players.Remove(player);
                await _context.SaveChangesAsync();
                TempData["Success"] = $"Player {player.Name} deleted.";
            }
            catch (DbUpdateException)
            {
                TempData["Error"] = $"Cannot delete {player.Name} — they are still referenced by match data.";
            }
        }
        return RedirectToAction(nameof(Index), new { teamId });
    }
}
