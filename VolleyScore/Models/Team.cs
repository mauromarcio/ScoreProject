// File: VolleyScore/Models/Team.cs
// Purpose: Represents a volleyball team with its roster

using System.ComponentModel.DataAnnotations;

namespace VolleyScore.Models;

public class Team
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Team name is required")]
    [StringLength(100, ErrorMessage = "Team name cannot exceed 100 characters")]
    [Display(Name = "Team Name")]
    public string Name { get; set; } = string.Empty;

    [StringLength(200, ErrorMessage = "Description cannot exceed 200 characters")]
    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    // Navigation properties
    public ICollection<Player> Players { get; set; } = new List<Player>();
}
