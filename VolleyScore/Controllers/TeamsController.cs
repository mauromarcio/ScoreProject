// File: VolleyScore/Controllers/TeamsController.cs
// Purpose: CRUD operations for volleyball teams + initial rotation management

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VolleyScore.Data;
using VolleyScore.Models;

namespace VolleyScore.Controllers;

public class TeamsController : Controller
{
    private readonly VolleyScoreContext _context;

    public TeamsController(VolleyScoreContext context)
    {
        _context = context;
    }

    // GET: Teams
    public async Task<IActionResult> Index()
    {
        var teams = await _context.Teams
            .Include(t => t.Players)
            .Include(t => t.Rotations)
            .OrderBy(t => t.Name)
            .ToListAsync();
        return View(teams);
    }

    // GET: Teams/Create
    public IActionResult Create()
    {
        return View(new Team());
    }

    // POST: Teams/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Team team)
    {
        if (!ModelState.IsValid)
            return View(team);

        _context.Teams.Add(team);
        await _context.SaveChangesAsync();
        TempData["Success"] = $"Team '{team.Name}' created successfully.";
        return RedirectToAction(nameof(Index));
    }

    // GET: Teams/Edit/5
    public async Task<IActionResult> Edit(int id)
    {
        var team = await _context.Teams.FindAsync(id);
        if (team == null) return NotFound();
        return View(team);
    }

    // POST: Teams/Edit/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Team team)
    {
        if (id != team.Id) return NotFound();
        if (!ModelState.IsValid) return View(team);

        try
        {
            _context.Update(team);
            await _context.SaveChangesAsync();
            TempData["Success"] = $"Team '{team.Name}' updated successfully.";
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!await _context.Teams.AnyAsync(t => t.Id == id))
                return NotFound();
            throw;
        }
        return RedirectToAction(nameof(Index));
    }

    // GET: Teams/Delete/5
    public async Task<IActionResult> Delete(int id)
    {
        var team = await _context.Teams
            .Include(t => t.Players)
            .FirstOrDefaultAsync(t => t.Id == id);
        if (team == null) return NotFound();
        return View(team);
    }

    // POST: Teams/Delete/5
    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var team = await _context.Teams.FindAsync(id);
        if (team != null)
        {
            _context.Teams.Remove(team);
            await _context.SaveChangesAsync();
            TempData["Success"] = $"Team deleted.";
        }
        return RedirectToAction(nameof(Index));
    }

    // GET: Teams/Rotation/5 — drag-drop editor for the team's initial 6-player lineup
    public async Task<IActionResult> Rotation(int id)
    {
        var team = await _context.Teams
            .Include(t => t.Players)
            .Include(t => t.Rotations).ThenInclude(r => r.Player)
            .FirstOrDefaultAsync(t => t.Id == id);
        if (team == null) return NotFound();
        return View(team);
    }

    // POST: Teams/SaveRotation — AJAX endpoint; replaces the team's full saved rotation
    [HttpPost]
    public async Task<IActionResult> SaveRotation([FromBody] SaveRotationRequest req)
    {
        if (req == null || req.TeamId == 0)
            return BadRequest(new { success = false, error = "Invalid request" });

        // Verify team exists
        if (!await _context.Teams.AnyAsync(t => t.Id == req.TeamId))
            return NotFound(new { success = false, error = "Team not found" });

        // Remove all existing rotation entries for this team, then re-insert
        var existing = await _context.TeamRotations
            .Where(r => r.TeamId == req.TeamId)
            .ToListAsync();
        _context.TeamRotations.RemoveRange(existing);

        int saved = 0;
        foreach (var pos in req.Positions.Where(p => p.PlayerId > 0 && p.Position >= 1 && p.Position <= 6))
        {
            _context.TeamRotations.Add(new TeamRotation
            {
                TeamId   = req.TeamId,
                PlayerId = pos.PlayerId,
                Position = pos.Position
            });
            saved++;
        }

        await _context.SaveChangesAsync();
        return Ok(new { success = true, saved });
    }
}

public class SaveRotationRequest
{
    public int TeamId { get; set; }
    public List<RotationPositionItem> Positions { get; set; } = new();
}

public class RotationPositionItem
{
    public int Position { get; set; }
    public int PlayerId { get; set; }
}
