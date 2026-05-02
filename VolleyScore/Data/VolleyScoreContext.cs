// File: VolleyScore/Data/VolleyScoreContext.cs
// Purpose: Entity Framework DbContext - database session factory and model configuration

using Microsoft.EntityFrameworkCore;
using VolleyScore.Models;

namespace VolleyScore.Data;

public class VolleyScoreContext : DbContext
{
    public VolleyScoreContext(DbContextOptions<VolleyScoreContext> options)
        : base(options)
    {
    }

    public DbSet<Team> Teams { get; set; }
    public DbSet<Player> Players { get; set; }
    public DbSet<Match> Matches { get; set; }
    public DbSet<GameSet> GameSets { get; set; }
    public DbSet<PlayerPosition> PlayerPositions { get; set; }
    public DbSet<TeamRotation> TeamRotations { get; set; }
    public DbSet<Tournament> Tournaments { get; set; }
    public DbSet<TournamentTeam> TournamentTeams { get; set; }
    public DbSet<TournamentMatch> TournamentMatches { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Team configuration
        modelBuilder.Entity<Team>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Description).HasMaxLength(200);
            entity.HasIndex(e => e.Name).IsUnique();
        });

        // Player configuration
        modelBuilder.Entity<Player>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.HasOne(e => e.Team)
                  .WithMany(t => t.Players)
                  .HasForeignKey(e => e.TeamId)
                  .OnDelete(DeleteBehavior.Cascade);
            // Unique jersey number per team
            entity.HasIndex(e => new { e.TeamId, e.Number }).IsUnique();
        });

        // Match configuration
        modelBuilder.Entity<Match>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.MatchReference).IsRequired().HasMaxLength(100);
            entity.Property(e => e.InitialScore).HasDefaultValue(0);
            entity.HasOne(e => e.HomeTeam)
                  .WithMany()
                  .HasForeignKey(e => e.HomeTeamId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.AwayTeam)
                  .WithMany()
                  .HasForeignKey(e => e.AwayTeamId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        // GameSet configuration
        modelBuilder.Entity<GameSet>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.Match)
                  .WithMany(m => m.Sets)
                  .HasForeignKey(e => e.MatchId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.MatchId, e.SetNumber }).IsUnique();
        });

        // PlayerPosition configuration
        modelBuilder.Entity<PlayerPosition>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Side).IsRequired().HasMaxLength(10);
            entity.HasOne(e => e.Match)
                  .WithMany(m => m.PlayerPositions)
                  .HasForeignKey(e => e.MatchId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Player)
                  .WithMany()
                  .HasForeignKey(e => e.PlayerId)
                  .OnDelete(DeleteBehavior.Restrict);
            // One player per position per set per side
            entity.HasIndex(e => new { e.MatchId, e.SetNumber, e.Side, e.Position }).IsUnique();
        });

        // TeamRotation configuration
        modelBuilder.Entity<TeamRotation>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.Team)
                  .WithMany(t => t.Rotations)
                  .HasForeignKey(e => e.TeamId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Player)
                  .WithMany()
                  .HasForeignKey(e => e.PlayerId)
                  .OnDelete(DeleteBehavior.Restrict);
            // One position slot per team
            entity.HasIndex(e => new { e.TeamId, e.Position }).IsUnique();
        });

        // Tournament configuration
        modelBuilder.Entity<Tournament>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Description).HasMaxLength(300);
        });

        // TournamentTeam configuration
        modelBuilder.Entity<TournamentTeam>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.Tournament)
                  .WithMany(t => t.TournamentTeams)
                  .HasForeignKey(e => e.TournamentId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Team)
                  .WithMany()
                  .HasForeignKey(e => e.TeamId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => new { e.TournamentId, e.TeamId }).IsUnique();
        });

        // TournamentMatch configuration
        modelBuilder.Entity<TournamentMatch>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.Tournament)
                  .WithMany(t => t.TournamentMatches)
                  .HasForeignKey(e => e.TournamentId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Match)
                  .WithMany()
                  .HasForeignKey(e => e.MatchId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.RefereeTeam)
                  .WithMany()
                  .HasForeignKey(e => e.RefereeTeamId)
                  .OnDelete(DeleteBehavior.Restrict)
                  .IsRequired(false);
        });
    }
}
