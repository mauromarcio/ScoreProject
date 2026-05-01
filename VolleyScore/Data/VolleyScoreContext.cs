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

    // Tournament system
    public DbSet<Tournament> Tournaments { get; set; }
    public DbSet<TournamentTeam> TournamentTeams { get; set; }
    public DbSet<Pool> Pools { get; set; }
    public DbSet<PoolTeam> PoolTeams { get; set; }
    public DbSet<TournamentMatch> TournamentMatches { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Team
        modelBuilder.Entity<Team>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Description).HasMaxLength(200);
            entity.HasIndex(e => e.Name).IsUnique();
        });

        // Player
        modelBuilder.Entity<Player>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.HasOne(e => e.Team)
                  .WithMany(t => t.Players)
                  .HasForeignKey(e => e.TeamId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.TeamId, e.Number }).IsUnique();
        });

        // Match
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

        // GameSet
        modelBuilder.Entity<GameSet>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.Match)
                  .WithMany(m => m.Sets)
                  .HasForeignKey(e => e.MatchId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.MatchId, e.SetNumber }).IsUnique();
        });

        // PlayerPosition
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
            entity.HasIndex(e => new { e.MatchId, e.SetNumber, e.Side, e.Position }).IsUnique();
        });

        // Tournament
        modelBuilder.Entity<Tournament>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Description).HasMaxLength(300);
        });

        // TournamentTeam
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

        // Pool
        modelBuilder.Entity<Pool>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(50);
            entity.HasOne(e => e.Tournament)
                  .WithMany(t => t.Pools)
                  .HasForeignKey(e => e.TournamentId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // PoolTeam
        modelBuilder.Entity<PoolTeam>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.Pool)
                  .WithMany(p => p.PoolTeams)
                  .HasForeignKey(e => e.PoolId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.TournamentTeam)
                  .WithMany(tt => tt.PoolTeams)
                  .HasForeignKey(e => e.TournamentTeamId)
                  .OnDelete(DeleteBehavior.Restrict);
            // A team can only be in one pool per tournament
            entity.HasIndex(e => new { e.PoolId, e.TournamentTeamId }).IsUnique();
        });

        // TournamentMatch
        modelBuilder.Entity<TournamentMatch>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Label).HasMaxLength(50);
            entity.HasOne(e => e.Tournament)
                  .WithMany(t => t.TournamentMatches)
                  .HasForeignKey(e => e.TournamentId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Match)
                  .WithMany()
                  .HasForeignKey(e => e.MatchId)
                  .OnDelete(DeleteBehavior.SetNull)
                  .IsRequired(false);
            entity.HasOne(e => e.Pool)
                  .WithMany(p => p.Matches)
                  .HasForeignKey(e => e.PoolId)
                  .OnDelete(DeleteBehavior.Restrict)
                  .IsRequired(false);
            entity.HasOne(e => e.HomeTeam)
                  .WithMany()
                  .HasForeignKey(e => e.HomeTeamId)
                  .OnDelete(DeleteBehavior.Restrict)
                  .IsRequired(false);
            entity.HasOne(e => e.AwayTeam)
                  .WithMany()
                  .HasForeignKey(e => e.AwayTeamId)
                  .OnDelete(DeleteBehavior.Restrict)
                  .IsRequired(false);
            entity.HasOne(e => e.RefereeTeam)
                  .WithMany()
                  .HasForeignKey(e => e.RefereeTeamId)
                  .OnDelete(DeleteBehavior.Restrict)
                  .IsRequired(false);
        });
    }
}
