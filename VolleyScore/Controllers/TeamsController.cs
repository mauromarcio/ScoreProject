// File: VolleyScore/Controllers/TeamsController.cs
// Purpose: CRUD operations for volleyball teams

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
}
