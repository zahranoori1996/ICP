using System.Data;
using System.Data.Common;
using System.IO;
using System.IO.Compression;
using System.Text;
using Application.Services;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shared.Wrapper;

namespace Infrastructure.Services;

/// <summary>
/// Implements project persistence operations using Entity Framework Core.
/// </summary>
public class ProjectPersistenceService : IProjectPersistenceService
{
    private readonly IsatisDbContext _db;
    private readonly ILogger<ProjectPersistenceService> _logger;
    private const int DefaultRawPageSize = 1000;
    private const int MaxRawPageSize = 10000;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProjectPersistenceService"/> class.
    /// </summary>
    /// <param name="db">The database context.</param>
    public ProjectPersistenceService(IsatisDbContext db, ILogger<ProjectPersistenceService> logger)
    {
        _db = db;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<Result<ProjectSaveResult>> SaveProjectAsync(Guid projectId, string projectName, string? owner, List<RawDataDto>? rawRows, string? stateJson, string? device = null, string? fileType = null, string? description = null)
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
                        Owner = owner,
                        Device = device ?? string.Empty,
                        FileType = fileType ?? string.Empty,
                        Description = description
                    };
                    _db.Projects.Add(project);
                }
                else
                {
                    //project.ProjectName = projectName;
                    //project.LastModifiedAt = now;
                    //project.Owner = owner;
                    //if (!string.IsNullOrEmpty(device)) project.Device = device;
                    //if (!string.IsNullOrEmpty(fileType)) project.FileType = fileType;

                    //if (description != null) project.Description = description;
                    //_db.Projects.Update(project);



                    if (!string.IsNullOrEmpty(projectName))
                        project.ProjectName = projectName;

                    project.LastModifiedAt = now;

                    // Owner رو فقط وقتی آپدیت کن که مقدار داشته باشه
                    if (!string.IsNullOrEmpty(owner))
                        project.Owner = owner;

                    // Device و FileType رو حتی اگه خالی باشن آپدیت کن (کاربر ممکنه بخواد پاک کنه)
                    if (device != null)
                        project.Device = device;

                    if (fileType != null)
                        project.FileType = fileType;

                    if (description != null)
                        project.Description = description;

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
    //public async Task<Result<ProjectLoadDto>> LoadProjectAsync(Guid projectId, bool includeRawRows = false, bool includeLatestState = false)
    //{
    //    try
    //    {
    //        var project = await _db.Projects
    //            .AsNoTracking()
    //            .Where(p => p.ProjectId == projectId)
    //            .Select(p => new
    //            {
    //                p.ProjectId,
    //                p.ProjectName,
    //                p.CreatedAt,
    //                p.LastModifiedAt,
    //                p.Owner,
    //                p.Device,
    //                p.FileType,
    //                p.Description
    //            })
    //            .FirstOrDefaultAsync();

    //        if (project == null)
    //            return Result<ProjectLoadDto>.Fail("Project not found.");

    //        var rawRows = includeRawRows
    //            ? await _db.RawDataRows
    //                .AsNoTracking()
    //                .Where(r => r.ProjectId == projectId)
    //                .OrderBy(r => r.DataId)
    //                .Select(r => new RawDataDto(r.ColumnData, r.SampleId))
    //                .ToListAsync()
    //            : new List<RawDataDto>();

    //        string? latestState = null;
    //        if (includeLatestState)
    //        {
    //            var stateResult = await GetLatestProjectStateJsonAsync(projectId);
    //            if (!stateResult.Succeeded)
    //                return Result<ProjectLoadDto>.Fail(stateResult.Messages?.FirstOrDefault() ?? "Failed to load latest state.");

    //            latestState = stateResult.Data;
    //        }

    //        var dto = new ProjectLoadDto(project.ProjectId, project.ProjectName, project.CreatedAt, project.LastModifiedAt, project.Owner, rawRows, latestState, project.Device, project.FileType, project.Description);

    //        return Result<ProjectLoadDto>.Success(dto);
    //    }
    //    catch (Exception ex)
    //    {
    //        return Result<ProjectLoadDto>.Fail($"Load failed: {ex.Message}");
    //    }
    //}




    public async Task<Result<ProjectLoadDto>> LoadProjectAsync(Guid projectId, bool includeRawRows = false, bool includeLatestState = false)
    {
        try
        {
            // 1. دریافت پروژه از دیتابیس
            var query = _db.Projects.AsNoTracking().Where(p => p.ProjectId == projectId);

            // (Include کردن ردیف‌های خام در صورت نیاز)
            if (includeRawRows)
            {
                query = query.Include(p => p.RawDataRows);
            }

            var project = await query.FirstOrDefaultAsync();
            if (project == null)
            {
                return Result<ProjectLoadDto>.Fail("Project not found");
            }

            // 2. مدیریت State (کد قبلی شما)
            string? latestState = null;
            if (includeLatestState)
            {
                var stateResult = await GetLatestProjectStateJsonAsync(projectId);
                if (!stateResult.Succeeded)
                    return Result<ProjectLoadDto>.Fail(stateResult.Messages?.FirstOrDefault() ?? "Failed to load latest state.");

                latestState = stateResult.Data;
            }

            // 3. نگاشت (Mapping) نهایی - بخش مهم تغییر اینجاست:
            var dto = new ProjectLoadDto(
                project.ProjectId,
                project.ProjectName,
                project.CreatedAt,
                project.LastModifiedAt,
                project.Owner,
                // تبدیل لیست ردیف‌ها به DTO
                project.RawDataRows?.Select(r => new RawDataDto(r.ColumnData, r.SampleId)).ToList() ?? new List<RawDataDto>(),
                latestState,

                // *** خطوط زیر را حتماً اضافه کنید ***
                project.Device,       // پاس دادن مقدار واقعی دیتابیس
                project.FileType,     // پاس دادن مقدار واقعی دیتابیس
                project.Description   // پاس دادن مقدار واقعی دیتابیس
            );

            return Result<ProjectLoadDto>.Success(dto);
        }
        catch (Exception ex)
        {
            return Result<ProjectLoadDto>.Fail($"Load failed: {ex.Message}");
        }
    }
    /// <inheritdoc/>
    public async Task<Result<List<RawDataDto>>> GetRawDataRowsAsync(Guid projectId, int skip = 0, int take = DefaultRawPageSize)
    {
        try
        {
            if (skip < 0)
                skip = 0;

            if (take <= 0)
                take = DefaultRawPageSize;

            take = Math.Min(take, MaxRawPageSize);

            var rows = await _db.RawDataRows
                .AsNoTracking()
                .Where(r => r.ProjectId == projectId)
                .OrderBy(r => r.DataId)
                .Skip(skip)
                .Take(take)
                .Select(r => new RawDataDto(r.ColumnData, r.SampleId))
                .ToListAsync();

            return Result<List<RawDataDto>>.Success(rows);
        }
        catch (Exception ex)
        {
            return Result<List<RawDataDto>>.Fail($"Failed to load raw rows: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<Result<string?>> GetLatestProjectStateJsonAsync(Guid projectId)
    {
        try
        {
            var latestStateId = await GetLatestStateIdAsync(projectId);
            if (!latestStateId.HasValue)
                return Result<string?>.Success(null);

            var compressed = await GetCompressedStateAsync(latestStateId.Value);
            if (compressed == null || compressed.Length == 0)
                return Result<string?>.Success(null);

            var data = DecompressToString(compressed);
            _logger.LogInformation("Decompressed state {StateId} size {SizeBytes} bytes.", latestStateId.Value, Encoding.UTF8.GetByteCount(data));
            return Result<string?>.Success(data);
        }
        catch (Exception ex)
        {
            return Result<string?>.Fail($"Failed to load latest state: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<Result<byte[]?>> GetLatestProjectStateCompressedAsync(Guid projectId)
    {
        try
        {
            var latestStateId = await GetLatestStateIdAsync(projectId);
            if (!latestStateId.HasValue)
                return Result<byte[]?>.Success(null);

            var compressed = await GetCompressedStateAsync(latestStateId.Value);
            return Result<byte[]?>.Success(compressed);
        }
        catch (Exception ex)
        {
            return Result<byte[]?>.Fail($"Failed to load latest state (compressed): {ex.Message}");
        }
    }

    private Task<int?> GetLatestStateIdAsync(Guid projectId)
    {
        return _db.ProjectStates
            .AsNoTracking()
            .Where(s => s.ProjectId == projectId)
            .OrderByDescending(s => s.Timestamp)
            .ThenByDescending(s => s.StateId)
            .Select(s => (int?)s.StateId)
            .FirstOrDefaultAsync();
    }

    private async Task<byte[]?> GetCompressedStateAsync(int stateId)
    {
        const string sql = "SELECT TOP (1) COMPRESS([Data]) FROM [ProjectStates] WHERE [StateId] = @stateId";
        DbConnection connection = _db.Database.GetDbConnection();
        bool wasClosed = connection.State == ConnectionState.Closed;

        try
        {
            if (wasClosed)
                await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = sql;
            var param = command.CreateParameter();
            param.ParameterName = "@stateId";
            param.DbType = DbType.Int32;
            param.Value = stateId;
            command.Parameters.Add(param);

            var result = await command.ExecuteScalarAsync();
            if (result is byte[] bytes)
            {
                _logger.LogInformation("Compressed state {StateId} size {SizeBytes} bytes.", stateId, bytes.Length);
                return bytes;
            }

            _logger.LogWarning("Compressed state {StateId} returned no data.", stateId);
            return null;
        }
        finally
        {
            if (wasClosed)
                await connection.CloseAsync();
        }
    }

    private static string DecompressToString(byte[] compressed)
    {
        using var input = new MemoryStream(compressed);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <inheritdoc/>
    //public async Task<Result<List<ProjectListItemDto>>> ListProjectsAsync(int page = 1, int pageSize = 20)
    //{
    //    try
    //    {
    //        if (page <= 0) page = 1;
    //        if (pageSize <= 0) pageSize = 20;

    //        var skip = (page - 1) * pageSize;

    //        var items = await _db.Projects
    //            .AsNoTracking()
    //            .OrderByDescending(p => p.CreatedAt)
    //            .Skip(skip)
    //            .Take(pageSize)
    //            .Select(p => new ProjectListItemDto(
    //                p.ProjectId,
    //                p.ProjectName,
    //                p.CreatedAt,
    //                p.LastModifiedAt,
    //                p.Owner,
    //                p.RawDataRows.Count,
    //                p.Device,
    //                p.FileType
    //            ))
    //            .ToListAsync();

    //        return Result<List<ProjectListItemDto>>.Success(items);
    //    }
    //    catch (Exception ex)
    //    {
    //        return Result<List<ProjectListItemDto>>.Fail($"List failed: {ex.Message}");
    //    }
    //}



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
                    p.RawDataRows.Count,

                    // *** این دو خط باید حتما باشند ***
                    p.Device,    // نگاشت به Device
                    p.FileType   // نگاشت به FileType
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
