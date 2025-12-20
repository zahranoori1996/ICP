using System.ComponentModel.DataAnnotations;

namespace Domain.Entities;

/// <summary>
/// Represents the output of a data processing operation, storing analysis results.
/// </summary>
public class ProcessedData
{
    /// <summary>
    /// Gets or sets the unique primary key identifier.
    /// </summary>
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the legacy or scoped identifier associated with the processing grouping.
    /// </summary>
    public int ProcessedId { get; set; }

    /// <summary>
    /// Gets or sets the foreign key identifier of the parent project.
    /// </summary>
    public Guid ProjectId { get; set; }

    /// <summary>
    /// Gets or sets the classification of the analysis (e.g., "CRM", "QC", "Unknown").
    /// </summary>
    public string AnalysisType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the serialized JSON payload containing the calculated results.
    /// </summary>
    public string Data { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the date and time when this record was persisted.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets the navigation property for the related project.
    /// </summary>
    public Project? Project { get; set; }
}
