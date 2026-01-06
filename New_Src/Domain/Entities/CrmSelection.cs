using System.ComponentModel.DataAnnotations;

namespace Domain.Entities;

/// <summary>
/// Stores per-row CRM selection (matches Python crm_selections with row_index).
/// </summary>
public class CrmSelection
{
    [Key]
    public int Id { get; set; }

    [Required]
    public Guid ProjectId { get; set; }

    [Required]
    [MaxLength(200)]
    public string SolutionLabel { get; set; } = string.Empty;

    public int RowIndex { get; set; }

    [Required]
    [MaxLength(200)]
    public string SelectedCrmKey { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? SelectedBy { get; set; }

    public DateTime SelectedAt { get; set; } = DateTime.UtcNow;
}
