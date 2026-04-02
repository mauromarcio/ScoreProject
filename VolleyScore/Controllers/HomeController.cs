// File: VolleyScore/Controllers/HomeController.cs
// Purpose: Landing page - shows active matches and quick-navigation links

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VolleyScore.Data;
using VolleyScore.Models;

namespace VolleyScore.Controllers;

public class HomeController : Controller
{
    private readonly VolleyScoreContext _context;

    public HomeController(VolleyScoreContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index()
    {
        var activeMatches = await _context.Matches
            .Include(m => m.HomeTeam)
            .Include(m => m.AwayTeam)
            .Include(m => m.Sets)
            .Where(m => m.Status != MatchStatus.Completed)
            .OrderByDescending(m => m.CreatedAt)
            .Take(10)
            .ToListAsync();

        return View(activeMatches);
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View();
    }
}
