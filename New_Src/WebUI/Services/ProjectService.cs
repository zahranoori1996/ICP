using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Net.Http;

namespace WebUI.Services;

// ============================================
// Project DTOs
// ============================================

// DTO matching API ProjectListItemDto
public class ProjectListItemDto
{
    [JsonPropertyName("projectId")]
    public Guid ProjectId { get; set; }

    [JsonPropertyName("projectName")]
    public string ProjectName { get; set; } = "";

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("lastModifiedAt")]
    public DateTime LastModifiedAt { get; set; }

    [JsonPropertyName("owner")]
    public string? Owner { get; set; }

    [JsonPropertyName("rawRowsCount")]
    public int RawRowsCount { get; set; }

    [JsonPropertyName("device")]
    public string? Device { get; set; }

    [JsonPropertyName("fileType")]
    public string? FileType { get; set; }
}

// DTO for loaded project (from GET /api/projects/{id})
public class ProjectInfoDto
{
    [JsonPropertyName("projectId")]
    public Guid ProjectId { get; set; }
    
    [JsonPropertyName("projectName")]
    public string? ProjectName { get; set; }
    
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }
    
    [JsonPropertyName("lastModifiedAt")]
    public DateTime LastModifiedAt { get; set; }
    
    [JsonPropertyName("owner")]
    public string? Owner { get; set; }

    [JsonPropertyName("latestStateJson")]
    public string? LatestStateJson { get; set; }

    [JsonPropertyName("device")]
    public string? Device { get; set; }

    [JsonPropertyName("fileType")]
    public string? FileType { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

public class ProjectDto
{
    // From import jobs response
    [JsonPropertyName("jobId")]
    public Guid JobId { get; set; }
    
    [JsonPropertyName("resultProjectId")]
    public Guid? ResultProjectId { get; set; }

    [JsonPropertyName("projectName")]
    public string ProjectName { get; set; } = "";
    
    [JsonPropertyName("state")]
    public int State { get; set; }
    
    [JsonPropertyName("totalRows")]
    public int TotalRows { get; set; }
    
    [JsonPropertyName("processedRows")]
    public int ProcessedRows { get; set; }
    
    [JsonPropertyName("percent")]
    public int Percent { get; set; }
    
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; }

    [JsonPropertyName("device")]
    public string? Device { get; set; }

    [JsonPropertyName("fileType")]
    public string? FileType { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    // Helper properties for UI compatibility
    public Guid ProjectId => ResultProjectId ?? JobId;
    public string Owner => "";
    public DateTime LastModifiedAt => UpdatedAt;
    public int RowCount => TotalRows;
    
    // Dashboard helpers
    public Guid Id => ProjectId;
    public string Name => ProjectName ?? $"Project-{JobId.ToString().Substring(0, 8)}";
    public int SampleCount => RowCount;
    public DateTime LastAccessed => LastModifiedAt;
    
    // Status helpers
    public bool IsCompleted => State == 2;
    public bool IsQueued => State == 0;
    public bool IsProcessing => State == 1;
    public bool IsFailed => State == 3;
}

public class ProjectListResult
{
    [JsonPropertyName("items")]
    public List<ProjectDto> Items { get; set; } = new();

    [JsonPropertyName("total")]
    public int Total { get; set; }
    
    [JsonPropertyName("page")]
    public int Page { get; set; }
    
    [JsonPropertyName("pageSize")]
    public int PageSize { get; set; }

    public int TotalCount => Total;
}

// ============================================
// Project Service
// ============================================

public class ProjectService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ProjectService> _logger;
    private readonly AuthService _authService;

    // Current selected project
    private static Guid? _currentProjectId;
    private static ProjectDto? _currentProject;

    public ProjectService(IHttpClientFactory clientFactory, ILogger<ProjectService> logger, AuthService authService)
    {
        _httpClient = clientFactory.CreateClient("Api");
        _logger = logger;
        _authService = authService;
    }

    public Guid? CurrentProjectId => _currentProjectId;
    public ProjectDto? CurrentProject => _currentProject;

    public void SetCurrentProject(ProjectDto? project)
    {
        _currentProject = project;
        _currentProjectId = project?.ProjectId;
    }

    public void SetCurrentProject(Guid projectId)
    {
        _currentProjectId = projectId;
    }

    /// <summary>
    /// Get list of all projects for Dashboard (simplified)
    /// </summary>
    public async Task<ServiceResult<List<ProjectDto>>> GetProjectsAsync()
    {
        var result = await GetProjectsAsync(1, 100, null);
        if (result.Succeeded && result.Data != null)
        {
            return ServiceResult<List<ProjectDto>>.Success(result.Data.Items);
        }
        return ServiceResult<List<ProjectDto>>.Fail(result.Message ?? "Failed to get projects");
    }

    /// <summary>
    /// Get list of all projects
    /// </summary>
    public async Task<ServiceResult<ProjectListResult>> GetProjectsAsync(int page = 1, int pageSize = 20, string? search = null)
    {
        try
        {
            SetAuthHeader();

            // Use the main projects endpoint
            var url = $"projects?page={page}&pageSize={pageSize}";

            var response = await _httpClient.GetAsync(url);
            var content = await response.Content.ReadAsStringAsync();

            _logger.LogDebug("Projects response: {Content}", content);

            if (response.IsSuccessStatusCode)
            {
                // Try new format first (direct list)
                var directResult = JsonSerializer.Deserialize<ApiResult<List<ProjectListItemDto>>>(content,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (directResult?.Succeeded == true && directResult.Data != null)
                {
                    // Convert to ProjectListResult format
                    var items = directResult.Data.Select(p => new ProjectDto
                    {
                        JobId = p.ProjectId,
                        ResultProjectId = p.ProjectId,
                        ProjectName = p.ProjectName,
                        TotalRows = p.RawRowsCount,
                        CreatedAt = p.CreatedAt,
                        UpdatedAt = p.LastModifiedAt,
                        State = 2, // Completed
                        Device = p.Device,
                        FileType = p.FileType
                    }).ToList();

                    var listResult = new ProjectListResult
                    {
                        Items = items,
                        Total = items.Count,
                        Page = page,
                        PageSize = pageSize
                    };

                    return ServiceResult<ProjectListResult>.Success(listResult);
                }

                return ServiceResult<ProjectListResult>.Fail(directResult?.Message ?? "Failed to load projects");
            }

            return ServiceResult<ProjectListResult>.Fail($"Server error: {response.StatusCode}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading projects");
            return ServiceResult<ProjectListResult>.Fail($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Get a single project by ID
    /// </summary>
    public async Task<ServiceResult<ProjectInfoDto>> GetProjectAsync(Guid projectId, bool includeLatestState = false)
    {
        try
        {
            SetAuthHeader();

            var response = await _httpClient.GetAsync($"projects/{projectId}");
            var content = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var result = JsonSerializer.Deserialize<ApiResult<ProjectInfoDto>>(content,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (result?.Succeeded == true && result.Data != null)
                {
                    if (includeLatestState)
                    {
                        var stateResult = await GetLatestProjectStateCompressedAsync(projectId);
                        if (stateResult.Succeeded)
                        {
                            result.Data.LatestStateJson = stateResult.Data;
                        }
                        else
                        {
                            _logger.LogWarning("Failed to load latest state for project {ProjectId}: {Message}", projectId, stateResult.Message);
                        }
                    }
                    return ServiceResult<ProjectInfoDto>.Success(result.Data);
                }

                return ServiceResult<ProjectInfoDto>.Fail(result?.Message ?? "Project not found");
            }

            return ServiceResult<ProjectInfoDto>.Fail($"Server error: {response.StatusCode}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading project {ProjectId}", projectId);
            return ServiceResult<ProjectInfoDto>.Fail($"Error: {ex.Message}");
        }
    }

    public async Task<ServiceResult<string?>> GetLatestProjectStateCompressedAsync(Guid projectId)
    {
        try
        {
            SetAuthHeader();

            var response = await _httpClient.GetAsync($"projects/{projectId}/state/latest/compressed");
            if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
            {
                return ServiceResult<string?>.Success(null);
            }

            if (!response.IsSuccessStatusCode)
            {
                return ServiceResult<string?>.Fail($"Server error: {response.StatusCode}");
            }

            var bytes = await response.Content.ReadAsByteArrayAsync();
            if (bytes.Length == 0)
            {
                return ServiceResult<string?>.Success(null);
            }

            var json = DecompressGzipToString(bytes);
            return ServiceResult<string?>.Success(json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading compressed state for project {ProjectId}", projectId);
            return ServiceResult<string?>.Fail($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Delete a project
    /// </summary>
    public async Task<ServiceResult<bool>> DeleteProjectAsync(Guid projectId)
    {
        try
        {
            SetAuthHeader();

            var response = await _httpClient.DeleteAsync($"projects/{projectId}");

            if (response.IsSuccessStatusCode)
            {
                if (_currentProjectId == projectId)
                {
                    _currentProjectId = null;
                    _currentProject = null;
                }
                return ServiceResult<bool>.Success(true);
            }

            var content = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<ApiResult<bool>>(content,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return ServiceResult<bool>.Fail(result?.Message ?? $"Failed to delete project");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting project {ProjectId}", projectId);
            return ServiceResult<bool>.Fail($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Update project metadata on server
    /// </summary>
    public async Task<ServiceResult<bool>> UpdateProjectAsync(Guid projectId, string projectName, string? device, string? fileType, string? description)
    {
        try
        {
            SetAuthHeader();

            var payload = new { projectName = projectName, device = device, fileType = fileType, description = description };
            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PutAsync($"projects/{projectId}", content);
            var respContent = await response.Content.ReadAsStringAsync();
            if (response.IsSuccessStatusCode)
            {
                return ServiceResult<bool>.Success(true);
            }

            if (string.IsNullOrWhiteSpace(respContent))
            {
                _logger.LogWarning("UpdateProject returned empty body. Status: {Status}", response.StatusCode);
                return ServiceResult<bool>.Fail($"Server error: {response.StatusCode} (empty response body)");
            }

            try
            {
                var result = JsonSerializer.Deserialize<ApiResult<bool>>(respContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return ServiceResult<bool>.Fail(result?.Message ?? $"Failed to update project. Status: {response.StatusCode}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to deserialize UpdateProject response: {Content}", respContent);
                return ServiceResult<bool>.Fail($"Server error: {response.StatusCode}. Response: {respContent}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating project {ProjectId}", projectId);
            return ServiceResult<bool>.Fail($"Error: {ex.Message}");
        }
    }

    private void SetAuthHeader()
    {
        var token = _authService.GetAccessToken();
        if (!string.IsNullOrEmpty(token))
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }
    }

    private static string DecompressGzipToString(byte[] compressed)
    {
        using var input = new MemoryStream(compressed);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        return reader.ReadToEnd();
    }
}