namespace Shared.Models;

/// <summary>
/// Data Transfer Object representing the status of an import job.
/// </summary>
/// <param name="JobId">The unique identifier of the job.</param>
/// <param name="State">The current state of the job.</param>
/// <param name="TotalRows">The total number of rows in the job.</param>
/// <param name="ProcessedRows">The number of rows processed so far.</param>
/// <param name="Message">A message describing the status or error.</param>
/// <param name="ProjectId">The associated project identifier.</param>
/// <param name="Percent">The completion percentage.</param>
public record ImportJobStatusDto(
    Guid JobId,
    ImportJobState State,
    int TotalRows,
    int ProcessedRows,
    string? Message,
    Guid? ProjectId,
    int Percent
);