using System.ComponentModel.DataAnnotations;

namespace Domain.Entities;

/// <summary>
/// Represents a specific version or snapshot of a project's data at a point in time.
/// </summary>
public class ProjectState
{
    /// <summary>
    /// Gets or sets the unique primary key identifier for this state snapshot.
    /// </summary>
    [Key]
    public int StateId { get; set; }

    /// <summary>
    /// Gets or sets the foreign key identifier of the project this state belongs to.
    /// </summary>
    public Guid ProjectId { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the predecessor state, forming a version history tree.
    /// </summary>
    public int? ParentStateId { get; set; }

    /// <summary>
    /// Gets or sets the incremental version number of this state within the project.
    /// </summary>
    public int VersionNumber { get; set; } = 1;

    /// <summary>
    /// Gets or sets the type of operation that produced this state (e.g., "Import", "Weight Correction").
    /// </summary>
    public string ProcessingType { get; set; } = "Import";

    /// <summary>
    /// Gets or sets the complete serialized JSON representation of the project data for this state.
    /// </summary>
    public string Data { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the date and time when this snapshot was created.
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets a user-facing description or comment regarding this state.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this state represents the current active version of the project.
    /// </summary>
    public bool IsActive { get; set; } = false;

    /// <summary>
    /// Gets or sets the navigation property for the associated project.
    /// </summary>
    public Project? Project { get; set; }

    /// <summary>
    /// Gets or sets the navigation property for the parent state.
    /// </summary>
    public ProjectState? ParentState { get; set; }

    /// <summary>
    /// Gets or sets the collection of derived child states.
    /// </summary>
    public ICollection<ProjectState> ChildStates { get; set; } = new List<ProjectState>();
}

/// <summary>
/// Provides constant values representing different data processing operations.
/// </summary>
public static class ProcessingTypes
{
    /// <summary>
    /// Represents the initial data import operation.
    /// </summary>
    public const string Import = "Import";

    /// <summary>
    /// Represents a correction applied to weight measurements.
    /// </summary>
    public const string WeightCorrection = "Weight Correction";

    /// <summary>
    /// Represents a correction applied to volume measurements.
    /// </summary>
    public const string VolumeCorrection = "Volume Correction";

    /// <summary>
    /// Represents a correction applied to dilution factors.
    /// </summary>
    public const string DfCorrection = "DF Correction";

    /// <summary>
    /// Represents a correction applied to instrument drift.
    /// </summary>
    public const string DriftCorrection = "Drift Correction";

    /// <summary>
    /// Represents a quality control check using Certified Reference Materials.
    /// </summary>
    public const string CrmCheck = "CRM Check";

    /// <summary>
    /// Represents a quality control check using Reference Materials.
    /// </summary>
    public const string RmCheck = "RM Check";

    /// <summary>
    /// Represents an operation where empty or invalid rows are removed.
    /// </summary>
    public const string EmptyRowRemoval = "Empty Row Removal";

    /// <summary>
    /// Represents a manual modification by a user.
    /// </summary>
    public const string ManualEdit = "Manual Edit";

    /// <summary>
    /// Represents an automated optimization process.
    /// </summary>
    public const string Optimization = "Optimization";
}
