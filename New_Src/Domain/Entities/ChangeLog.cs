namespace Domain.Entities;

/// <summary>
/// Represents an audit log entry for tracking modifications to project data.
/// </summary>
public class ChangeLog
{
    /// <summary>
    /// Gets or sets the unique identifier for the change log entry.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the foreign key identifier of the associated project.
    /// </summary>
    public Guid ProjectId { get; set; }

    /// <summary>
    /// Gets or sets the type of modification that occurred (e.g., Weight, Volume, Drift).
    /// </summary>
    public string ChangeType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the solution label or sample identifier involved in the change.
    /// </summary>
    public string? SolutionLabel { get; set; }

    /// <summary>
    /// Gets or sets the specific dataset element affected by the change.
    /// </summary>
    public string? Element { get; set; }

    /// <summary>
    /// Gets or sets the value before the modification.
    /// </summary>
    public string? OldValue { get; set; }

    /// <summary>
    /// Gets or sets the value after the modification.
    /// </summary>
    public string? NewValue { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the user who performed the action.
    /// </summary>
    public string? ChangedBy { get; set; }

    /// <summary>
    /// Gets or sets the date and time when the change was recorded.
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets descriptive details regarding the modification.
    /// </summary>
    public string? Details { get; set; }

    /// <summary>
    /// Gets or sets the optional batch identifier to group related changes together.
    /// </summary>
    public Guid? BatchId { get; set; }

    /// <summary>
    /// Gets or sets the navigation property for the associated project.
    /// </summary>
    public virtual Project? Project { get; set; }
}
