// File: VolleyScore/Models/PoolTeam.cs
// Purpose: Assignment of a TournamentTeam to a specific Pool, with drag-drop sort order

using System.ComponentModel.DataAnnotations.Schema;

namespace VolleyScore.Models;

public class PoolTeam
{
    public int Id { get; set; }
    public int PoolId { get; set; }
    public int TournamentTeamId { get; set; }

    /// <summary>Display order within the pool (user-adjustable via drag-drop)</summary>
    public int SortOrder { get; set; }

    [ForeignKey("PoolId")]
    public Pool? Pool { get; set; }

    [ForeignKey("TournamentTeamId")]
    public TournamentTeam? TournamentTeam { get; set; }
}
