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
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        db.Database.EnsureCreated();
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred creating the database.");
    }

    // Idempotent schema patches – safe to run on every startup.
    // Adds columns/tables introduced after the initial EnsureCreated so that
    // existing databases are upgraded automatically without EF Migrations.
    try
    {
        await ApplySchemaUpdates(db);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Schema update failed – the app may work with reduced functionality.");
    }
}

static async Task ApplySchemaUpdates(VolleyScoreContext db)
{
    // ── Matches table additions ──────────────────────────────────────────────
    await db.Database.ExecuteSqlRawAsync(@"
        IF NOT EXISTS (SELECT 1 FROM sys.columns
                       WHERE object_id = OBJECT_ID(N'Matches') AND name = N'TotalSets')
            ALTER TABLE Matches ADD TotalSets INT NOT NULL DEFAULT 3;");

    await db.Database.ExecuteSqlRawAsync(@"
        IF NOT EXISTS (SELECT 1 FROM sys.columns
                       WHERE object_id = OBJECT_ID(N'Matches') AND name = N'InitialScore')
            ALTER TABLE Matches ADD InitialScore INT NOT NULL DEFAULT 0;");

    // ── Tournament tables ────────────────────────────────────────────────────
    await db.Database.ExecuteSqlRawAsync(@"
        IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'Tournaments')
        CREATE TABLE Tournaments (
            Id          INT IDENTITY(1,1) PRIMARY KEY,
            Name        NVARCHAR(100) NOT NULL,
            Description NVARCHAR(300) NULL,
            MaxTeams    INT NOT NULL DEFAULT 50,
            SetsPerMatch INT NOT NULL DEFAULT 1,
            InitialScore INT NOT NULL DEFAULT 0,
            Status      INT NOT NULL DEFAULT 0,
            CreatedAt   DATETIME2 NOT NULL DEFAULT GETDATE()
        );");

    await db.Database.ExecuteSqlRawAsync(@"
        IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'TournamentTeams')
        CREATE TABLE TournamentTeams (
            Id            INT IDENTITY(1,1) PRIMARY KEY,
            TournamentId  INT NOT NULL REFERENCES Tournaments(Id) ON DELETE CASCADE,
            TeamId        INT NOT NULL REFERENCES Teams(Id) ON DELETE NO ACTION,
            SeedPosition  INT NOT NULL DEFAULT 0,
            CONSTRAINT UQ_TournamentTeam UNIQUE (TournamentId, TeamId)
        );");

    await db.Database.ExecuteSqlRawAsync(@"
        IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'Pools')
        CREATE TABLE Pools (
            Id           INT IDENTITY(1,1) PRIMARY KEY,
            TournamentId INT NOT NULL REFERENCES Tournaments(Id) ON DELETE CASCADE,
            Name         NVARCHAR(50) NOT NULL,
            SortOrder    INT NOT NULL DEFAULT 0
        );");

    await db.Database.ExecuteSqlRawAsync(@"
        IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'PoolTeams')
        CREATE TABLE PoolTeams (
            Id               INT IDENTITY(1,1) PRIMARY KEY,
            PoolId           INT NOT NULL REFERENCES Pools(Id) ON DELETE CASCADE,
            TournamentTeamId INT NOT NULL REFERENCES TournamentTeams(Id) ON DELETE NO ACTION,
            SortOrder        INT NOT NULL DEFAULT 0,
            CONSTRAINT UQ_PoolTeam UNIQUE (PoolId, TournamentTeamId)
        );");

    await db.Database.ExecuteSqlRawAsync(@"
        IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'TournamentMatches')
        CREATE TABLE TournamentMatches (
            Id                 INT IDENTITY(1,1) PRIMARY KEY,
            TournamentId       INT NOT NULL REFERENCES Tournaments(Id) ON DELETE CASCADE,
            MatchId            INT NULL     REFERENCES Matches(Id)      ON DELETE SET NULL,
            PoolId             INT NULL     REFERENCES Pools(Id)         ON DELETE NO ACTION,
            Phase              INT NOT NULL DEFAULT 0,
            Label              NVARCHAR(50) NOT NULL DEFAULT '',
            HomeTeamId         INT NULL     REFERENCES Teams(Id)         ON DELETE NO ACTION,
            AwayTeamId         INT NULL     REFERENCES Teams(Id)         ON DELETE NO ACTION,
            WinnerTeamId       INT NULL,
            HomeSourceMatchId  INT NULL,
            HomeFromWinner     BIT NOT NULL DEFAULT 1,
            AwaySourceMatchId  INT NULL,
            AwayFromWinner     BIT NOT NULL DEFAULT 1,
            SortOrder          INT NOT NULL DEFAULT 0,
            MatchNumber        INT NOT NULL DEFAULT 0,
            CourtNumber        INT NOT NULL DEFAULT 1,
            RefereeTeamId      INT NULL     REFERENCES Teams(Id) ON DELETE NO ACTION,
            HomeTeamLongWait   BIT NOT NULL DEFAULT 0,
            AwayTeamLongWait   BIT NOT NULL DEFAULT 0
        );");

    // ── Column additions for existing TournamentMatches tables ──────────────
    await db.Database.ExecuteSqlRawAsync(@"
        IF NOT EXISTS (SELECT 1 FROM sys.columns
                       WHERE object_id = OBJECT_ID(N'TournamentMatches') AND name = N'MatchNumber')
            ALTER TABLE TournamentMatches ADD MatchNumber INT NOT NULL DEFAULT 0;");

    await db.Database.ExecuteSqlRawAsync(@"
        IF NOT EXISTS (SELECT 1 FROM sys.columns
                       WHERE object_id = OBJECT_ID(N'TournamentMatches') AND name = N'CourtNumber')
            ALTER TABLE TournamentMatches ADD CourtNumber INT NOT NULL DEFAULT 1;");

    await db.Database.ExecuteSqlRawAsync(@"
        IF NOT EXISTS (SELECT 1 FROM sys.columns
                       WHERE object_id = OBJECT_ID(N'TournamentMatches') AND name = N'RefereeTeamId')
            ALTER TABLE TournamentMatches ADD RefereeTeamId INT NULL REFERENCES Teams(Id) ON DELETE NO ACTION;");

    await db.Database.ExecuteSqlRawAsync(@"
        IF NOT EXISTS (SELECT 1 FROM sys.columns
                       WHERE object_id = OBJECT_ID(N'TournamentMatches') AND name = N'HomeTeamLongWait')
            ALTER TABLE TournamentMatches ADD HomeTeamLongWait BIT NOT NULL DEFAULT 0;");

    await db.Database.ExecuteSqlRawAsync(@"
        IF NOT EXISTS (SELECT 1 FROM sys.columns
                       WHERE object_id = OBJECT_ID(N'TournamentMatches') AND name = N'AwayTeamLongWait')
            ALTER TABLE TournamentMatches ADD AwayTeamLongWait BIT NOT NULL DEFAULT 0;");

    // ── Column additions for existing Tournaments tables ─────────────────────
    await db.Database.ExecuteSqlRawAsync(@"
        IF NOT EXISTS (SELECT 1 FROM sys.columns
                       WHERE object_id = OBJECT_ID(N'Tournaments') AND name = N'NumberOfCourts')
            ALTER TABLE Tournaments ADD NumberOfCourts INT NOT NULL DEFAULT 1;");

    await db.Database.ExecuteSqlRawAsync(@"
        IF NOT EXISTS (SELECT 1 FROM sys.columns
                       WHERE object_id = OBJECT_ID(N'Tournaments') AND name = N'TournamentType')
            ALTER TABLE Tournaments ADD TournamentType INT NOT NULL DEFAULT 0;");

    await db.Database.ExecuteSqlRawAsync(@"
        IF NOT EXISTS (SELECT 1 FROM sys.columns
                       WHERE object_id = OBJECT_ID(N'Tournaments') AND name = N'NumberOfPools')
            ALTER TABLE Tournaments ADD NumberOfPools INT NOT NULL DEFAULT 1;");

    await db.Database.ExecuteSqlRawAsync(@"
        IF NOT EXISTS (SELECT 1 FROM sys.columns
                       WHERE object_id = OBJECT_ID(N'Tournaments') AND name = N'CrossPoolMatchCount')
            ALTER TABLE Tournaments ADD CrossPoolMatchCount INT NOT NULL DEFAULT 0;");

    await db.Database.ExecuteSqlRawAsync(@"
        IF NOT EXISTS (SELECT 1 FROM sys.columns
                       WHERE object_id = OBJECT_ID(N'Tournaments') AND name = N'TeamsAdvancingPerPool')
            ALTER TABLE Tournaments ADD TeamsAdvancingPerPool INT NOT NULL DEFAULT 2;");

    // ── Column additions for existing Matches tables ──────────────────────────
    await db.Database.ExecuteSqlRawAsync(@"
        IF NOT EXISTS (SELECT 1 FROM sys.columns
                       WHERE object_id = OBJECT_ID(N'Matches') AND name = N'IsDoubles')
            ALTER TABLE Matches ADD IsDoubles BIT NOT NULL DEFAULT 0;");

    await db.Database.ExecuteSqlRawAsync(@"
        IF NOT EXISTS (SELECT 1 FROM sys.columns
                       WHERE object_id = OBJECT_ID(N'Tournaments') AND name = N'PointsToWin')
            ALTER TABLE Tournaments ADD PointsToWin INT NOT NULL DEFAULT 25;");

    await db.Database.ExecuteSqlRawAsync(@"
        IF NOT EXISTS (SELECT 1 FROM sys.columns
                       WHERE object_id = OBJECT_ID(N'Tournaments') AND name = N'PointsCap')
            ALTER TABLE Tournaments ADD PointsCap INT NOT NULL DEFAULT 0;");

    await db.Database.ExecuteSqlRawAsync(@"
        IF NOT EXISTS (SELECT 1 FROM sys.columns
                       WHERE object_id = OBJECT_ID(N'Matches') AND name = N'PointsToWin')
            ALTER TABLE Matches ADD PointsToWin INT NOT NULL DEFAULT 25;");

    await db.Database.ExecuteSqlRawAsync(@"
        IF NOT EXISTS (SELECT 1 FROM sys.columns
                       WHERE object_id = OBJECT_ID(N'Matches') AND name = N'PointsCap')
            ALTER TABLE Matches ADD PointsCap INT NOT NULL DEFAULT 0;");
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
