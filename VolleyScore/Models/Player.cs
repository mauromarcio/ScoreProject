// File: VolleyScore/Models/Player.cs
// Purpose: Represents an individual volleyball player belonging to a team

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace VolleyScore.Models;

public class Player
{
    public int Id { get; set; }

    [Required]
    public int TeamId { get; set; }

    [Required(ErrorMessage = "Player name is required")]
    [StringLength(100, ErrorMessage = "Player name cannot exceed 100 characters")]
    [Display(Name = "Player Name")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Jersey number is required")]
    [Range(1, 99, ErrorMessage = "Jersey number must be between 1 and 99")]
    [Display(Name = "Jersey #")]
    public int Number { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    // Navigation properties
    [ForeignKey("TeamId")]
    public Team? Team { get; set; }
}
