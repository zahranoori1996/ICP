using Shared.Wrapper;

namespace Application.Services;

/// <summary>
/// Represents the raw data content of a single row in the project, typically storing imported values.
/// </summary>
/// <param name="ColumnData">The core data serialized as a JSON string.</param>
/// <param name="SampleId">An optional identifier for the sample, if extracted.</param>
public record RawDataDto(string ColumnData, string? SampleId);

/// <summary>
/// Represents the result of a project save operation.
/// </summary>
/// <param name="ProjectId">The unique identifier of the saved project.</param>
public record ProjectSaveResult(Guid ProjectId);

/// <summary>
/// Contains the complete details of a project loaded from the persistence layer.
/// </summary>
/// <param name="ProjectId">The unique identifier of the project.</param>
/// <param name="ProjectName">The display name of the project.</param>
/// <param name="CreatedAt">The timestamp when the project was first created.</param>
/// <param name="LastModifiedAt">The timestamp when the project was last updated.</param>
/// <param name="Owner">The identifier of the project owner.</param>
/// <param name="RawRows">The collection of raw data rows associated with the project.</param>
/// <param name="LatestStateJson">The JSON representation of the most recent processed state.</param>
public record ProjectLoadDto(
    Guid ProjectId,
    string ProjectName,
    DateTime CreatedAt,
    DateTime LastModifiedAt,
    string? Owner,
    List<RawDataDto> RawRows,
    string? LatestStateJson
);

/// <summary>
/// Represents a summarized view of a project for listing purposes.
/// </summary>
/// <param name="ProjectId">The unique identifier of the project.</param>
/// <param name="ProjectName">The display name of the project.</param>
/// <param name="CreatedAt">The timestamp when the project was created.</param>
/// <param name="LastModifiedAt">The timestamp when the project was last modified.</param>
/// <param name="Owner">The identifier of the project owner.</param>
/// <param name="RawRowsCount">The total number of raw rows contained in the project.</param>
public record ProjectListItemDto(
    Guid ProjectId,
    string ProjectName,
    DateTime CreatedAt,
    DateTime LastModifiedAt,
    string? Owner,
    int RawRowsCount
);

/// <summary>
/// Defines the contract for services responsible for the persistence, retrieval, and management of project data.
/// </summary>
public interface IProjectPersistenceService
{
    /// <summary>
    /// Persists a new project or updates an existing one with optional raw data and state.
    /// </summary>
    /// <param name="projectId">The unique identifier of the project.</param>
    /// <param name="projectName">The name of the project.</param>
    /// <param name="owner">The owner of the project.</param>
    /// <param name="rawRows">A list of raw data rows to save, if applicable.</param>
    /// <param name="stateJson">The initial JSON state configuration, if applicable.</param>
    /// <returns>A result containing details of the saved project.</returns>
    Task<Result<ProjectSaveResult>> SaveProjectAsync(Guid projectId, string projectName, string? owner, List<RawDataDto>? rawRows, string? stateJson);

    /// <summary>
    /// Retrieves the complete data and metadata for a specific project.
    /// </summary>
    /// <param name="projectId">The unique identifier of the project to load.</param>
    /// <returns>A result containing the full project details.</returns>
    Task<Result<ProjectLoadDto>> LoadProjectAsync(Guid projectId);

    /// <summary>
    /// Retrieves a paginated list of available projects with summary information.
    /// </summary>
    /// <param name="page">The page number to retrieve. Defaults to 1.</param>
    /// <param name="pageSize">The number of items per page. Defaults to 20.</param>
    /// <returns>A result containing a list of project summaries.</returns>
    Task<Result<List<ProjectListItemDto>>> ListProjectsAsync(int page = 1, int pageSize = 20);

    /// <summary>
    /// Permanently deletes a project and all its associated data from the system.
    /// </summary>
    /// <param name="projectId">The unique identifier of the project to delete.</param>
    /// <returns>A result indicating true if the deletion was successful.</returns>
    Task<Result<bool>> DeleteProjectAsync(Guid projectId);
}