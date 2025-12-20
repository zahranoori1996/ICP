using Application.Services;
using Domain.Entities;
using Infrastructure.Persistence;
using Shared.Wrapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;

/// <summary>
/// Service for managing project versions with a tree-based structure.
/// Allows branching and history navigation similar to version control systems.
/// </summary>
public class VersionService : IVersionService
{
    private readonly IsatisDbContext _db;
    private readonly ILogger<VersionService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="VersionService"/> class.
    /// </summary>
    /// <param name="db">The database context.</param>
    /// <param name="logger">The logger instance.</param>
    public VersionService(IsatisDbContext db, ILogger<VersionService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<Result<ProjectState>> CreateVersionAsync(CreateVersionDto dto, CancellationToken ct = default)
    {
        try
        {
            // Validate project exists
            var projectExists = await _db.Projects.AnyAsync(p => p.ProjectId == dto.ProjectId, ct);
            if (!projectExists)
                return Result<ProjectState>.Fail("Project not found");

            // Get next version number
            var maxVersion = await _db.ProjectStates
                .Where(s => s.ProjectId == dto.ProjectId)
                .MaxAsync(s => (int?)s.VersionNumber, ct) ?? 0;

            // Deactivate all other versions for this project
            await _db.ProjectStates
                .Where(s => s.ProjectId == dto.ProjectId && s.IsActive)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false), ct);

            var newState = new ProjectState
            {
                ProjectId = dto.ProjectId,
                ParentStateId = dto.ParentStateId,
                VersionNumber = maxVersion + 1,
                ProcessingType = dto.ProcessingType,
                Data = dto.Data,
                Description = dto.Description,
                Timestamp = DateTime.UtcNow,
                IsActive = true
            };

            _db.ProjectStates.Add(newState);
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Created version {Version} for project {ProjectId}: {ProcessingType}",
                newState.VersionNumber, dto.ProjectId, dto.ProcessingType);

