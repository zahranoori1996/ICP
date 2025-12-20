using System.ComponentModel.DataAnnotations;

namespace Domain.Entities;

/// <summary>
/// Represents a background job task responsible for importing or processing project data files.
/// </summary>
public class ProjectImportJob
{
    /// <summary>
    /// Gets or sets the unique primary key identifier for the import job.
    /// </summary>
    [Key]
    public Guid JobId { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the existing project if updating, or the intended project context.
    /// </summary>
    public Guid? ProjectId { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the project created or modified as a result of this job.
    /// </summary>
    public Guid? ResultProjectId { get; set; }

    /// <summary>
    /// Gets or sets the proposed or actual name of the project being imported.
    /// </summary>
    public string? ProjectName { get; set; }

    /// <summary>
    /// Gets or sets the classification of the job (e.g., "import", "process", "validate").
    /// </summary>
    public string? JobType { get; set; }

    /// <summary>
    /// Gets or sets the numeric state code representing the job's progress (e.g., Pending, Running, Completed, Failed).
    /// </summary>
    public int State { get; set; }

    /// <summary>
    /// Gets or sets the total number of records expected to be processed.
    /// </summary>
    public int TotalRows { get; set; }

    /// <summary>
    /// Gets or sets the count of records currently processed.
    /// </summary>
    public int ProcessedRows { get; set; }

    /// <summary>
    /// Gets or sets the calculated progress percentage (0-100).
    /// </summary>
    public int Percent { get; set; }

    /// <summary>
    /// Gets or sets a descriptive message regarding the job's status or failure reason.
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// Gets or sets the filesystem path to the temporary file associated with this job.
    /// </summary>
    public string? TempFilePath { get; set; }

    /// <summary>
    /// Gets or sets the date and time when the job was queued.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the date and time when the job status was last refreshed.
    /// </summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// Gets or sets a correlation identifier to track unique operations or prevent duplicate submissions.
    /// </summary>
    public Guid? OperationId { get; set; }

    /// <summary>
    /// Gets or sets the retry count for failed execution attempts.
    /// </summary>
    public int Attempts { get; set; } = 0;

    /// <summary>
    /// Gets or sets the detailed error message from the most recent failure.
    /// </summary>
    public string? LastError { get; set; }

    /// <summary>
    /// Gets or sets the scheduled timestamp for the subsequent retry attempt.
    /// </summary>
    public DateTime? NextAttemptAt { get; set; }
}
