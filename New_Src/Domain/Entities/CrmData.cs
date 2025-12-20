using System.ComponentModel.DataAnnotations;

namespace Domain.Entities;

/// <summary>
/// Represents Certified Reference Material (CRM) data used for calibration and quality control.
/// </summary>
public class CrmData
{
    /// <summary>
    /// Gets or sets the unique identifier for the CRM data entry.
    /// </summary>
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the unique name or identifier of the CRM (e.g., "OREAS 258").
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string CrmId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the laboratory analysis method applied to the CRM.
    /// </summary>
    [MaxLength(200)]
    public string? AnalysisMethod { get; set; }

    /// <summary>
    /// Gets or sets the categorization or type of the reference material.
    /// </summary>
    [MaxLength(100)]
    public string? Type { get; set; }

    /// <summary>
    /// Gets or sets the serialized JSON structure containing element concentration values.
    /// </summary>
    public string ElementValues { get; set; } = "{}";

    /// <summary>
    /// Gets or sets a value indicating whether this CRM is part of the internal standard set (e.g., Our Oreas).
    /// </summary>
    public bool IsOurOreas { get; set; } = false;

    /// <summary>
    /// Gets or sets the date and time when the record was initially created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets the date and time when the record was last modified.
    /// </summary>
    public DateTime? UpdatedAt { get; set; }
}
