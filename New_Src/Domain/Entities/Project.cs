using System.ComponentModel.DataAnnotations;

namespace Domain.Entities;

/// <summary>
/// Represents a high-level container for analysis data, including raw inputs, processed results, and state history.
/// </summary>
public class Project
{
    /// <summary>
    /// Gets or sets the unique primary key identifier for the project.
    /// </summary>
    [Key]
    public Guid ProjectId { get; set; }

    /// <summary>
    /// Gets or sets the descriptive name of the project.
    /// </summary>
    public string ProjectName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the identifier or name of the user who owns or created the project.
    /// </summary>
    public string? Owner { get; set; }

    /// <summary>
    /// Gets or sets the date and time when the project was originally created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets the date and time when the project was last updated.
    /// </summary>
    public DateTime LastModifiedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets the collection of raw data entries imported into this project.
    /// </summary>
    public ICollection<RawDataRow> RawDataRows { get; set; } = new List<RawDataRow>();

    /// <summary>
    /// Gets or sets the history of state changes associated with this project.
    /// </summary>
    public ICollection<ProjectState> ProjectStates { get; set; } = new List<ProjectState>();

    /// <summary>
    /// Gets or sets the collection of analysis results and processed outputs.
    /// </summary>
    public ICollection<ProcessedData> ProcessedDatas { get; set; } = new List<ProcessedData>();
}
