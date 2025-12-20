using System.Text.Json;
using System.Text.Json.Serialization;

namespace WebUI.Services;

// ============================================
// Pivot DTOs
// ============================================

public class PivotRequest
{
    [JsonPropertyName("projectId")]
    public Guid ProjectId { get; set; }

    [JsonPropertyName("searchText")]
    public string? SearchText { get; set; }

    [JsonPropertyName("selectedSolutionLabels")]
    public List<string>? SelectedSolutionLabels { get; set; }

    [JsonPropertyName("selectedElements")]
    public List<string>? SelectedElements { get; set; }

    [JsonPropertyName("numberFilters")]
    public Dictionary<string, NumberFilter>? NumberFilters { get; set; }

    [JsonPropertyName("useOxide")]
    public bool UseOxide { get; set; }

    [JsonPropertyName("decimalPlaces")]
    public int DecimalPlaces { get; set; } = 2;

    [JsonPropertyName("page")]
    public int Page { get; set; } = 1;

    [JsonPropertyName("pageSize")]
    public int PageSize { get; set; } = 100;
}

public class NumberFilter
{
    [JsonPropertyName("min")]
    public decimal? Min { get; set; }

    [JsonPropertyName("max")]
    public decimal? Max { get; set; }
}

public class PivotResultDto
{
    [JsonPropertyName("columns")]
    public List<string> Columns { get; set; } = new();

    [JsonPropertyName("rows")]
    public List<PivotRowDto> Rows { get; set; } = new();

    [JsonPropertyName("totalCount")]
    public int TotalCount { get; set; }

    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("pageSize")]
    public int PageSize { get; set; }

    [JsonPropertyName("metadata")]
    public PivotMetadataDto? Metadata { get; set; }
}

public class PivotRowDto
{
    [JsonPropertyName("solutionLabel")]
    public string SolutionLabel { get; set; } = "";

    [JsonPropertyName("values")]
    public Dictionary<string, decimal?> Values { get; set; } = new();

    [JsonPropertyName("originalIndex")]
    public int OriginalIndex { get; set; }
}

public class PivotMetadataDto
{
    [JsonPropertyName("allSolutionLabels")]
    public List<string> AllSolutionLabels { get; set; } = new();

    [JsonPropertyName("allElements")]
    public List<string> AllElements { get; set; } = new();

    [JsonPropertyName("columnStats")]
    public Dictionary<string, ColumnStatsDto> ColumnStats { get; set; } = new();
}

public class ColumnStatsDto
{
    [JsonPropertyName("min")]
    public decimal? Min { get; set; }

    [JsonPropertyName("max")]
    public decimal? Max { get; set; }

    [JsonPropertyName("mean")]
    public decimal? Mean { get; set; }

    [JsonPropertyName("stdDev")]
    public decimal? StdDev { get; set; }

    [JsonPropertyName("nonNullCount")]
    public int NonNullCount { get; set; }
}

// ============================================
// Advanced Pivot DTOs (Python-compatible duplicate handling)
// ============================================

public record AdvancedPivotRequest(
    [property: JsonPropertyName("projectId")] Guid ProjectId,
    [property: JsonPropertyName("searchText")] string? SearchText = null,
    [property: JsonPropertyName("selectedSolutionLabels")] List<string>? SelectedSolutionLabels = null,
    [property: JsonPropertyName("selectedElements")] List<string>? SelectedElements = null,
    [property: JsonPropertyName("numberFilters")] Dictionary<string, NumberFilter>? NumberFilters = null,
    [property: JsonPropertyName("useOxide")] bool UseOxide = false,
    [property: JsonPropertyName("useInt")] bool UseInt = false,
    [property: JsonPropertyName("decimalPlaces")] int DecimalPlaces = 2,
    [property: JsonPropertyName("page")] int Page = 1,
    [property: JsonPropertyName("pageSize")] int PageSize = 100,
    [property: JsonPropertyName("aggregation")] string Aggregation = "First",
    [property: JsonPropertyName("mergeRepeats")] bool MergeRepeats = false
);

public class AdvancedPivotResultDto
{
    [JsonPropertyName("columns")]
    public List<string> Columns { get; set; } = new();

    [JsonPropertyName("rows")]
    public List<AdvancedPivotRowDto> Rows { get; set; } = new();

    [JsonPropertyName("totalCount")]
    public int TotalCount { get; set; }

    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("pageSize")]
    public int PageSize { get; set; }

    [JsonPropertyName("metadata")]
    public AdvancedPivotMetadataDto? Metadata { get; set; }
}

public class AdvancedPivotRowDto
{
    [JsonPropertyName("solutionLabel")]
    public string SolutionLabel { get; set; } = "";

    [JsonPropertyName("values")]
    public Dictionary<string, decimal?> Values { get; set; } = new();

    [JsonPropertyName("originalIndex")]
    public int OriginalIndex { get; set; }

    [JsonPropertyName("setIndex")]
    public int SetIndex { get; set; }

    [JsonPropertyName("setSize")]
    public int SetSize { get; set; }
}

public class AdvancedPivotMetadataDto
{
    [JsonPropertyName("allSolutionLabels")]
    public List<string> AllSolutionLabels { get; set; } = new();

