using System.ComponentModel.DataAnnotations;

namespace Domain.Entities;

/// <summary>
/// Represents a single row of raw input data associated with a project.
/// </summary>
public class RawDataRow
{
    /// <summary>
    /// Gets or sets the unique primary key identifier for the data row.
    /// </summary>
    [Key]
    public int DataId { get; set; }

    /// <summary>
    /// Gets or sets the foreign key identifier of the parent project.
    /// </summary>
    public Guid ProjectId { get; set; }

    /// <summary>
    /// Gets or sets the serialized JSON string containing the raw column values for this row.
    /// </summary>
    public string ColumnData { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the sample identifier, label, or row index associated with this entry.
    /// </summary>
    public string? SampleId { get; set; }

    /// <summary>
    /// Gets or sets the navigation property for the associated project.
    /// </summary>
    public Project? Project { get; set; }
}