            return Result<ProjectState>.Success(newState);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create version for project {ProjectId}", dto.ProjectId);
            return Result<ProjectState>.Fail($"Failed to create version: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<Result<List<VersionNodeDto>>> GetVersionTreeAsync(Guid projectId, CancellationToken ct = default)
    {
        try
        {
            var allStates = await _db.ProjectStates
                .Where(s => s.ProjectId == projectId)
                .OrderBy(s => s.VersionNumber)
                .ToListAsync(ct);

            // Build tree structure
            var stateDict = allStates.ToDictionary(s => s.StateId);
            var rootNodes = new List<VersionNodeDto>();

            VersionNodeDto BuildNode(ProjectState state)
            {
                var children = allStates
                    .Where(s => s.ParentStateId == state.StateId)
                    .Select(BuildNode)
                    .ToList();

                return new VersionNodeDto(
                    state.StateId,
                    state.ParentStateId,
                    state.VersionNumber,
                    state.ProcessingType,
                    state.Description,
                    state.Timestamp,
                    state.IsActive,
                    children
                );
            }

            // Find root nodes (no parent)
            foreach (var state in allStates.Where(s => s.ParentStateId == null))
            {
                rootNodes.Add(BuildNode(state));
            }

            return Result<List<VersionNodeDto>>.Success(rootNodes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get version tree for project {ProjectId}", projectId);
            return Result<List<VersionNodeDto>>.Fail($"Failed to get version tree: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<Result<List<ProjectState>>> GetAllVersionsAsync(Guid projectId, CancellationToken ct = default)
    {
        try
        {
            var versions = await _db.ProjectStates
                .Where(s => s.ProjectId == projectId)
                .OrderByDescending(s => s.Timestamp)
                .ToListAsync(ct);

            return Result<List<ProjectState>>.Success(versions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get versions for project {ProjectId}", projectId);
            return Result<List<ProjectState>>.Fail($"Failed to get versions: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<Result<ProjectState?>> GetActiveVersionAsync(Guid projectId, CancellationToken ct = default)
    {
        try
        {
            var activeState = await _db.ProjectStates
                .Where(s => s.ProjectId == projectId && s.IsActive)
                .FirstOrDefaultAsync(ct);

            // If no active, get the latest
            if (activeState == null)
            {
                activeState = await _db.ProjectStates
                    .Where(s => s.ProjectId == projectId)
                    .OrderByDescending(s => s.Timestamp)
                    .FirstOrDefaultAsync(ct);
            }

            return Result<ProjectState?>.Success(activeState);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get active version for project {ProjectId}", projectId);
            return Result<ProjectState?>.Fail($"Failed to get active version: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> SwitchToVersionAsync(Guid projectId, int stateId, CancellationToken ct = default)
    {
        try
        {
            var state = await _db.ProjectStates
                .FirstOrDefaultAsync(s => s.StateId == stateId && s.ProjectId == projectId, ct);

            if (state == null)
                return Result<bool>.Fail("Version not found");

            // Deactivate all versions
            await _db.ProjectStates
                .Where(s => s.ProjectId == projectId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false), ct);

            // Activate selected version
            state.IsActive = true;
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Switched to version {Version} (StateId={StateId}) for project {ProjectId}",
                state.VersionNumber, stateId, projectId);

            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to switch to version {StateId}", stateId);
            return Result<bool>.Fail($"Failed to switch version: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<Result<ProjectState?>> GetVersionAsync(int stateId, CancellationToken ct = default)
    {
        try
        {
            var state = await _db.ProjectStates
                .FirstOrDefaultAsync(s => s.StateId == stateId, ct);

            return Result<ProjectState?>.Success(state);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get version {StateId}", stateId);
            return Result<ProjectState?>.Fail($"Failed to get version: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<Result<List<ProjectState>>> GetVersionPathAsync(int stateId, CancellationToken ct = default)
    {
        try
        {
            var path = new List<ProjectState>();
            var currentId = (int?)stateId;

            while (currentId.HasValue)
            {
                var state = await _db.ProjectStates
                    .FirstOrDefaultAsync(s => s.StateId == currentId.Value, ct);

                if (state == null) break;

                path.Insert(0, state); // Insert at beginning to get root-to-leaf order
                currentId = state.ParentStateId;
            }

            return Result<List<ProjectState>>.Success(path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get version path for {StateId}", stateId);
            return Result<List<ProjectState>>.Fail($"Failed to get version path: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> DeleteVersionAsync(int stateId, bool deleteChildren = false, CancellationToken ct = default)
    {
        try
        {
            var state = await _db.ProjectStates
                .FirstOrDefaultAsync(s => s.StateId == stateId, ct);

            if (state == null)
                return Result<bool>.Fail("Version not found");

            // Check for children
            var hasChildren = await _db.ProjectStates
                .AnyAsync(s => s.ParentStateId == stateId, ct);

            if (hasChildren && !deleteChildren)
                return Result<bool>.Fail("Version has children. Set deleteChildren=true to delete recursively.");

            if (deleteChildren)
            {
                // Recursively delete children
                await DeleteChildrenRecursive(stateId, ct);
            }

            _db.ProjectStates.Remove(state);
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Deleted version {StateId}", stateId);
            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete version {StateId}", stateId);
            return Result<bool>.Fail($"Failed to delete version: {ex.Message}");
        }
    }

    private async Task DeleteChildrenRecursive(int parentId, CancellationToken ct)
    {
        var children = await _db.ProjectStates
            .Where(s => s.ParentStateId == parentId)
            .ToListAsync(ct);

        foreach (var child in children)
        {
            await DeleteChildrenRecursive(child.StateId, ct);
            _db.ProjectStates.Remove(child);
        }
    }

    /// <inheritdoc/>
    public async Task<Result<ProjectState>> ForkVersionAsync(int parentStateId, string processingType, string data, string? description = null, CancellationToken ct = default)
    {
        try
        {
            var parentState = await _db.ProjectStates
                .FirstOrDefaultAsync(s => s.StateId == parentStateId, ct);

            if (parentState == null)
                return Result<ProjectState>.Fail("Parent version not found");

            var dto = new CreateVersionDto(
                parentState.ProjectId,
                parentStateId,
                processingType,
                data,
                description ?? $"Fork from v{parentState.VersionNumber}"
            );

            return await CreateVersionAsync(dto, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fork version {ParentStateId}", parentStateId);
            return Result<ProjectState>.Fail($"Failed to fork version: {ex.Message}");
        }
    }
}
