// File: VolleyScore/Program.cs
// Purpose: Application entry point - configures services, middleware, routing, and database

using Microsoft.EntityFrameworkCore;
using VolleyScore.Data;
using VolleyScore.Hubs;

var builder = WebApplication.CreateBuilder(args);

// ── Services ──────────────────────────────────────────────────────────────────

builder.Services.AddControllersWithViews();

builder.Services.AddDbContext<VolleyScoreContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// SignalR for real-time score broadcast (Part 2 display screen)
builder.Services.AddSignalR();

// Enable session for short-lived UI state (court drag-drop setup)
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(8);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

var app = builder.Build();

// ── Database Initialisation ───────────────────────────────────────────────────
// Creates all tables if they do not exist (no migrations needed on first run)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<VolleyScoreContext>();
    try
    {
        db.Database.EnsureCreated();
    }
    catch (Exception ex)
    {
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred creating the database.");
    }
}

// ── Middleware Pipeline ───────────────────────────────────────────────────────
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAuthorization();

// ── Routes ────────────────────────────────────────────────────────────────────
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// SignalR hub endpoint - clients connect here for real-time score updates
app.MapHub<ScoreHub>("/scoreHub");

app.Run();
