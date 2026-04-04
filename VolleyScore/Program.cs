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
// Creates all tables if they do not exist (no migrations needed on first run).
// Also applies safe ALTER TABLE additions for schema upgrades on existing databases.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<VolleyScoreContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        // Create any missing tables (new installs get everything; existing databases keep their data)
        db.Database.EnsureCreated();

        // ── Safe schema upgrades for existing databases ───────────────────────
        // Add InitialScore column to Matches if it doesn't exist yet
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Matches' AND COLUMN_NAME = 'InitialScore')
                ALTER TABLE Matches ADD InitialScore INT NOT NULL DEFAULT 0;
            IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Matches' AND COLUMN_NAME = 'IsDeleted')
                ALTER TABLE Matches ADD IsDeleted BIT NOT NULL DEFAULT 0;
            IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Matches' AND COLUMN_NAME = 'DeletedAt')
                ALTER TABLE Matches ADD DeletedAt DATETIME2 NULL;
            IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Matches' AND COLUMN_NAME = 'SetCap')
                ALTER TABLE Matches ADD SetCap INT NULL;
        ");

        // Create Tournament tables if they don't exist (EnsureCreated only creates
        // tables for a completely new database; existing DBs need explicit CREATE IF NOT EXISTS)
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Tournaments')
            BEGIN
                CREATE TABLE Tournaments (
                    Id INT IDENTITY(1,1) PRIMARY KEY,
                    Name NVARCHAR(100) NOT NULL,
                    Description NVARCHAR(300) NULL,
                    PoolSetsPerMatch INT NOT NULL DEFAULT 2,
                    PlayoffSetsPerMatch INT NOT NULL DEFAULT 3,
                    InitialScore INT NOT NULL DEFAULT 0,
                    Status INT NOT NULL DEFAULT 0,
                    CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE()
                )
            END
        ");

        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'TournamentTeams')
            BEGIN
                CREATE TABLE TournamentTeams (
                    Id INT IDENTITY(1,1) PRIMARY KEY,
                    TournamentId INT NOT NULL REFERENCES Tournaments(Id) ON DELETE CASCADE,
                    TeamId INT NOT NULL REFERENCES Teams(Id),
                    SeedOrder INT NOT NULL DEFAULT 1,
                    CONSTRAINT UQ_TournamentTeam UNIQUE (TournamentId, TeamId)
                )
            END
        ");

        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'TournamentMatches')
            BEGIN
                CREATE TABLE TournamentMatches (
                    Id INT IDENTITY(1,1) PRIMARY KEY,
                    TournamentId INT NOT NULL REFERENCES Tournaments(Id) ON DELETE CASCADE,
                    MatchId INT NOT NULL REFERENCES Matches(Id),
                    Stage INT NOT NULL DEFAULT 0,
                    MatchNumber INT NOT NULL DEFAULT 1
                )
            END
        ");

        // ── ALTER TABLE upgrades for tables that exist with old column names ──
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Tournaments' AND COLUMN_NAME = 'PoolSetsPerMatch')
                ALTER TABLE Tournaments ADD PoolSetsPerMatch INT NOT NULL DEFAULT 2;
            IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Tournaments' AND COLUMN_NAME = 'PoolSetCap')
                ALTER TABLE Tournaments ADD PoolSetCap INT NULL;
            IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Tournaments' AND COLUMN_NAME = 'PlayoffSetsPerMatch')
                ALTER TABLE Tournaments ADD PlayoffSetsPerMatch INT NOT NULL DEFAULT 3;
            IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Tournaments' AND COLUMN_NAME = 'Description')
                ALTER TABLE Tournaments ADD Description NVARCHAR(300) NULL;
            IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Tournaments' AND COLUMN_NAME = 'InitialScore')
                ALTER TABLE Tournaments ADD InitialScore INT NOT NULL DEFAULT 0;
            IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Tournaments' AND COLUMN_NAME = 'Status')
                ALTER TABLE Tournaments ADD Status INT NOT NULL DEFAULT 0;
            IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Tournaments' AND COLUMN_NAME = 'CreatedAt')
                ALTER TABLE Tournaments ADD CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE();
            IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'TournamentTeams' AND COLUMN_NAME = 'SeedOrder')
                ALTER TABLE TournamentTeams ADD SeedOrder INT NOT NULL DEFAULT 1;
            IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'TournamentMatches' AND COLUMN_NAME = 'Stage')
                ALTER TABLE TournamentMatches ADD Stage INT NOT NULL DEFAULT 0;
            IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'TournamentMatches' AND COLUMN_NAME = 'MatchNumber')
                ALTER TABLE TournamentMatches ADD MatchNumber INT NOT NULL DEFAULT 1;
            IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'TournamentMatches' AND COLUMN_NAME = 'RefereeTeamId')
                ALTER TABLE TournamentMatches ADD RefereeTeamId INT NULL REFERENCES Teams(Id);
        ");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred initialising the database.");
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