    [JsonPropertyName("allElements")]
    public List<string> AllElements { get; set; } = new();

    [JsonPropertyName("columnStats")]
    public Dictionary<string, ColumnStatsDto> ColumnStats { get; set; } = new();

    [JsonPropertyName("hasRepeats")]
    public bool HasRepeats { get; set; }

    [JsonPropertyName("setSizes")]
    public Dictionary<string, int> SetSizes { get; set; } = new();

    [JsonPropertyName("repeatedElements")]
    public Dictionary<string, List<string>> RepeatedElements { get; set; } = new();
}

// ============================================
// Pivot Service
// ============================================

public class PivotService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<PivotService> _logger;
    private readonly AuthService _authService;

    public PivotService(IHttpClientFactory clientFactory, ILogger<PivotService> logger, AuthService authService)
    {
        _httpClient = clientFactory.CreateClient("Api");
        _logger = logger;
        _authService = authService;
    }

    /// <summary>
    /// Get pivot table data
    /// </summary>
    public async Task<ServiceResult<PivotResultDto>> GetPivotTableAsync(PivotRequest request)
    {
        try
        {
            SetAuthHeader();

            var response = await _httpClient.PostAsJsonAsync("pivot", request);
            var content = await response.Content.ReadAsStringAsync();

            _logger.LogDebug("Pivot response: {Content}", content);

            if (response.IsSuccessStatusCode)
            {
                var result = JsonSerializer.Deserialize<ApiResult<PivotResultDto>>(content,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (result?.Succeeded == true && result.Data != null)
                {
                    return ServiceResult<PivotResultDto>.Success(result.Data);
                }

                return ServiceResult<PivotResultDto>.Fail(result?.Message ?? "Failed to load pivot data");
            }

            return ServiceResult<PivotResultDto>.Fail($"Server error: {response.StatusCode}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading pivot table");
            return ServiceResult<PivotResultDto>.Fail($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Get advanced pivot table with Python-compatible duplicate handling
    /// Uses GCD algorithm for set size calculation and Element_with_id for repeats
    /// </summary>
    public async Task<ServiceResult<AdvancedPivotResultDto>> GetAdvancedPivotTableAsync(AdvancedPivotRequest request)
    {
        try
        {
            SetAuthHeader();

            var response = await _httpClient.PostAsJsonAsync("pivot/advanced", request);
            var content = await response.Content.ReadAsStringAsync();

            _logger.LogDebug("Advanced Pivot response: {Content}", content);

            if (response.IsSuccessStatusCode)
            {
                var result = JsonSerializer.Deserialize<ApiResult<AdvancedPivotResultDto>>(content,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (result?.Succeeded == true && result.Data != null)
                {
                    return ServiceResult<AdvancedPivotResultDto>.Success(result.Data);
                }

                return ServiceResult<AdvancedPivotResultDto>.Fail(result?.Message ?? "Failed to load pivot data");
            }

            return ServiceResult<AdvancedPivotResultDto>.Fail($"Server error: {response.StatusCode}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading advanced pivot table");
            return ServiceResult<AdvancedPivotResultDto>.Fail($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Get all solution labels for a project
    /// </summary>
    public async Task<ServiceResult<List<string>>> GetSolutionLabelsAsync(Guid projectId)
    {
        try
        {
            SetAuthHeader();

            var response = await _httpClient.GetAsync($"pivot/{projectId}/labels");
            var content = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var result = JsonSerializer.Deserialize<ApiResult<List<string>>>(content,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (result?.Succeeded == true && result.Data != null)
                {
                    return ServiceResult<List<string>>.Success(result.Data);
                }

                return ServiceResult<List<string>>.Fail(result?.Message ?? "Failed");
            }

            return ServiceResult<List<string>>.Fail($"Server error: {response.StatusCode}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading solution labels");
            return ServiceResult<List<string>>.Fail($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Get all elements for a project
    /// </summary>
    public async Task<ServiceResult<List<string>>> GetElementsAsync(Guid projectId)
    {
        try
        {
            SetAuthHeader();

            var response = await _httpClient.GetAsync($"pivot/{projectId}/elements");
            var content = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var result = JsonSerializer.Deserialize<ApiResult<List<string>>>(content,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (result?.Succeeded == true && result.Data != null)
                {
                    return ServiceResult<List<string>>.Success(result.Data);
                }

                return ServiceResult<List<string>>.Fail(result?.Message ?? "Failed");
            }

            return ServiceResult<List<string>>.Fail($"Server error: {response.StatusCode}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading elements");
            return ServiceResult<List<string>>.Fail($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Export pivot data to CSV
    /// </summary>
    public async Task<ServiceResult<byte[]>> ExportToCsvAsync(PivotRequest request)
    {
        try
        {
            SetAuthHeader();

            var response = await _httpClient.PostAsJsonAsync("pivot/export", request);

            if (response.IsSuccessStatusCode)
            {
                var bytes = await response.Content.ReadAsByteArrayAsync();
                return ServiceResult<byte[]>.Success(bytes);
            }

            return ServiceResult<byte[]>.Fail($"Export failed: {response.StatusCode}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting to CSV");
            return ServiceResult<byte[]>.Fail($"Error: {ex.Message}");
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
}
