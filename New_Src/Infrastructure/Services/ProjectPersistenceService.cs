using Application.Services;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Shared.Wrapper;

namespace Infrastructure.Services;

/// <summary>
/// Implements project persistence operations using Entity Framework Core.
/// </summary>
public class ProjectPersistenceService : IProjectPersistenceService
{
    private readonly IsatisDbContext _db;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProjectPersistenceService"/> class.
    /// </summary>
    /// <param name="db">The database context.</param>
    public ProjectPersistenceService(IsatisDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc/>
    public async Task<Result<ProjectSaveResult>> SaveProjectAsync(Guid projectId, string projectName, string? owner, List<RawDataDto>? rawRows, string? stateJson)
    {
        // Use execution strategy for retry-safe transactions
        var strategy = _db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            using var tx = await _db.Database.BeginTransactionAsync();

            try
            {
                var project = await _db.Projects.FirstOrDefaultAsync(p => p.ProjectId == projectId);
                var now = DateTime.UtcNow;

                if (project == null)
                {
                    project = new Project
                    {
                        ProjectId = projectId == Guid.Empty ? Guid.NewGuid() : projectId,
                        ProjectName = projectName,
                        CreatedAt = now,
                        LastModifiedAt = now,
                        Owner = owner
                    };
                    _db.Projects.Add(project);
                }
                else
                {
                    project.ProjectName = projectName;
                    project.LastModifiedAt = now;
                    project.Owner = owner;
                    _db.Projects.Update(project);
                }

                if (rawRows != null && rawRows.Count > 0)
                {
                    foreach (var r in rawRows)
                    {
                        _db.RawDataRows.Add(new RawDataRow
                        {
                            ProjectId = project.ProjectId,
                            ColumnData = r.ColumnData,
                            SampleId = r.SampleId
                        });
                    }
                }

                if (!string.IsNullOrEmpty(stateJson))
                {
                    _db.ProjectStates.Add(new ProjectState
                    {
                        ProjectId = project.ProjectId,
                        Data = stateJson,
                        Timestamp = now,
                        Description = "ManualSave"
                    });
                }

                await _db.SaveChangesAsync();
                await tx.CommitAsync();

                return Result<ProjectSaveResult>.Success(new ProjectSaveResult(project.ProjectId));
            }
            catch (Exception ex)
            {
                try { await tx.RollbackAsync(); } catch { }
                return Result<ProjectSaveResult>.Fail($"Save failed: {ex.Message}");
            }
        });
    }

    /// <inheritdoc/>
    public async Task<Result<ProjectLoadDto>> LoadProjectAsync(Guid projectId)
    {
        try
        {
            var project = await _db.Projects
                .AsNoTracking()
                .Where(p => p.ProjectId == projectId)
                .Select(p => new
                {
                    p.ProjectId,
                    p.ProjectName,
                    p.CreatedAt,
                    p.LastModifiedAt,
                    p.Owner
                })
                .FirstOrDefaultAsync();

            if (project == null)
                return Result<ProjectLoadDto>.Fail("Project not found.");

            var rawRows = await _db.RawDataRows
                .AsNoTracking()
                .Where(r => r.ProjectId == projectId)
                .OrderBy(r => r.DataId)
                .Select(r => new RawDataDto(r.ColumnData, r.SampleId))
                .ToListAsync();

            var latestStateId = await _db.ProjectStates
                .AsNoTracking()
                .Where(s => s.ProjectId == projectId)
                .OrderByDescending(s => s.Timestamp)
                .ThenByDescending(s => s.StateId)
                .Select(s => (int?)s.StateId)
                .FirstOrDefaultAsync();

            string? latestState = null;
            if (latestStateId.HasValue)
            {
                latestState = await _db.ProjectStates
                    .AsNoTracking()
                    .Where(s => s.StateId == latestStateId.Value)
                    .Select(s => s.Data)
                    .FirstOrDefaultAsync();
            }

            var dto = new ProjectLoadDto(project.ProjectId, project.ProjectName, project.CreatedAt, project.LastModifiedAt, project.Owner, rawRows, latestState);

            return Result<ProjectLoadDto>.Success(dto);
        }
        catch (Exception ex)
        {
            return Result<ProjectLoadDto>.Fail($"Load failed: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<Result<List<ProjectListItemDto>>> ListProjectsAsync(int page = 1, int pageSize = 20)
    {
        try
        {
            if (page <= 0) page = 1;
            if (pageSize <= 0) pageSize = 20;

            var skip = (page - 1) * pageSize;

            var items = await _db.Projects
                .AsNoTracking()
                .OrderByDescending(p => p.CreatedAt)
                .Skip(skip)
                .Take(pageSize)
                .Select(p => new ProjectListItemDto(
                    p.ProjectId,
                    p.ProjectName,
                    p.CreatedAt,
                    p.LastModifiedAt,
                    p.Owner,
                    p.RawDataRows.Count
                ))
                .ToListAsync();

            return Result<List<ProjectListItemDto>>.Success(items);
        }
        catch (Exception ex)
        {
            return Result<List<ProjectListItemDto>>.Fail($"List failed: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> DeleteProjectAsync(Guid projectId)
    {
        var strategy = _db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                var project = await _db.Projects.FirstOrDefaultAsync(p => p.ProjectId == projectId);
                if (project == null)
                    return Result<bool>.Fail("Project not found.");

                _db.Projects.Remove(project);
                await _db.SaveChangesAsync();
                await tx.CommitAsync();
                return Result<bool>.Success(true);
            }
            catch (Exception ex)
            {
                try { await tx.RollbackAsync(); } catch { }
                return Result<bool>.Fail($"Delete failed: {ex.Message}");
            }
        });
    }
}
